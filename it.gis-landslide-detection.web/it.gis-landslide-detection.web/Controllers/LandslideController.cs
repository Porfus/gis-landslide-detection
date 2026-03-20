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

    public LandslideController(ApplicationDbContext context, ISentinelService sentinelService)
    {
        _context = context;
        _sentinelService = sentinelService;
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
        

        
        //HARDCODED rik and message
        bool historicalRisk = (lat >= 43.095 && lat <= 43.101 &&
                               lng >= 12.997 && lng <= 13.008);
        double scoreStorico = historicalRisk ? 100.0 : 0.0;

        // sentinel (valori hardcoded se non presenti nel json)
        var sentinel      = await _sentinelService.GetSoilMoistureAsync();
        int  soilScore    = sentinel?.SoilMoistureScore ?? 75;
        double vvDb       = sentinel?.VvMeanDb          ?? -15.0;
        double delta      = sentinel?.DeltaScore        ?? 0;

        double precipMmh  = 47.0;
        int precipitation = 85;
        

        //Mock calculation
        double riskScore = (scoreStorico  * 0.40) +
                           (soilScore  * 0.35) +
                           (precipitation * 0.25);
        string riskLevel = riskScore switch {
            >= 70 => "CRITICAL",
            >= 40 => "MEDIUM",
            _     => "LOW"
        };
 
        return Ok(new Response(
            lat: lat,
            lng: lng,
            riskScore: (int)riskScore,
            riskLevel: riskLevel,
            message: riskLevel == "CRITICAL"
                ? "Sentiero bloccato: rischio frana critico."
                : "Percorribile con cautela.",
            historicalRisk: historicalRisk,
            iffiLevel: "N/A", 
            historicalScore: (int)scoreStorico,
            soilMoisture: soilScore,
            vvMeanDb: vvDb,
            deltaScore: delta,
            precipitation: precipitation,
            precipitationMmh: precipMmh
        ));
    }
}