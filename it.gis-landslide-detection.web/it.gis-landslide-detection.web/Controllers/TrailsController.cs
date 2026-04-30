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
        private readonly IRiskScoreEngine _riskEngine;
        private readonly ILogger<TrailsController> _logger;

        public TrailsController(ApplicationDbContext context, IIffiService iffiService, ISentinelService sentinelService, IWeatherService weatherService, IRiskScoreEngine riskEngine, ILogger<TrailsController> logger)
        {
            _context = context;
            _iffi = iffiService;
            _sentinel = sentinelService;
            _weather = weatherService;
            _riskEngine = riskEngine;
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

        // GET /api/trails/{id}/risk
        [HttpGet("{id}/risk")]
        public async Task<IActionResult> GetRisk(long id)
        {
          try
          {
            // get punto critico lungo il trail
            var iffiResult = await _iffi.GetTrailRiskAsync(id);
            if (iffiResult == null)
                return NotFound(new { error = $"Trail {id} non trovato." });

            // Usa le coordinate del punto critico o del trail calcolate dal risk calculator per gli altri service
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

            // --- Calcolo rischio tramite RiskScoreEngine (R1/R2/R3 inclusi) ---
            double histScore = iffiResult.HazardScore;
            var risk = _riskEngine.Calculate(
                iffiHazardScore:  histScore,
                iffiTipo:         iffiResult.IffiTipo,
                soilMoistureScore: soilScore,
                apiScore:         apiScore,
                currentRainScore: currentRainScore,
                precipMmh:        precipMmh,
                weatherDataUnavailable: weatherDataUnavailable
            );

            double riskScore = Safe(risk.RiskScore);
            histScore = Safe(histScore);

            return Ok(new
            {
                // Trail
                TrailId = id,
                TrailName = iffiResult.TrailName,
                // Score finale
                RiskScore = (int)riskScore,
                RiskLevel = risk.RiskLevel,
                Message = risk.RiskLevel switch
                {
                    "CRITICAL" => "Sentiero bloccato: rischio frana critico.",
                    "HIGH"     => "Sconsigliato: elevata probabilità di instabilità.",
                    "MEDIUM"   => "Percorrere con cautela.",
                    _          => "Sentiero sicuro."
                },
                // Punto critico da mostrare sulla mappa
                CriticalPointLat = iffiResult.HasRisk ? Safe(queryLat) : (double?)null,
                CriticalPointLng = iffiResult.HasRisk ? Safe(queryLng) : (double?)null,
                
                // Componenti diagnostiche
                Components = new 
                {
                    Iffi = new { Score = (int)Safe(histScore), Weight = 0.35, Tipo = iffiResult.IffiTipo, ZoneCount = iffiResult.ZoneCount },
                    SoilMoisture = new { 
                        Unavailable = sentinelUnavailable, 
                        Score = soilScore, 
                        Weight = Safe(Math.Round(risk.WSoil, 4)), 
                        VvDb = Safe(Math.Round(vvDb, 2)), 
                        Source = sentinelSrc 
                    },
                    AntecedentPrecip = new { Score = apiScore, Weight = Safe(Math.Round(risk.WApi, 4)), ApiMm = Safe(Math.Round(apiMm, 2)), Days = 7, DecayK = 0.85 },
                    CurrentRain = new { Score = currentRainScore, Weight = Safe(Math.Round(risk.WRain, 4)), Mmh = Safe(Math.Round(precipMmh, 2)), Source = meteoSrc },
                    // Diagnostica formula
                    SaturationIndex = Safe(Math.Round(risk.SaturationIndex, 2)),
                    TriggerMultiplier = Safe(Math.Round(risk.TriggerMultiplier, 4)),
                    BaseHazard = Safe(risk.BaseHazard),
                    FlashOverrideApplied = risk.FlashOverrideApplied,
                    SaturationFloorApplied = risk.SaturationFloorApplied,
                    WeatherDataUnavailable = risk.WeatherDataUnavailable
                }
            });
          }
          catch (Exception ex)
          {
              _logger.LogError(ex, "Errore nel calcolo del rischio per trail {TrailId}", id);
              return StatusCode(500, new { error = $"Errore interno nel calcolo del rischio per il trail {id}.", detail = ex.Message });
          }
        }

    }
}