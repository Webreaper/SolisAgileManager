using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SolisManager.Shared;
using SolisManager.Shared.Interfaces;
using SolisManager.Shared.InverterConfigs;
using SolisManager.Shared.Models;

namespace SolisManager.Inverters.Solis;

/// A wrapper for the Solis API Based on Jon Glass's implementation
/// here: https://github.com/jmg48/solis-cloud
/// But extended to support setting charges (based on
/// https://github.com/stevegal/solis-control
public class SolisAPI : InverterBase<InverterConfigSolis>, IInverter
{
    private readonly MemoryCacheEntryOptions _cacheOptionsWeek =
        new MemoryCacheEntryOptions()
            .SetSize(1)
            .SetAbsoluteExpiration(TimeSpan.FromDays(7));

    private readonly MemoryCacheEntryOptions _cacheOptionsDay =
        new MemoryCacheEntryOptions()
            .SetSize(1)
            .SetAbsoluteExpiration(TimeSpan.FromDays(1));

    private readonly HttpClient client = new();
    private readonly ILogger logger;
    private readonly IUserAgentProvider userAgentProvider;
    private readonly IMemoryCache memoryCache;
    private bool? newFirmwareVersion;

    private int eepromWrites;
    private DateTime eepromCountDate = DateTime.UtcNow;
    private readonly bool slot1 = true;
    private readonly bool stateTracking;
    private static readonly CommandIDs[] excludedFromStateTracking = [CommandIDs.CheckFirmware, CommandIDs.SetInverterTime];

    // Command IDs (CIDs) from: https://oss.soliscloud.com/doc/SolisCloud_control_api_command_list.xls
    private enum CommandIDs
    {
        CheckFirmware = 6798,
        SetInverterTime = 56,
        
        // CIDs for the older firmware
        SetCharge = 103,
        ReadChargeState = 4643,

        // CIDs for the newer firmware
        ChargeSlot1_Time = 5946,
        ChargeSlot1_Amps = 5948,
        ChargeSlot1_SOC = 5928,
        
        ChargeSlot4_Time = 5955,
        ChargeSlot4_Amps = 5957,
        ChargeSlot4_SOC = 5931,
        
        DischargeSlot1_Time = 5964,
        DischargeSlot1_Amps = 5967,
        DischargeSlot1_SOC = 5965,

        DischargeSlot4_Time = 5976,
        DischargeSlot4_Amps = 5979,
        DischargeSlot4_SOC = 5977,

        ChargeTimeSlot1Switch = 5916,
        DischargeTimeSlot1Switch = 5922,
        ChargeTimeSlot4Switch = 5919,
        DischargeTimeSlot4Switch = 5925,
    }

    private readonly Dictionary<CommandIDs, string> commandState = new();

    private async Task<bool> CommandStateIsCorrect(CommandIDs cmdId, string newState)
    {
        // First try and read from the inverter 
        var existing = await ReadControlState(cmdId);
        
        if (existing == newState)
        {
            logger.LogInformation("EEPROM: No need to write {Cmd}\n   Old: {I}\n   New: {V}", cmdId.ToString(), existing, newState);
            return true;
        }

        logger.LogInformation("EEPROM: Need to write {Cmd}\n   Old: {I}\n   New: {V}", cmdId.ToString(), existing, newState);
        return false;
    }

    private void TrackStateChange(CommandIDs commandID, string newState)
    {
        if (!stateTracking || excludedFromStateTracking.Contains(commandID))
            return;
        
        // Save the state
        commandState[commandID] = newState;
    }
    
    public SolisAPI(SolisManagerConfig _config, IMemoryCache _cache, IUserAgentProvider _userAgentProvider, ILogger<SolisAPI> _logger)
    {
        SetInverterConfig(_config);
        
        userAgentProvider = _userAgentProvider;
        logger = _logger;
        memoryCache = _cache;
        client.BaseAddress = new Uri("https://www.soliscloud.com:13333");
        stateTracking = true;

        var altSlotValue = Environment.GetEnvironmentVariable("ALTERNATE_SLOT");
        var trackstate = Environment.GetEnvironmentVariable("TRACK_STATE");
        
        if (!string.IsNullOrEmpty(altSlotValue))
        {
            slot1 = false;
            logger.LogInformation("Alternate slot enabled via env var ALTERNATE_SLOT: slot 4 will be used");
        }

        if (!string.IsNullOrEmpty(trackstate) && trackstate.Equals("false", StringComparison.OrdinalIgnoreCase))
            stateTracking = false;

        logger.LogInformation("Inverter State Tracking {S}", stateTracking ? "enabled" : "disabled");
    }
    
    private async Task<InverterDetails?> InverterState()
    {
        ArgumentNullException.ThrowIfNull(inverterConfig);

        var result = await Post<InverterDetails>(1,"inverterDetail", 
            new { sn = inverterConfig.SolisInverterSerial
            });
        
        return result;
    }

    private async Task<int> GetFirmwareVersion()
    {
        ArgumentNullException.ThrowIfNull(inverterConfig);
        var firmwareVersion = 0;
        
        // Attempt to read it
        var result = await ReadControlState(CommandIDs.CheckFirmware);
        
        if (! string.IsNullOrEmpty(result) && int.TryParse(result, out firmwareVersion))
        {
            // Store this so we remember for next time
            inverterConfig.FirmwareVersion = firmwareVersion;
            logger.LogInformation("Firmware version: {V} ({H})", firmwareVersion, result);
        }
        else
        {
            // Otherwise, use stored config version
            if (inverterConfig?.FirmwareVersion != null)
            {
                firmwareVersion = inverterConfig.FirmwareVersion.Value;
                logger.LogWarning("Restored firmware version from config: {V} ({H})", firmwareVersion, firmwareVersion.ToString("X"));
            }
            else
                logger.LogError("Unable to determine firmware version");
        }

        return firmwareVersion;
    }
    
    
    private async Task<bool> IsNewFirmwareVersion()
    {
        if( newFirmwareVersion.HasValue )
            return newFirmwareVersion.Value;

        var sixSlotFirmwareVer =int.Parse("AA55", NumberStyles.HexNumber);

        var firmwareVersion = await GetFirmwareVersion();
        
        if(firmwareVersion >= sixSlotFirmwareVer )
        {
            // It's the new 6-slot firmware version
            newFirmwareVersion = true;

            logger.LogInformation("Detected 6-slot firmware version: {V} ({H})", firmwareVersion, firmwareVersion.ToString("X"));
        }
        else
        {
            // Assume it's the old one.
            newFirmwareVersion = false;
        }

        return newFirmwareVersion.Value;
    }
    
    private async Task<ChargeStateData?> ReadChargingState()
    {
        if (await IsNewFirmwareVersion())
        {
            var chargeAmp = await ReadControlStateInt(slot1 ? CommandIDs.ChargeSlot1_Amps : CommandIDs.ChargeSlot4_Amps);
            var chargeTime = await ReadControlState(slot1 ? CommandIDs.ChargeSlot1_Time : CommandIDs.ChargeSlot4_Time);
            var dischargeAmp = await ReadControlStateInt(slot1 ? CommandIDs.DischargeSlot1_Amps : CommandIDs.DischargeSlot4_Amps);
            var dischargeTime = await ReadControlState(slot1 ? CommandIDs.DischargeSlot1_Time  : CommandIDs.DischargeSlot4_Time);

            if (chargeAmp.HasValue && chargeTime != null && dischargeAmp.HasValue && dischargeTime != null)
            {
                return new ChargeStateData(
                    chargeAmp.Value,
                    dischargeAmp.Value,
                    chargeTime,
                    dischargeTime
                );
            }

            logger.LogWarning("Error reading inverter charge slot state");
        }
        else
        {
            var result = await ReadControlState(CommandIDs.ReadChargeState);

            if (!string.IsNullOrEmpty(result))
            {
                try
                {
                    return ChargeStateData.FromChargeStateData(result);
                }
                catch (Exception ex)
                {
                    // These are only warnings - if this call fails it just means we'll explicitly
                    // write to the inverter instead of doing a no-op if the inverter is already
                    // in the right state.
                    logger.LogWarning(ex, "Error reading inverter charge slot state");
                }
            }
        }

        return null;
    }

    private TimeOnly ParseTime(string time)
    {
        if (!TimeOnly.TryParse(time, out var result))
        {
            var parts = time.Split(':', 2);
            int hours = int.Parse(parts[0]);
            int minutes = int.Parse(parts[1]);

            if (hours > 24)
            {
                logger.LogWarning("Time returned from inverter was {H}hrs - wrapping...", hours);
                hours %= 24;
                time = $"{hours:D2}:{minutes:D2}";
                if (TimeOnly.TryParse(time, out result))
                    return result;
            }
        }
        else
            return result;

        throw new ArgumentException($"Invalid time pair {time}");
    }

    /// <summary>
    /// Convert a time-slot string, e.g., "05:30-10:45" into an actual date time
    /// so we can compare it.
    /// </summary>
    /// <param name="chargeTimePair"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    private (DateTime start, DateTime end) ConvertToRealDates(string chargeTimePair)
    {
        var now = DateOnly.FromDateTime(DateTime.Now);
        var nowTime = TimeOnly.FromDateTime(DateTime.Now);
        
        var parts = chargeTimePair.Split('-', 2, StringSplitOptions.TrimEntries);

        if (parts.Length != 2)
            throw new ArgumentException($"Invalid time pair {chargeTimePair}");

        var startTime = ParseTime(parts[0]);
        var endTime = ParseTime(parts[1]);

        var start = new DateTime(now, startTime);
        var end = new DateTime(now, endTime);

        if (startTime < nowTime) 
        {
            start = start.AddDays(1);    
            end = end.AddDays(1);    
        }

        if (endTime < startTime)
            end = end.AddDays(1);
        
        return (start, end);
    }
    
    /// <summary>
    /// Complicated logic to determine if the new charge and discharge settings are materially different
    /// to what's in the inverter at the moment. So we check the current/amps for charge and discharge,
    /// and also check whether or not the charging time for the new settings starts within the existing
    /// settings, and has the same end-time. If so, then there's no point submitting a change to the
    /// inverter, as it won't make any difference to the behaviour.
    /// </summary>
    /// <param name="chargePower"></param>
    /// <param name="dischargePower"></param>
    /// <param name="chargeTimes"></param>
    /// <param name="dischargeTimes"></param>
    /// <returns></returns>
    private async Task<bool> InverterNeedsUpdating(int chargePower, int dischargePower, string chargeTimes, string dischargeTimes)
    {
        // Get the current state of the inverter
        var currentChargeState = await ReadChargingState();

        // If for some reason we didn't get the current state, we'll *have* to write
        if (currentChargeState == null)
            return true;
        
        var currchargeTime = ConvertToRealDates(currentChargeState.chargeTimes);
        var currdischargeTime = ConvertToRealDates(currentChargeState.dischargeTimes);

        var newchargeTime = ConvertToRealDates(chargeTimes);
        var newdischargeTime = ConvertToRealDates(dischargeTimes);
        
        bool chargeIsEquivalent = newchargeTime.start >= currchargeTime.start &&
                                  newchargeTime.start <= currchargeTime.end &&
                                  newchargeTime.end == currchargeTime.end &&
                                  chargePower == currentChargeState.chargeAmps;

        bool dischargeIsEquivalent = newdischargeTime.start >= currdischargeTime.start &&
                                  newdischargeTime.start <= currdischargeTime.end &&
                                  newdischargeTime.end == currdischargeTime.end &&
                                  dischargePower == currentChargeState.dischargeAmps;

        if (!chargeIsEquivalent)
        {
            logger.LogInformation("  Inverter charge did not match request: {NS:dd-MMM HH:mm}-{NE:dd-MMM HH:mm} ({NP}A) does not match current: {CS:dd-MMM HH:mm}-{CE:dd-MMM HH:mm} ({CP}A)",
                    newchargeTime.start, newchargeTime.end, chargePower, currchargeTime.start, currchargeTime.end, currentChargeState.chargeAmps);
            return true;
        }

        if (!dischargeIsEquivalent)
        {
            logger.LogInformation("  Inverter discharge did not match request: {NS:dd-MMM HH:mm}-{NE:dd-MMM HH:mm} ({NP}A) does not match current: {CS:dd-MMM HH:mm}-{CE:dd-MMM HH:mm} ({CP}A)",
                newchargeTime.start, newchargeTime.end, chargePower, currchargeTime.start, currchargeTime.end, currentChargeState.chargeAmps);
            return true;
        }

        return false;
    }
    
    public async Task<bool> UpdateInverterState(SolisManagerState inverterState)
    {
        try
        {
            // Get the battery charge state from the inverter
            var solisState = await InverterState();

            if (solisState?.data != null)
            {
                var latestBatterySOC = solisState.data.batteryList
                    .Select(x => x.batteryCapacitySoc)
                    .FirstOrDefault();

                if (latestBatterySOC != 0)
                    inverterState.BatterySOC = latestBatterySOC;
                else
                    logger.LogInformation("Battery SOC returned as zero. Invalid inverter state data");

                inverterState.StationId = solisState.data.stationId;
                inverterState.CurrentPVkW = solisState.data.pac;
                inverterState.TodayPVkWh = solisState.data.eToday;
                inverterState.CurrentBatteryPowerKW = solisState.data.batteryPower;
                inverterState.TodayExportkWh = solisState.data.gridSellEnergy;
                inverterState.TodayImportkWh = solisState.data.gridPurchasedEnergy;
                inverterState.ImportPowerKW = solisState.data.psum > 0 ? 0 : Math.Abs(solisState.data.psum);
                inverterState.ExportPowerKW = Math.Max(0, solisState.data.psum);
                inverterState.InverterTemp = solisState.data.inverterTemperature;
                inverterState.HouseLoadkW = solisState.data.pac - solisState.data.psum - solisState.data.batteryPower;
                if (ParseTimeStr(solisState.data.timeStr, out var timestamp))
                    inverterState.InverterDataTimestamp = timestamp;
                else
                    inverterState.InverterDataTimestamp = DateTime.UtcNow;

                // Clear these each time
                inverterState.Sunrise = null;
                inverterState.Sunset = null;

                // Pass through the number of EEPROM writes so we can display in the UI
                inverterState.DailyEepromWrites = eepromWrites;

                if (long.TryParse(inverterState.StationId, out var stationId))
                {
                    var stationData = await GetStationData(stationId);

                    if (stationData != null)
                    {
                        if (TimeSpan.TryParse(stationData.data.sr, out var sunrise))
                            inverterState.Sunrise = sunrise;
                        if (TimeSpan.TryParse(stationData.data.ss, out var sunset))
                            inverterState.Sunset = sunset;
                        logger.LogDebug("Evaluted inverter sunrise: {Sr}, sunset: {Ss}", sunrise, sunset);
                    }
                }

                return true;
            }

            logger.LogError("No state returned from the inverter");
            return false;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Exception retrieving state from Solis Inverter");
            return false;
        }
    }
    
    /// <summary>
    /// Reads the state of a particular CID
    /// </summary>
    /// <param name="cid"></param>
    private async Task<string?> ReadControlState(CommandIDs cid)
    {
        ArgumentNullException.ThrowIfNull(inverterConfig);

        var result = await Post<AtReadResponse>(2, "atRead",
            new { inverterSn = inverterConfig.SolisInverterSerial, cid });

        if (result?.data != null && !string.IsNullOrEmpty(result.data.msg))
        {
            if (result.data.msg != "ERROR")
            {
                // Store the successfully-read value into the state tracker
                TrackStateChange(cid, result.data.msg);
                return result.data.msg;
            }

            logger.LogWarning("ERROR reading control state (CID = {C})", cid);
        }
        else
        {
            logger.LogWarning("No data returned reading control state (CID = {C})", cid);
        }

        // If there was no existing value, try and get it from the commandState
        if (commandState.TryGetValue(cid, out var trackedValue))
        {
            logger.LogWarning("Using value ({C}) from state-tracker", trackedValue);
            return trackedValue;
        }

        return null;
    }

    private static bool firstCall = true;
    private async Task EnableChargingSlots(bool simulateOnly)
    {
        await EnableSlot(slot1 ?CommandIDs.ChargeTimeSlot1Switch  : CommandIDs.ChargeTimeSlot4Switch, simulateOnly);
        await EnableSlot(slot1 ? CommandIDs.DischargeTimeSlot1Switch : CommandIDs.DischargeTimeSlot4Switch, simulateOnly);
        firstCall = false;
    }
    
    /// <summary>
    /// See if the slot is enabled
    /// </summary>
    /// <param name="commandId"></param>
    /// <param name="simulateOnly"></param>
    private async Task EnableSlot(CommandIDs commandId, bool simulateOnly)
    {
        var flag = await ReadControlStateInt(commandId);
        if (flag != null)
        {
            if (flag == 0)
            {
                logger.LogWarning("Slot {Cid} was not enabled - sending enable command...", commandId);
                // For the new firmware, set the Charge Time Slot switch to 'on' 
                await SendControlRequest(commandId, "1", simulateOnly);
            }
            else if (firstCall)
            {
                logger.LogInformation("Slot for {Cid} is enabled and ready to control", commandId);
            }
        }
        else
            logger.LogWarning("Unable to read {Cid} enable state", commandId.ToString());
    }
    
    private async Task<int?> ReadControlStateInt(CommandIDs cid)
    {
        var result = await ReadControlState(cid);
        
        if( result != null && int.TryParse(result, out var resultInt))
            return resultInt;

        return null;
    }

    /// <summary>
    /// Set the inverter to charge or discharge for a particular period
    /// All parameters passed in are UTC. This method will convert.
    /// </summary>
    /// <returns></returns>
    public async Task SetCharge(DateTime? chargeStart, DateTime? chargeEnd, 
                                          DateTime? dischargeStart, DateTime? dischargeEnd, 
                                          bool holdCharge, int? overrideAmps = null, bool simulateOnly = false)
    {
        ArgumentNullException.ThrowIfNull(inverterConfig);
        
        const string clearChargeSlot = "00:00-00:00";

        bool newFirmWare = await IsNewFirmwareVersion();
        
        var chargeTimes = clearChargeSlot;
        var dischargeTimes = clearChargeSlot;
        int chargePower = 0;
        int dischargePower = 0;

        if (chargeStart != null && chargeEnd != null)
        {
            chargeTimes = $"{chargeStart.Value:HH:mm}-{chargeEnd.Value:HH:mm}";
            chargePower = overrideAmps ?? inverterConfig.MaxChargeRateAmps;
        }
        
        if (dischargeStart != null && dischargeEnd != null)
        {
            dischargeTimes = $"{dischargeStart.Value:HH:mm}-{dischargeEnd.Value:HH:mm}";
            dischargePower = holdCharge ? 0 : (overrideAmps ?? inverterConfig.MaxChargeRateAmps);
        }
        
        // Now check if we actually need to do anything. No point making a write call to the 
        // inverter if it's already in the correct state. It's an EEPROM, so the fewer writes
        // we can do for longevity, the better.
        if (await InverterNeedsUpdating(chargePower, dischargePower, chargeTimes, dischargeTimes))
        {
            if(newFirmWare)
            {
                // Set the charge and discharge SOC. Should trigger the first time and then be a no-op
                var chargeSOC = 100;
                var dischargeSOC = inverterConfig.MinDischargeSOC;

                logger.LogInformation("Sending new charge instruction to {Inv}: {CA}, {DA}, {CT}, {DT}, SOC: {SoC}%, D-SOC: {DSoC}%", 
                    simulateOnly ? "mock inverter" : "Solis Inverter",
                    chargePower, dischargePower, chargeTimes, dischargeTimes, chargeSOC, dischargeSOC);

                // Ensure the charge slots are enabled
                await EnableChargingSlots(simulateOnly);

                await SendControlRequest(slot1 ? CommandIDs.ChargeSlot1_SOC : CommandIDs.ChargeSlot4_SOC, $"{chargeSOC}", simulateOnly);
                await SendControlRequest(slot1 ? CommandIDs.DischargeSlot1_SOC : CommandIDs.DischargeSlot4_SOC, $"{dischargeSOC}", simulateOnly);
                
                // Now, set the actual state.
                await SendControlRequest(slot1 ? CommandIDs.ChargeSlot1_Amps : CommandIDs.ChargeSlot4_Amps, chargePower, simulateOnly);
                await SendControlRequest(slot1 ? CommandIDs.ChargeSlot1_Time : CommandIDs.ChargeSlot4_Time, chargeTimes, simulateOnly);
                await SendControlRequest(slot1 ? CommandIDs.DischargeSlot1_Amps : CommandIDs.DischargeSlot4_Amps, dischargePower, simulateOnly);
                await SendControlRequest(slot1 ? CommandIDs.DischargeSlot1_Time : CommandIDs.DischargeSlot4_Time, dischargeTimes, simulateOnly);
            }
            else
            {
                // This is only used for the old FW
                var chargeValues = $"{chargePower},{dischargePower},{chargeTimes},{dischargeTimes},0,0,00:00-00:00,00:00-00:00,0,0,00:00-00:00,00:00-00:00";
        
                logger.LogInformation("Sending new charge instruction to {Inv}: {CA}, {DA}, {CT}, {DT}",
                    simulateOnly ? "mock inverter" : "Solis Inverter",
                    chargePower, dischargePower, chargeTimes, dischargeTimes);

                await SendControlRequest(CommandIDs.SetCharge, chargeValues, simulateOnly);
            }
        }
        else
        {
            logger.LogInformation("Inverter already in correct state ({CA}, {DA}, {CT}, {DT}) so no charge instructions need to be applied", 
                                                chargePower, dischargePower, chargeTimes, dischargeTimes);
        }
    }

    public async Task<IEnumerable<InverterFiveMinData>?> GetHistoricData(int dayOffset = 0)
    {
        var dayToQuery = DateTime.UtcNow.AddDays(-1 * dayOffset);
        return await GetHistoricData(dayToQuery);
    }

    private bool ParseTimeStr(string timeStr, out DateTime date)
    {
        return DateTime.TryParseExact(timeStr, "yyyy-MM-dd HH:mm:ss", 
            CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    public async Task<IEnumerable<InverterFiveMinData>?> GetHistoricData(DateTime dayToQuery)
    {
        var cacheKey = $"inverterDay-{dayToQuery:yyyyMMdd}";

        if (DateTime.UtcNow.Date != dayToQuery.Date)
        {
            // For previous days, see if we already have it cached. We don't cache for today
            // because as we move through the day it's going to change. :)
            if (memoryCache.TryGetValue(cacheKey, out IEnumerable<InverterFiveMinData>? inverterDay))
                return inverterDay;
        }

        var rawData = await GetInverterDayInternal(dayToQuery);

        var result = new List<InverterFiveMinData>();
        
        if (rawData != null)
        {
            var lastYieldTotal = 0M;
            var lastHouseTotal = 0M;
            var lastImportTotal = 0M;
            var lastExportTotal = 0M;

            foreach (var entry in rawData.data)
            {
                if (entry.homeLoadTodayEnergy < 0)
                {
                    logger.LogWarning("Discarding historic data with negative home load: {T}", entry.timeStr);
                    continue;
                }
                
                if (ParseTimeStr(entry.timeStr, out var date))
                {
                    result.Add(new InverterFiveMinData(date,
                        entry.batteryCapacitySoc,
                        entry.batteryPower,
                        entry.pSum / 1000M,
                        entry.familyLoadPower,
                        Math.Max(0,entry.homeLoadTodayEnergy - lastHouseTotal),
                        Math.Max(0, entry.eToday - lastYieldTotal),
                        Math.Max(0, entry.gridPurchasedTodayEnergy - lastImportTotal),
                        Math.Max(0, entry.gridSellTodayEnergy - lastExportTotal),
                        entry.inverterTemperature
                    ));

                    lastYieldTotal = entry.eToday;
                    lastHouseTotal = entry.homeLoadTodayEnergy;
                    lastImportTotal = entry.gridPurchasedTodayEnergy;
                    lastExportTotal = entry.gridSellTodayEnergy;
                }
            }
        }
        
        if (result.Any())
            memoryCache.Set(cacheKey, result, _cacheOptionsWeek);

        return result;
    }
    
    /// <summary>
    /// Get station metadata
    /// </summary>
    /// <returns></returns>
    private async Task<StationDetail?> GetStationData(long stationId)
    {
        var cacheKey = $"station-{stationId}";
        
        if (memoryCache.TryGetValue(cacheKey, out StationDetail? stationData))
            return stationData;

        ArgumentNullException.ThrowIfNull(inverterConfig);

        var result = await Post<StationDetail>(1, "stationDetail",
            new
            {
                id = stationId
            });

        if (result != null)
        {
            // Cache for 24 hours
            memoryCache.Set(cacheKey, result, _cacheOptionsDay);
            logger.LogInformation("Retrieved data for station {Id}", stationId);
        } 
        
        return result;
    }

    /// <summary>
    /// Get the historic graph data for the inverter
    /// </summary>
    /// <returns></returns>
    private async Task<InverterDayResponse?> GetInverterDayInternal(DateTime dayToQuery)
    {
        ArgumentNullException.ThrowIfNull(inverterConfig);

        var result = await Post<InverterDayResponse>(1, "inverterDay",
            new
            {
                sn = inverterConfig.SolisInverterSerial,
                money = "UKP",
                time = $"{dayToQuery:yyyy-MM-dd}",
                timezone = 0
            });

        if (result != null)
            logger.LogInformation("Retrieved {C} inverter stats for {D:dd-MMM-yyyy}", result.data.Count(), dayToQuery);

        return result;
    }

    public async Task UpdateInverterTime(bool simulateOnly)
    {
        var allowedDriftSecs = 30;
        var timeNow = DateTime.Now;
        var time = timeNow.ToString("yyyy-MM-dd HH:mm:ss");
        var clockDriftRetries = 0;

        while (clockDriftRetries++ < 4)
        {
            var attempt = clockDriftRetries == 1 ? string.Empty : $" (attempt {clockDriftRetries})";
            var currentTimeStr = await ReadControlState(CommandIDs.SetInverterTime);

            logger.LogInformation("Current inverter time is {T}", currentTimeStr);

            if (currentTimeStr != null && ParseTimeStr(currentTimeStr, out var inverterTime))
            {
                var timeDrift = Math.Abs((inverterTime - timeNow).TotalSeconds);

                if (timeDrift > allowedDriftSecs)
                {
                    logger.LogInformation("Updating inverter time to {T} avoid drift{A}...", time, attempt);

                    // Don't validate the call here - the value we'll get will *always* be different to what we set
                    await SendControlRequest(CommandIDs.SetInverterTime, time, simulateOnly, false);
                }
                else
                {    
                    logger.LogInformation("Inverter time drift ({T:N1}s) is within {A}s so no action required",
                        (int)timeDrift, allowedDriftSecs);
                    return;
                }
            }
            else
                logger.LogWarning("Inverter time was unavailable to check clock drift{A}", attempt);
        }
    }

    private async Task SendControlRequest(CommandIDs cmdId, int value, bool simulateOnly, bool validatePersistence = true)
    {
        await SendControlRequest(cmdId, value.ToString(), simulateOnly, validatePersistence);
    }

    /// <summary>
    /// Send the actual control request to the inverter. 
    /// </summary>
    /// <param name="cmdId"></param>
    /// <param name="newValue"></param>
    /// <param name="simulateOnly"></param>
    /// <param name="validatePersistence">If true, will read the value after the update to check it persisted correctly</param>
    private async Task SendControlRequest(CommandIDs cmdId, string newValue, bool simulateOnly, bool validatePersistence = true)
    {
        ArgumentNullException.ThrowIfNull(inverterConfig);

        var requestBody = new
        {
            inverterSn = inverterConfig.SolisInverterSerial,
            cid = (int)cmdId,
            value = newValue
        };
        
        if (simulateOnly)
        {
            logger.LogInformation("Simulated inverter control request: {B}", requestBody);
        }
        else
        {
            // Wait gradually longer and longer
            int[] backoffRetryDelays = [50, 200, 500, 1000, 5000];

            if (await CommandStateIsCorrect(cmdId, newValue))
                return;
            
            for (var attempt = 0; attempt < backoffRetryDelays.Length; attempt++)
            {
                // Actually write it. 
                await Post<object>(2, "control", requestBody);

                TrackStateChange(cmdId, newValue);
                LogEepromWrites();

                if (!validatePersistence)
                    break;
                
                // If we're validating the persistence worked, give it a chance to persist.
                await Task.Delay(backoffRetryDelays[attempt]);
                
                // Now try and read it back
                var result = await ReadControlState(cmdId);

                if (result == newValue)
                {
                    if (attempt > 0)
                        logger.LogInformation("Control request (CID: {C}, Value: {V}) succeeded on retry {A}",
                            cmdId, newValue, attempt);
                    return; // Success
                }

                logger.LogWarning("Inverter control request did not stick: CID: {C}, Expected: {V}, Actual: {A} (attempt: {Try})",
                    cmdId, newValue, result, attempt);
            }
        }
    }

    private void LogEepromWrites()
    {
        var now = DateTime.Now;

        if (eepromCountDate.Date != now.Date)
        {
            // Reset the counter when we have the first write of the day
            eepromWrites = 0;
            eepromCountDate = now;
        }

        eepromWrites++;
        
        logger.LogInformation("Total EEPROM writes today: {W}", eepromWrites);
    }

    private async Task<T?> Post<T>(int apiVersion, string resource, object body)
    {
        var content = JsonSerializer.Serialize(body);
        var response = await Post($"/v{apiVersion}/api/{resource}", content);
        if (!string.IsNullOrEmpty(response))
        {
            try
            {
                return JsonSerializer.Deserialize<T>(response);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error deserializing inverter response: {R}", response);
            }
        }

        logger.LogError("No response data returned from Solis API: Resource={R} Body={B}", resource, body);
        return default;
    }
    
    private async Task<string> Post(string url, string content)
    {
        ArgumentNullException.ThrowIfNull(inverterConfig);

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            var date = DateTime.UtcNow.ToString("ddd, d MMM yyyy HH:mm:ss 'GMT'");

            request.Content = new StringContent(content);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json;charset=UTF-8");

            var contentMd5 = Convert.ToBase64String(MD5.HashData(Encoding.UTF8.GetBytes(content)));
            var hmacSha1 = new HMACSHA1(Encoding.UTF8.GetBytes(inverterConfig.SolisAPISecret));
            var param = $"POST\n{contentMd5}\napplication/json\n{date}\n{url}";
            var sign = Convert.ToBase64String(hmacSha1.ComputeHash(Encoding.UTF8.GetBytes(param)));
            var auth = $"API {inverterConfig.SolisAPIKey}:{sign}";

            request.Headers.Add("Time", date);
            request.Headers.Add("Authorization", auth);
            request.Headers.Add("User-Agent", userAgentProvider.UserAgent );
            request.Content.Headers.Add("Content-Md5", contentMd5);

            var result = await client.SendAsync(request);

            result.EnsureSuccessStatusCode();

            logger.LogDebug("Posted request to SolisCloud: {U} {C}", url, content);

            return await result.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error posting request to SolisCloud: {U} {C}", url, content);
        }

        return string.Empty;
    }
    
    private async Task<IReadOnlyList<UserStation>> UserStationList(int pageNo, int pageSize)
    {
        var result = await Post<ListResponse<UserStation>>(1,"userStationList", new UserStationListRequest(pageNo, pageSize));
        if(result != null)
            return result.data.page.records;

        return [];
    }

    private async Task<IReadOnlyList<Inverter>> InverterList(int pageNo, int pageSize, int? stationId)
    {
        var result = await Post<ListResponse<Inverter>>(1,"inverterList", new InverterListRequest(pageNo, pageSize, stationId));
        if( result != null )
            return result.data.page.records;
        return [];
    }

    /// <summary>
    /// Get the historic graph data for the inverter
    /// </summary>
    /// <returns></returns>
    private async Task<StationEnergyDayResponse?> GetStationEnergyDay(int dayOffset = 0)
    {
        var dayToQuery = DateTime.UtcNow.AddDays(-1 * dayOffset);
        var cacheKey = $"stationDayEnergyList-{dayToQuery:yyyyMMdd}";
        
        if( memoryCache.TryGetValue(cacheKey, out StationEnergyDayResponse? inverterDay))
            return inverterDay;

        logger.LogInformation("Getting inverter stats for {D:dd-MMM-yyyy}...", dayToQuery);

        var result = await Post<StationEnergyDayResponse>(1, "stationDayEnergyList",
            new
            {
                pageNo = 0,
                pageSize = 100,
                time = $"{dayToQuery:yyyy-MM-dd}",
            });

        if (result != null)
            memoryCache.Set(cacheKey, result, _cacheOptionsWeek);

        return result;
    }
}
