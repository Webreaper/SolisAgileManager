using Humanizer;

namespace SolisManager.Shared.Models;

public enum PriceType
{
    Average = 0,
    Cheapest,
    BelowThreshold,
    BelowAverage,
    Dropping,
    MostExpensive,
    Negative,
    IOGDispatch
}

public enum SlotAction
{
    DoNothing,
    Charge,
    ChargeIfLowBattery,
    Discharge, 
    Hold
}

public record OctopusPriceSlot
{
    public enum SlotOverrideType
    {
        None,
        Manual,
        Scheduled,
        NegativePrices
    };
    public decimal value_inc_vat { get; set;  }
    public DateTime valid_from { get; set;  }
    public DateTime valid_to { get; set;  }
    public PriceType PriceType { get; set; } = PriceType.Average;
    public SlotAction PlanAction { get; set; } = SlotAction.DoNothing;
    public SlotAction? OverrideAction { get; set; }
    public int? OverrideAmps { get; set; }
    public SlotOverrideType OverrideType { get; set; } = SlotOverrideType.None;
    
    public string ActionReason { get; set; } = string.Empty;
    public Guid Id { get; set; } = Guid.NewGuid();
    public decimal? pv_est_kwh { get; set; }

    public SlotAction ActionToExecute => OverrideAction ?? PlanAction;
    
    public override string ToString()
    {
        return $"{valid_from:dd-MMM-yyyy HH:mm}-{valid_to:HH:mm}: {ActionToExecute.Humanize()} (price: {value_inc_vat}p/kWh, Reason: {ActionReason})";
    }
}

