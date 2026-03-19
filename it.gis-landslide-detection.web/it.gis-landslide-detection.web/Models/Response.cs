namespace it.gis_landslide_detection.web.Models;

public record Response
{
    public Response(double lat, double lon, int riskScore, string? riskLevel, int soilMoisture, int precipitation, bool historicalRisk, string? message)
    {
        Lat = lat;
        Lon = lon;
        RiskScore = riskScore;
        RiskLevel = riskLevel;
        SoilMoisture = soilMoisture;
        Precipitation = precipitation;
        HistoricalRisk = historicalRisk;
        Message = message;
    }

    public double Lat { get; set; }
    public double Lon { get; set; }
    public int RiskScore { get; set; }
    public string? RiskLevel { get; set; }
    public int SoilMoisture { get; set;}
    public int Precipitation { get; set; }
    public bool HistoricalRisk { get; set; }
    public string? Message { get; set; }
}