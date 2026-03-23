namespace it.gis_landslide_detection.web.Models
{
    public record WeatherData 
    {
        public double PrecipitationMmh { get; set; }   // mm/h attuali
        public double PastPrecipitationMm { get; set; } // mm cumulati ultimi giorni
        public int PrecipitationScore { get; set; } // normalizzato 0-100
        public string? Source { get; set; } // 'Open-Meteo' o 'fallback'

        public WeatherData(double precipitationMmh, double pastPrecipitationMm, int precipitationScore, string? source)
        {
            PrecipitationMmh = precipitationMmh;
            PastPrecipitationMm = pastPrecipitationMm;
            PrecipitationScore = precipitationScore;
            Source = source;
        }
    }
}
