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
        private readonly ITrailRiskCalculator _riskCalculator;

        public IffiService(ApplicationDbContext context, ITrailRiskCalculator riskCalculator)
        {
            _context = context;
            _riskCalculator = riskCalculator;
        }

        public async Task<IffiZone?> GetZoneAsync(double lat, double lng)
        {
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
            var punto = factory.CreatePoint(new Coordinate(lng, lat));

            return await _context.IffiZones
                .Where(z => z.Geom != null 
                         && z.NomeTipo != null
                         && TrailRiskCalculator.TipiPericolosi.Contains(z.NomeTipo) // filtro tipo
                         && z.Geom.Contains(punto))
                .FirstOrDefaultAsync();
        }

        public async Task<TrailRiskResult?> GetTrailRiskAsync(long trailId)
        {
            var trail = await _context.HikingTrails
                .FirstOrDefaultAsync(t => t.Id == trailId);
            
            if (trail?.Geom == null) return null;

            // 1. Semplificazione della geometria del sentiero per ridurre drasticamente i vertici inviati via SQL
            // 0.0001 gradi = circa 11 metri di tolleranza, ottimizza le spline in linee rette mantenendo il percorso base
            var simplifiedTrail = NetTopologySuite.Simplify.TopologyPreservingSimplifier.Simplify(trail.Geom, 0.0001);
            simplifiedTrail.SRID = 4326; // CRITICAL: Restore SRID dropped by simplifier!

            // 2. Creazione della Bounding Box (rettangolo di selezione)
            var envelope = simplifiedTrail.EnvelopeInternal;
            var factory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(4326);
            var extent = factory.ToGeometry(envelope); // Rende l'envelope una Geometry interrogabile (un poligono 4 angoli)
            extent.SRID = 4326;

            // 3. Incrementiamo temporaneamente il timeout del DbContext da 30s a 60s per questa specifica unità di lavoro
            _context.Database.SetCommandTimeout(60);

            // 4. Query spaziale ottimizzata bypassando EF Core.
            // Quando passiamo parametri geometrici tramite EF Core ({0}, {1}), PostgreSQL genera un 
            // "Generic Plan" (piano generico) per la query. Avendo a che fare con geometrie, se non conosce 
            // l'area esatta del perimetro in anticipo, il DB decide (erroneamente) di scartare l'indice e fare un Seq Scan.
            // Soluzione estrema infallibile: Scriviamo l'envelope direttamente dentro la stringa SQL come WKT letterale (es. 'POLYGON((...))').
            // In questo modo Postgres genera un "Custom Plan" conoscendo le coordinate precise in anticipo, innescando col 100%
            // di probabilità l'indice spaziale. Il calcolo finale .Intersects lo facciamo direttamente in RAM lato C# (NetTopologySuite è un fulmine).
            
            var wktWriter = new NetTopologySuite.IO.WKTWriter();
            string extentWkt = wktWriter.Write(extent);

            var rawSql = $@"
                SELECT * FROM landslide_zones 
                WHERE nome_tipo IN ('Colamento rapido', 'Crollo/Ribaltamento', 'Scivolamento rotazionale/traslativo', 'Complesso')
                  AND geom && ST_GeomFromText('{extentWkt}', 4326)
            ";

            // Usiamo SqlQueryRaw puro e portiamo in ram le (poche) zone candidate trovate col Bounding Box
            #pragma warning disable EF1000 // Disabilita warning per parametrizzazione, la WKT è sicura in quanto generata da NTS (no SQL-Injection possible)
            var candidateZones = await _context.IffiZones
                .FromSqlRaw(rawSql)
                .ToListAsync();
            #pragma warning restore EF1000

            // 5. Calcolo intersezione esatta lato C# (estremamente veloce, essendo i candidati nell'ordine delle unità/decine)
            var zoneIntersecanti = candidateZones
                .Where(z => z.Geom != null && z.Geom.Intersects(simplifiedTrail))
                .ToList();

            // Delega la business logic del calcolo del rischio al Calculator
            return _riskCalculator.CalculateRisk(trail, zoneIntersecanti);
        }
    }
}