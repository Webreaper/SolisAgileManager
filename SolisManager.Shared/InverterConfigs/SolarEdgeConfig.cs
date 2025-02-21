using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using SolisManager.Shared.Interfaces;

namespace SolisManager.Shared.InverterConfigs;

public class InverterConfigSolarEdge : InverterConfigBase
{
    public string SolarEdgeAPIKey { get; set; } = string.Empty;
    public string SolarEdgeAPISecret { get; set; } = string.Empty;
    
    [JsonIgnore]
    public override bool IsValid => !string.IsNullOrEmpty(SolarEdgeAPIKey) &&
                                               !string.IsNullOrEmpty(SolarEdgeAPISecret);
}