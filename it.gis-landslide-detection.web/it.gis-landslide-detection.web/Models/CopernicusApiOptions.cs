namespace it.gis_landslide_detection.web.Models;

public class CopernicusApiOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string TokenUrl { get; set; } = string.Empty;
    public string ProcessUrl { get; set; } = string.Empty;

    /// <summary>Risoluzione spaziale del dato Sentinel-1 GRD in metri (standard: 20m/pixel).</summary>
    public int SarResolutionMeters { get; set; } = 20;

    /// <summary>Soglia dB per suolo completamente secco (empirica, terreni appenninici).</summary>
    public double DbDryThreshold { get; set; } = -20.0;

    /// <summary>Soglia dB per suolo saturo d'acqua (empirica, terreni appenninici).</summary>
    public double DbSaturatedThreshold { get; set; } = -5.0;

    /// <summary>Offset in gradi per il bounding box attorno al punto query (~0.01° ≈ 1.1km).</summary>
    public double BboxOffsetDegrees { get; set; } = 0.01;

    /// <summary>Finestra temporale in giorni per il periodo "corrente" (wet candidate).</summary>
    public int CurrentPeriodDays { get; set; } = 30;

    /// <summary>
    ///   Mese di inizio del periodo di baseline "dry" (default: 7 = luglio).
    ///   NOTA: Valido per aree appenniniche/mediterranee dove l'estate è secca.
    ///   Per zone alpine o con neve in quota, considerare di spostare il baseline
    ///   a giugno o a un periodo specifico per la stagionalità locale.
    /// </summary>
    public int DryBaselineMonthStart { get; set; } = 7;

    /// <summary>
    ///   Mese di fine del periodo di baseline "dry" (default: 8 = agosto).
    ///   Vedere commento su DryBaselineMonthStart.
    /// </summary>
    public int DryBaselineMonthEnd { get; set; } = 8;
}

