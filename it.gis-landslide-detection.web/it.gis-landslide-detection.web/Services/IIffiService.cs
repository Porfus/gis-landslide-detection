using it.gis_landslide_detection.web.Models;

namespace it.gis_landslide_detection.web.Services
{
    public interface IIffiService
    {
        // Metodo esistente — punto singolo
        Task<IffiZone?> GetZoneAsync(double lat, double lng);

        // Nuovo metodo — trail completo
        Task<TrailRiskResult?> GetTrailRiskAsync(long trailId);
    }

    // DTO per il risultato del trail
    public record TrailRiskResult(
        long TrailId,
        string? TrailName,
        bool HasRisk,
        string Message,
        double ReferenceLat,   // coordinate del punto più pericoloso o del centroide del sentiero
        double ReferenceLng,
        string? IffiTipo,          // tipo di frana trovato
        int ZoneCount              // quante zone IFFI interseca il trail
    );

    public interface ITrailRiskCalculator
    {
        TrailRiskResult CalculateRisk(HikingTrail trail, IReadOnlyCollection<IffiZone> intersectingZones);
    }
}