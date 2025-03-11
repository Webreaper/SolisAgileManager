using System.Diagnostics;
using Octokit;
using SolisManager.APIWrappers;
using SolisManager.Extensions;
using SolisManager.Inverters.Solis;
using SolisManager.Shared;
using SolisManager.Shared.Interfaces;
using SolisManager.Shared.Models;

namespace SolisManager.Services;

public class InverterManager : IInverterManagerService, IInverterRefreshService
{
    public SolisManagerState InverterState { get; } = new();

    private readonly List<HistoryEntry> executionHistory = [];
    private const string executionHistoryFile = "SolisManagerExecutionHistory.csv";
    private readonly NewVersionResponse appVersion = new();
    private List<OctopusPriceSlot>? simulationData;
    private const int maxExecutionHistory = 180 * 48;

    private readonly SolisManagerConfig config;
    private readonly OctopusAPI octopusAPI;
    private readonly IInverter inverterAPI;
    private readonly SolcastAPI solcastApi;
    private readonly ILogger<InverterManager> logger;
    
    public InverterManager(
        SolisManagerConfig _config,
        OctopusAPI _octopusAPI,
        InverterFactory _inverterFactory,
        SolcastAPI _solcastApi,
        ILogger<InverterManager> _logger)
    {
        config = _config;
        octopusAPI = _octopusAPI;
        solcastApi = _solcastApi; 
        logger = _logger;

        var inverterImplementation = _inverterFactory.GetInverter();
        
        if( inverterImplementation != null )
            inverterAPI = inverterImplementation;
    }
    
    private void EnrichWithSolcastData(IEnumerable<OctopusPriceSlot>? slots)
    {
        var solcast = solcastApi.GetSolcastForecast();

        // Store the last update time
        InverterState.SolcastTimeStamp = solcastApi.lastAPIUpdate;
            
        if (solcast == null || !solcast.Any())
            return;

        // Calculate the totals for today and tomorrow
        InverterState.TodayForecastKWH = solcast.Where( x => x.PeriodStart.Date == DateTime.UtcNow.Date )
            .Sum(x => x.ForecastkWh);
        InverterState.TomorrowForecastKWH = solcast.Where( x => x.PeriodStart.Date == DateTime.UtcNow.Date.AddDays(1) )
            .Sum(x => x.ForecastkWh);

        if (slots == null || ! slots.Any())
            return;

        var lookup = solcast.ToDictionary(x => x.PeriodStart);

        var matchedData = false;
        foreach (var slot in slots)
        {
            if (lookup.TryGetValue(slot.valid_from, out var solcastEstimate))
            {
                slot.pv_est_kwh = solcastEstimate.ForecastkWh;
                matchedData = true;
            }
            else
            {
                // No data
                slot.pv_est_kwh = null;
            }
        }
        
        if( ! matchedData )
            logger.LogError("Solcast Data was retrieved, but no entries matched current slots");
    }

    private async Task AddToExecutionHistory(OctopusPriceSlot slot)
    {
        try
        {
            var newEntry = new HistoryEntry(slot, InverterState);
            var lastEntry = executionHistory.LastOrDefault();

            if (lastEntry == null || lastEntry.Start != newEntry.Start)
            {
                // Add the item
                executionHistory.Add(newEntry);

                // And write
                await WriteExecutionHistory();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to add entry to execution history");
        }
    }

    private async Task WriteExecutionHistory()
    {
        var historyFilePath = Path.Combine(Program.ConfigFolder, executionHistoryFile);

        var lines = executionHistory.TakeLast(maxExecutionHistory)
                                                    .Select(x => x.GetAsCSV());

        // And write
        await File.WriteAllLinesAsync(historyFilePath, lines);
    }
    
    /// <summary>
    /// Loads the execution history from disk if it's not already available
    /// </summary>
    private async Task LoadExecutionHistory()
    {
        try
        {
            var historyFilePath = Path.Combine(Program.ConfigFolder, executionHistoryFile);

            if (!executionHistory.Any() && File.Exists(historyFilePath))
            {
                var lines = await File.ReadAllLinesAsync(historyFilePath);
                logger.LogInformation("Loaded {C} entries from execution history file {F}", lines.Length,
                    executionHistoryFile);

                // At 48 slots per day, we store 180 days or 6 months of data
                var entries = lines.TakeLast(maxExecutionHistory)
                    .Select(HistoryEntry.TryParse)
                    .DistinctBy(x => x?.Start)
                    .Where(x => x != null)
                    .Select(x => x!)
                    .ToList();

                executionHistory.AddRange(entries);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load execution history");
        }
    }

    private async Task EnrichHistoryWithInverterData()
    {
        // Find any days where there's no data, and add them to the list to backfill.
        // Ignore export, because there's some days when we won't export anything
        var daysToProcess = executionHistory.GroupBy(x => x.Start.Date)
            .Where(x => x.Sum(r => r.ActualKWH) == 0 ||
                                                   x.Sum(r => r.ImportedKWH) == 0 ||
                                                   x.Sum(r => r.HouseLoadKWH) == 0 ||
                                                   // Temp bugfix for when we write import data as export data
                                                   x.All(r => r.ImportedKWH == r.ExportedKWH))
            .Select(x => x.Key)
            .OrderDescending()
            .ToList();
        
        var today = DateTime.UtcNow.Date;
        
        if( ! daysToProcess.Exists(x => x == today))
            daysToProcess.Insert(0, today);
        
        logger.LogInformation("Enriching history with PV yield for {D} days", daysToProcess.Count());

        var allData = new List<InverterFiveMinData>();

        foreach (var day in daysToProcess)
        {
            var data = await inverterAPI.GetHistoricData(day);

            if (data != null && data.Any())
                allData.AddRange(data);
        }

        var oneMinuteData = new List<(DateTime start, decimal actual, decimal import, decimal export, decimal load)>();

        foreach (var datapoint in allData)
        {
            // Split into minute sections
            foreach (var min in Enumerable.Range(0, 4))
            {
                oneMinuteData.Add( (datapoint.Start.AddMinutes(min), 
                            datapoint.PVYieldKWH / 5.0M,
                            datapoint.ImportKWH / 5.0M,
                            datapoint.ExportKWH / 5.0M,
                            datapoint.HomeLoadKWH / 5.0M
                            ));
            }
        }

        var lookup = executionHistory.ToDictionary(x => x.Start);
        var batches = oneMinuteData.GroupBy(x => x.start.GetRoundedToMinutes(30))
            .ToList();

        bool changes = false;
        
        foreach (var batch in batches)
        {
            if (lookup.TryGetValue(batch.Key, out var historyEntry))
            {
                historyEntry.ActualKWH = batch.Sum(x => x.actual);
                historyEntry.ImportedKWH = batch.Sum(x => x.import);
                historyEntry.ExportedKWH = batch.Sum(x => x.export);
                historyEntry.HouseLoadKWH = batch.Sum(x => x.load);
                changes = true;
            }
        }
        
        if( changes )
            await WriteExecutionHistory();
    }
    
    private async Task RefreshTariffDataAndRecalculate()
    {
        try
        {
            // Don't even attempt this if there's no config
            if (!config.IsValid())
                return;

            // Save the overrides
            var overrides = GetExistingManualSlotOverrides();

            // Our working set
            IEnumerable<OctopusPriceSlot> slots;

            if (config.Simulate && simulationData != null)
            {
                slots = simulationData;
            }
            else
            {
                logger.LogTrace("Refreshing data...");

                slots = await octopusAPI.GetOctopusRates(config.OctopusProductCode);

                // Stamp the last time we did an update
                InverterState.PricesUpdate = DateTime.UtcNow;
                
                LogSlotUpdateDetails(slots);
            }

            // Now reapply the overrides to the updated slots
            ApplyPreviouManualOverrides(slots, overrides);

            // And recalculate the plan
            await RecalculateSlotPlan(slots);

            // Do this last, as it uses a lot of API calls
            await EnrichHistoryWithInverterData();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexexpected error recalculating slot plan");
        }
    }

    private void LogSlotUpdateDetails(IEnumerable<OctopusPriceSlot> slots)
    {
        if (slots.Any())
        {
            var lastSlot = InverterState.Prices?.MaxBy(x => x.valid_from);

            var newlatestSlot = slots.MaxBy(x => x.valid_from);

            if (newlatestSlot != null && (lastSlot == null || newlatestSlot.valid_from > lastSlot.valid_from))
            {
                var newslots = (lastSlot == null ? slots : slots.Where(x => x.valid_from > lastSlot.valid_from))
                    .ToList();

                var newSlotCount = newslots.Count;
                var cheapest = newslots.Min(x => x.value_inc_vat);
                var peak = newslots.Max(x => x.value_inc_vat);

                logger.LogInformation(
                    "{N} new Octopus rates available to {L:dd-MMM-yyyy HH:mm} (cheapest: {C}p/kWh, peak: {P}p/kWh)",
                    newSlotCount, newlatestSlot.valid_to, cheapest, peak);
            }
        }        
    }

    /// <summary>
    /// Where the actual work happens - this gets evaluated every time a config
    /// setting is changed, or otherwise every 5 minutes.
    /// </summary>
    private async Task RecalculateSlotPlan(IEnumerable<OctopusPriceSlot> sourceSlots)
    {
        // Take a copy so we can reprocess
        var slots = sourceSlots.Clone();
        ArgumentNullException.ThrowIfNull(slots);

        await LoadExecutionHistory();
        
        // First, ensure the slots have the latest forecast data
        EnrichWithSolcastData(slots);
        
        // Regenerate the plan
        var processedSlots = EvaluateSlotActions(slots.ToArray());

        // If the tariff is IOG, apply any charging when there's smart-charge slots
        await ApplyIOGDispatches(processedSlots);

        InverterState.Prices = processedSlots;

        // Update the state
        if (config.Simulate)
            simulationData = processedSlots;

        // And execute
        await ExecuteSlotChanges(processedSlots);
    }

    private void ExecuteSimulationUpdates(IEnumerable<OctopusPriceSlot> slots)
    {
        if (config.Simulate)
        {
            var rnd = new Random();
            var firstSlot = slots.FirstOrDefault();

            if (firstSlot != null)
            {
                InverterState.BatterySOC = firstSlot.PlanAction switch
                {
                    SlotAction.Charge => Math.Min(InverterState.BatterySOC += 100 / config.SlotsForFullBatteryCharge,
                        100),
                    SlotAction.DoNothing => Math.Max(InverterState.BatterySOC -= rnd.Next(4, 7), 20),
                    SlotAction.Discharge => Math.Max(InverterState.BatterySOC -= 100 / config.SlotsForFullBatteryCharge,
                        20),
                    _ => InverterState.BatterySOC
                };
            }
        }
    }
    
    private IEnumerable<ChangeSlotActionRequest> GetExistingManualSlotOverrides()
    {
        return InverterState.Prices
            .Where(x => x.OverrideAction != null && x.OverrideType == OctopusPriceSlot.SlotOverrideType.Manual)
            .Select(x => new ChangeSlotActionRequest
            {
                SlotStart = x.valid_from,
                NewAction = x.OverrideAction!.Value
            });
    }

    private void ApplyPreviouManualOverrides(IEnumerable<OctopusPriceSlot> slots, IEnumerable<ChangeSlotActionRequest> overrides)
    {
        var lookup = overrides.ToDictionary(x => x.SlotStart);
        foreach (var slot in slots)
        {
            if (lookup.TryGetValue(slot.valid_from, out var overRide))
            {
                slot.OverrideAction = overRide.NewAction;
                slot.OverrideType = OctopusPriceSlot.SlotOverrideType.Manual;
            }
            else
            {
                slot.OverrideAction = null;
                slot.OverrideType = OctopusPriceSlot.SlotOverrideType.None;
            }
        }
    }
    
    private async Task ExecuteSlotChanges(IEnumerable<OctopusPriceSlot> slots)
    {
        var firstSlot = slots.FirstOrDefault();
        if (firstSlot != null)
        {
            if (!config.Simulate)
                await AddToExecutionHistory(firstSlot);

            var matchedSlots = slots.TakeWhile(x => x.ActionToExecute == firstSlot.ActionToExecute).ToList();

            if (matchedSlots.Any())
            {
                logger.LogDebug("Found {N} slots with matching action to conflate", matchedSlots.Count);

                // The timespan is from the start of the first slot, to the end of the last slot.
                var start = matchedSlots.First().valid_from;
                var end = matchedSlots.Last().valid_to;

                if (firstSlot.ActionToExecute == SlotAction.Charge)
                {
                    await inverterAPI.SetCharge(start, end, null, null, false, 
                        firstSlot.OverrideAmps, config.Simulate);
                }
                else if (firstSlot.ActionToExecute == SlotAction.Discharge)
                {
                    await inverterAPI.SetCharge(null, null, start, end, false, 
                        firstSlot.OverrideAmps, config.Simulate);
                }
                else if (firstSlot.ActionToExecute == SlotAction.Hold)
                {
                    await inverterAPI.SetCharge(null, null, start, end, true, null, config.Simulate);
                }
                else
                {
                    // Clear the charge
                    await inverterAPI.SetCharge(null, null, null, null, false, null, config.Simulate);
                }
            }
        }
    }
    
    /// <summary>
    /// The main strategy calculation. Gets evaluated at least every 5 minutes
    /// </summary>
    /// <param name="slots"></param>
    /// <returns></returns>
    private List<OctopusPriceSlot> EvaluateSlotActions(OctopusPriceSlot[]? slots)
    {
        if (slots == null)
            return [];

        logger.LogTrace("Evaluating slot actions...");

        try
        {
            // First, reset all the slot states
            foreach (var slot in slots)
            {
                slot.PlanAction = SlotAction.DoNothing;
                slot.ActionReason = "Average price - no charge or discharge required";
                slot.OverrideAmps = null;
            }
            
            OctopusPriceSlot[]? cheapestSlots = null;
            OctopusPriceSlot[]? priciestSlots = null;
            decimal cheapestPrice = 100, mostExpensivePrice = 0;
            
            // See what the difference is between the target SOC and what we need now.
            decimal chargeNeededForPeak = Math.Max(0, config.PeakPeriodBatteryUse - (InverterState.BatterySOC / 100.0M));
            int chargeSlotsNeeededNow = 0;
            
            // See if we actually need a charge
            if (chargeNeededForPeak > 0)
            {
                // Calculate how many slots we'd need to charge from full starting *right now*
                chargeSlotsNeeededNow = (int)Math.Round(config.SlotsForFullBatteryCharge * chargeNeededForPeak,
                    MidpointRounding.ToPositiveInfinity);

                // First, find the cheapest period for charging the battery. This is the set of contiguous
                // slots, long enough when combined that they can charge the battery from empty to full, and
                // that has the cheapest average price for that period. This will typically be around 1am in 
                // the morning, but can shift around a bit. 
                for (var i = 0; i <= slots.Length - chargeSlotsNeeededNow; i++)
                {
                    var chargePeriod = slots[i .. (i + chargeSlotsNeeededNow)];
                    var chargePeriodTotal = chargePeriod.Sum(x => x.value_inc_vat);

                    if (cheapestSlots == null || chargePeriodTotal < cheapestSlots.Sum(x => x.value_inc_vat))
                        cheapestSlots = chargePeriod;
                }

                if (cheapestSlots != null && cheapestSlots.First().valid_from == slots[0].valid_from)
                {
                    // If the cheapest period starts *right now* then reduce the number of slots
                    // required down based on the battery SOC. E.g., if we've got 6 slots, but
                    // the battery is 50% full, we don't need all six. So take the n cheapest. 
                    cheapestSlots = cheapestSlots.OrderBy(x => x.value_inc_vat)
                        .Take(chargeSlotsNeeededNow)
                        .ToArray();
                }
            }

            // Similar calculation for the peak period.
            int peakPeriodLength = 7; // Peak period is usually 4pm - 7:30pm, so 7 slots.
            for (var i = 0; i <= slots.Length - peakPeriodLength; i++)
            {
                var peakPeriod = slots[i .. (i + peakPeriodLength)];
                var peakPeriodTotal = peakPeriod.Sum(x => x.value_inc_vat);

                if (priciestSlots == null || peakPeriodTotal > priciestSlots.Sum(x => x.value_inc_vat))
                    priciestSlots = peakPeriod;
            }

            // First, mark the priciest slots as 'peak'. That way we'll avoid them at all cost.
            if (priciestSlots != null)
            {
                foreach (var slot in priciestSlots)
                {
                    slot.PriceType = PriceType.MostExpensive;
                    slot.ActionReason = "Peak price slot - avoid charging";
                    
                    if( slot.value_inc_vat > mostExpensivePrice )
                        mostExpensivePrice = slot.value_inc_vat;
                }
            }

            if (cheapestSlots != null)
            {
                // Now mark the cheapest slots - unless they happen to coincide with the most expensive
                // which can happen in scenarios where there's only 5-10 slots before the new tariff 
                // data comes in. This is really a display issue, as by the time we get to these slots
                // we'll have more data and a better cheaper slot will have been found
                foreach (var slot in cheapestSlots.Where(x => x.PriceType != PriceType.MostExpensive))
                {
                    slot.PriceType = PriceType.Cheapest;
                    slot.PlanAction = SlotAction.Charge;
                    slot.ActionReason = "This is the cheapest set of slots, to fully charge the battery";

                    if( slot.value_inc_vat < cheapestPrice )
                        cheapestPrice = slot.value_inc_vat;
                }
            }

            // We've calculated the most expensive price and the cheapest price. So go through and
            // find any slots which have the same peak or cheap price and categorise them the same.
            // This will make it more consisten for all tariffs - those like Go and Cosy will show
            // all cheapest and most expensive categorisations.
            foreach (var slot in slots)
            {
                if (slot.value_inc_vat == cheapestPrice)
                {
                    slot.PriceType = PriceType.Cheapest;
                    slot.ActionReason = "This is the cheapest set of slots, to fully charge the battery";
                }

                if (slot.value_inc_vat == mostExpensivePrice)
                {
                    slot.PriceType = PriceType.MostExpensive;
                    slot.ActionReason = "Peak price slot - avoid charging";
                }
            }
            
            // Now, we've calculated the cheapest and most expensive slots. From the remaining slots, calculate
            // the average rate across them. We then use that average rate to determine if any other slots across
            // the day are a bit cheaper. So look for anything that's 90% of the average, or below, and mark it
            // as BelowAverage. For those slots, if the battery is low, we'll take the opportunity to charge as 
            // they're a bit cheaper-than-average.
            var averagePriceSlots = slots.Where(x => x.PriceType == PriceType.Average).ToList();

            if (averagePriceSlots.Any())
            {
                var averagePrice = decimal.Round(averagePriceSlots.Average(x => x.value_inc_vat), 2);
                decimal cheapThreshold = averagePrice * (decimal)0.9;

                foreach (var slot in slots.Where(x =>
                             x.PriceType == PriceType.Average && x.value_inc_vat < cheapThreshold))
                {
                    slot.PriceType = PriceType.BelowAverage;
                    slot.PlanAction = SlotAction.ChargeIfLowBattery;
                    slot.ActionReason =
                        $"Price is at least 10% below the average price of {averagePrice}p/kWh, so flagging as potential top-up";
                }
            }

            if (cheapestSlots != null)
            {
                // If we have a set of cheapest slots, then the price will usually start to 
                // drop a few slots before it's actually cheapest; these will likely be 
                // slots that are BelowAverage pricing in the run-up to the cheapest period.
                // However, we don't want to charge then, because otherwise by the time we
                // get to the cheapest period, the battery will be full. So back up n slots
                // and even if they're BelowAverage, remove their charging instruction.
                var firstCheapest = cheapestSlots.First();

                bool beforeCheapest = false;
                int dipSlots = config.SlotsForFullBatteryCharge;
                
                foreach (var slot in slots.Reverse())
                {
                    if (slot.Id == firstCheapest.Id)
                    {
                        beforeCheapest = true;
                        continue;
                    }

                    if (beforeCheapest && slot.PriceType == PriceType.BelowAverage)
                    {
                        slot.PriceType = PriceType.Dropping;
                        slot.PlanAction = SlotAction.DoNothing;
                        slot.ActionReason = "Price is falling in the run-up to the cheapest period, so don't charge";
                        dipSlots--;
                        if (dipSlots == 0)
                            break;
                    }
                }
            }

            if (priciestSlots != null)
            {
                // If we have a set of priciest slots, we want to charge before them. Now, it doesn't 
                // matter if the charging slots aren't all contiguous - so we can have a bit of 
                // flexibility. We also only need to charge the battery enough to get us to the 
                // PeakPeriodBatteryUse percentage (e.g., 50%). 
                var chargeSlotChoices = slots.GetPreviousNItems(chargeSlotsNeeededNow + 2, x => x.valid_from == priciestSlots.First().valid_from);

                if (chargeSlotChoices.Any())
                {
                    // Get the pre-peak slot choices, sorted by price
                    var prePeakSlots = chargeSlotChoices.OrderBy(x => x.value_inc_vat)
                        .Take(chargeSlotsNeeededNow)
                        .ToList();

                    foreach (var prePeakSlot in prePeakSlots)
                    {
                        // It's expensive, but not terrible. Suck it up and charge
                        prePeakSlot.PlanAction = SlotAction.Charge;
                        prePeakSlot.ActionReason = $"Cheaper slot to ensure battery is charged to {config.PeakPeriodBatteryUse:P0} before the peak period";
                    }
                }
            }

            EvaluateSolcastThresholdRule(slots);
            EvaluatePriceBasedRules(slots);
            EvaluateChargeIfLowBatteryRule(slots);
            EvaluateScheduleActionRules(slots);
            EvaluateMaintainChargeRule(slots);
            EvaluateDumpAndRechargeIfFreeRule(slots);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected exception during slot action evaluation:");
        }

        return slots.ToList();
    }

    private void EvaluateDumpAndRechargeIfFreeRule(OctopusPriceSlot[] slots)
    {
        // Now it gets interesting. Find the groups of slots that have negative prices. So we
        // might end up with 3 negative prices, and another group of 7 negative prices. For any
        // groups that are long enough to charge the battery fully, discharge the battery for 
        // all the slots that aren't needed to recharge the battery. 
        // NOTE/TODO: We should check, and if any of the groups of negative slots are *now*
        // then we should factor in the SOC.
        var negativeSpans = slots.GetAdjacentGroups(x => x.PriceType == PriceType.Negative);

        foreach (var negSpan in negativeSpans)
        {
            if (negSpan.Count() > config.SlotsForFullBatteryCharge)
            {
                var dischargeSlots = negSpan.SkipLast(config.SlotsForFullBatteryCharge).ToList();

                dischargeSlots.ForEach(x =>
                {
                    x.PlanAction = SlotAction.Discharge;
                    x.OverrideAction = SlotAction.Discharge;
                    x.OverrideType = OctopusPriceSlot.SlotOverrideType.NegativePrices; 
                    x.ActionReason =
                        "Contiguous negative slots allow the battery to be discharged and charged again.";
                });
            }
        }
    }
    
    private void EvaluateScheduleActionRules(OctopusPriceSlot[] slots)
    {
        // Now apply any scheduled actions to the slots for the next 24-48 hours. 
        if (config.ScheduledActions != null && config.ScheduledActions.Any())
        {
            foreach (var slot in slots)
            {
                foreach (var scheduledAction in config.ScheduledActions.Where( x => !x.Disabled))
                {
                    if (scheduledAction.StartTime != null)
                    {
                        var actionTime = scheduledAction.StartTime.Value;
                        if (slot.valid_from.TimeOfDay == actionTime)
                        {
                            string reason = "Overridden by a scheduled action";
                            
                            if (scheduledAction.Action is SlotAction.Charge or SlotAction.Discharge)
                            {
                                var actionText = scheduledAction.Action.ToString().ToLower();
                                if( scheduledAction.Amps != null)
                                    reason += $" ({actionText} at {scheduledAction.Amps}A";
                                else
                                    reason += $" ({actionText}";
                            }

                            if (scheduledAction.SOCTrigger != null)
                            {
                                if (scheduledAction.Action == SlotAction.Charge &&
                                    InverterState.BatterySOC > scheduledAction.SOCTrigger)
                                    continue;

                                if (scheduledAction.Action == SlotAction.Discharge &&
                                    InverterState.BatterySOC < scheduledAction.SOCTrigger)
                                    continue;

                                var sign = scheduledAction.Action == SlotAction.Charge ? "is less than" : "is more than";
                                reason += $" while SOC {sign} {scheduledAction.SOCTrigger}%";
                            }

                            reason += ")";

                            slot.OverrideAction = scheduledAction.Action;
                            slot.OverrideAmps = scheduledAction.Amps;
                            slot.ActionReason = reason;
                            slot.OverrideType = OctopusPriceSlot.SlotOverrideType.Scheduled;
                        }
                    }
                }
            }
        }
    }
    
    private void EvaluateChargeIfLowBatteryRule(OctopusPriceSlot[] slots)
    {
        // Very occasionally if there's an error, the inverter state
        // returns zero as the SOC. So just ignore it and do nothing.
        if (InverterState.BatterySOC == 0)
        {
            logger.LogWarning("SOC is zero, so skipping charge if low battery due to bad inverter state data");
            return;
        }

        // For any slots that are set to "charge if low battery", update them to 'charge' if the 
        // battery SOC is, indeed, low. Only do this for enough slots to fully charge the battery.
        if (InverterState.BatterySOC < config.LowBatteryPercentage)
        {
            foreach (var slot in slots.Where(x => x.PlanAction == SlotAction.ChargeIfLowBattery)
                         .Take(config.SlotsForFullBatteryCharge))
            {
                slot.PlanAction = SlotAction.Charge;
                slot.ActionReason =
                    $"Upcoming slot is set to charge if low battery; battery is currently at {InverterState.BatterySOC}%";
            }
        }
    }

    private void EvaluateMaintainChargeRule(OctopusPriceSlot[] slots)
    {
        var firstSlot = slots.FirstOrDefault();

        if (firstSlot != null)
        {
            // Very occasionally if there's an error, the inverter state
            // returns zero as the SOC. So just ignore it and do nothing.
            if (InverterState.BatterySOC == 0)
            {
                logger.LogWarning("SOC is zero, so skipping maintain charge rule due to bad inverter state data");
                return;
            }

            // High precedence rule - if the 'Always charge below SOC' is set, we want to maintain
            // a minimum charge level. So we always charge if the battery is below this SOC. 
            // We check this every 30 minutes
            if (InverterState.BatterySOC < config.AlwaysChargeBelowSOC)
            {
                firstSlot.PlanAction = SlotAction.Charge;
                firstSlot.ActionReason =
                    $"Battery SOC % is below minimum threshold of {config.AlwaysChargeBelowSOC}%.";
            }
        }
    }

    private void EvaluatePriceBasedRules(OctopusPriceSlot[] slots)
    {
        var extraReason = string.Empty;

        if (config.SkipOvernightCharge && config.ForecastThreshold < InverterState.TomorrowForecastKWH * config.SolcastDampFactor)
            extraReason = " (even though forcast is above the threshold for tomorrow)";
            
        // If there are any slots below our "Blimey it's cheap" threshold, elect to charge them anyway.
        foreach (var slot in slots.Where(s => s.value_inc_vat < config.AlwaysChargeBelowPrice))
        {
            slot.PriceType = PriceType.BelowThreshold;
            slot.PlanAction = SlotAction.Charge;
            slot.ActionReason =
                $"Price is below the threshold of {config.AlwaysChargeBelowPrice}p/kWh, so always charge{extraReason}";
        }

        foreach (var slot in slots.Where(s => s.value_inc_vat < 0))
        {
            slot.PriceType = PriceType.Negative;
            slot.PlanAction = SlotAction.Charge;
            slot.ActionReason = "Negative price - always charge";
        }
    }

    private void EvaluateSolcastThresholdRule(OctopusPriceSlot[] slots)
    {
        var dampedForecast = config.SolcastDampFactor * InverterState.TomorrowForecastKWH;
        
        if (config.SkipOvernightCharge && config.ForecastThreshold < dampedForecast )
        {
            // If the 'skip overnight charge if forecast is good' setting is enabled, we check that.
            // First we need to find when 'night' is. Iterate through the slots, looking for the first
            // one where the forecast is zero. That's the start of night. Then the first one where the
            // forecast is non-zero, is the end of night. 
            // We could possibly do this by the sunrise/sunset data from the inverter, but this will 
            // do for now.
            DateTime? nightStart = null, nightEnd = null;

            foreach (var slot in slots)
            {
                if (nightStart == null && slot.pv_est_kwh == 0)
                    nightStart = slot.valid_from;

                if (nightStart != null && slot.pv_est_kwh > 0)
                {
                    nightEnd = slot.valid_to;
                    break;
                }
            }

            var overnightChargeSlots = slots.Where(x =>
                    x.valid_from >= nightStart &&
                    x.valid_to <= nightEnd &&
                    x.PlanAction == SlotAction.Charge)
                .ToList();

            logger.LogInformation("Forecast = {F:F2}kWh (so > {T}kWh). Found {C} overnight charge slots to skip between {S} => {E}", 
                dampedForecast, config.ForecastThreshold, overnightChargeSlots.Count, nightStart, nightEnd);

            foreach (var slot in overnightChargeSlots)
            {
                slot.PlanAction = SlotAction.DoNothing;
                slot.ActionReason = $"Skipping overnight charge due to forecast of {dampedForecast:F2}kWh tomorrow";
            }
        }
    }


    private void CreateSomeNegativeSlots(IEnumerable<OctopusPriceSlot> slots)
    {
        if (Debugger.IsAttached && ! slots.Any(x => x.value_inc_vat < 0))
        {
            var averageSlots = slots
                .OrderBy(x => x.valid_from)
                .Where(x => x.PriceType == PriceType.Average)
                .ToArray();
            
            var rand = new Random();
            var index = rand.Next(averageSlots.Length);
            List<OctopusPriceSlot> negs = [averageSlots[index]];
            for (int n = 0; n < rand.Next(5, 9); n++)
            {
                if (--index > 0)
                    negs.Add(averageSlots[index]);
            }

            foreach (var slot in negs)
                slot.value_inc_vat = (rand.Next(10, 100) / 10M) * -1;
        }
        
    }
    
    public Task RefreshInverterState()
    {
        // Nothing to do on the server side, the refresh is triggered by the scheduler
        return Task.CompletedTask;
    }

    private string lastStateMessage = string.Empty;
    public async Task UpdateInverterState()
    {
        if (!config.IsValid())
            return;

        try
        {
            InverterState.LastUpdate = DateTime.UtcNow;

            // Get the battery charge state from the inverter
            if (await inverterAPI.UpdateInverterState(InverterState))
            {
                var stateMsg = string.Format(
                    $"Refreshed state: SOC = {InverterState.BatterySOC}%, Current PV = {InverterState.CurrentPVkW}kW, " +
                    $"House Load = {InverterState.HouseLoadkW}kW, Forecast today: {InverterState.TodayForecastKWH}kWh, " +
                    $"tomorrow: {InverterState.TomorrowForecastKWH}kWh");

                if (stateMsg != lastStateMessage)
                {
                    lastStateMessage = stateMsg;
                    // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
                    logger.LogInformation(stateMsg);
                }
            }
            else
                logger.LogWarning("Unable to read state from inverter");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected exception during inverter state refresh");
        }
    }
    
    public async Task RefreshAgileRates()
    {
        await RefreshTariffDataAndRecalculate();
    }

    public async Task RecalculateSlotPlan()
    {
        await RecalculateSlotPlan(InverterState.Prices);
    }

    private async Task<bool> UpdateConfigWithOctopusTariff(SolisManagerConfig theConfig)
    {
        try
        {
            if (!string.IsNullOrEmpty(theConfig.OctopusAPIKey) && !string.IsNullOrEmpty(theConfig.OctopusAccountNumber))
            {
                var productCode =
                    await octopusAPI.GetCurrentOctopusTariffCode(theConfig.OctopusAPIKey,
                        theConfig.OctopusAccountNumber);

                if (!string.IsNullOrEmpty(productCode))
                {
                    if (theConfig.OctopusProductCode != productCode)
                        logger.LogInformation("Octopus product code has changed: {Old} => {New}",
                            theConfig.OctopusProductCode, productCode);

                    theConfig.OctopusProductCode = productCode;
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected exception during Octopus tariff check");
        }

        return false;
    }

    private async Task ApplyIOGDispatches(IEnumerable<OctopusPriceSlot> slots)
    {
        if (config is { TariffIsIntelligentGo: true, IntelligentGoCharging: true })
        {
            try
            {
                var dispatches = await octopusAPI.GetIOGSmartChargeTimes(config.OctopusAPIKey, config.OctopusAccountNumber);
                if (dispatches != null && dispatches.Any())
                {
                    var iogChargeSlots = new Dictionary<DateTime, OctopusPriceSlot>();
                    
                    foreach (var dispatch in dispatches)
                    {
                        if (dispatch.end <= DateTime.UtcNow)
                        {
                            logger.LogInformation("Unexpected past dispatch - ignoring... ({S} - {E}", dispatch.start, dispatch.end);
                            continue;
                        }
                        
                        foreach (var slot in slots)
                        {
                            if( slot.valid_from < dispatch.end && slot.valid_to >= dispatch.start)
                                iogChargeSlots.TryAdd(slot.valid_from, slot);
                        }
                    }

                    if (iogChargeSlots.Any())
                    {
                        // The smart charge price should be the same as the lowest price in the tariff data.
                        var iogPrice = slots.Min(x => x.value_inc_vat);
                        
                        logger.LogInformation("Applying charge action to {N} slots for IOG Smart-Charge", iogChargeSlots.Count);

                        foreach (var slot in iogChargeSlots.Values)
                        {
                            if (slot.PlanAction != SlotAction.Charge)
                            {
                                slot.value_inc_vat = iogPrice;
                                slot.PlanAction = SlotAction.Charge;
                                slot.ActionReason = "IOG Smart-Charge period";
                                slot.PriceType = PriceType.IOGDispatch;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected exception during IOG Dispatch Query");
            }
        }
    }

    public async Task RefreshTariff()
    {
        if (!string.IsNullOrEmpty(config.OctopusAPIKey) && !string.IsNullOrEmpty(config.OctopusAccountNumber))
        {
            logger.LogDebug("Executing Tariff Refresh scheduler");
            await UpdateConfigWithOctopusTariff(config);
        }
    }

    public async Task UpdateInverterTime()
    {
        try
        {
            if (config.AutoAdjustInverterTime)
            {
                await inverterAPI.UpdateInverterTime(config.Simulate);
            }
        }
        catch (Exception ex)
        { 
            logger.LogError(ex, "Unexpected exception during inverter time refresh");
        }
    }

    public async Task UpdateInverterDayData()
    {
        for (int i = 0; i < 7; i++)
        {
            // Call this to prime the cache with the last 7 days' inverter data
            await inverterAPI.GetHistoricData(i);
            // Max 3 calls every 5 seconds
            await Task.Delay(1750);
        }
    }

    public Task<List<HistoryEntry>> GetHistory()
    {
        return Task.FromResult(executionHistory);
    }

    public Task<SolisManagerConfig> GetConfig()
    {
        return Task.FromResult(config);
    }

    public async Task<ConfigSaveResponse> SaveConfig(SolisManagerConfig newConfig)
    {
        logger.LogInformation("Saving config to server...");

        var siteIds = SolcastAPI.GetSolcastSites(newConfig.SolcastSiteIdentifier);

        if (siteIds.Length > 2)
        {
            return new ConfigSaveResponse
            {
                Success = false,
                Message = "A maximum of two Solcast site IDs can be specified"
            };
        }
        
        if (siteIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != siteIds.Length)
        {
            return new ConfigSaveResponse
            {
                Success = false,
                Message = "Each specified Site ID must be unique"
            };
        }
        
        if (!string.IsNullOrEmpty(newConfig.OctopusAPIKey) && !string.IsNullOrEmpty(newConfig.OctopusAccountNumber))
        {
            try
            {
                var result = await UpdateConfigWithOctopusTariff(newConfig);

                if (!result)
                    throw new InvalidOperationException("Could not get product code");
            }
            catch(Exception ex)
            {
                logger.LogError(ex, "Octopus account lookup failed");
                return new ConfigSaveResponse
                {
                    Success = false,
                    Message = "Unable to get tariff details from Octopus. Please check your account and API key."
                };
            }
        }
        newConfig.CopyPropertiesTo(config);
        await config.SaveToFile(Program.ConfigFolder);
        
        // Update the inverter with the new config
        inverterAPI.SetInverterConfig(config);
        
        if (config.Simulate)
            await ResetSimulation();
        
        await RefreshTariffDataAndRecalculate();
        
        return new ConfigSaveResponse{ Success = true };
    }

    public async Task OverrideSlotAction(ChangeSlotActionRequest change)
    {
        logger.LogInformation("Updating slot action for {S} to {A}...", change.SlotStart, change.NewAction);
        await SetManualOverrides([change]);
    }

    private int NearestHalfHour(int minute) => minute - (minute % 30);

    private IEnumerable<ChangeSlotActionRequest> CreateOverrides(DateTime start, SlotAction action, int slotCount)
    {
        var currentSlot =  new DateTime(start.Year, start.Month, start.Day, start.Hour, NearestHalfHour(start.Minute), 0);

        List<ChangeSlotActionRequest> overrides = new();
        
        foreach (var slot in  Enumerable.Range(0, slotCount))
        {
            yield return new ChangeSlotActionRequest
            {
                NewAction = action,
                SlotStart = currentSlot
            };

            currentSlot = currentSlot.AddMinutes(30);
        }
    }

    public async Task TestCharge()
    {
        logger.LogInformation("Starting test charge for 5 minutes");
        var start = DateTime.UtcNow;
        var end = start.AddMinutes(5);
        
        // Explicitly pass false for 'simulate' - we always do this
        await inverterAPI.SetCharge(start, end, null, null, false, null, false);
    }

    public async Task ChargeBattery()
    {
        // Work out the percentage charge, and then calculate how many slots it'll take to achieve that
        double percentageToCharge = (100 - InverterState.BatterySOC) / 100.0;
        var slotsRequired = (int)Math.Round(config.SlotsForFullBatteryCharge * percentageToCharge,
            MidpointRounding.ToPositiveInfinity);

        var overrides = CreateOverrides(DateTime.UtcNow, SlotAction.Charge, slotsRequired).ToList();
        await SetManualOverrides(overrides);
    }

    public async Task DischargeBattery()
    {
        var overrides = CreateOverrides(DateTime.UtcNow, SlotAction.Discharge, CalculateDischargeSlots()).ToList();
        await SetManualOverrides(overrides);
    }

    private int CalculateDischargeSlots()
    {
        double slotsRequired = config.SlotsForFullBatteryCharge * (InverterState.BatterySOC / 100.0);
        return (int)Math.Round(slotsRequired, MidpointRounding.ToPositiveInfinity);
    }

    public async Task DumpAndChargeBattery()
    {
        var discharge = CreateOverrides(DateTime.UtcNow, SlotAction.Discharge, CalculateDischargeSlots()).ToList();
        var lastDischarge = discharge.Last().SlotStart.AddMinutes(30);
        var charge = CreateOverrides(lastDischarge, SlotAction.Charge, config.SlotsForFullBatteryCharge);
        await SetManualOverrides(discharge.Concat(charge).ToList());
    }

    private async Task SetManualOverrides(List<ChangeSlotActionRequest> overrides)
    {
        var lookup = InverterState.Prices.ToDictionary(x => x.valid_from);

        foreach (var overRide in overrides)
        {
            if (lookup.TryGetValue(overRide.SlotStart, out var slot))
            {
                if (slot.PlanAction == overRide.NewAction)
                {
                    // Clear the existing override
                    slot.OverrideAction = null;
                    slot.OverrideType = OctopusPriceSlot.SlotOverrideType.None;
                    logger.LogInformation("Cleared override: {S}", overRide);
                    continue;
                }

                // Set the override
                slot.OverrideAction = overRide.NewAction;
                slot.OverrideType = OctopusPriceSlot.SlotOverrideType.Manual;
                logger.LogInformation("Set override: {S}", overRide);
            }
        }

        await RecalculateSlotPlan(InverterState.Prices);
    }
    
    public async Task ClearManualOverrides()
    {
        foreach (var slot in InverterState.Prices.Where(x => 
                     x is { OverrideAction: not null, OverrideType: OctopusPriceSlot.SlotOverrideType.Manual }))
        {
            slot.OverrideAction = null;
            slot.OverrideType = OctopusPriceSlot.SlotOverrideType.None;
        }
        await RecalculateSlotPlan(InverterState.Prices);
    }

    public async Task AdvanceSimulation()
    {
        if (config.Simulate)
        {
            if (simulationData is { Count: > 0 })
            {
                // Apply some charging or discharging for the slot that's about to drop off
                ExecuteSimulationUpdates(simulationData);

                simulationData.RemoveAt(0);
                await RefreshTariffDataAndRecalculate();
            }
            else
            {
                await ResetSimulation();
            }
        }
    }

    public async Task ResetSimulation()
    {
        if (config.Simulate)
        {
            simulationData = null;
            await RefreshTariffDataAndRecalculate();
        }
    }

    public Task<NewVersionResponse> GetVersionInfo()
    {
        return Task.FromResult(appVersion);
    }

    public async Task<OctopusProductResponse?> GetOctopusProducts()
    {
        return await octopusAPI.GetOctopusProducts();
    }

    public async Task<OctopusTariffResponse?> GetOctopusTariffs(string product)
    {
        return await octopusAPI.GetOctopusTariffs(product);
    }

    public async Task CheckForNewVersion()
    {
        try
        {
            var client = new GitHubClient(new ProductHeaderValue("SolisAgileManager"));

            var newRelease = await client.Repository.Release.GetLatest("webreaper", "SolisAgileManager");
            if (newRelease != null && Version.TryParse(newRelease.TagName, out var newVersion))
            {
                appVersion.NewVersion = newVersion;
                appVersion.NewReleaseName = newRelease.Name;
                appVersion.ReleaseUrl = newRelease.HtmlUrl;

                if( appVersion.UpgradeAvailable )
                    logger.LogInformation("A new version of Damselfly is available: {N}", newRelease.Name);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Unable to check GitHub for latest version: {E}", ex);
        }
    }

    public async Task<TariffComparison> GetTariffComparisonData(string tariffA, string tariffB)
    {
        logger.LogInformation("Running comparison for {A} vs {B}...", tariffA, tariffB);

        var ratesATask = octopusAPI.GetOctopusRates(tariffA);
        var ratesBTask = octopusAPI.GetOctopusRates(tariffB);

        await Task.WhenAll(ratesATask, ratesBTask);
        
        return new TariffComparison
        {
            TariffA = tariffA,
            TariffAPrices = await ratesATask,
            TariffB = tariffB,
            TariffBPrices = await ratesBTask
        };
    }

    public async Task CalculateForecastWeightings(IEnumerable<HistoryEntry> forecastHistory)
    {
        // Not quite ready for this yet...
        if (!Debugger.IsAttached)
            return;
        
        var powerReadings = new List<(DateTime start, InverterFiveMinData record)>();

        for (int i = 0; i < 7; i++)
        {
            var result = await inverterAPI.GetHistoricData(i);

            if (result != null)
            {
                foreach (var datapoint in result)
                {
                    powerReadings.Add( (datapoint.Start, datapoint));
                }
            }
        }

        var avgThirtyMinActualPower = powerReadings
            .GroupBy(x => x.start.GetRoundedToMinutes(30))
            .Select( x => new { Start = x.Key, AvgPowerKW = Math.Round(x.Average(x => x.record.CurrentPVYieldKW / 1000.0M), 4)})
            .ToList();

        // Now iterate through the historic forecasts, and compare them
        var prevForecast = forecastHistory
            .Where(x => x.Start > DateTime.UtcNow.AddDays(-7))
            .DistinctBy(x => x.Start)
            // Convert the forecast back from kWh to power (kW)
            .ToDictionary(x => x.Start, x => x.ForecastKWH * 2.0M);

        foreach (var actual in avgThirtyMinActualPower)
        {
            if (prevForecast.TryGetValue(actual.Start, out var forecastAvgKw))
            {
                if (forecastAvgKw == 0)
                    continue;
                
                var percentage =  Math.Abs(actual.AvgPowerKW - forecastAvgKw) / actual.AvgPowerKW;
                logger.LogInformation("{D:dd-MMM HH:mm}, forecast = {F:F2}kW, actual = {A:F2}kW, percentage = {P:P1}",
                                actual.Start, forecastAvgKw, actual.AvgPowerKW, percentage);
            }
        }
        
        var avgPowerPerDay = avgThirtyMinActualPower
            .Where( x => x.AvgPowerKW > 0 )
            .GroupBy(x => x.Start.Date)
            .Select(x => new {Date =x.Key, Energy = x.Average(v => v.AvgPowerKW) })
            .OrderBy(x => x.Date)
            .ToList();

        foreach( var d in avgPowerPerDay )
        {
            logger.LogInformation("PV average power {D:dd-MMM-yyyy} = {Y:F2} kW", d.Date, d.Energy);
        }
        
        var avgForecastPowerPerDay = prevForecast
                .Where( x => x.Value != 0)
                .GroupBy( x => x.Key.Date )
                .Select(x => new {Date =x.Key, Energy = x.Average( v => v.Value ) })
                .OrderBy(x => x.Date)
                .ToList();

        foreach( var d in avgForecastPowerPerDay )
        {
            logger.LogInformation("PV forecast power {D:dd-MMM-yyyy} = {Y:F2} kW", d.Date, d.Energy);
        }
    }
}