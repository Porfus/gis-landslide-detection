// Controllers/TrailsController.cs
using it.gis_landslide_detection.web.Data;
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

        public TrailsController(ApplicationDbContext context)
        {
            _context = context;
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
        // Placeholder — il Tech Lead completerà questo metodo
        [HttpGet("{id}/risk")]
        public IActionResult GetRisk(long id)
        {
            return Ok(new { trailId = id, status = "coming soon" });
        }
    }
}