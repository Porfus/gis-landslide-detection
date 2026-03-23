using it.gis_landslide_detection.web.Models;

namespace it.gis_landslide_detection.web.Services
{
    public interface IWeatherService
    {


        /// <summary>
        /// Restituisce le precipitazioni attuali in mm/h
        /// per le coordinate specificate.
        /// Ritorna null se la chiamata fallisce.
        /// </summary>
        Task<WeatherData?> GetCurrentPrecipitationAsync(double lat, double lng);


    }
}

