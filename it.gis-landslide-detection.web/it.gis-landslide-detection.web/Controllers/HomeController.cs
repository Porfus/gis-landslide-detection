using System.Diagnostics;
using it.gis_landslide_detection.web.Data;
using it.gis_landslide_detection.web.Models;
using it.gis_landslide_detection.web.Services;
using Microsoft.AspNetCore.Mvc;

namespace it.gis_landslide_detection.web.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        
        private readonly ApplicationDbContext _context;
        private readonly IWeatherService _weatherService;
        public HomeController(ILogger<HomeController> logger, ApplicationDbContext context, IWeatherService weatherService)
        {
            _logger = logger;
            _context = context;
            _weatherService = weatherService;
        }

        //public IActionResult Index()
        //{
        //    var count = _context.HikingPoints.Count();
        //    Console.WriteLine($"Punti nel DB: {count}");
        //    return View();
        //}


        public async Task<IActionResult> Index()
        {
            var weather = await _weatherService
                .GetCurrentPrecipitationAsync(40.72384970631184, -74.01526921338605);
            Console.WriteLine($"Meteo: {weather?.PrecipitationMmh} mm/h " +
                              $"(score {weather?.CurrentRainScore}) " +
                              $"da {weather?.Source}");
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
