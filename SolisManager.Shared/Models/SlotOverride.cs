namespace SolisManager.Shared.Models;

public enum AutoOverrideType
{
    None,
    IOGSmartCharge,
    ChargeIfLowBattery,
    AlwayChargeBelowSOC
}

public class SlotOverride
{
    public SlotAction Action { get; set; }
    public int? OverrideAmps { get; set; }
    public decimal? OverridePrice { get; set; }
    public AutoOverrideType Type { get; set; } = AutoOverrideType.None;
    public string? Explanation { get; set; }
}