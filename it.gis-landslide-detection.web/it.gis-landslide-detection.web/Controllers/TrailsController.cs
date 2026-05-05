// Controllers/TrailsController.cs
using it.gis_landslide_detection.web.Data;
using it.gis_landslide_detection.web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetTopologySuite.IO;

namespace it.gis_landslide_detection.web.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TrailsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IIffiService _iffi;
        private readonly ISentinelService _sentinel;
        private readonly IWeatherService _weather;
        private readonly IHazardScoreEngine _hazardEngine;
        private readonly ILogger<TrailsController> _logger;

        public TrailsController(ApplicationDbContext context, IIffiService iffiService, ISentinelService sentinelService, IWeatherService weatherService, IHazardScoreEngine hazardEngine, ILogger<TrailsController> logger)
        {
            _context = context;
            _iffi = iffiService;
            _sentinel = sentinelService;
            _weather = weatherService;
            _hazardEngine = hazardEngine;
            _logger = logger;
        }

        /// <summary>
        /// Sanitizes a double value: replaces NaN and Infinity with a fallback (default 0).
        /// This prevents System.Text.Json serialization failures.
        /// </summary>
        private static double Safe(double value, double fallback = 0.0)
            => double.IsFinite(value) ? value : fallback;

        // GET /api/trails
        // Restituisce tutti i trail come array JSON con id, name e geom (GeoJSON)
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var trails = await _context.HikingTrails
                .Select(t => new {
                    t.Id,
                    t.Name,
                    t.SacScale,
                    // Serializza la geometria come stringa GeoJSON
                    Geom = t.Geom != null
                        ? new GeoJsonWriter().Write(t.Geom)
                        : null
                })
                .ToListAsync();

            return Ok(trails);
        }

        // GET /api/trails/{id}/hazard
        [HttpGet("{id}/hazard")]
        public async Task<IActionResult> GetHazard(long id)
        {
          try
          {
            // get punto critico lungo il trail
            var iffiResult = await _iffi.GetTrailHazardAsync(id);
            if (iffiResult == null)
                return NotFound(new { error = $"Trail {id} non trovato." });

            // Usa le coordinate del punto critico o del trail calcolate dal hazard calculator per gli altri service
            double queryLat = iffiResult.ReferenceLat;
            double queryLng = iffiResult.ReferenceLng;

            // Chiama Sentinel e Weather per il punto critico
            var sentinel = await _sentinel
                .GetSoilMoistureForPointAsync(queryLat, queryLng);
            var weather = await _weather
                .GetCurrentPrecipitationAsync(queryLat, queryLng);

            // Valori con fallback
            bool sentinelUnavailable = sentinel == null;
            int soilScore = sentinel?.SoilMoistureScore ?? 0;
            double vvDb = sentinel?.VvMeanDb ?? -20.0; // Default a secco invece di 0 (che per il SAR significa saturo)
            string sentinelSrc = sentinel?.Fonte ?? "Dati non disponibili";
            
            bool weatherDataUnavailable = weather == null;

            int apiScore = weather?.ApiScore ?? 0;
            double apiMm = weather?.AntecedentPrecipIndex ?? 0;
            int currentRainScore = weather?.CurrentRainScore ?? 0;
            double precipMmh = weather?.PrecipitationMmh ?? 0;
            string meteoSrc = weather?.Source ?? "fallback";

            // --- Calcolo pericolosità tramite HazardScoreEngine (R1/R2/R3 inclusi) ---
            double histScore = iffiResult.HazardScore;
            var hazard = _hazardEngine.Calculate(
                iffiHazardScore:  histScore,
                iffiTipo:         iffiResult.IffiTipo,
                soilMoistureScore: soilScore,
                apiScore:         apiScore,
                currentRainScore: currentRainScore,
                precipMmh:        precipMmh,
                weatherDataUnavailable: weatherDataUnavailable
            );

            double hazardScore = Safe(hazard.HazardScore);
            histScore = Safe(histScore);

            return Ok(new
            {
                // Trail
                TrailId = id,
                TrailName = iffiResult.TrailName,
                // Score finale
                HazardScore = (int)hazardScore,
                HazardLevel = hazard.HazardLevel,
                Message = hazard.HazardLevel switch
                {
                    "CRITICAL" => "Sentiero bloccato: pericolosità frana critica.",
                    "HIGH"     => "Sconsigliato: elevata probabilità di instabilità.",
                    "MEDIUM"   => "Percorrere con cautela.",
                    _          => "Sentiero sicuro."
                },
                // Punto critico da mostrare sulla mappa
                CriticalPointLat = iffiResult.HasHazard ? Safe(queryLat) : (double?)null,
                CriticalPointLng = iffiResult.HasHazard ? Safe(queryLng) : (double?)null,
                
                // Componenti diagnostiche
                Components = new 
                {
                    Iffi = new { Score = (int)Safe(histScore), Weight = 0.35, Tipo = iffiResult.IffiTipo, ZoneCount = iffiResult.ZoneCount },
                    SoilMoisture = new { 
                        Unavailable = sentinelUnavailable, 
                        Score = soilScore, 
                        Weight = Safe(Math.Round(hazard.WSoil, 4)), 
                        VvDb = Safe(Math.Round(vvDb, 2)), 
                        Source = sentinelSrc 
                    },
                    AntecedentPrecip = new { Score = apiScore, Weight = Safe(Math.Round(hazard.WApi, 4)), ApiMm = Safe(Math.Round(apiMm, 2)), Days = 7, DecayK = 0.85 },
                    CurrentRain = new { Score = currentRainScore, Weight = Safe(Math.Round(hazard.WRain, 4)), Mmh = Safe(Math.Round(precipMmh, 2)), Source = meteoSrc },
                    // Diagnostica formula
                    SaturationIndex = Safe(Math.Round(hazard.SaturationIndex, 2)),
                    TriggerMultiplier = Safe(Math.Round(hazard.TriggerMultiplier, 4)),
                    BaseHazard = Safe(hazard.BaseHazard),
                    FlashOverrideApplied = hazard.FlashOverrideApplied,
                    SaturationFloorApplied = hazard.SaturationFloorApplied,
                    WeatherDataUnavailable = hazard.WeatherDataUnavailable
                }
            });
          }
          catch (Exception ex)
          {
              _logger.LogError(ex, "Errore nel calcolo della pericolosità per trail {TrailId}", id);
              return StatusCode(500, new { error = $"Errore interno nel calcolo della pericolosità per il trail {id}.", detail = ex.Message });
          }
        }

    }
}