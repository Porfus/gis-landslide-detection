// Controllers/TrailsController.cs
using it.gis_landslide_detection.web.Data;
using it.gis_landslide_detection.web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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



        public TrailsController(ApplicationDbContext context, IIffiService iffiService, ISentinelService sentinelService, IWeatherService weatherService)
        {
            _context = context;
            _iffi = iffiService;
            _sentinel = sentinelService;
            _weather = weatherService;

        }

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
            int precipScore = weather?.PrecipitationScore ?? 85;
            double precipMmh = weather?.PrecipitationMmh ?? 47.0;
            string meteoSrc = weather?.Source ?? "fallback";
            double pastPrecipitationMm = weather?.PastPrecipitationMm ?? 60.0;

            // Calcolo score pesato
            double histScore = iffiResult.HazardScore;
            double riskScore = (histScore * 0.40)
                             + (soilScore * 0.35)
                             + (precipScore * 0.25);

            string level = riskScore switch
            {
                >= 70 => "CRITICAL",
                >= 40 => "MEDIUM",
                _ => "LOW"
            };

            // 5. Risposta completa
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
                                   : level == "MEDIUM"
                                       ? "Percorrere con cautela."
                                       : "Sentiero sicuro.",
                // Punto critico da mostrare sulla mappa
                CriticalPointLat = iffiResult.HasRisk ? queryLat : (double?)null,
                CriticalPointLng = iffiResult.HasRisk ? queryLng : (double?)null,
                // Componente 1 — IFFI
                HistoricalRisk = iffiResult.HasRisk,
                IffiTipo = iffiResult.IffiTipo,
                IffiZoneCount = iffiResult.ZoneCount,
                HistoricalScore = (int)histScore,
                // Componente 2 — Sentinel
                SoilMoisture = soilScore,
                VvMeanDb = vvDb,
                SentinelSource = sentinel?.Fonte ?? "fallback",
                // Componente 3 — Meteo
                Precipitation = precipScore,
                PrecipitationMmh = precipMmh,
                WeatherSource = meteoSrc,
                Past3DaysPrecipitationMm = pastPrecipitationMm
            });
        }

    }
}