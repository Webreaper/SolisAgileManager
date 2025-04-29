
using System.Net;
using System.Text.Json;
using Flurl.Http;
using Microsoft.Extensions.Caching.Memory;
using SolisManager.Shared;
using SolisManager.Shared.Interfaces;
using SolisManager.Shared.Models;
using static SolisManager.Extensions.GeneralExtensions;

namespace SolisManager.APIWrappers;

public class OctopusAPI(IMemoryCache memoryCache, ILogger<OctopusAPI> logger, IUserAgentProvider userAgentProvider)
{
    private readonly MemoryCacheEntryOptions _productCacheOptions =
        new MemoryCacheEntryOptions()
                    .SetSize(1)
                    .SetAbsoluteExpiration(TimeSpan.FromDays(7));
    
    private readonly MemoryCacheEntryOptions _accountCacheOptions =
        new MemoryCacheEntryOptions()
            .SetSize(1)
            .SetAbsoluteExpiration(TimeSpan.FromDays(1));

    private readonly MemoryCacheEntryOptions _authTokenCacheOptions =
        new MemoryCacheEntryOptions()
            .SetSize(1)
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(45));

    public async Task<IEnumerable<OctopusRate>> GetOctopusRates(string tariffCode, DateTime? startTime = null, DateTime? endTime = null)
    {
        if (startTime == null)
            startTime = DateTime.UtcNow;

        var from = startTime.Value;
        // Use the end time passed in or default to 5 days from now
        var to = endTime ?? startTime.Value.AddDays(5);

        var product = tariffCode.GetProductFromTariffCode();
        
        // https://api.octopus.energy/v1/products/AGILE-24-10-01/electricity-tariffs/E-1R-AGILE-24-10-01-A/standard-unit-rates/

        try
        {
            var url = "https://api.octopus.energy"
                .WithHeader("User-Agent", userAgentProvider.UserAgent)
                .AppendPathSegment("/v1/products")
                .AppendPathSegment(product)
                .AppendPathSegment("electricity-tariffs")
                .AppendPathSegment(tariffCode)
                .AppendPathSegment("standard-unit-rates")
                .SetQueryParams(new
                {
                    period_from = from,
                    period_to = to
                });
            
            var result = await url.GetJsonAsync<OctopusPrices?>();

            if (result != null)
            {
                if (result.count != 0 && result.results != null)
                {
                    // Some tariffs don't have an end date. So go through and fill in the end date with 
                    // an hour into the future, so we can split to 30 minute slots properly.
                    foreach( var rate in result.results )
                        if (rate.valid_to == null)
                            rate.valid_to = DateTime.UtcNow.AddHours(1);
                    
                    // Ensure they're in date order. Sometimes they come back in random order!!!
                    var orderedSlots = result.results!.OrderBy(x => x.valid_from).ToList();

                    var first = orderedSlots.FirstOrDefault()?.valid_from;
                    var last = orderedSlots.LastOrDefault()?.valid_to;
                    logger.LogInformation(
                        "Retrieved {C} rates from Octopus ({S:dd-MMM-yyyy HH:mm} - {E:dd-MMM-yyyy HH:mm}) for product {Code}",
                        result.count, first, last, tariffCode);

                    // Never go out more than 2 days - so 96 slots
                    var thirtyMinSlots = SplitToHalfHourSlots(orderedSlots)
                                                            .Where(x => x.valid_from >= from && x.valid_to <= to)
                                                            .ToList();
                    
                     // Now, ensure we're in the right TZ
                    foreach (var thirtyMinSlot in thirtyMinSlots)
                    {
                        thirtyMinSlot.valid_from = thirtyMinSlot.valid_from.ToLocalTime();
                        thirtyMinSlot.valid_to = thirtyMinSlot.valid_to!.Value.ToLocalTime();
                    }
                    
                    return thirtyMinSlots;
                }
            }
        }
        catch (FlurlHttpException ex)
        {
            if (ex.StatusCode == (int)HttpStatusCode.TooManyRequests)
            {
                logger.LogWarning("Octpus API failed - too many requests. Waiting 3 seconds before next call...");
                await Task.Delay(3000);
            }
            else
                logger.LogError("HTTP Exception getting octopus tariff rates: {E})", ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving rates from Octopus");
        }

        return [];
    }

    /// <summary>
    /// Keep the granularity for easy manual overrides
    /// </summary>
    /// <param name="slots"></param>
    /// <returns></returns>
    private IEnumerable<OctopusRate> SplitToHalfHourSlots(IEnumerable<OctopusRate> slots)
    {
        List<OctopusRate> result = new();

        foreach (var slot in slots.OrderBy(x => x.valid_from))
        {
            if (slot.valid_to == null)
                slot.valid_to = slot.valid_from.AddMinutes(30);
            
            var slotLength = slot.valid_to.Value - slot.valid_from;

            if ((int)slotLength.TotalMinutes == 30)
            {
                result.Add(slot);
                continue;
            }

            var start = slot.valid_from;
            while (start < slot.valid_to)
            {
                var smallSlot = new OctopusRate
                {
                    valid_from = start,
                    valid_to = start.AddMinutes(30),
                    value_inc_vat = slot.value_inc_vat,
                };
                
                result.Add(smallSlot);
                start = start.AddMinutes(30);
            }
        }

        return result.ToList();
    }

    private async Task<string?> GetAuthToken(string apiKey)
    {
        const string cacheKey = "octAuthToken";
        
        if (memoryCache.TryGetValue<string?>(cacheKey, out var token))
            return token;

        var krakenQuery = """
                          mutation krakenTokenAuthentication($api: String!) {
                          obtainKrakenToken(input: {APIKey: $api}) {
                              token
                          }
                          }
                          """;
        var variables = new { api = apiKey };
        var payload = new { query = krakenQuery, variables = variables };

        var response = await "https://api.octopus.energy"
            .WithHeader("User-Agent", userAgentProvider.UserAgent)
            .AppendPathSegment("/v1/graphql/")
            .PostJsonAsync(payload)
            .ReceiveJson<KrakenTokenResponse>();
        
        token = response?.data?.obtainKrakenToken?.token;

        if( ! string.IsNullOrEmpty(token))
            memoryCache.Set(cacheKey, token, _authTokenCacheOptions);

        return token;
    }

    public async Task<KrakenPlannedDispatch[]?> GetIOGSmartChargeTimes(string apiKey, string accountNumber)
    {
        var token = await GetAuthToken(apiKey);
        
        var krakenQuery = """
                          query getData($input: String!) {
                              plannedDispatches(accountNumber: $input) {
                                  start 
                                  end
                                  delta
                                  meta {
                                      location
                                      source
                                  }
                              }
                              completedDispatches(accountNumber: $input) {
                                  start 
                                  end
                                  delta
                                  meta {
                                      location
                                      source
                                  }
                              }
                          }
                          """;
        var variables = new { input = accountNumber };
        var payload = new { query = krakenQuery, variables = variables };

        var responseStr = await "https://api.octopus.energy"
            .WithHeader("User-Agent", userAgentProvider.UserAgent)
            .WithOctopusAuth(token)
            .AppendPathSegment("/v1/graphql/")
            .PostJsonAsync(payload)
            .ReceiveString();

        if (!string.IsNullOrEmpty(responseStr))
        {
            var response = JsonSerializer.Deserialize<KrakenDispatchResponse>(responseStr);

            if (response?.data?.plannedDispatches != null && response.data.plannedDispatches.Length != 0)
            {
                // Pick out the ones with smart-charge, they're the ones we care about
                var smartChargeDispatches = response.data.plannedDispatches
                    .Where(x => !string.IsNullOrEmpty(x.meta?.source ) && 
                                        x.meta.source.Equals("smart-charge", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                logger.LogInformation("Found {S} IOG Smart-Charge slots (out of a total of {N} planned and {C} completed dispatches)", 
                                    smartChargeDispatches.Length, response.data.plannedDispatches.Length, response.data.completedDispatches.Length);

                if (smartChargeDispatches.Any())
                {
                    var logLines = smartChargeDispatches
                                .Select( x => $"  Time: {x.start:HH:mm} - {x.end:HH:mm}, Type: {x.meta?.source}, Delta: {x.delta}")
                                .ToArray();
                    logger.LogInformation("SmartCharge Dispatches:\n{L}", string.Join("\n", logLines) );
                }
                
                return smartChargeDispatches;
            }
        }

        return [];
    }

    public record KrakenDispatchMeta(string? location, string? source);
    public record KrakenPlannedDispatch(DateTime? start, DateTime? end, string delta, KrakenDispatchMeta? meta);
    public record KrakenDispatchData(KrakenPlannedDispatch[] plannedDispatches, KrakenPlannedDispatch[] completedDispatches);
    public record KrakenDispatchResponse(KrakenDispatchData data);
    
    private record KrakenToken(string token);
    private record KrakenResponse(KrakenToken obtainKrakenToken);

    private record KrakenTokenResponse(KrakenResponse data);
    
    
    public async Task<OctopusAccountDetails?> GetOctopusAccount(string apiKey, string accountNumber)
    {
        var token = await GetAuthToken(apiKey);

        // https://api.octopus.energy/v1/accounts/{number}

        try
        {
            var response = await "https://api.octopus.energy/"
                .WithHeader("User-Agent", userAgentProvider.UserAgent)
                .WithOctopusAuth(token)
                .AppendPathSegment($"/v1/accounts/{accountNumber}/")
                .GetStringAsync();

            var result = JsonSerializer.Deserialize<OctopusAccountDetails>(response);
            
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get Octopus account details");
        }

        return null;
    }
    
    private enum MeterType
    {
        Import,
        Export
    };

    private async Task<IEnumerable<OctopusMeterPoints>> GetMeters(string apiKey, string accountNumber)
    {
        string cacheKey = "octopus-meter-" + accountNumber.ToLower();
        
        if (memoryCache.TryGetValue<IEnumerable<OctopusMeterPoints>>(cacheKey, out var meters) && meters != null)
            return meters;

        var accountDetails = await GetOctopusAccount(apiKey, accountNumber);

        if (accountDetails != null)
        {
            var now = DateTime.UtcNow;
            var currentProperty =
                accountDetails.properties.FirstOrDefault(x => x.moved_in_at < now && x.moved_out_at == null);

            if (currentProperty != null)
            {
                meters = currentProperty.electricity_meter_points; 
                memoryCache.Set(cacheKey, meters, _accountCacheOptions);
                return meters;
            }
        }

        
        return [];
    }

    private async Task<OctopusMeterPoints?> GetMeter(string apiKey, string accountNumber, MeterType type)
    {
        var meters = await GetMeters(apiKey, accountNumber);

        if (meters.Any())
        {
            bool export = type == MeterType.Export;
            return meters.FirstOrDefault(x => x.is_export == export);
        }
        
        return null;
    }

    
    public async Task<string?> GetCurrentOctopusTariffCode(string apiKey, string accountNumber)
    {
        var importMeter = await GetMeter(apiKey, accountNumber, MeterType.Import);
        var now = DateTime.UtcNow;

        if (importMeter != null)
        {
            // Look for a contract with no end date.
            var contract = importMeter.agreements.FirstOrDefault(x => x.valid_from < now && x.valid_to == null);

            // It's possible it has an end-date set, that's later than today. 
            if (contract == null)
                contract = importMeter.agreements.FirstOrDefault(x =>
                    x.valid_from < now && x.valid_to != null && x.valid_to > now);

            if (contract != null)
            {
                logger.LogInformation("Found Octopus Product/Contract: {P}, Starts {S:dd-MMM-yyyy}",
                    contract.tariff_code, contract.valid_from);
                return contract.tariff_code;
            }
        }
        
        return null;
    }

    public async Task<OctopusTariffResponse?> GetOctopusTariffs(string code)
    {
        string cacheKey = "octopus-tariff-" + code.ToLower();
     
        if (memoryCache.TryGetValue<OctopusTariffResponse>(cacheKey, out var tariff))
            return tariff;
        
        try
        {
            var response = await "https://api.octopus.energy/"
                .WithHeader("User-Agent", userAgentProvider.UserAgent)
                .AppendPathSegment($"/v1/products/{code}")
                .AppendQueryParam("is_business", false)
                .GetStringAsync();

            tariff = JsonSerializer.Deserialize<OctopusTariffResponse>(response);
            if (tariff != null)
            {
                memoryCache.Set(cacheKey, tariff, _productCacheOptions);
                return tariff;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get Octopus tariff details");
        }

        return null;
    }
    
    public async Task<OctopusProductResponse?> GetOctopusProducts()
    {
        const string cacheKey = "octopus-products";
     
        if (memoryCache.TryGetValue<OctopusProductResponse>(cacheKey, out var products))
            return products;
        
        try
        {
            var response = await "https://api.octopus.energy/"
                .WithHeader("User-Agent", userAgentProvider.UserAgent)
                .AppendPathSegment($"/v1/products/")
                .GetStringAsync();

            products = JsonSerializer.Deserialize<OctopusProductResponse>(response);

            if (products != null)
            {
                memoryCache.Set(cacheKey, products, _productCacheOptions);
                return products;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get Octopus product details");
        }

        return null;
    }

    public async Task<IEnumerable<OctopusConsumption>?> GetConsumption(string apiKey, string accountNumber, DateTime startDate, DateTime endDate)
    {
        logger.LogInformation("Querying consumption date from {S} to {E}", startDate, endDate);
        
        // Do this first and cache it for the following requests
        var token = await GetAuthToken(apiKey);

        // https://api.octopus.energy/v1/electricity-meter-points/< MPAN >/meters/< meter serial number >/consumption/?
        //                  page_size=100&period_from=2023-03-29T00:00Z&period_to=2023-03-29T01:29Z&order_by=period
        var meters = await GetMeters(apiKey, accountNumber);

        var importMeter = meters.FirstOrDefault(x => !x.is_export);
        var exportMeter = meters.FirstOrDefault(x => x.is_export);

        if (importMeter != null && exportMeter != null && !string.IsNullOrEmpty(token))
        {
            var importMeterTask = GetConsumptionForMeter(apiKey, importMeter, startDate, endDate);
            var exportMeterTask = GetConsumptionForMeter(apiKey, exportMeter, startDate, endDate);

            await Task.WhenAll(importMeterTask, exportMeterTask);

            var importConsumption = await importMeterTask;
            var exportConsumption = await exportMeterTask;

            if (importConsumption != null && exportConsumption != null)
            {
                await EnrichConsumptionWithTariffPricess(importConsumption, importMeter);
                await EnrichConsumptionWithTariffPricess(exportConsumption, exportMeter);

                var lookup = importConsumption.ToDictionary(
                    x => x.interval_start,
                    x => new OctopusConsumption
                    {
                        PeriodStart = x.interval_start, 
                        ImportConsumption = x.consumption,
                        Tariff = x.tariff ?? "Unknown",
                        ImportPrice = x.price ?? 0,
                    });

                foreach (var export in exportConsumption)
                {
                    if (lookup.TryGetValue(export.interval_start, out var consumptionValue))
                    {
                        consumptionValue.ExportConsumption = export.consumption;
                        consumptionValue.ExportPrice = export.price ?? 0;
                    }
                }
                
                return lookup.Values.OrderBy(x => x.PeriodStart).ToList();
            }
        }

        return null;
    }

    private async Task EnrichConsumptionWithTariffPricess(IEnumerable<ConsumptionRecord> consumptions, OctopusMeterPoints meter)
    {
        var minDate = consumptions.Min(x => x.interval_start);
        var maxDate = consumptions.Max(x => x.interval_end);

        var tariffs = new List<(DateTime? valid_from, String tariff_code)>();
        
        foreach (var agreement in meter.agreements.OrderBy(x => x.valid_from))
        {
            if (agreement.valid_to < minDate)
                continue;
            if (agreement.valid_from > maxDate)
                break;
            tariffs.Add( (agreement.valid_from, agreement.tariff_code) );
        }

        if (tariffs.Any())
        {
            // Create a set of tasks for each tariff that applied during the period
            var tasks = tariffs.Select(x => GetOctopusTariffRates(x.tariff_code, minDate, maxDate)).ToList();
            
            // Get the rates
            var results = await Task.WhenAll(tasks);
            
            // Create a multi-level lookup that will go from tariff code => date => price
            var prices = results.ToDictionary(x => x.tariff, 
                                    x => x.rates.ToDictionary(x => x.valid_from));

            // Now, loop through the consumption objects and resolve their rates
            foreach (var consumption in consumptions)
            {
                // First, find the tariff that applied at the point of consumption
                var tariff = tariffs.OrderByDescending(x => x.valid_from)
                    .FirstOrDefault(x => x.valid_from < consumption.interval_start)
                    .tariff_code;

                if (!string.IsNullOrEmpty(tariff))
                {
                    // Store the tariff
                    consumption.tariff = tariff;

                    // Now find the rate for the tariff at that time
                    if (prices.TryGetValue(tariff, out var rates))
                    {
                        if (rates.TryGetValue(consumption.interval_start, out var rate))
                        {
                            // We got a price - save it. 
                            consumption.price = rate.value_inc_vat;
                        }
                    }
                }
                else
                    logger.LogWarning("No tariff found for consumption on {D}", consumption.interval_start);
            }
        }
    }

    private async Task<(string tariff, IEnumerable<OctopusRate> rates)> GetOctopusTariffRates(string tariffCode, DateTime startDate, DateTime endDate)
    {
        var rates = await GetOctopusRates(tariffCode, startDate, endDate);
        if( !rates.Any())
            logger.LogWarning("No rates returned for {T} between {S} and {E}", tariffCode, startDate, endDate);
        
        return (tariffCode, rates);
    }
    
    public async Task<IEnumerable<ConsumptionRecord>?> GetConsumptionForMeter(string apiKey, OctopusMeterPoints meter, DateTime startDate, DateTime endDate)
    {
        var authToken = await GetAuthToken(apiKey);
        
        // https://api.octopus.energy/v1/electricity-meter-points/< MPAN >/meters/< meter serial number >/consumption/?
        //                  page_size=100&period_from=2023-03-29T00:00Z&period_to=2023-03-29T01:29Z&order_by=period

        ArgumentNullException.ThrowIfNull(meter);

        // Which meter to use? Use the last one.
        var serial = meter.meters.LastOrDefault()?.serial_number;

        if (!string.IsNullOrEmpty(serial))
        {
            try
            {
                var url = "https://api.octopus.energy"
                    .WithOctopusAuth(authToken)
                    .WithHeader("User-Agent", userAgentProvider.UserAgent)
                    .AppendPathSegment("/v1/electricity-meter-points")
                    .AppendPathSegment(meter.mpan)
                    .AppendPathSegment("meters")
                    .AppendPathSegment(serial)
                    .AppendPathSegment("consumption/")
                    .SetQueryParams(new
                    {
                        period_from = startDate,
                        period_to = endDate,
                        page_size = 100,
                        order_by = "period"
                    });
                    
                var result = await url.GetJsonAsync<Consumption>();
                
                if (result != null)
                {
                    var results = new List<ConsumptionRecord>(result.results);

                    // Paginate
                    while (!string.IsNullOrEmpty(result?.next))
                    {
                        result = await result.next
                            .WithOctopusAuth(authToken)
                            .WithHeader("User-Agent", userAgentProvider.UserAgent)
                            .GetJsonAsync<Consumption?>();

                        if (result != null)
                            results.AddRange(result.results);
                    }
                    
                    return results;
                }
            }
            catch (FlurlHttpException ex)
            {
                if (ex.StatusCode == (int)HttpStatusCode.TooManyRequests)
                {
                    logger.LogWarning("Octpus API failed - too many requests. Waiting 3 seconds before next call...");
                    await Task.Delay(3000);
                }
                else
                    logger.LogError("HTTP Exception getting octopus consumption data rates: {E}", ex);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving consumption data from Octopus");
            }
        }

        return [];
    }
    
    public record OctopusAgreement(string tariff_code, DateTime? valid_from, DateTime? valid_to);
    public record OctopusMeter(string serial_number);
    public record OctopusMeterPoints(string mpan, OctopusMeter[] meters, OctopusAgreement[] agreements, bool is_export);
    public record OctopusProperty(int id, OctopusMeterPoints[] electricity_meter_points, DateTime? moved_in_at, DateTime? moved_out_at);
    public record OctopusAccountDetails(string number, OctopusProperty[] properties);
    
    private record OctopusPrices(int count, OctopusRate[] results);

    public record Consumption(int count, IEnumerable<ConsumptionRecord> results, string? next);

    public record ConsumptionRecord
    {
        public decimal consumption { get; set; }
        public DateTime interval_start { get; set; }
        public DateTime interval_end { get; set; }
        public string? tariff { get; set; }
        public decimal? price { get; set; }
    }
}
