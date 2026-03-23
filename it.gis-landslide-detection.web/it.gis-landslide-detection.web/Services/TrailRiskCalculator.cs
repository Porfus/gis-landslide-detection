using it.gis_landslide_detection.web.Models;
using NetTopologySuite.Geometries;

namespace it.gis_landslide_detection.web.Services;

public class TrailRiskCalculator : ITrailRiskCalculator
{
    public static readonly string[] TipiPericolosi = {
        "Colamento rapido",
        "Crollo/Ribaltamento",
        "Scivolamento rotazionale/traslativo",
        "Complesso"
    };

    public TrailRiskResult CalculateRisk(HikingTrail trail, IReadOnlyCollection<IffiZone> intersectingZones)
    {
        if (trail == null) throw new ArgumentNullException(nameof(trail));
        
        var zones = intersectingZones?.ToList() ?? new List<IffiZone>();

        if (!zones.Any())
        {
            // Nessuna interazione: il sentiero è sicuro. Recupera il centroide del trail come punto di riferimento mappa
            var sentieroCentroid = trail.Geom?.Centroid;
            return new TrailRiskResult(
                TrailId: trail.Id,
                TrailName: trail.Name,
                HasRisk: false,
                Message: "Nessuna intersezione con aree franose rilevata. Il sentiero è sicuro dal punto di vista storico.",
                ReferenceLat: sentieroCentroid?.Y ?? 43.098,
                ReferenceLng: sentieroCentroid?.X ?? 13.003,
                IffiTipo: null,
                ZoneCount: 0
            );
        }

        // Caso con rischio: trova la zona più pericolosa
        var zonaPiuPericolosa = zones
            .OrderBy(z => 
            {
                var pt = Array.IndexOf(TipiPericolosi, z.NomeTipo);
                return pt >= 0 ? pt : int.MaxValue;
            })
            .First();

        Geometry geomDaAnalizzare = zonaPiuPericolosa.Geom!;
        Point puntoCritico;

        // Calcola l'intersezione esatta tra sentiero e zona più pericolosa
        if (trail.Geom != null && trail.Geom.Intersects(geomDaAnalizzare))
        {
            var intersezione = trail.Geom.Intersection(geomDaAnalizzare);
            // Se l'intersezione non produce un centroide valido per qualche anomalia geometrica, usa la zona
            puntoCritico = intersezione.Centroid ?? geomDaAnalizzare.Centroid;
        }
        else
        {
            puntoCritico = geomDaAnalizzare.Centroid;
        }

        return new TrailRiskResult(
            TrailId: trail.Id,
            TrailName: trail.Name,
            HasRisk: true,
            Message: $"Attenzione: il sentiero interseca {zones.Count} area/e franosa/e. Tipo più critico rilevato: {zonaPiuPericolosa.NomeTipo}.",
            ReferenceLat: puntoCritico.Y,
            ReferenceLng: puntoCritico.X,
            IffiTipo: zonaPiuPericolosa.NomeTipo,
            ZoneCount: zones.Count
        );
    }
}
