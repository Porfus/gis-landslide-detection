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
        private readonly ILogger<TrailsController> _logger;

        public TrailsController(ApplicationDbContext context, IIffiService iffiService, ISentinelService sentinelService, IWeatherService weatherService, ILogger<TrailsController> logger)
        {
            _context = context;
            _iffi = iffiService;
            _sentinel = sentinelService;
            _weather = weatherService;
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
            int soilScore = sentinel?.SoilMoistureScore ?? 75;
            double vvDb = sentinel?.VvMeanDb ?? -15.0;
            string sentinelSrc = sentinel?.Fonte ?? "fallback";
            
            int apiScore = weather?.ApiScore ?? 85;
            double apiMm = weather?.AntecedentPrecipIndex ?? 68.3;
            int currentRainScore = weather?.CurrentRainScore ?? 60;
            double precipMmh = weather?.PrecipitationMmh ?? 18.0;
            string meteoSrc = weather?.Source ?? "fallback";

            // --- Pesi Dinamici in base al tipo Geofisico ---
            double wSoil = 0.40;
            double wApi = 0.35;
            double wRain = 0.25;

            var tipo = iffiResult.IffiTipo;
            if (tipo == "Crollo/Ribaltamento")
            {
                // Roccia: non si imbeve come il terreno. Conta la pioggia istantanea (fessurazione/pressione idrostatica)
                wSoil = 0.10;
                wApi = 0.20;
                wRain = 0.70;
            }
            else if (tipo == "Scivolamento rotazionale/traslativo" || tipo == "Colamento rapido")
            {
                // Terreno/Fango: saturazione e pioggia passata sono i trigger primari
                wSoil = 0.45;
                wApi = 0.40;
                wRain = 0.15;
            }

            // --- Indice di Saturazione Combinato ---
            double saturationIndex = (soilScore * wSoil) + (apiScore * wApi) + (currentRainScore * wRain);

            // --- Rischio Finale ---
            double histScore = iffiResult.HazardScore;
            double riskScore = (histScore * 0.35) + (saturationIndex * 0.65);

            string level = riskScore switch
            {
                >= 75 => "CRITICAL",
                >= 50 => "HIGH",
                >= 30 => "MEDIUM",
                _ => "LOW"
            };

            // 5. Risposta completa — sanitize di tutti i double per evitare NaN/Infinity nel JSON
            riskScore = Safe(riskScore);
            histScore = Safe(histScore);

            return Ok(new
            {
                // Trail
                TrailId = id,
                TrailName = iffiResult.TrailName,
                // Score finale
                RiskScore = (int)riskScore,
                RiskLevel = level,
                Message = level == "CRITICAL"
                                   ? "Sentiero bloccato: rischio frana critico."
                                   : level == "HIGH"
                                       ? "Sconsigliato: elevata probabilità di instabilità."
                                       : level == "MEDIUM"
                                           ? "Percorrere con cautela."
                                           : "Sentiero sicuro.",
                // Punto critico da mostrare sulla mappa
                CriticalPointLat = iffiResult.HasRisk ? Safe(queryLat) : (double?)null,
                CriticalPointLng = iffiResult.HasRisk ? Safe(queryLng) : (double?)null,
                
                // Nuove componenti diagnostiche
                Components = new 
                {
                    Iffi = new { Score = (int)Safe(histScore), Weight = 0.35, Tipo = iffiResult.IffiTipo, ZoneCount = iffiResult.ZoneCount },
                    SoilMoisture = new { Score = soilScore, Weight = Safe(Math.Round(wSoil * 0.65, 4)), VvDb = Safe(Math.Round(vvDb, 2)), Source = sentinelSrc },
                    AntecedentPrecip = new { Score = apiScore, Weight = Safe(Math.Round(wApi * 0.65, 4)), ApiMm = Safe(Math.Round(apiMm, 2)), Days = 7, DecayK = 0.85 },
                    CurrentRain = new { Score = currentRainScore, Weight = Safe(Math.Round(wRain * 0.65, 4)), Mmh = Safe(Math.Round(precipMmh, 2)), Source = meteoSrc }
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