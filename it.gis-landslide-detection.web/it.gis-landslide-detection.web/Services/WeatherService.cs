using it.gis_landslide_detection.web.Models;
using System.Text.Json;

namespace it.gis_landslide_detection.web.Services
{
    public class WeatherService : IWeatherService
    {
        private readonly IHttpClientFactory _factory;
        private readonly ILogger<WeatherService> _log;

        public WeatherService(IHttpClientFactory factory,
                              ILogger<WeatherService> log)
        {
            _factory = factory;
            _log = log;
        }

        public async Task<WeatherData?> GetCurrentPrecipitationAsync(
            double lat, double lng)
        {
            try
            {
                var client = _factory.CreateClient("openmeteo");
                // current=precipitation restituisce mm/h in tempo reale
                var url = System.FormattableString.Invariant($"/v1/forecast?latitude={lat:F4}&longitude={lng:F4}&current=precipitation&timeformat=unixtime");

                var res = await client.GetAsync(url);
                res.EnsureSuccessStatusCode();

                var json = await res.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);

                var mmh = doc.RootElement
                    .GetProperty("current")
                    .GetProperty("precipitation")
                    .GetDouble();

                // Normalizza: 0 mm/h = score 0, >= 50 mm/h = score 100
                int score = (int)Math.Min(100, mmh / 50.0 * 100);

                return new WeatherData(mmh, score, "Open-Meteo");
            }
            catch (Exception ex)
            {
                _log.LogWarning("WeatherService fallback: {msg}", ex.Message);
                // Fallback hardcoded caso studio luglio 2024
                return new WeatherData(47.0, 85, "fallback");
            }
        }

    }
}
