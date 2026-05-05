using it.gis_landslide_detection.web.Models;

namespace it.gis_landslide_detection.web.Services
{
    public interface IIffiService
    {
        // Metodo esistente — punto singolo
        Task<IffiZone?> GetZoneAsync(double lat, double lng);

        // Nuovo metodo — trail completo
        Task<TrailHazardResult?> GetTrailHazardAsync(long trailId);
    }

    // DTO per il risultato del trail
    public record TrailHazardResult(
        long TrailId,
        string? TrailName,
        bool HasHazard,
        string Message,
        double ReferenceLat,   // coordinate del punto più pericoloso o del centroide del sentiero
        double ReferenceLng,
        string? IffiTipo,          // tipo di frana trovato
        int ZoneCount,             // quante zone IFFI interseca il trail
        double HazardScore         // punteggio di pericolosità basato sul tipo
    );

    public interface ITrailHazardCalculator
    {
        TrailHazardResult CalculateHazard(HikingTrail trail, IReadOnlyCollection<IffiZone> intersectingZones);
    }
}