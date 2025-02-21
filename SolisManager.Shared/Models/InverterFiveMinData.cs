namespace SolisManager.Shared.Models;

public record InverterFiveMinData(
    DateTime Start,
    decimal BatterySOC,
    decimal BatteryChargePowerKW,
    decimal CurrentPVYieldKW,
    decimal CurrentHouseLoadKW,
    decimal HomeLoadKWH,
    decimal PVYieldKWH,
    decimal ImportKWH,
    decimal ExportKWH
);
