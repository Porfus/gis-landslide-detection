namespace it.gis_landslide_detection.web.Models;

public record SentinelData
{
    public double PrecipitationMmh { get; set; }   // mm/h attuali
    public int PrecipitationScore { get; set; } // normalizzato 0-100
    public string Source { get; set; } // 'Open-Meteo' o 'fallback'

}