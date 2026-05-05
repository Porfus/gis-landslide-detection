using it.gis_landslide_detection.web.Models;
using NetTopologySuite.Geometries;

namespace it.gis_landslide_detection.web.Services;

public class TrailHazardCalculator : ITrailHazardCalculator
{
    private static readonly Point DefaultMapCenter = new Point(13.003, 43.098);

    public static readonly string[] TipiPericolosi = {
        IffiHazardTypes.ColamentoRapido,
        IffiHazardTypes.CrolloRibaltamento,
        IffiHazardTypes.ScivolamentoRotazionaleTraslativo,
        IffiHazardTypes.Complesso
    };

    private static double GetHazardScore(string? tipo)
    {
        return tipo switch
        {
            IffiHazardTypes.ColamentoRapido => 100.0,
            IffiHazardTypes.CrolloRibaltamento => 80.0,
            IffiHazardTypes.ScivolamentoRotazionaleTraslativo => 60.0,
            IffiHazardTypes.Complesso => 40.0,
            _ => 0.0
        };
    }

    public TrailHazardResult CalculateHazard(HikingTrail trail, IReadOnlyCollection<IffiZone> intersectingZones)
    {
        if (trail == null) throw new ArgumentNullException(nameof(trail));
        
        var zones = intersectingZones?.ToList() ?? new List<IffiZone>();

        if (!zones.Any())
        {
            // Nessuna interazione: il sentiero è sicuro. Recupera il centroide del trail come punto di riferimento mappa
            var sentieroCentroid = trail.Geom?.Centroid;
            return new TrailHazardResult(
                TrailId: trail.Id,
                TrailName: trail.Name,
                HasHazard: false,
                Message: "Nessuna intersezione con aree franose rilevata. Il sentiero è sicuro dal punto di vista storico.",
                ReferenceLat: sentieroCentroid?.Y ?? DefaultMapCenter.Y,
                ReferenceLng: sentieroCentroid?.X ?? DefaultMapCenter.X,
                IffiTipo: null,
                ZoneCount: 0,
                HazardScore: 0.0
            );
        }

        // Caso con pericolosità: trova la zona più pericolosa
        var zonaPiuPericolosa = zones
            .OrderBy(z => 
            {
                var pt = Array.IndexOf(TipiPericolosi, z.NomeTipo);
                return pt >= 0 ? pt : int.MaxValue;
            })
            .First();

        Geometry geomDaAnalizzare = zonaPiuPericolosa.Geom!;
        var puntoCritico = CalcolaPuntoCritico(trail.Geom, geomDaAnalizzare);

        // Ultima verifica: se anche il centroide della zona è invalido, usa quello del trail
        if (!double.IsFinite(puntoCritico.X) || !double.IsFinite(puntoCritico.Y))
        {
            var trailCentroid = trail.Geom?.Centroid;
            if (trailCentroid != null && double.IsFinite(trailCentroid.X) && double.IsFinite(trailCentroid.Y))
                puntoCritico = trailCentroid;
            else
                puntoCritico = DefaultMapCenter; // fallback assoluto
        }

        return new TrailHazardResult(
            TrailId: trail.Id,
            TrailName: trail.Name,
            HasHazard: true,
            Message: $"Attenzione: il sentiero interseca {zones.Count} area/e franosa/e. Tipo più critico rilevato: {zonaPiuPericolosa.NomeTipo}.",
            ReferenceLat: puntoCritico.Y,
            ReferenceLng: puntoCritico.X,
            IffiTipo: zonaPiuPericolosa.NomeTipo,
            ZoneCount: zones.Count,
            HazardScore: GetHazardScore(zonaPiuPericolosa.NomeTipo)
        );
    }

    /// <summary>
    /// Calcola il punto critico dell'intersezione tra sentiero e zona franosa.
    /// Restituisce il centroide dell'intersezione se valido, altrimenti il centroide della zona (fallback).
    /// </summary>
    private static Point CalcolaPuntoCritico(Geometry? trailGeom, Geometry zonaGeom)
    {
        if (trailGeom != null && trailGeom.Intersects(zonaGeom))
        {
            try
            {
                var candidato = trailGeom.Intersection(zonaGeom).Centroid;
                // Le geometrie degenerate possono produrre centroidi NaN/Infinity
                if (candidato != null && double.IsFinite(candidato.X) && double.IsFinite(candidato.Y))
                    return candidato;
            }
            catch (TopologyException)
            {
                // Fallback garantito in caso di topologia invalida
            }
        }

        return zonaGeom.Centroid;
    }
}
