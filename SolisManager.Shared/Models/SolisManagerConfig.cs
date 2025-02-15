using System.Reflection;
using System.Text.Json;

namespace SolisManager.Shared.Models;

public record SolisManagerConfig
{
    private static string settingsFileName = "SolisManagerConfig.json";
    
    public string SolisAPIKey { get; set; } = string.Empty;
    public string SolisAPISecret { get; set; } = string.Empty;
    public string SolisInverterSerial { get; set; } = string.Empty;
    public string OctopusAccountNumber { get; set; } = string.Empty;
    public string OctopusAPIKey { get; set; } = string.Empty;
    public string OctopusProductCode { get; set; } = String.Empty;
    public int SlotsForFullBatteryCharge { get; set; }
    public int AlwaysChargeBelowPrice { get; set; } = 10;
    public int? AlwaysChargeBelowSOC { get; set; } = null;
    public int LowBatteryPercentage { get; set; } = 25;
    public int MaxChargeRateAmps { get; set; } = 50;

    public string SolcastAPIKey { get; set; } = string.Empty;  
    public string SolcastSiteIdentifier { get; set; } = string.Empty;
    public decimal SolcastDampFactor { get; set; } = 1M; // Default to 100% of the solcast value
    public bool SolcastExtraUpdates { get; set; } = false;

    public bool IntelligentGoCharging { get; set; } = false;
    
    // Solcast Rules
    public bool SkipOvernightCharge { get; set; }
    public decimal ForecastThreshold { get; set; } = 20.0M;

    public decimal PeakPeriodBatteryUse { get; set; } = 0.5M;
    public bool Simulate { get; set; } = true;
    public string? LastComparisonTariff { get; set; }

    public bool AutoAdjustInverterTime { get; set; } = false;

    public List<ScheduledAction>? ScheduledActions { get; set; } = null;

    public async Task SaveToFile(string folder)
    {
        var configPath = Path.Combine(folder, settingsFileName);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(configPath, json);
    }
    
    public bool ReadFromFile(string folder)
    {
        var configPath = Path.Combine(folder, settingsFileName);

        if (File.Exists(configPath))
        {
            var content = File.ReadAllText(configPath);
            var settings = JsonSerializer.Deserialize<SolisManagerConfig>(content);
            
            settings.CopyPropertiesTo(this);
            return true;
        }

        return false;
    }

    public bool SolcastValid()
    {
        if (string.IsNullOrEmpty(SolcastSiteIdentifier)) return false;
        if (string.IsNullOrEmpty(SolcastAPIKey)) return false;
        return true;
    }

    public bool IsValid()
    {
        if (string.IsNullOrEmpty(SolisAPIKey)) return false;
        if (string.IsNullOrEmpty(SolisAPISecret)) return false;
        if (string.IsNullOrEmpty(SolisInverterSerial)) return false;

        if (string.IsNullOrEmpty(OctopusAPIKey) && string.IsNullOrEmpty(OctopusAccountNumber))
        {
            if (string.IsNullOrEmpty(OctopusProductCode))
                return false;
        }

        return true;
    }

    public bool TariffIsIntelligentGo => OctopusProductCode.Contains("-INTELLI-VAR-");
}