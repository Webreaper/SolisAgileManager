using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using SolisManager.Shared.Interfaces;

namespace SolisManager.Shared.InverterConfigs;

public class InverterConfigSolis : InverterConfigBase
{
    public string SolisAPIKey { get; set; } = string.Empty;
    public string SolisAPISecret { get; set; } = string.Empty;
    public string SolisInverterSerial { get; set; } = string.Empty;
    public int MaxChargeRateAmps { get; set; } = 50;
    public int MinDischargeSOC { get; set; } = 15;
    
    public int? FirmwareVersion { get; set; }
    
    [JsonIgnore]
    public override bool IsValid => !string.IsNullOrEmpty(SolisAPIKey) &&
                                               !string.IsNullOrEmpty(SolisAPISecret) &&
                                               !string.IsNullOrEmpty(SolisInverterSerial);
}