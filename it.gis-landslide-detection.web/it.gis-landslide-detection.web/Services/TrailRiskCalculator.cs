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

    private static double GetHazardScore(string? tipo)
    {
        return tipo switch
        {
            "Colamento rapido" => 100.0,
            "Crollo/Ribaltamento" => 80.0,
            "Scivolamento rotazionale/traslativo" => 60.0,
            "Complesso" => 40.0,
            _ => 0.0
        };
    }

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
                ZoneCount: 0,
                HazardScore: 0.0
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
            try
            {
                var intersezione = trail.Geom.Intersection(geomDaAnalizzare);
                var candidato = intersezione.Centroid;
                // Verifica che il centroide abbia coordinate finite (non NaN/Infinity)
                // Le geometrie degenerate (es. intersezione puntiforme quasi-tangente) possono produrre centroidi non validi
                if (candidato != null && double.IsFinite(candidato.X) && double.IsFinite(candidato.Y))
                    puntoCritico = candidato;
                else
                    puntoCritico = geomDaAnalizzare.Centroid;
            }
            catch
            {
                // In caso di errore geometrico (TopologyException, ecc.), fallback al centroide della zona
                puntoCritico = geomDaAnalizzare.Centroid;
            }
        }
        else
        {
            puntoCritico = geomDaAnalizzare.Centroid;
        }

        // Ultima verifica: se anche il centroide della zona è invalido, usa quello del trail
        if (!double.IsFinite(puntoCritico.X) || !double.IsFinite(puntoCritico.Y))
        {
            var trailCentroid = trail.Geom?.Centroid;
            if (trailCentroid != null && double.IsFinite(trailCentroid.X) && double.IsFinite(trailCentroid.Y))
                puntoCritico = trailCentroid;
            else
                puntoCritico = new Point(13.003, 43.098); // fallback assoluto
        }

        return new TrailRiskResult(
            TrailId: trail.Id,
            TrailName: trail.Name,
            HasRisk: true,
            Message: $"Attenzione: il sentiero interseca {zones.Count} area/e franosa/e. Tipo più critico rilevato: {zonaPiuPericolosa.NomeTipo}.",
            ReferenceLat: puntoCritico.Y,
            ReferenceLng: puntoCritico.X,
            IffiTipo: zonaPiuPericolosa.NomeTipo,
            ZoneCount: zones.Count,
            HazardScore: GetHazardScore(zonaPiuPericolosa.NomeTipo)
        );
    }
}
