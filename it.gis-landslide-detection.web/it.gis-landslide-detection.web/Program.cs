using it.gis_landslide_detection.web.Data;
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
builder.Services.AddControllersWithViews();
builder.Services.AddCors(options =>
{
    options.AddPolicy("DevPolicy", policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});


var app = builder.Build();
app.UseCors("DevPolicy");
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

app.UseAuthorization();

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
