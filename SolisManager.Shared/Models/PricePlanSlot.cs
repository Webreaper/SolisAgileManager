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

public record OctopusRate
{
    public decimal value_inc_vat { get; set; }
    public DateTime valid_from { get; set; }
    public DateTime? valid_to { get; set; } = DateTime.MaxValue;
}

public record PricePlanSlot
{
    public decimal value_inc_vat { get; set;  }
    public DateTime valid_from { get; set;  }
    public DateTime valid_to { get; set;  } = DateTime.UtcNow;
    public bool Daytime { get; set; } = true;
    public PriceType PriceType { get; set; } = PriceType.Average;
    public SlotAction PlanAction { get; set; } = SlotAction.DoNothing;
    
    public string ActionReason { get; set; } = string.Empty;
    public Guid Id { get; set; } = Guid.NewGuid();
    public decimal? pv_est_kwh { get; set; }

    public SlotOverride? ManualOverride { get; set; }
    public SlotOverride? ScheduledOverride { get; set; }
    public SlotOverride? AutoOverride { get; set; }

    public (SlotAction action, string type, int? overrideAmps, string reason) ActionToExecute {
        get
        {
            // Default to the standard plan
            var action = PlanAction;
            var actionType = "Plan";
            string? reason = null;
            int? overrideAmps = null;

            if (ManualOverride != null)
            {
                // Manual overrides are the highest priority
                action = ManualOverride.Action;
                reason = ManualOverride.Explanation;
                overrideAmps = ManualOverride.OverrideAmps;
                actionType = "Manual";
            }
            else if (AutoOverride != null)
            {
                // Then auto overrides like SOC and IOG
                action = AutoOverride.Action;
                reason = AutoOverride.Explanation;
                overrideAmps = AutoOverride.OverrideAmps;
                actionType = "Auto";
            }
            else if (ScheduledOverride != null)
            {
                // Scheduled overrides next
                action = ScheduledOverride.Action;
                reason = ScheduledOverride.Explanation;
                overrideAmps = ScheduledOverride.OverrideAmps;
                actionType = "Scheduled";
            }

            reason ??= ActionReason;
            
            return (action, actionType, overrideAmps, reason);
        }
    }
    
    public override string ToString()
    {
        var act = ActionToExecute;
        return $"{valid_from:dd-MMM-yyyy HH:mm}-{valid_to:HH:mm}: {act.type}={act.action.Humanize()} (price: {value_inc_vat}p/kWh, Reason: {ActionReason})";
    }
}

