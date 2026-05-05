namespace it.gis_landslide_detection.web.Services;

/// <summary>
/// Implementazione del motore di calcolo pericolosità frane.
///
/// Modello Moltiplicativo Ibrido con tre meccanismi di correzione:
///   R1 — Flash Event Override:   pioggia >50 mm/h → floor al 75% del baseHazard
///   R2 — Saturation Floor:       sat.idx >80 → floor a 30 + sat.idx × 0.50
///   R3 — Pesi Stagionali:        in estate (giu–set), per Colamento rapido e
///                                 Scivolamento, wRain sale da 0.15 a 0.45
///
/// Formula base invariata:
///   saturationIndex = soilScore×wSoil + apiScore×wApi + currentRainScore×wRain
///   baseHazard      = max(iffiScore, ε=25)
///   triggerMult     = 0.3 + saturationIndex/100
///   hazardScore       = min(100, baseHazard × triggerMult)
///   hazardScore       = max(hazardScore, flashFloor, satFloor)   ← R1/R2
/// </summary>
public class HazardScoreEngine : IHazardScoreEngine
{
    // ── Soglie di override ────────────────────────────────────────────────
    private const double FlashRainThresholdMmh = 50.0;   // R1: soglia temporale V-shaped
    private const double FlashHazardFactor     = 0.75;   // R1: % del baseHazard garantita
    private const double SaturationFloorThreshold = 80.0; // R2: soglia sat.idx per attivare il floor
    private const double SaturationFloorBase   = 30.0;    // R2: base del floor additivo
    private const double SaturationFloorCoeff  = 0.50;    // R2: coefficiente lineare
    private const double Epsilon               = 25.0;    // Suscettibilità base per zone non mappate IFFI

    public HazardAssessment Calculate(
        double iffiHazardScore,
        string? iffiTipo,
        int soilMoistureScore,
        int apiScore,
        int currentRainScore,
        double precipMmh,
        bool weatherDataUnavailable = false,
        DateTime? referenceDate = null)
    {
        // ── 1. Pesi dinamici in base al tipo geofisico IFFI ──────────────
        double wSoil = 0.40;
        double wApi  = 0.35;
        double wRain = 0.25;

        if (iffiTipo == it.gis_landslide_detection.web.Models.IffiHazardTypes.CrolloRibaltamento)
        {
            // Roccia: non si imbeve. Conta la pioggia istantanea (fessurazione/pressione idrostatica)
            wSoil = 0.10;
            wApi  = 0.20;
            wRain = 0.70;
        }
        else if (iffiTipo is it.gis_landslide_detection.web.Models.IffiHazardTypes.ScivolamentoRotazionaleTraslativo or it.gis_landslide_detection.web.Models.IffiHazardTypes.ColamentoRapido)
        {
            // Terreno/Fango: saturazione e pioggia passata sono i trigger primari
            wSoil = 0.45;
            wApi  = 0.40;
            wRain = 0.15;
        }

        // ── R3: Pesi stagionali ──────────────────────────────────────────
        // In estate (giugno-settembre) il suolo è tipicamente secco e l'API è basso.
        // L'unico segnale realistico per un flash event è la pioggia istantanea.
        // Ribilanciamo i pesi per colamento rapido e scivolamento.
        int month = (referenceDate ?? DateTime.UtcNow).Month;
        bool isSummer = month >= 6 && month <= 9;

        if (isSummer && iffiTipo is it.gis_landslide_detection.web.Models.IffiHazardTypes.ColamentoRapido or it.gis_landslide_detection.web.Models.IffiHazardTypes.ScivolamentoRotazionaleTraslativo)
        {
            wSoil = 0.20;  // era 0.45
            wApi  = 0.35;  // era 0.40
            wRain = 0.45;  // era 0.15
        }

        // ── 2. Indice di Saturazione Combinato ───────────────────────────
        double saturationIndex = (soilMoistureScore * wSoil)
                               + (apiScore * wApi)
                               + (currentRainScore * wRain);

        // ── 3. Pericolosità Finale (Modello Moltiplicativo Ibrido) ────────────
        double baseHazard = Math.Max(iffiHazardScore, Epsilon);
        double triggerMultiplier = 0.3 + (saturationIndex / 100.0);
        double hazardScore = Math.Min(100.0, baseHazard * triggerMultiplier);

        // ── R1: Flash Event Override ─────────────────────────────────────
        // Un temporale V-shaped (>50 mm/h) è un trigger indipendente dalla saturazione.
        // Nelle zone mappate garantisce CRITICAL. Nelle zone non mappate (Epsilon)
        // garantisce almeno livello HIGH (50.0) per sicurezza.
        bool flashOverrideApplied = false;
        if (precipMmh > FlashRainThresholdMmh)
        {
            double flashFloor = Math.Max(baseHazard * FlashHazardFactor, 50.0);
            if (flashFloor > hazardScore)
            {
                hazardScore = flashFloor;
                flashOverrideApplied = true;
            }
        }

        // ── R2: Saturation Floor ─────────────────────────────────────────
        // Condizioni meteo estreme devono poter sovrascrivere un IFFI score basso.
        // Con sat.idx > 80, il floor garantisce almeno HIGH indipendentemente dal tipo IFFI
        // (scenario Nov 2020, Acquasanta Terme: IFFI "Complesso"=40, sat.idx=81.7).
        bool saturationFloorApplied = false;
        if (saturationIndex > SaturationFloorThreshold)
        {
            double satFloor = SaturationFloorBase + (saturationIndex * SaturationFloorCoeff);
            if (satFloor > hazardScore)
            {
                hazardScore = satFloor;
                saturationFloorApplied = true;
            }
        }

        // ── 4. Clamp finale ──────────────────────────────────────────────
        hazardScore = Math.Min(100.0, hazardScore);

        // ── R4: Weather Data Fallback ────────────────────────────────────
        // Sistema B2B (Protezione Civile): assenza di dati non deve generare falsi
        // positivi (alert fatigue). Segnaliamo esplicitamente lo stato UNKNOWN.
        string level;
        if (weatherDataUnavailable)
        {
            hazardScore = -1.0; // Valore convenzionale per assenza dati
            level = "UNKNOWN";
        }
        else
        {
            level = hazardScore switch
            {
                >= 75 => "CRITICAL",
                >= 50 => "HIGH",
                >= 30 => "MEDIUM",
                _     => "LOW"
            };
        }

        return new HazardAssessment(
            HazardScore:               hazardScore,
            HazardLevel:               level,
            SaturationIndex:         saturationIndex,
            TriggerMultiplier:       triggerMultiplier,
            BaseHazard:              baseHazard,
            WSoil:                   wSoil,
            WApi:                    wApi,
            WRain:                   wRain,
            FlashOverrideApplied:    flashOverrideApplied,
            SaturationFloorApplied:  saturationFloorApplied,
            WeatherDataUnavailable:  weatherDataUnavailable
        );
    }
}
