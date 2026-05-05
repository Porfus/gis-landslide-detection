using System.Text.Json;
using System.Text.Json.Serialization;
using it.gis_landslide_detection.web.Data;
using it.gis_landslide_detection.web.Services;
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
    client.Timeout = TimeSpan.FromSeconds(5);
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
        if (db.Database.CanConnect())
        {
            Console.WriteLine("Supabase: connessione OK");
        }
        else
        {
            Console.WriteLine("Supabase: impossibile connettersi al database.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Supabase: errore durante il test di connessione - {ex.Message}");
    }
}

app.Run();
