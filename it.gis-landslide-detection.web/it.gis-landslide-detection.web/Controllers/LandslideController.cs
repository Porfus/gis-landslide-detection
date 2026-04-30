using it.gis_landslide_detection.web.Data;
using it.gis_landslide_detection.web.Models;
using it.gis_landslide_detection.web.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace it.gis_landslide_detection.web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LandslideController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ISentinelService _sentinelService;
    private readonly IWeatherService _weatherService;
    private readonly IIffiService _iffiService;

    public LandslideController(ApplicationDbContext context, ISentinelService sentinelService, IWeatherService weatherService, IIffiService iffiService)
    {
        _context = context;
        _sentinelService = sentinelService;
        _weatherService = weatherService;
        _iffiService = iffiService;
    }

    /**[HttpGet]
    public IActionResult OLDGet([FromQuery] double lat, [FromQuery] double lng)
    {
        // Valori hardcoded per Lame Rosse, caso studio luglio 2024
        var response = new Response(
            lat,
            lng,
            87,
            "CRITICAL",
            82,
            85,
            true,
            "Sentiero bloccato: rischio frana critico rilevato."
        );
        return Ok(response);

    }**/

    //MOCK ONLY
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] double lat, [FromQuery] double lng)
    {
        var factory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(4326);
        var punto    = factory.CreatePoint(new NetTopologySuite.Geometries.Coordinate(lng, lat));
        

        // VERIFICA REALE SUL DATABASE IFFI
        bool historicalRisk = false;
        string iffiTipo = "Nessun rischio rilevato";
        double scoreStorico = 0.0;

        try 
        {
            var iffiZone = await _iffiService.GetZoneAsync(lat, lng);
            if (iffiZone != null)
            {
                historicalRisk = true;
                iffiTipo = iffiZone.NomeTipo ?? "Sconosciuto";
                scoreStorico = iffiTipo switch
                {
                    "Colamento rapido" => 100.0,
                    "Crollo/Ribaltamento" => 80.0,
                    "Scivolamento rotazionale/traslativo" => 60.0,
                    "Complesso" => 40.0,
                    _ => 20.0
                };
            }
        } 
        catch (Exception)
        {
            // Fallback gracefully only if there is a DB connection issue
            iffiTipo = "Errore Connessione DB (N/A)";
        }

        // sentinel (valori hardcoded se non presenti nel json)
        var sentinel      = await _sentinelService.GetSoilMoistureForPointAsync(lat, lng);
        int  soilScore    = sentinel?.SoilMoistureScore ?? 75;
        double vvDb       = sentinel?.VvMeanDb          ?? -15.0;
        double delta      = sentinel?.DeltaScore        ?? 0;
        string sentinelSource = sentinel?.Fonte         ?? "Assente/Fallback Base";

        var weather       = await _weatherService.GetCurrentPrecipitationAsync(lat, lng);
        double precipMmh  = weather?.PrecipitationMmh ?? 47.0;
        int precipitation = weather?.CurrentRainScore ?? 85;
        int currentRainScore = weather?.CurrentRainScore ?? 60;
        int apiScore      = weather?.ApiScore ?? 85;

        // CALCOLO AVANZATO CON PESI DINAMICI
        double wSoil = 0.40;
        double wApi = 0.35;
        double wRain = 0.25;

        if (iffiTipo == "Crollo/Ribaltamento")
        {
            wSoil = 0.10;
            wApi = 0.20;
            wRain = 0.70;
        }
        else if (iffiTipo == "Scivolamento rotazionale/traslativo" || iffiTipo == "Colamento rapido")
        {
            wSoil = 0.45;
            wApi = 0.40;
            wRain = 0.15;
        }

        double saturationIndex = (soilScore * wSoil) + (apiScore * wApi) + (currentRainScore * wRain);

        double riskScore = (scoreStorico * 0.35) + (saturationIndex * 0.65);

        string riskLevel = riskScore switch {
            >= 75 => "CRITICAL",
            >= 50 => "HIGH",
            >= 30 => "MEDIUM",
            _     => "LOW"
        };
 
        return Ok(new Response(
            lat: lat,
            lng: lng,
            riskScore: (int)riskScore,
            riskLevel: riskLevel,
            message: riskLevel switch {
                "CRITICAL" => "⚠️ EMERGENZA: Sentiero chiuso. Rischio frana altissimo.",
                "HIGH"     => "🚩 PERICOLO: Escursione sconsigliata. Suolo instabile.",
                "MEDIUM"   => "🔸 ATTENZIONE: Percorribile con cautela. Possibili detriti sul sentiero.",
                "LOW"      => "✅ SICURO: Condizioni ottimali. Goditi l'escursione!",
                _          => "Dati non disponibili."
            },
            historicalRisk: historicalRisk,
            iffiLevel: iffiTipo, 
            historicalScore: (int)scoreStorico,
            soilMoisture: soilScore,
            vvMeanDb: vvDb,
            deltaScore: delta,
            sentinelSource: sentinelSource,
            precipitation: precipitation,
            precipitationMmh: precipMmh
        ));
    }
}