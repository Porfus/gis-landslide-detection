namespace it.gis_landslide_detection.web.Models;

public class CopernicusApiOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string TokenUrl { get; set; } = string.Empty;
    public string ProcessUrl { get; set; } = string.Empty;
    public int SarResolutionMeters { get; set; } = 20;
    public double DbDryThreshold { get; set; } = -20.0;
    public double DbSaturatedThreshold { get; set; } = -5.0;
}
