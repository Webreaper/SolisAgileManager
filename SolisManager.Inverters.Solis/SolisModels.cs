namespace SolisManager.Inverters.Solis;

internal record StationEnergyRecord(
    decimal energy,
    string date,
    decimal gridPurchasedEnergy,
    decimal gridSellEnergy
);

internal record StationEnergyDayResponse(string msg, StationEnergyDayRecords data);

internal record StationEnergyDayRecords(IEnumerable<StationEnergyRecord> recordss);

internal record InverterDayRecord(
    string timeStr,
    string dataTimestamp,
    decimal batteryCapacitySoc, // Battery charge level
    decimal batteryPower, // Battery Charge power
    decimal pSum, // Current PV Output
    decimal familyLoadPower, // House load
    decimal homeLoadTodayEnergy, // Total house load today
    decimal gridPurchasedTodayEnergy, // Cumulative import kwh
    decimal gridSellTodayEnergy, // Cumulative export kwh
    decimal pac, // Current grid import 
    string pacStr,
    decimal eToday // Today PV total
);

internal record InverterDayResponse(string msg, IEnumerable<InverterDayRecord> data);

internal record UserStationListRequest(int pageNo, int pageSize);

public record InverterListRequest(int pageNo, int pageSize, int? stationId);

internal record ListResponse<T>(Data<T> data);

internal record Data<T>(Page<T> page);

internal record Page<T>(int current, int pages, List<T> records);

internal record AtReadData(string msg);
internal record AtReadResponse(AtReadData? data);

internal record ChargeStateData( int chargeAmps, int dischargeAmps, string chargeTimes, string dischargeTimes)
{
    public static ChargeStateData FromChargeStateData(string msg)
    {
        var parts = msg.Split(',', 5, StringSplitOptions.TrimEntries);
        if (parts.Length == 5)
        {
            return new ChargeStateData(
                int.Parse(parts[0]),
                int.Parse(parts[1]),
                parts[2],
                parts[3]);
        }

        throw new AggregateException($"Unable to parse atRead response: {msg}");
    }
}

internal record InverterDetails(InverterData data);

internal record InverterData(
    IEnumerable<Battery> batteryList, 
    decimal eToday, 
    decimal pac, // Current PV output kW
    string stationId, 
    decimal batteryPower,
    decimal gridSellEnergy, // Today export KWH
    decimal homeLoadEnergy, // Today load KWH
    decimal gridPurchasedEnergy, // Today import KWH
    decimal psum, // Current grid output Kw
    string version,
    string timeStr);

internal record Battery(int batteryCapacitySoc);
internal record UserStation(string id, string installer, string installerId, double allEnergy1, double allIncome,
    double dayEnergy1, double dayIncome, double gridPurchasedTodayEnergy, double gridPurchasedTotalEnergy,
    double gridSellTodayEnergy, double gridSellTotalEnergy, double homeLoadTodayEnergy, double homeLoadTotalEnergy,
    double monthEnergy1, double power1, double yearEnergy1);

internal record Inverter(string id, string collectorId, string collectorSn, string dataTimestamp, string dataTimestampStr,
    double etoday1, double etotal1, double familyLoadPower, double gridPurchasedTodayEnergy, double gridSellTodayEnergy,
    double homeLoadTodayEnergy, double pac1, double pow1, double pow2, double power1, double totalFullHour,
    double totalLoadPower);