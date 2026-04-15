using it.gis_landslide_detection.web.Models;

namespace it.gis_landslide_detection.web.Services;

public interface ISentinelService
{
    Task<SentinelData?> GetSoilMoistureForPointAsync(double lat, double lng);
}

public record SentinelData(
    int    SoilMoistureScore,   // 0-100
    double VvMeanDb,            // valore grezzo in dB
    int    SoilMoistureScoreDry,// score periodo asciutto (confronto)
    double DeltaScore,          // differenza saturo - asciutto
    string Periodo,             // es. '2024-06-15 / 2024-07-15'
    string Fonte
);
