using it.gis_landslide_detection.web.Models;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace it.gis_landslide_detection.web.Services
{
    public class WeatherService : IWeatherService
    {
        private readonly IHttpClientFactory _factory;
        private readonly ILogger<WeatherService> _log;
        private readonly IMemoryCache _cache;

        public WeatherService(IHttpClientFactory factory,
                              ILogger<WeatherService> log,
                              IMemoryCache cache)
        {
            _factory = factory;
            _log = log;
            _cache = cache;
        }

        public async Task<WeatherData?> GetCurrentPrecipitationAsync(
            double lat, double lng)
        {
            string cacheKey = $"weather:{lat:F2},{lng:F2}";
            if (_cache.TryGetValue(cacheKey, out WeatherData? cachedData))
            {
                _log.LogInformation("Returning Weather data from cache for cell {Lat:F2}, {Lng:F2}", lat, lng);
                return cachedData;
            }

            try
            {
                var client = _factory.CreateClient("openmeteo");
                
                // Richiede precipitazioni attuali e 7 giorni passati
                var url = System.FormattableString.Invariant($"/v1/forecast?latitude={lat:F4}&longitude={lng:F4}&current=precipitation&daily=precipitation_sum&past_days=7&forecast_days=1&timezone=auto&timeformat=unixtime");

                var res = await client.GetAsync(url);
                res.EnsureSuccessStatusCode();

                var json = await res.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);

                var mmh = doc.RootElement
                    .GetProperty("current")
                    .GetProperty("precipitation")
                    .GetDouble();

                double pastPrecipitation = 0.0;
                double antecedentPrecipIndex = 0.0;
                const double k = 0.85; // decay coefficient

                if (doc.RootElement.TryGetProperty("daily", out var dailyElem) &&
                    dailyElem.TryGetProperty("precipitation_sum", out var precipArray))
                {
                    // L'array ha ampiezza 8 (7 giorni passati + oggi). Prendiamo i primi 7 giorni.
                    int count = Math.Min(7, precipArray.GetArrayLength());
                    for (int i = 0; i < count; i++)
                    {
                        var val = precipArray[i];
                        if (val.ValueKind != JsonValueKind.Null)
                        {
                            double dailyMm = val.GetDouble();
                            pastPrecipitation += dailyMm;
                            // Ordine cronologico: indice 0 è 7 giorni fa.
                            antecedentPrecipIndex = (k * antecedentPrecipIndex) + dailyMm;
                        }
                    }
                }

                // Normalizza: API >= 80 mm = score 100
                int apiScore = (int)Math.Clamp((antecedentPrecipIndex / 80.0) * 100.0, 0, 100);
                
                // Normalizza: intensità attuale >= 30 mm/h = score 100
                int currentRainScore = (int)Math.Clamp((mmh / 30.0) * 100.0, 0, 100);

                var result = new WeatherData(
                    mmh, 
                    pastPrecipitation, 
                    antecedentPrecipIndex, 
                    apiScore, 
                    currentRainScore, 
                    "Open-Meteo"
                );

                _cache.Set(cacheKey, result, TimeSpan.FromMinutes(15));
                return result;
            }
            catch (Exception ex)
            {
                _log.LogWarning("WeatherService API fallita: {msg}", ex.Message);
                return null;
            }
        }

    }
}
