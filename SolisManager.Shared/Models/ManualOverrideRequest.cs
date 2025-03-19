namespace SolisManager.Shared.Models;

public record ManualOverrideRequest
{
    public DateTime SlotStart { get; set; }
    public SlotAction NewAction { get; init; }

    public bool ClearManualOverride { get; init; } = false;

    public override string ToString()
    {
        return $"{SlotStart:HH:mm} - {NewAction}";
    }
}