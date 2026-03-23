using System.Text.Json;

namespace it.gis_landslide_detection.web.Services;

public class SentinelService : ISentinelService
{
    private readonly IWebHostEnvironment _env;
 
    public SentinelService(IWebHostEnvironment env)
    {
        _env = env;
    }

    public async Task<SentinelData?> GetSoilMoistureAsync()
    {
        var path = Path.Combine(_env.WebRootPath, "data",
            "soil_moisture_results.json");
        if (!File.Exists(path)) return null;
 
        var json = await File.ReadAllTextAsync(path);
        var doc  = JsonDocument.Parse(json);
        var root = doc.RootElement;
 
        return new SentinelData(
            SoilMoistureScore:    root.GetProperty("soil_moisture_score").GetInt32(),
            VvMeanDb:             root.GetProperty("vv_mean_db").GetDouble(),
            SoilMoistureScoreDry: root.TryGetProperty("soil_moisture_score_dry", out var dry)
                ? dry.GetInt32() : 0,
            DeltaScore:           root.TryGetProperty("delta_score", out var delta)
                ? delta.GetDouble() : 0,
            Periodo:              root.TryGetProperty("saturated_period", out var p)
                ? p.GetString() ?? "" : "",
            Fonte:                root.TryGetProperty("source", out var f)
                ? f.GetString() ?? "" : ""
        );
    }

    public async Task<SentinelData?> GetSoilMoistureForPointAsync(double queryLat, double queryLng)
    {
        var gridPath = Path.Combine(_env.WebRootPath, "data",
                               "soil_moisture_grid.json");

        // Fallback al JSON globale se la griglia non è ancora disponibile
        if (!File.Exists(gridPath))
            return await GetSoilMoistureAsync();

        var json = await File.ReadAllTextAsync(gridPath);
        var doc = JsonDocument.Parse(json);
        var grid = doc.RootElement.GetProperty("grid");

        // Trova il punto della griglia più vicino alle coordinate richieste
        // usando distanza euclidea (sufficiente per piccole aree)
        double minDist = double.MaxValue;
        double bestVv = -15.0;
        int bestScore = 75;

        foreach (var cell in grid.EnumerateArray())
        {
            var cLat = cell.GetProperty("lat").GetDouble();
            var cLng = cell.GetProperty("lng").GetDouble();
            var dist = Math.Sqrt(Math.Pow(cLat - queryLat, 2) +
                                 Math.Pow(cLng - queryLng, 2));
            if (dist < minDist)
            {
                minDist = dist;
                bestVv = cell.GetProperty("vv_db").GetDouble();
                bestScore = cell.GetProperty("score").GetInt32();
            }
        }

        var root = doc.RootElement;
        var periodo = root.TryGetProperty("period", out var periodoEl)
            ? periodoEl.GetString() ?? ""
                : "";

        return new SentinelData(
            SoilMoistureScore: bestScore,
            VvMeanDb: bestVv,
            SoilMoistureScoreDry: 0,
            DeltaScore: 0,
            Periodo: periodo,
            Fonte: "Sentinel-1 SAR grid lookup"
        );

    }
}