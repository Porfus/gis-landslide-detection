using it.gis_landslide_detection.web.Data;
using it.gis_landslide_detection.web.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace it.gis_landslide_detection.web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LandslideController : Controller
{
    private readonly ApplicationDbContext _context;

    public LandslideController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
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

    }

    //MOCK ONLY
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] double lat, [FromQuery] double lng)
    {
        var factory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(4326);
        var punto    = factory.CreatePoint(new NetTopologySuite.Geometries.Coordinate(lng, lat));
        
        //HARDCODED
        bool historicalRisk = (lat >= 43.095 && lat <= 43.101 &&
                               lng >= 12.997 && lng <= 13.008);
        double scoreStorico = historicalRisk ? 100.0 : 0.0;

        int soilMoisture = 75;
        
        int precipitation = 85;
        
        //END HARCODED
        
        var jsonPath = Path.Combine("wwwroot", "data", "soil_moisture_risultati.json");
        if (System.IO.File.Exists(jsonPath))
        {
            var json   = await System.IO.File.ReadAllTextAsync(jsonPath);
            var parsed = System.Text.Json.JsonDocument.Parse(json);
            soilMoisture = parsed.RootElement.GetProperty("soil_moisture_score").GetInt32();
        }

        //Mock calculation
        double riskScore = (scoreStorico  * 0.40) +
                           (soilMoisture  * 0.35) +
                           (precipitation * 0.25);
        string riskLevel = riskScore switch {
            >= 70 => "CRITICAL",
            >= 40 => "MEDIUM",
            _     => "LOW"
        };
 
        return Ok(new Response(
            lat,
            lng,
            (int)riskScore,
            riskLevel, 
            soilMoisture,
            precipitation,
            historicalRisk,
            riskLevel == "CRITICAL"
                ? "Sentiero bloccato: rischio frana critico rilevato."
                : "Sentiero percorribile con cautela."
        ));
    }
}