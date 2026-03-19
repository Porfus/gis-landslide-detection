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
    public IActionResult Get([FromQuery] double lat, [FromQuery] double lng)
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
}