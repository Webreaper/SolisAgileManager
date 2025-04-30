namespace SolisManager.Shared.Models;

public class OctopusConsumption
{
    public DateTime PeriodStart { get; set; }
    public string Tariff { get; init; } = string.Empty;
    public decimal? DailyStandingCharge { get; set; }
    public decimal ImportConsumption { get; set; }
    public decimal ExportConsumption { get; set; }
    public decimal ImportPrice { get; set; }
    public decimal ExportPrice { get; set; }
    public decimal ImportCost => ImportPrice * ImportConsumption;
    public decimal ExportProfit => ExportPrice * ExportConsumption;
    public decimal NetCost => ImportCost - ExportProfit;
}