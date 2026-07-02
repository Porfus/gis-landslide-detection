using System.Text.Json;
using System.Text.Json.Serialization;
using it.gis_landslide_detection.web.Data;
using it.gis_landslide_detection.web.Services;
using it.gis_landslide_detection.web.Repositories;
using Microsoft.EntityFrameworkCore;

using NetTopologySuite;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL; 
using Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite; 


var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("ApplicationDbContext") ?? throw new InvalidOperationException("Connection string 'ApplicationDbContext' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, sqlOptions =>
    {
        sqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
        sqlOptions.UseNetTopologySuite();
    }));

// Add services to the container.
builder.Services.AddControllersWithViews()
    .AddJsonOptions(opts =>
    {
        // Safety net: se un valore NaN/Infinity sfugge alla sanitizzazione manuale,
        // il serializzatore lo scrive come stringa invece di lanciare ArgumentException
        opts.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals;
    });
builder.Services.AddMemoryCache();
builder.Services.Configure<it.gis_landslide_detection.web.Models.CopernicusApiOptions>(
    builder.Configuration.GetSection("CopernicusApi"));

builder.Services.AddScoped<IIffiService, IffiService>();
builder.Services.AddScoped<ITrailHazardCalculator, TrailHazardCalculator>();
builder.Services.AddScoped<IWeatherService, WeatherService>();
builder.Services.AddScoped<IHazardScoreEngine, HazardScoreEngine>();
builder.Services.AddScoped<ITrailsRepository, TrailsRepository>();
builder.Services.AddScoped<ITrailsService, TrailsService>();
builder.Services.AddScoped<IHikingPointsRepository, HikingPointsRepository>();
builder.Services.AddScoped<IHikingPointsService, HikingPointsService>();
builder.Services.AddScoped<IIffiZonesRepository, IffiZonesRepository>();
builder.Services.AddScoped<IIffiZonesService, IffiZonesService>();
builder.Services.AddScoped<IRoutingService, RoutingService>();
builder.Services.AddScoped<ITspService, TspService>();

// Refresh della topologia di routing in background (debounced): CREATE/UPDATE/DELETE
// di un sentiero non aspettano più i 20+ secondi di refresh_routing_topology().
builder.Services.AddSingleton<RoutingTopologyRefreshService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RoutingTopologyRefreshService>());
builder.Services.AddSingleton<IRoutingTopologyRefreshQueue>(sp => sp.GetRequiredService<RoutingTopologyRefreshService>());


builder.Services.AddCors(options =>
{
    options.AddPolicy("DevPolicy", policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});
builder.Services.AddScoped<ISentinelService, SentinelService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "GIS Landslide API", Version = "v1" });
});

builder.Services.AddHttpClient("openmeteo", client =>
{
    client.BaseAddress = new Uri("https://api.open-meteo.com");
    client.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddHttpClient("copernicus", client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});


var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "GIS Landslide API V1");
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseCors("DevPolicy");

app.UseAuthorization();

app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        
        // Assicura che il database e la struttura PostGIS vengano creati
        db.Database.EnsureCreated();

        if (db.Database.CanConnect())
        {
            Console.WriteLine("Database: connessione OK");
            // Popola con i dati di mock GIS
            await it.gis_landslide_detection.web.Data.GisDataSeeder.SeedAsync(db);

            // Riallinea le sequence IDENTITY al max(id) reale di ogni tabella.
            // I dati GIS arrivano da init.sql.gz con id ESPLICITI, che NON avanzano la
            // sequence IDENTITY. Senza questo, il primo INSERT via EF (aggiungi linea/
            // punto/poligono) genera un id già esistente → "23505 duplicate key" e una
            // developer-exception-page enorme finiva dritta nel toast del frontend.
            // Idempotente: ad ogni avvio porta la sequence a max(id)+1 (o a 1 se vuota).
            try
            {
                foreach (var table in new[] { "gis_lines", "gis_points", "gis_polygons" })
                {
                    await db.Database.ExecuteSqlRawAsync(
                        $@"SELECT setval(
                                pg_get_serial_sequence('{table}', 'id'),
                                GREATEST(COALESCE((SELECT max(id) FROM {table}), 1), 1),
                                (SELECT count(*) > 0 FROM {table})
                           );");
                }
                Console.WriteLine("Sequenze IDENTITY riallineate (gis_lines/points/polygons).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Sequenze IDENTITY: errore riallineamento - {ex.Message}");
            }

            // Reimporta i sentieri OSM se assenti (sopravvive a riavvii senza docker down -v).
            // z_osm_trails.sql.gz è in db-init/ ed è anche nel db-init del repo per i fresh init.
            try
            {
                var conn = db.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
                using var checkCmd = conn.CreateCommand();
                checkCmd.CommandText = "SELECT COUNT(1) FROM gis_lines WHERE name LIKE 'OSM_%'";
                var osmCount = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                if (osmCount == 0)
                {
                    // Cerca il gz relativo al progetto (due livelli su dalla ContentRoot)
                    var gzPath = Path.GetFullPath(
                        Path.Combine(builder.Environment.ContentRootPath, "..", "..", "db-init", "z_osm_trails.sql.gz"));
                    if (!File.Exists(gzPath))
                        gzPath = Path.Combine(builder.Environment.ContentRootPath, "db-init", "z_osm_trails.sql.gz");
                    if (File.Exists(gzPath))
                    {
                        string osmSql;
                        using (var fs = File.OpenRead(gzPath))
                        using (var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress))
                        using (var sr = new StreamReader(gz, System.Text.Encoding.UTF8))
                            osmSql = await sr.ReadToEndAsync();
                        await db.Database.ExecuteSqlRawAsync(osmSql);
                        Console.WriteLine("OSM trails: reimportati (erano assenti).");
                    }
                    else
                    {
                        Console.WriteLine($"OSM trails: z_osm_trails.sql.gz non trovato in {gzPath}, skip.");
                    }
                }
                else
                {
                    Console.WriteLine($"OSM trails: presenti ({osmCount}), skip reimport.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OSM trails: errore reimport - {ex.Message}");
            }

            // Esegue setup_dynamic_routing.sql: crea/aggiorna refresh_routing_topology()
            // e fa partire il primo refresh (snap geometrie + bridge componenti isolate).
            // Idempotente: può essere rieseguito ad ogni avvio senza danni.
            try
            {
                var sqlPath = Path.Combine(builder.Environment.ContentRootPath, "setup_dynamic_routing.sql");
                if (File.Exists(sqlPath))
                {
                    var sql = await File.ReadAllTextAsync(sqlPath);
                    await db.Database.ExecuteSqlRawAsync(sql);
                    Console.WriteLine("Routing: setup topologia eseguito (sentieri snappati + componenti unite).");
                }
                else
                {
                    Console.WriteLine($"Routing: setup SQL non trovato in {sqlPath}, skip.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Routing: errore setup topologia - {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine("Database: impossibile connettersi al database.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Database: errore durante il test di connessione - {ex.Message}");
    }
}

app.Run();
