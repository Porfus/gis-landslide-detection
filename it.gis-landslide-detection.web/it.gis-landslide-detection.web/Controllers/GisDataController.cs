using it.gis_landslide_detection.web.Data;
using it.gis_landslide_detection.web.Helpers;
using it.gis_landslide_detection.web.Models;
using it.gis_landslide_detection.web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace it.gis_landslide_detection.web.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class GisDataController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IRoutingService _routingService;
        private readonly ITspService _tspService;
        private readonly GeometryFactory _geometryFactory;

        public GisDataController(ApplicationDbContext context, IRoutingService routingService, ITspService tspService)
        {
            _context = context;
            _routingService = routingService;
            _tspService = tspService;
            _geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);
        }

        // ==========================================
        // 1. READ / QUERY ENDPOINTS (GEOJSON OUTPUT)
        // ==========================================

        // GET: api/GisData/points
        [HttpGet("points")]
        public async Task<IActionResult> GetPoints([FromQuery] string? type)
        {
            var query = _context.GisPoints.AsQueryable();
            if (!string.IsNullOrEmpty(type))
            {
                query = query.Where(p => p.Type == type);
            }

            var points = await query.ToListAsync();
            var features = GeoJsonFormatter.ToFeatureCollection(points, 
                p => p.Geom, 
                p => new Dictionary<string, object> { { "id", p.Id }, { "name", p.Name ?? "" }, { "type", p.Type ?? "" } });

            var geoJson = GeoJsonFormatter.Format(features);
            return Content(geoJson, "application/json");
        }

        // GET: api/GisData/lines
        [HttpGet("lines")]
        public async Task<IActionResult> GetLines([FromQuery] string? type)
        {
            // Esclude i bridge sintetici (__BRIDGE_AUTO__) dal rendering: esistono
            // solo nel grafo pgRouting come "scorciatoie" tra componenti isolate,
            // ma non sono sentieri reali e non devono apparire sulla mappa.
            var query = _context.GisLines.Where(l => l.Name != "__BRIDGE_AUTO__");
            if (!string.IsNullOrEmpty(type))
            {
                query = query.Where(l => l.Type == type);
            }

            var lines = await query.ToListAsync();
            var features = GeoJsonFormatter.ToFeatureCollection(lines, 
                l => l.Geom, 
                l => new Dictionary<string, object> { { "id", l.Id }, { "name", l.Name ?? "" }, { "type", l.Type ?? "" } });

            var geoJson = GeoJsonFormatter.Format(features);
            return Content(geoJson, "application/json");
        }

        // GET: api/GisData/polygons
        // minArea: superficie minima in m² (filtro spaziale su ST_Area)
        [HttpGet("polygons")]
        public async Task<IActionResult> GetPolygons(
            [FromQuery] int? minPopulation, 
            [FromQuery] double? minArea,
            [FromQuery] double? minLat,
            [FromQuery] double? minLng,
            [FromQuery] double? maxLat,
            [FromQuery] double? maxLng)
        {
            var query = _context.GisPolygons.AsQueryable();
            if (minPopulation.HasValue)
            {
                query = query.Where(p => p.Population >= minPopulation.Value);
            }

            // 1. Filtro spaziale Bounding Box (database-side)
            if (minLat.HasValue && minLng.HasValue && maxLat.HasValue && maxLng.HasValue)
            {
                var bbox = new Envelope(minLng.Value, maxLng.Value, minLat.Value, maxLat.Value);
                var searchArea = _geometryFactory.ToGeometry(bbox);
                query = query.Where(p => p.Geom != null && p.Geom.Intersects(searchArea));
            }
            else
            {
                // Limita di default all'area di studio (Camerino / Sibillini) per evitare di caricare 30k+ poligoni
                var bbox = new Envelope(12.9, 13.3, 42.9, 43.2);
                var searchArea = _geometryFactory.ToGeometry(bbox);
                query = query.Where(p => p.Geom != null && p.Geom.Intersects(searchArea));
            }

            // 2. Filtro per superficie (database-side)
            if (minArea.HasValue && minArea.Value > 0)
            {
                // Approssimazione: 1 grado ≈ 111320 m
                double minAreaDeg2 = minArea.Value / (111320.0 * 111320.0);
                query = query.Where(p => p.Geom != null && p.Geom.Area >= minAreaDeg2);
            }

            var polygons = await query.ToListAsync();

            var features = GeoJsonFormatter.ToFeatureCollection(polygons, 
                p => p.Geom, 
                p => new Dictionary<string, object> 
                { 
                    { "id", p.Id }, 
                    { "name", p.Name ?? "" }, 
                    { "population", p.Population },
                    // Espone anche riskLevel come alias semantico per il frontend
                    { "riskLevel", p.Population }
                });

            var geoJson = GeoJsonFormatter.Format(features);
            return Content(geoJson, "application/json");
        }

        // GET: api/GisData/nearest
        // type: filtro opzionale per tipologia POI (es. "Baita", "PuntoRistoro").
        // Senza type ritorna i `limit` punti più vicini di qualunque tipo;
        // con type filtra prima per tipologia e poi ordina per distanza.
        [HttpGet("nearest")]
        public async Task<IActionResult> GetNearestPoints([FromQuery] double lat, [FromQuery] double lng, [FromQuery] int limit = 5, [FromQuery] string? type = null)
        {
            var location = _geometryFactory.CreatePoint(new Coordinate(lng, lat));

            var query = _context.GisPoints.AsQueryable();
            if (!string.IsNullOrEmpty(type))
            {
                query = query.Where(p => p.Type == type);
            }

            var points = await query
                .OrderBy(p => p.Geom!.Distance(location))
                .Take(limit)
                .ToListAsync();

            var features = GeoJsonFormatter.ToFeatureCollection(points, 
                p => p.Geom, 
                p => new Dictionary<string, object> { { "id", p.Id }, { "name", p.Name ?? "" }, { "type", p.Type ?? "" } });

            var geoJson = GeoJsonFormatter.Format(features);
            return Content(geoJson, "application/json");
        }

        // POST: api/GisData/points/within (Spatial query — punti all'interno di un'area)
        [HttpPost("points/within")]
        public async Task<IActionResult> GetPointsWithin([FromBody] PolygonDto areaDto)
        {
            if (areaDto.Coordinates == null || areaDto.Coordinates.Count < 3)
            {
                return BadRequest("Geometria del poligono non valida.");
            }

            var shell = areaDto.Coordinates.Select(c => new Coordinate(c[0], c[1])).ToArray();
            var searchPolygon = _geometryFactory.CreatePolygon(shell);

            var pointsQuery = _context.GisPoints.Where(p => p.Geom!.Within(searchPolygon));
            if (!string.IsNullOrEmpty(areaDto.Type) && areaDto.Type != "Mostra tutto")
            {
                pointsQuery = pointsQuery.Where(p => p.Type == areaDto.Type);
            }
            var points = await pointsQuery.ToListAsync();

            var features = GeoJsonFormatter.ToFeatureCollection(points, 
                p => p.Geom, 
                p => new Dictionary<string, object> { { "id", p.Id }, { "name", p.Name ?? "" }, { "type", p.Type ?? "" } });

            var geoJson = GeoJsonFormatter.Format(features);
            return Content(geoJson, "application/json");
        }

        // POST: api/GisData/intersections (Spatial query — sentieri che intersecano un'area disegnata)
        [HttpPost("intersections")]
        public async Task<IActionResult> GetIntersectingTrails([FromBody] PolygonDto areaDto)
        {
            if (areaDto.Coordinates == null || areaDto.Coordinates.Count < 3)
            {
                return BadRequest("Geometria del poligono non valida.");
            }

            var shell = areaDto.Coordinates.Select(c => new Coordinate(c[0], c[1])).ToArray();
            var searchPolygon = _geometryFactory.CreatePolygon(shell);

            // Trova tutti i sentieri (e opzionalmente fiumi/torrenti) che intersecano l'area.
            // Esclude i bridge sintetici (vedi commento in GetLines).
            var linesQuery = _context.GisLines.Where(l => l.Geom != null && l.Name != "__BRIDGE_AUTO__" && l.Geom.Intersects(searchPolygon));
            
            // Se specificato un tipo, filtra anche per tipo
            if (!string.IsNullOrEmpty(areaDto.Type) && areaDto.Type != "Mostra tutto")
            {
                linesQuery = linesQuery.Where(l => l.Type == areaDto.Type);
            }

            var lines = await linesQuery.ToListAsync();

            var features = GeoJsonFormatter.ToFeatureCollection(lines, 
                l => l.Geom, 
                l => new Dictionary<string, object> { { "id", l.Id }, { "name", l.Name ?? "" }, { "type", l.Type ?? "" } });

            var geoJson = GeoJsonFormatter.Format(features);
            return Content(geoJson, "application/json");
        }

        // GET: api/GisData/route (Dijkstra Routing su sentieri)
        [HttpGet("route")]
        public async Task<IActionResult> GetRoute([FromQuery] double startLat, [FromQuery] double startLng, [FromQuery] double endLat, [FromQuery] double endLng)
        {
            var start = new Coordinate(startLng, startLat);
            var end = new Coordinate(endLng, endLat);

            var result = await _routingService.CalculateShortestPathAsync(start, end);
            if (result == null || result.Path == null)
            {
                return NotFound("Nessun percorso trovato tra i punti selezionati.");
            }

            var feature = GeoJsonFormatter.ToFeature(result.Path,
                g => g,
                g => new Dictionary<string, object>
                {
                    { "name", "Percorso più breve" },
                    { "length_deg", result.Path.Length },
                    { "length_m", result.RouteDistanceM },
                    { "used_bridge", result.UsedBridge },
                    { "snappedStartLat", result.SnappedStart!.Y },
                    { "snappedStartLng", result.SnappedStart!.X },
                    { "snappedEndLat", result.SnappedEnd!.Y },
                    { "snappedEndLng", result.SnappedEnd!.X }
                });

            if (feature == null)
            {
                return NotFound("Errore durante la creazione della feature geografica.");
            }

            var geoJson = GeoJsonFormatter.Format(feature);
            return Content(geoJson, "application/json");
        }

        // POST: api/GisData/tsp (Traveling Salesman — tour ottimale tra N POI)
        [HttpPost("tsp")]
        public async Task<IActionResult> GetTspTour([FromBody] TspRequestDto dto)
        {
            if (dto?.Points == null || dto.Points.Count < 2)
            {
                return BadRequest("Servono almeno 2 punti per il TSP.");
            }
            if (dto.Points.Count > 12)
            {
                return BadRequest("Massimo 12 punti supportati (complessità computazionale).");
            }

            var pois = dto.Points.Select(p => new Coordinate(p[0], p[1])).ToList();
            var result = await _tspService.SolveAsync(pois, dto.Closed);
            if (result?.FullPath == null)
            {
                return NotFound("Impossibile calcolare il tour TSP (nessun percorso trovato fra i POI).");
            }

            var orderedLngLat = result.OrderedPoints.Select(c => new[] { c.X, c.Y }).ToList();
            var feature = GeoJsonFormatter.ToFeature(result.FullPath,
                g => g,
                g => new Dictionary<string, object>
                {
                    { "name", "Tour TSP" },
                    { "length_deg", result.TotalLengthDeg },
                    { "visitOrder", result.VisitOrder },
                    { "orderedPoints", orderedLngLat },
                    { "closed", dto.Closed }
                });

            if (feature == null) return NotFound("Errore creazione feature TSP.");
            var geoJson = GeoJsonFormatter.Format(feature);
            return Content(geoJson, "application/json");
        }

        // ==========================================
        // 2. CRUD OPERATIONS (WRITE/EDIT/DELETE)
        // ==========================================

        // POST: api/GisData/points
        [HttpPost("points")]
        public async Task<ActionResult> CreatePoint([FromBody] PointDto dto)
        {
            var point = new GisPoint
            {
                Name = dto.Name,
                Type = dto.Type,
                Geom = _geometryFactory.CreatePoint(new Coordinate(dto.Lng, dto.Lat))
            };

            _context.GisPoints.Add(point);
            await _context.SaveChangesAsync();

            return Ok(new { id = point.Id, message = "Punto aggiunto correttamente!" });
        }

        // PUT: api/GisData/points/{id}
        [HttpPut("points/{id}")]
        public async Task<ActionResult> UpdatePoint(long id, [FromBody] PointDto dto)
        {
            var point = await _context.GisPoints.FindAsync(id);
            if (point == null) return NotFound();

            point.Name = dto.Name;
            point.Type = dto.Type;
            point.Geom = _geometryFactory.CreatePoint(new Coordinate(dto.Lng, dto.Lat));

            await _context.SaveChangesAsync();
            return Ok(new { message = "Punto aggiornato correttamente!" });
        }

        // DELETE: api/GisData/points/{id}
        [HttpDelete("points/{id}")]
        public async Task<ActionResult> DeletePoint(long id)
        {
            var point = await _context.GisPoints.FindAsync(id);
            if (point == null) return NotFound();

            _context.GisPoints.Remove(point);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Punto eliminato correttamente!" });
        }

        // POST: api/GisData/lines
        [HttpPost("lines")]
        public async Task<ActionResult> CreateLine([FromBody] LineDto dto)
        {
            if (dto.Coordinates == null || dto.Coordinates.Count < 2)
            {
                return BadRequest("Una linea deve contenere almeno due punti.");
            }

            var coords = dto.Coordinates.Select(c => new Coordinate(c[0], c[1])).ToArray();
            var line = new GisLine
            {
                Name = dto.Name,
                Type = dto.Type,
                Geom = _geometryFactory.CreateLineString(coords)
            };

            _context.GisLines.Add(line);
            await _context.SaveChangesAsync();

            // Aggiorna topologia di routing quando si aggiunge un sentiero
            if (line.Type == "Sentiero")
            {
                try { await _context.Database.ExecuteSqlRawAsync("SELECT refresh_routing_topology();"); } catch { }
            }

            return Ok(new { id = line.Id, message = "Linea creata correttamente!" });
        }

        // PUT: api/GisData/lines/{id}
        [HttpPut("lines/{id}")]
        public async Task<ActionResult> UpdateLine(long id, [FromBody] LineDto dto)
        {
            var line = await _context.GisLines.FindAsync(id);
            if (line == null) return NotFound();

            if (dto.Coordinates == null || dto.Coordinates.Count < 2)
            {
                return BadRequest("Una linea deve contenere almeno due punti.");
            }

            var coords = dto.Coordinates.Select(c => new Coordinate(c[0], c[1])).ToArray();
            line.Name = dto.Name;
            line.Type = dto.Type;
            line.Geom = _geometryFactory.CreateLineString(coords);

            await _context.SaveChangesAsync();
            
            if (line.Type == "Sentiero" || dto.Type == "Sentiero")
            {
                try { await _context.Database.ExecuteSqlRawAsync("SELECT refresh_routing_topology();"); } catch { }
            }
            
            return Ok(new { message = "Linea aggiornata correttamente!" });
        }

        // DELETE: api/GisData/lines/{id}
        [HttpDelete("lines/{id}")]
        public async Task<ActionResult> DeleteLine(long id)
        {
            var line = await _context.GisLines.FindAsync(id);
            if (line == null) return NotFound();

            var isSentiero = line.Type == "Sentiero";
            _context.GisLines.Remove(line);
            await _context.SaveChangesAsync();

            if (isSentiero)
            {
                try { await _context.Database.ExecuteSqlRawAsync("SELECT refresh_routing_topology();"); } catch { }
            }

            return Ok(new { message = "Linea eliminata correttamente!" });
        }

        // POST: api/GisData/polygons
        [HttpPost("polygons")]
        public async Task<ActionResult> CreatePolygon([FromBody] PolygonDto dto)
        {
            if (dto.Coordinates == null || dto.Coordinates.Count < 3)
            {
                return BadRequest("Un poligono deve contenere almeno tre punti.");
            }

            var coords = dto.Coordinates.Select(c => new Coordinate(c[0], c[1])).ToArray();
            var polygon = new GisPolygon
            {
                Name = dto.Name,
                Population = dto.Population, // Population = RiskLevel (0-100)
                Geom = _geometryFactory.CreatePolygon(coords)
            };

            _context.GisPolygons.Add(polygon);
            await _context.SaveChangesAsync();

            return Ok(new { id = polygon.Id, message = "Poligono creato correttamente!" });
        }

        // PUT: api/GisData/polygons/{id}
        [HttpPut("polygons/{id}")]
        public async Task<ActionResult> UpdatePolygon(long id, [FromBody] PolygonDto dto)
        {
            var polygon = await _context.GisPolygons.FindAsync(id);
            if (polygon == null) return NotFound();

            if (dto.Coordinates == null || dto.Coordinates.Count < 3)
            {
                return BadRequest("Un poligono deve contenere almeno tre punti.");
            }

            var coords = dto.Coordinates.Select(c => new Coordinate(c[0], c[1])).ToArray();
            polygon.Name = dto.Name;
            polygon.Population = dto.Population; // Population = RiskLevel (0-100)
            polygon.Geom = _geometryFactory.CreatePolygon(coords);

            await _context.SaveChangesAsync();
            return Ok(new { message = "Poligono aggiornato correttamente!" });
        }

        // DELETE: api/GisData/polygons/{id}
        [HttpDelete("polygons/{id}")]
        public async Task<ActionResult> DeletePolygon(long id)
        {
            var polygon = await _context.GisPolygons.FindAsync(id);
            if (polygon == null) return NotFound();

            _context.GisPolygons.Remove(polygon);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Poligono eliminato correttamente!" });
        }
    }

    // ==========================================
    // 3. DTO DATA TRANSFER OBJECTS FOR API
    // ==========================================

    public class PointDto
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
        public double Lat { get; set; }
        public double Lng { get; set; }
    }

    public class LineDto
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
        public List<double[]> Coordinates { get; set; } = new(); // [[lng, lat], [lng, lat]]
    }

    public class PolygonDto
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
        public int Population { get; set; } // Population = RiskLevel (0-100) per i poligoni
        public List<double[]> Coordinates { get; set; } = new(); // [[lng, lat], ..., [lng, lat]] (il primo e l'ultimo elemento devono coincidere)
    }

    public class TspRequestDto
    {
        public List<double[]> Points { get; set; } = new(); // [[lng, lat], ...]
        public bool Closed { get; set; } = true; // true = tour chiuso (torna alla partenza)
    }
}
