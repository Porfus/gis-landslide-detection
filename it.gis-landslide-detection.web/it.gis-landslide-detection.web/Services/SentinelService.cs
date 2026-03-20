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
            "soil_moisture_risultati.json");
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
            Periodo:              root.TryGetProperty("periodo_saturo", out var p)
                ? p.GetString() ?? "" : "",
            Fonte:                root.TryGetProperty("fonte", out var f)
                ? f.GetString() ?? "" : ""
        );
    }
}