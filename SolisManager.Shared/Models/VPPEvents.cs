namespace SolisManager.Shared.Models;

public record AxleVPPEventHistory
{
    public List<AxleHistoricEvent> events { get; init; } = [];
};

public record AxleHistoricEvent
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public bool Export { get; set; } = true;
    public decimal? KWH { get; set; }
    public decimal? Earnings { get; set; }
    public string UniqueID { get; init; } = GetUniqueId();
    
    public static string GetUniqueId() => Guid.NewGuid().ToString("N");
}