using it.gis_landslide_detection.web.Data;
using it.gis_landslide_detection.web.Models;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite;
using NetTopologySuite.Geometries;

namespace it.gis_landslide_detection.web.Services
{
    public class IffiService : IIffiService
    {
        private readonly ApplicationDbContext _context;

        // Lista dei tipi di frana rilevanti per l'alert
        private static readonly string[] TipiPericolosi = {
            "Colamento rapido",
            "Crollo/Ribaltamento",
            "Scivolamento rotazionale/traslativo",
            "Complesso"
        };

        public IffiService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IffiZone?> GetZoneAsync(double lat, double lng)
        {
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
            var punto = factory.CreatePoint(new Coordinate(lng, lat));

            return await _context.IffiZones
                .Where(z => z.Geom != null 
                         && z.NomeTipo != null
                         && TipiPericolosi.Contains(z.NomeTipo) // filtro tipo
                         && z.Geom.Contains(punto))
                .FirstOrDefaultAsync();
        }

        public async Task<TrailRiskResult?> GetTrailRiskAsync(long trailId)
        {
            // 1. Carica il trail dal DB
            var trail = await _context.HikingTrails
                .FirstOrDefaultAsync(t => t.Id == trailId);
            
            if (trail?.Geom == null) return null;

            // 2. Trova le zone IFFI pericolose che intersecano il trail
            var zoneIntersecanti = await _context.IffiZones
                .Where(z => z.Geom != null 
                         && z.NomeTipo != null
                         && TipiPericolosi.Contains(z.NomeTipo)
                         && z.Geom.Intersects(trail.Geom))
                .ToListAsync();

            if (!zoneIntersecanti.Any())
                return new TrailRiskResult(trailId, trail.Name, false, 0, 0, null, 0);

            // 3. Calcola il punto di intersezione più critico
            // Prendi la prima zona (o quella con tipo più pericoloso in base all'ordine dell'array)
            var zonaPiuPericolosa = zoneIntersecanti
                .OrderBy(z => Array.IndexOf(TipiPericolosi, z.NomeTipo))
                .First();

            // 4. Calcola il centroide dell'intersezione tra trail e zona
            var intersezione = trail.Geom.Intersection(zonaPiuPericolosa.Geom!);
            var puntoCritico = intersezione.Centroid;

            return new TrailRiskResult(
                TrailId: trailId,
                TrailName: trail.Name,
                HasRisk: true,
                CriticalPointLat: puntoCritico.Y,  // Y = latitudine
                CriticalPointLng: puntoCritico.X,  // X = longitudine
                IffiTipo: zonaPiuPericolosa.NomeTipo,
                ZoneCount: zoneIntersecanti.Count
            );
        }
    }
}