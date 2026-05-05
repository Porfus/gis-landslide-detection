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
        private static readonly SemaphoreSlim[] _shardedLocks = System.Linq.Enumerable.Range(0, 256).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

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
            // Cache key arrotondato a 0.01° (~1km) per raggruppare punti vicini
            string cacheKey = $"sentinel:{lat:F2},{lng:F2}";

            if (_cache.TryGetValue(cacheKey, out SentinelData? cachedData))
            {
                _logger.LogInformation("Returning Sentinel data from cache for cell {Lat:F2}, {Lng:F2}", lat, lng);
                return cachedData;
            }

            var semaphore = _shardedLocks[Math.Abs(cacheKey.GetHashCode()) % 256];
            await semaphore.WaitAsync();

            try
            {
                if (_cache.TryGetValue(cacheKey, out cachedData))
                    return cachedData;

                if (string.IsNullOrEmpty(_options.ClientId) || string.IsNullOrEmpty(_options.ClientSecret) || _options.ClientId.Contains("INSERISCI"))
                {
                    _logger.LogWarning("Copernicus API credentials missing. Running fallback.");
                    return await GetFallbackDataAsync(lat, lng);
                }

                var token = await GetAccessTokenAsync();
                if (string.IsNullOrEmpty(token))
                    return await GetFallbackDataAsync(lat, lng);

                // --- Fetch periodo corrente (wet candidate) ---
                string currentFrom = DateTime.UtcNow.AddDays(-_options.CurrentPeriodDays).ToString("yyyy-MM-ddTHH:mm:ssZ");
                string currentTo   = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

                _logger.LogInformation("Fetching current SAR data for {Lat:F4},{Lng:F4} [{From} → {To}]", lat, lng, currentFrom, currentTo);
                double? currentDb = await FetchMeanVvDbAsync(lat, lng, token, currentFrom, currentTo);

                if (currentDb == null)
                {
                    _logger.LogWarning("Current SAR fetch returned null. Running fallback.");
                    return await GetFallbackDataAsync(lat, lng);
                }

                // --- Fetch dry baseline (luglio-agosto anno precedente, cache lunga) ---
                // NOTA: Il baseline è fisso per anno e locazione → viene cachato 30 giorni.
                // Per aree appenniniche/mediterranee luglio-agosto è tipicamente il periodo più secco.
                // ⚠️ FUTURA ESTENSIONE: Per zone alpine o con neve in quota questo periodo
                //    potrebbe non essere rappresentativo. Parametrizzato via DryBaselineMonthStart/End.
                double? dryDb = await GetOrFetchDryBaselineAsync(lat, lng, token);

                double? vvDeltaDb = dryDb.HasValue ? currentDb.Value - dryDb.Value : null;
                double dbSat      = _options.DbSaturatedThreshold; // -5.0 dB = suolo saturo
                double dbDry      = _options.DbDryThreshold;       // -20.0 dB = suolo secco

                int currentScore  = (int)Math.Clamp((currentDb.Value - dbDry) / (dbSat - dbDry) * 100.0, 0, 100);
                int? dryScore     = dryDb.HasValue
                    ? (int)Math.Clamp((dryDb.Value - dbDry) / (dbSat - dbDry) * 100.0, 0, 100)
                    : null;
                double? deltaScore = dryScore.HasValue ? currentScore - dryScore.Value : null;

                string periodo    = $"{currentFrom} / {currentTo}";
                string fonte      = dryDb.HasValue ? "CDSE Sentinel-1" : "CDSE Sentinel-1 (Missing Dry Baseline)";

                var result = new SentinelData(
                    SoilMoistureScore:    currentScore,
                    VvMeanDb:             currentDb.Value,
                    SoilMoistureScoreDry: dryScore,
                    DeltaScore:           deltaScore,
                    VvDeltaDb:            vvDeltaDb,
                    Periodo:              periodo,
                    Fonte:                fonte
                );

                // Cache del risultato combinato per 24 ore (sistema di sicurezza: reattività agli eventi meteo)
                _cache.Set(cacheKey, result, TimeSpan.FromHours(24));
                return result;
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

        // ─── Dry Baseline ──────────────────────────────────────────────────────────

        /// <summary>
        ///   Recupera (o restituisce dalla cache) il valore dB medio del periodo di baseline
        ///   "dry" (luglio-agosto anno precedente). Con cache di 30 giorni, ogni locazione
        ///   genera una sola chiamata API per l'intera stagione, evitando un dilagare di richieste.
        /// </summary>
        private async Task<double?> GetOrFetchDryBaselineAsync(double lat, double lng, string token)
        {
            string dryKey = $"sentinel:dry:{lat:F2},{lng:F2}";

            if (_cache.TryGetValue(dryKey, out double cachedDryDb))
                return cachedDryDb;

            int baselineYear  = DateTime.UtcNow.Year - 1;
            string dryFrom    = new DateTime(baselineYear, _options.DryBaselineMonthStart, 1).ToString("yyyy-MM-ddTHH:mm:ssZ");
            string dryTo      = new DateTime(baselineYear, _options.DryBaselineMonthEnd,   DateTime.DaysInMonth(baselineYear, _options.DryBaselineMonthEnd))
                                    .ToString("yyyy-MM-ddTHH:mm:ssZ");

            _logger.LogInformation("Fetching dry baseline for {Lat:F4},{Lng:F4} [{From} → {To}]", lat, lng, dryFrom, dryTo);
            double? dryDb = await FetchMeanVvDbAsync(lat, lng, token, dryFrom, dryTo);

            if (dryDb.HasValue)
                // Baseline fisso per l'anno → cache 30 giorni
                _cache.Set(dryKey, dryDb.Value, TimeSpan.FromDays(30));

            return dryDb;
        }

        // ─── Core SAR fetch ────────────────────────────────────────────────────────

        /// <summary>
        ///   Chiama la Process API di Copernicus per l'area/periodo specificati e
        ///   restituisce il valore medio in dB della banda VV.
        ///
        ///   IMPORTANTE — Ordine operazioni (identico al notebook Python):
        ///   1. Recupera i valori lineari (FLOAT32) da ogni pixel
        ///   2. Converte PRIMA ogni singolo pixel in dB: dB = 10 * log10(val)
        ///   3. POI calcola la media dei dB
        ///   Fare la media lineare prima e poi il log produrrebbe un risultato errato
        ///   per il teorema di Jensen (overestima sempre il valore reale).
        /// </summary>
        private async Task<double?> FetchMeanVvDbAsync(double lat, double lng, string token, string fromDate, string toDate)
        {
            double offset = _options.BboxOffsetDegrees; // default 0.01° ≈ 1.1km
            double minLng = lng - offset;
            double minLat = lat - offset;
            double maxLng = lng + offset;
            double maxLat = lat + offset;

            // Risoluzione pixel proporzionale a _options.SarResolutionMeters (default 20m/pixel)
            // Stesso approccio del notebook: bbox_to_dimensions(bbox, resolution=20)
            const double metersPerDegLat = 111_000.0;
            double cosLat    = Math.Cos(lat * Math.PI / 180.0);
            int pixelWidth   = Math.Max(10, (int)Math.Round((maxLng - minLng) * metersPerDegLat * cosLat / _options.SarResolutionMeters));
            int pixelHeight  = Math.Max(10, (int)Math.Round((maxLat - minLat) * metersPerDegLat           / _options.SarResolutionMeters));

            _logger.LogDebug("SAR request: bbox={MinLat},{MinLng} → {MaxLat},{MaxLng}  size={W}×{H}px",
                minLat, minLng, maxLat, maxLng, pixelWidth, pixelHeight);

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
                    width  = pixelWidth,
                    height = pixelHeight,
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

            var client  = _httpClientFactory.CreateClient("copernicus");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var content  = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await client.PostAsync(_options.ProcessUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Process API failed: {Status} - {Err}", response.StatusCode, err);
                return null;
            }

            var tiffBytes = await response.Content.ReadAsByteArrayAsync();
            return await ParseMeanVvDbAsync(tiffBytes);
        }

        /// <summary>
        ///   Legge il TIFF e calcola la media in dB secondo l'approccio corretto:
        ///   converti prima ogni pixel in dB, poi fai la media dei dB.
        /// </summary>
        private async Task<double?> ParseMeanVvDbAsync(byte[] tiffBytes)
        {
            string tempTiff = Path.Combine(Path.GetTempPath(), $"sentinel_{Guid.NewGuid():N}.tiff");
            try
            {
                await File.WriteAllBytesAsync(tempTiff, tiffBytes);
                using var tiff = Tiff.Open(tempTiff, "r");
                if (tiff == null) return null;

                int width       = tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
                int height      = tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
                int scanlineSize = tiff.ScanlineSize();
                byte[] buffer   = new byte[scanlineSize];

                double sumDb    = 0.0;
                int validPixels = 0;

                for (int row = 0; row < height; row++)
                {
                    tiff.ReadScanline(buffer, row);
                    for (int col = 0; col < width; col++)
                    {
                        float val = BitConverter.ToSingle(buffer, col * 4);

                        // Guard: scarta pixel no-data, NaN o Infinity
                        if (val > 0 && float.IsFinite(val))
                        {
                            // STEP 1: converti il singolo pixel in dB
                            double db = 10.0 * Math.Log10(val);
                            if (double.IsFinite(db))
                            {
                                sumDb += db;
                                validPixels++;
                            }
                        }
                    }
                }

                if (validPixels == 0) return null;

                // STEP 2: media dei valori in dB (non media lineare poi log)
                double resultDb = sumDb / validPixels;
                
                return resultDb;
            }
            finally
            {
                if (File.Exists(tempTiff))
                    File.Delete(tempTiff);
            }
        }

        // ─── Auth token ────────────────────────────────────────────────────────────

        private async Task<string?> GetAccessTokenAsync()
        {
            if (_cache.TryGetValue("copernicus:token", out string? cachedToken))
                return cachedToken;

            var client  = _httpClientFactory.CreateClient("copernicus");
            var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenUrl);

            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type",    "client_credentials"),
                new KeyValuePair<string, string>("client_id",     _options.ClientId),
                new KeyValuePair<string, string>("client_secret", _options.ClientSecret)
            });

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to authenticate with Copernicus API: {Status}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("access_token", out var tokenElement))
            {
                var t = tokenElement.GetString();

                // Leggi expires_in dalla risposta OAuth2 per una cache TTL dinamica
                // Default: 8 minuti se il campo non è presente (margine conservativo)
                int cacheSec = 480;
                if (doc.RootElement.TryGetProperty("expires_in", out var expiresEl))
                    cacheSec = Math.Max(60, expiresEl.GetInt32() - 120); // margine di 2 min

                _cache.Set("copernicus:token", t, TimeSpan.FromSeconds(cacheSec));
                return t;
            }

            return null;
        }

        // ─── Fallback da JSON statico ──────────────────────────────────────────────

        private async Task<SentinelData?> GetFallbackDataAsync(double queryLat, double queryLng)
        {
            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var gridPath   = Path.Combine(webRoot, "data", "soil_moisture_grid.json");
            var globalPath = Path.Combine(webRoot, "data", "soil_moisture_results.json");

            if (File.Exists(gridPath))
            {
                var json = await File.ReadAllTextAsync(gridPath);
                using var doc = JsonDocument.Parse(json);
                var grid = doc.RootElement.GetProperty("grid");

                double minDist = double.MaxValue;
                double bestVv  = -15.0;

                foreach (var cell in grid.EnumerateArray())
                {
                    var cLat = cell.GetProperty("lat").GetDouble();
                    var cLng = cell.GetProperty("lng").GetDouble();
                    var dist = Math.Sqrt(Math.Pow(cLat - queryLat, 2) + Math.Pow(cLng - queryLng, 2));
                    if (dist < minDist)
                    {
                        minDist = dist;
                        bestVv  = cell.GetProperty("vv_db").GetDouble();
                    }
                }

                // Ricalcola lo score dal vv_db grezzo usando le soglie correnti (_options)
                // così il fallback è sempre coerente con il calcolo live, indipendentemente
                // da con quali soglie è stato generato il JSON dal notebook Python.
                int bestScore = (int)Math.Clamp(
                    (bestVv - _options.DbDryThreshold) / (_options.DbSaturatedThreshold - _options.DbDryThreshold) * 100.0,
                    0, 100);

                var root    = doc.RootElement;
                string periodo = root.TryGetProperty("period", out var periodoEl) ? periodoEl.GetString() ?? "" : "";

                // Se il punto più vicino è troppo lontano (> 0.05 gradi, ~5km), consideriamo i dati non disponibili
                if (minDist > 0.05) return null;

                return new SentinelData(bestScore, bestVv, null, null, null, periodo, "⚠️ Fallback Grid (dati statici: " + periodo + ")");
            }
            else if (File.Exists(globalPath))
            {
                var json = await File.ReadAllTextAsync(globalPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                return new SentinelData(
                    SoilMoistureScore:    root.GetProperty("soil_moisture_score_sat").GetInt32(),
                    VvMeanDb:             root.GetProperty("vv_mean_db_saturated").GetDouble(),
                    SoilMoistureScoreDry: null,
                    DeltaScore:           null,
                    VvDeltaDb:            null,
                    Periodo:              root.TryGetProperty("saturated_period", out var p) ? p.GetString() ?? "" : "",
                    Fonte:                "⚠️ Fallback Global (dati statici: non aggiornato)"
                );
            }

            return null;
        }
    }
}
