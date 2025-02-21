using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using SolisManager.Shared.InverterConfigs;

namespace SolisManager.Shared.Interfaces;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "InverterType")]
[JsonDerivedType(typeof(InverterConfigBase), typeDiscriminator: "base")]
[JsonDerivedType(typeof(InverterConfigSolis), typeDiscriminator: "solis")]
[JsonDerivedType(typeof(InverterConfigSolarEdge), typeDiscriminator: "solaredge")]
public class InverterConfigBase
{
    [JsonIgnore] public virtual bool IsValid => false;
}