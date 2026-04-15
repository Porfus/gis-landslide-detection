namespace it.gis_landslide_detection.web.Models
{
    public record WeatherData 
    {
        public double PrecipitationMmh { get; set; }       // mm/h attuali
        public double PastPrecipitationMm { get; set; }    // mm cumulati ultimi 7 giorni
        public double AntecedentPrecipIndex { get; set; }  // API calcolato con decay k=0.85
        public int    ApiScore { get; set; }               // API normalizzato 0-100
        public int    CurrentRainScore { get; set; }       // pioggia attuale normalizzata 0-100
        public int    PrecipitationScore { get; set; }     // score combinato 
        public string? Source { get; set; }

        public WeatherData(double precipitationMmh, double pastPrecipitationMm, double antecedentPrecipIndex, int apiScore, int currentRainScore, int precipitationScore, string? source)
        {
            PrecipitationMmh = precipitationMmh;
            PastPrecipitationMm = pastPrecipitationMm;
            AntecedentPrecipIndex = antecedentPrecipIndex;
            ApiScore = apiScore;
            CurrentRainScore = currentRainScore;
            PrecipitationScore = precipitationScore;
            Source = source;
        }
    }
}
