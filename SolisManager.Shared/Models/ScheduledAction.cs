using System.ComponentModel.DataAnnotations;

namespace SolisManager.Shared.Models;

public class ScheduledAction
{
    [Required] public TimeSpan? StartTime { get; set; }
    [Required] public SlotAction Action { get; set; }
    public bool Disabled { get; set; }
    public int? Amps { get; set; } = null;
    public int? SOCTrigger { get; set; } = null;
}