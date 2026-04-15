using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Hosting;
using BitMiracle.LibTiff.Classic;
using it.gis_landslide_detection.web.Models;

namespace it.gis_landslide_detection.web.Services
{
    public class SentinelService : ISentinelService
    {
        private readonly IWebHostEnvironment _env;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _cache;
        private readonly CopernicusApiOptions _options;
        private readonly ILogger<SentinelService> _logger;
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

        public SentinelService(
            IWebHostEnvironment env, 
            IHttpClientFactory httpClientFactory, 
            IMemoryCache cache, 
            IOptions<CopernicusApiOptions> options,
            ILogger<SentinelService> logger)
        {
            _env = env;
            _httpClientFactory = httpClientFactory;
            _cache = cache;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<SentinelData?> GetSoilMoistureForPointAsync(double lat, double lng)
        {
            string cacheKey = $"sentinel:{lat:F2},{lng:F2}";
            
            if (_cache.TryGetValue(cacheKey, out SentinelData? cachedData))
            {
                _logger.LogInformation("Returning Sentinel data from cache for cell {Lat:F2}, {Lng:F2}", lat, lng);
                return cachedData;
            }

            var semaphore = _locks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync();

            try
            {
                // Double check interno
                if (_cache.TryGetValue(cacheKey, out cachedData))
                {
                    return cachedData;
                }

                if (string.IsNullOrEmpty(_options.ClientId) || string.IsNullOrEmpty(_options.ClientSecret) || _options.ClientId.Contains("INSERISCI"))
                {
                    _logger.LogWarning("Copernicus API credentials missing. Running fallback.");
                    return await GetFallbackDataAsync(lat, lng);
                }

                var token = await GetAccessTokenAsync();
                if (string.IsNullOrEmpty(token))
                    return await GetFallbackDataAsync(lat, lng);

                var moistureData = await FetchMoistureDataAsync(lat, lng, token);
                
                if (moistureData != null)
                {
                    _cache.Set(cacheKey, moistureData, TimeSpan.FromHours(6));
                    return moistureData;
                }
                
                return await GetFallbackDataAsync(lat, lng);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching Sentinel data. Running fallback.");
                return await GetFallbackDataAsync(lat, lng);
            }
            finally
            {
                semaphore.Release();
            }
        }

        private async Task<string?> GetAccessTokenAsync()
        {
            if (_cache.TryGetValue("copernicus:token", out string? cachedToken))
                return cachedToken;

            var client = _httpClientFactory.CreateClient("copernicus");
            var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenUrl);
            
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("client_id", _options.ClientId),
                new KeyValuePair<string, string>("client_secret", _options.ClientSecret)
            });

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to authenticate with Copernicus API: {status}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("access_token", out var tokenElement))
            {
                var t = tokenElement.GetString();
                _cache.Set("copernicus:token", t, TimeSpan.FromMinutes(8));
                return t;
            }

            return null;
        }

        private async Task<SentinelData?> FetchMoistureDataAsync(double lat, double lng, string token)
        {
            double offset = 0.005; // ~500m bounding box
            double minLng = lng - offset;
            double minLat = lat - offset;
            double maxLng = lng + offset;
            double maxLat = lat + offset;

            // Ultimi 30 giorni per assicurarsi di catturare almeno un'orbita
            string fromDate = DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-ddTHH:mm:ssZ");
            string toDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

            var requestBody = new
            {
                input = new
                {
                    bounds = new
                    {
                        bbox = new[] { minLng, minLat, maxLng, maxLat },
                        properties = new { crs = "http://www.opengis.net/def/crs/EPSG/0/4326" }
                    },
                    data = new[]
                    {
                        new
                        {
                            type = "sentinel-1-grd",
                            dataFilter = new
                            {
                                timeRange = new { from = fromDate, to = toDate },
                                acquisitionMode = "IW",
                                polarization = "DV"
                            },
                            processing = new
                            {
                                backCoeff = "GAMMA0_TERRAIN",
                                orthorectify = true
                            }
                        }
                    }
                },
                output = new
                {
                    width = 50,
                    height = 50,
                    responses = new[]
                    {
                        new { identifier = "default", format = new { type = "image/tiff" } }
                    }
                },
                evalscript = @"
//VERSION=3
function setup() {
  return {
    input: [{ bands: ['VV'] }],
    output: { bands: 1, sampleType: 'FLOAT32' }
  };
}
function evaluatePixel(sample) {
  return [sample.VV];
}"
            };

            var client = _httpClientFactory.CreateClient("copernicus");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await client.PostAsync(_options.ProcessUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Process API failed: {status} - {err}", response.StatusCode, err);
                return null;
            }

            var tiffBytes = await response.Content.ReadAsByteArrayAsync();
            
            string tempTiff = Path.GetTempFileName();
            try
            {
                await File.WriteAllBytesAsync(tempTiff, tiffBytes);
                using var tiff = Tiff.Open(tempTiff, "r");
                if (tiff == null) return null;

                int width = tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
                int height = tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt();

                int scanlineSize = tiff.ScanlineSize();
                byte[] buffer = new byte[scanlineSize];

                double sumLinear = 0;
                int validPixels = 0;

                for (int row = 0; row < height; row++)
                {
                    tiff.ReadScanline(buffer, row);
                    for (int col = 0; col < width; col++)
                    {
                        float val = BitConverter.ToSingle(buffer, col * 4);
                        if (val > 0)
                        {
                            sumLinear += val;
                            validPixels++;
                        }
                    }
                }

                if (validPixels == 0) return null;

                double meanLinear = sumLinear / validPixels;
                double vvDb = 10.0 * Math.Log10(Math.Max(1e-10, meanLinear));
                
                double dbSat = _options.DbSaturatedThreshold;
                double dbDry = _options.DbDryThreshold;
                
                int score = (int)Math.Clamp((vvDb - dbDry) / (dbSat - dbDry) * 100.0, 0, 100);

                return new SentinelData(
                    SoilMoistureScore: score,
                    VvMeanDb: vvDb,
                    SoilMoistureScoreDry: 0,
                    DeltaScore: 0,
                    Periodo: $"{fromDate} / {toDate}",
                    Fonte: "CDSE Sentinel-1"
                );
            }
            finally
            {
                if (File.Exists(tempTiff))
                    File.Delete(tempTiff);
            }
        }

        private async Task<SentinelData?> GetFallbackDataAsync(double queryLat, double queryLng)
        {
            var gridPath = Path.Combine(_env.WebRootPath, "data", "soil_moisture_grid.json");
            var globalPath = Path.Combine(_env.WebRootPath, "data", "soil_moisture_results.json");

            if (File.Exists(gridPath))
            {
                var json = await File.ReadAllTextAsync(gridPath);
                using var doc = JsonDocument.Parse(json);
                var grid = doc.RootElement.GetProperty("grid");

                double minDist = double.MaxValue;
                double bestVv = -15.0;
                int bestScore = 75;

                foreach (var cell in grid.EnumerateArray())
                {
                    var cLat = cell.GetProperty("lat").GetDouble();
                    var cLng = cell.GetProperty("lng").GetDouble();
                    var dist = Math.Sqrt(Math.Pow(cLat - queryLat, 2) + Math.Pow(cLng - queryLng, 2));
                    if (dist < minDist)
                    {
                        minDist = dist;
                        bestVv = cell.GetProperty("vv_db").GetDouble();
                        bestScore = cell.GetProperty("score").GetInt32();
                    }
                }

                var root = doc.RootElement;
                string periodo = root.TryGetProperty("period", out var periodoEl) ? periodoEl.GetString() ?? "" : "";

                return new SentinelData(bestScore, bestVv, 0, 0, periodo, "Fallback Grid");
            }
            else if (File.Exists(globalPath))
            {
                 var json = await File.ReadAllTextAsync(globalPath);
                 using var doc = JsonDocument.Parse(json);
                 var root = doc.RootElement;
                 
                 return new SentinelData(
                     SoilMoistureScore:    root.GetProperty("soil_moisture_score").GetInt32(),
                     VvMeanDb:             root.GetProperty("vv_mean_db").GetDouble(),
                     SoilMoistureScoreDry: 0,
                     DeltaScore:           0,
                     Periodo:              root.TryGetProperty("saturated_period", out var p) ? p.GetString() ?? "" : "",
                     Fonte:                "Fallback Global"
                 );
            }
            return null;
        }
    }
}
