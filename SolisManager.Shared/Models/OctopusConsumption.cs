namespace SolisManager.Shared.Models;

public class OctopusConsumption
{
    public DateTime PeriodStart { get; set; }
    public decimal ImportConsumption { get; set; }
    public decimal ExportConsumption { get; set; }
}