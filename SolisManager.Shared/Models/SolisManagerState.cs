namespace SolisManager.Shared.Models;

public class SolisManagerState
{
    public DateTime PricesUpdate { get; set; }
    public IEnumerable<OctopusPriceSlot> Prices { get; set; } = [];
    public int BatterySOC { get; set; }
    public decimal CurrentBatteryPowerKW { get; set; }
    public decimal TodayPVkWh { get; set; }
    public decimal TodayExportkWh { get; set; }
    public decimal TodayImportkWh { get; set; }
    public decimal CurrentPVkW { get; set; }
    public decimal HouseLoadkW { get; set; }
    public decimal TodayForecastKWH { get; set; }
    public decimal TomorrowForecastKWH { get; set; }
    public string StationId { get; set; } = string.Empty;
    public DateTime InverterDataTimestamp { get; set; }
    public DateTime? SolcastTimeStamp { get; set; }
}