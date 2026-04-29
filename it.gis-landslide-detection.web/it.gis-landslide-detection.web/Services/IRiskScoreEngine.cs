namespace it.gis_landslide_detection.web.Services;

/// <summary>
/// Motore di calcolo del rischio finale: combina hazard storico IFFI,
/// soil moisture SAR, Antecedent Precipitation Index e pioggia istantanea
/// in un unico RiskScore 0-100 con livello associato.
/// </summary>
public interface IRiskScoreEngine
{
    RiskAssessment Calculate(
        double iffiHazardScore,
        string? iffiTipo,
        int soilMoistureScore,
        int apiScore,
        int currentRainScore,
        double precipMmh,
        bool weatherDataUnavailable = false
    );
}

/// <summary>
/// Risultato completo del calcolo di rischio, con tracciabilità
/// di tutti i fattori intermedi e dei meccanismi di override attivati.
/// </summary>
public record RiskAssessment(
    double RiskScore,
    string RiskLevel,
    double SaturationIndex,
    double TriggerMultiplier,
    double BaseHazard,
    double WSoil,
    double WApi,
    double WRain,
    bool FlashOverrideApplied,
    bool SaturationFloorApplied,
    bool WeatherDataUnavailable
);
