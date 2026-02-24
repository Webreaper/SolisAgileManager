namespace SolisManager.Shared.Models;

public class OctopusConsumption
{
    public DateTime PeriodStart { get; set; }
    public decimal? DailyStandingCharge { get; set; }
    public string ImportTariff { get; init; } = string.Empty;
    public string ExportTariff { get; set; } = string.Empty;
    public decimal ImportConsumption { get; set; }
    public decimal ExportConsumption { get; set; }
    public decimal ImportPrice { get; set; }
    public decimal ExportPrice { get; set; }
    public decimal ImportCost => ImportPrice * ImportConsumption;
    public decimal ExportProfit => ExportPrice * ExportConsumption;
    public decimal NetCost => ImportCost - ExportProfit;
}

public enum GroupByType
{
    Day,
    Week,
    Month
};

public class GroupedConsumption
{
    public object? GroupingKey { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string Tariffs { get; set; } = string.Empty;
    public decimal AverageStandingCharge { get; set; }
    public decimal TotalImport { get; set; }
    public decimal TotalImportCost { get; set; }
    public decimal AverageImportPrice { get; set; }
    public decimal TotalExport { get; set; }
    public decimal TotalExportProfit { get; set; }
    public decimal AverageExportPrice { get; set; }
    public decimal NetCost => TotalImportCost - TotalExportProfit;
}