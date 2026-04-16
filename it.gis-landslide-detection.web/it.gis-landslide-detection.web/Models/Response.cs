namespace it.gis_landslide_detection.web.Models;

public record Response
{
    public Response(double lat, double lng, int riskScore, string? riskLevel, string? message, bool historicalRisk, string iffiLevel, int historicalScore, int soilMoisture, double vvMeanDb, double deltaScore, string sentinelSource, int precipitation, double precipitationMmh)
    {
        Lat = lat;
        Lng = lng;
        RiskScore = riskScore;
        RiskLevel = riskLevel;
        Message = message;
        HistoricalRisk = historicalRisk;
        IffiLevel = iffiLevel;
        HistoricalScore = historicalScore;
        SoilMoisture = soilMoisture;
        VvMeanDb = vvMeanDb;
        DeltaScore = deltaScore;
        SentinelSource = sentinelSource;
        Precipitation = precipitation;
        PrecipitationMmh = precipitationMmh;
    }

    public double Lat { get; set; }
    public double Lng { get; set; }
    public int RiskScore { get; set; }
    public string? RiskLevel { get; set; }
    public string? Message { get; set; }
    // Componente 1 — storico IFFI
    public bool HistoricalRisk { get; set; }

    public string IffiLevel { get; set; }
    public int HistoricalScore { get; set; }
    // Componente 2 — soil moisture Sentinel-1 
    public int SoilMoisture { get; set;}
    public double VvMeanDb { get; set; }
    public double DeltaScore { get; set; }
    public string SentinelSource { get; set; }
// Componente 3 — precipitazioni ECMWF 
    public int Precipitation { get; set; }
    public double PrecipitationMmh { get; set; }


}