
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

    private readonly MemoryCacheEntryOptions _ratesCacheOptions =
        new MemoryCacheEntryOptions()
            .SetSize(1)
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(15));

    private async Task<IEnumerable<OctopusRate>?> GetOctopusTariffPrices(string tariffCode, DateTime from, DateTime to, CancellationToken token)
    {
        var cacheKey = $"prices-{tariffCode.ToLower()}-{from:yyyyMMddHHmm}-{to:yyyyMMddHHmm}";

        if (memoryCache.TryGetValue(cacheKey, out List<OctopusRate>? rates))
            return rates;

        var product = tariffCode.GetProductFromTariffCode();
        var pageSize = ((to - from).TotalDays / 30) * 200;

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
                    period_to = to,
                    page_size = pageSize
                });

            var prices = await url.GetJsonAsync<OctopusPrices?>(cancellationToken:token);

            if (prices != null && prices.count != 0)
            {
                rates = new List<OctopusRate>(prices.results);

                // Paginate
                while (!string.IsNullOrEmpty(prices?.next))
                {
                    prices = await prices.next
                        .WithHeader("User-Agent", userAgentProvider.UserAgent)
                        .GetJsonAsync<OctopusPrices?>(cancellationToken:token);

                    if (prices != null)
                        rates.AddRange(prices.results);
                }

                var first = rates.OrderBy(x => x.valid_from).FirstOrDefault()?.valid_from;
                var last = rates.OrderBy(x => x.valid_to).LastOrDefault()?.valid_to;

                logger.LogInformation(
                    "Retrieved {C} rates from Octopus ({S:dd-MMM-yyyy HH:mm} - {End}) for product {Code}",
                    rates.Count(), first, last == null ? "today" : $"{last:dd-MMM-yyyy HH:mm}", tariffCode);

                memoryCache.Set(cacheKey, rates, _ratesCacheOptions);

                // Return a copy - so that any manipulation of the collection won't subvert the cache
                return rates.Select(x => new OctopusRate
                {
                    valid_from = x.valid_from,
                    valid_to = x.valid_to,
                    value_inc_vat = x.value_inc_vat
                }).ToList();
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

    public async Task<IEnumerable<OctopusRate>> GetOctopusRates(string tariffCode, DateTime from, DateTime to, CancellationToken token)
    {
        var rates = await GetOctopusTariffPrices(tariffCode, from, to, token);
        
        if (rates != null && rates.Any())
        {
            // Some tariffs don't have an end date. So go through and fill in the end date with 
            // an hour into the future, so we can split to 30 minute slots properly.
            foreach (var rate in rates)
                if (rate.valid_to == null)
                    rate.valid_to = DateTime.UtcNow.AddHours(1);

            // Ensure they're in date order. Sometimes they come back in random order!!!
            var orderedSlots = rates.OrderBy(x => x.valid_from).ToList();

            var thirtyMinSlots = SplitToHalfHourSlots(orderedSlots);

            // Now, ensure we're in the right TZ
            foreach (var thirtyMinSlot in thirtyMinSlots)
            {
                thirtyMinSlot.valid_from = thirtyMinSlot.valid_from.ToLocalTime();
                thirtyMinSlot.valid_to = thirtyMinSlot.valid_to!.Value.ToLocalTime();
            }

            return thirtyMinSlots;
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
    
    
    private async Task<OctopusAccountDetails?> GetOctopusAccount(string apiKey, string accountNumber)
    {
        var cacheKey = $"account-{accountNumber.ToLower()}";
        var token = await GetAuthToken(apiKey);

        if( memoryCache.TryGetValue<OctopusAccountDetails>(cacheKey, out var accountDetails ) )
            return accountDetails;
        
        // https://api.octopus.energy/v1/accounts/{number}

        try
        {
            var response = await "https://api.octopus.energy/"
                .WithHeader("User-Agent", userAgentProvider.UserAgent)
                .WithOctopusAuth(token)
                .AppendPathSegment($"/v1/accounts/{accountNumber}/")
                .GetStringAsync();

            accountDetails = JsonSerializer.Deserialize<OctopusAccountDetails>(response);
            
            if( accountDetails != null )
                memoryCache.Set(cacheKey, accountDetails, _authTokenCacheOptions);
            
            return accountDetails;
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
        string cacheKey = "octopus-meter-" + accountNumber.Replace(",", "-").ToLower();
        
        if (memoryCache.TryGetValue<List<OctopusMeterPoints>>(cacheKey, out var allMeters) && allMeters != null)
            return allMeters;

        allMeters = new();
        
        var accountNumbers = accountNumber.Split(',', StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);

        foreach (var account in accountNumbers)
        {
            var accountDetails = await GetOctopusAccount(apiKey, account);

            if (accountDetails != null)
            {
                var now = DateTime.UtcNow;
                var currentProperty = accountDetails.properties.FirstOrDefault(x => x.moved_in_at < now &&
                    (x.moved_out_at == null || x.moved_out_at >= now));

                if (currentProperty != null)
                {
                    allMeters.AddRange(currentProperty.electricity_meter_points);
                    continue;
                }

                logger.LogWarning("No current property found for meter in account {Acc}!", account);
            }
            else
                logger.LogWarning("Account details not found for {Acc} while querying for meters!", account);
        }

        if (allMeters.Any())
        {
            memoryCache.Set(cacheKey, allMeters, _accountCacheOptions);
            return allMeters;
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

    private async Task<decimal?> GetStandingChargeFromTariff(string tariffCode)
    {
        var cacheKey = "standing-charge-" + tariffCode.ToLower();
        
        if (memoryCache.TryGetValue<decimal?>(cacheKey, out var tariffStandingCharge))
            return tariffStandingCharge;

        var productCode = tariffCode.GetProductFromTariffCode();
        var product = await GetOctopusTariffs(productCode);

        if (product != null)
        {
            var tariff = product.single_register_electricity_tariffs;
            if (tariff != null)
            {
                var region = tariff.FirstOrDefault(x => 
                    x.Value?.direct_debit_monthly?.code != null &&
                    x.Value.direct_debit_monthly.code == tariffCode);

                if (region.Value != null)
                {
                    tariffStandingCharge = region.Value.direct_debit_monthly.standing_charge_inc_vat;
                    memoryCache.Set(cacheKey, tariffStandingCharge, _productCacheOptions);
                    return tariffStandingCharge;
                }

                logger.LogWarning("No standing charge data found in tariffs");
            }
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

    public async Task<IEnumerable<OctopusConsumption>?> GetConsumption(string apiKey, string accountNumber,
        DateTime startDate,
        DateTime endDate, CancellationToken token)
    {
        var cacheKey = $"consumption-{accountNumber.ToLower()}-{startDate:yyyyMMddHH}-{endDate:yyyyMMddHH}";

        if (memoryCache.TryGetValue<IEnumerable<OctopusConsumption>>(cacheKey, out var result))
            return result;

        // Do this first and cache it for the following requests
        var authToken = await GetAuthToken(apiKey);
        if (string.IsNullOrEmpty(authToken))
        {
            logger.LogWarning("No auth token created");
            return null;
        }

        logger.LogInformation("Querying consumption data from {S} to {E}", startDate, endDate);

        // https://api.octopus.energy/v1/electricity-meter-points/< MPAN >/meters/< meter serial number >/consumption/?
        //                  page_size=100&period_from=2023-03-29T00:00Z&period_to=2023-03-29T01:29Z&order_by=period
        var meters = await GetMeters(apiKey, accountNumber);

        if (meters == null || !meters.Any())
        {
            logger.LogWarning("No active meters found for account");
            return null;
        }

        var importMeter = meters.FirstOrDefault(x => !x.is_export);

        if (importMeter == null)
        {
            logger.LogWarning("No import meter found in account");
            return null;
        }
        
        var importMeterTask = GetConsumptionForMeter(apiKey, importMeter, startDate, endDate, false, token);
        Task<IEnumerable<ConsumptionRecord>?> exportMeterTask = Task.FromResult<IEnumerable<ConsumptionRecord>?>(null);
        
        var exportMeter = meters.FirstOrDefault(x => x.is_export);

        if (exportMeter != null)
            exportMeterTask = GetConsumptionForMeter(apiKey, exportMeter, startDate, endDate, true, token);
        else
            logger.LogWarning("No export meter found in account");

        await Task.WhenAll(importMeterTask, exportMeterTask);

        var importConsumption = await importMeterTask;
        var exportConsumption = await exportMeterTask;

        if (importConsumption != null && importConsumption.Any())
        {
            await EnrichConsumptionWithTariffPrices(importConsumption, importMeter, true, token);

            var lookup = importConsumption
                .DistinctBy(x => x.interval_start)
                .ToDictionary(
                    x => x.interval_start,
                    x => new OctopusConsumption
                    {
                        PeriodStart = x.interval_start,
                        ImportConsumption = x.consumption,
                        Tariff = x.tariff ?? "Unknown",
                        DailyStandingCharge = x.dailyStandingCharge,
                        ImportPrice = x.price ?? 0,
                    });

            // It's possible that somebody might have an import meter but no export meter
            // So only enrich if we got consumption data from an export meter.
            if (exportConsumption != null && exportConsumption.Any())
            {
                await EnrichConsumptionWithTariffPrices(exportConsumption, exportMeter, false, token);

                foreach (var export in exportConsumption)
                {
                    if (lookup.TryGetValue(export.interval_start, out var consumptionValue))
                    {
                        consumptionValue.ExportConsumption = export.consumption;
                        consumptionValue.ExportPrice = export.price ?? 0;
                    }
                }
            }
            else
                logger.LogWarning("No consumption data found from export meter");

            result = lookup.Values.OrderBy(x => x.PeriodStart).ToList();
            memoryCache.Set(cacheKey, result, _ratesCacheOptions);
            return result;
        }

        logger.LogWarning("No consumption data found from import meter");
        return null;
    }

    private async Task EnrichConsumptionWithTariffPrices(IEnumerable<ConsumptionRecord> consumptions, OctopusMeterPoints meter, 
                        bool getStandingCharge, CancellationToken token)
    {
        var minDate = consumptions.Min(x => x.interval_start);
        var maxDate = consumptions.Max(x => x.interval_end);

        var tariffs = new List<(DateTime? valid_from, String tariff_code)>();
        
        // For some reason it's possible to have a tariff agreement with the same
        // start and end date. So filter them out!
        var validAggreements = meter.agreements
            .Where(x => (x.valid_to == null || (x.valid_to - x.valid_from)?.TotalDays > 0))
            .OrderBy(x => x.valid_from)
            .ToList();
        
        foreach (var agreement in validAggreements) 
        {
            if (agreement.valid_to < minDate)
                continue;
            if (agreement.valid_from > maxDate)
                break;
            tariffs.Add( (agreement.valid_from, agreement.tariff_code) );
        }

        if (tariffs.Any())
        {
            logger.LogInformation("  Found {N} tariffs for meter. Querying rates (Tariffs: {T}", tariffs.Count, string.Join(", ", tariffs.Select(x => x.tariff_code)));

            // Create a set of tasks for each tariff that applied during the period
            var tasks = tariffs.Select(x => GetOctopusTariffRates(x.tariff_code, minDate, maxDate, token)).ToList();
            
            // Get the rates
            var results = await Task.WhenAll(tasks);
            
            // Create a multi-level lookup that will go from tariff code => date => price
            // The DistinctBy here is needed because the time-difference when we cross the 
            // DST boundary can result in two tariff entries for the same time - which causes
            // the dictionary to blow up. So discard one, arbitrarily.
            var prices = results
                                    .DistinctBy(x => x.tariff)
                                    .ToDictionary(x => x.tariff, 
                                            x => x.rates.DistinctBy(x => x.valid_from)
                                                           .ToDictionary(x => x.valid_from));

            // Now, loop through the consumption objects and resolve their rates
            foreach (var consumption in consumptions)
            {
                if (token.IsCancellationRequested)
                    break;
                
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

                    if (getStandingCharge)
                        consumption.dailyStandingCharge = await GetStandingChargeFromTariff(tariff);
                }
                else
                    logger.LogWarning("No tariff found for consumption on {D}", consumption.interval_start);
            }
        }
    }

    private async Task<(string tariff, IEnumerable<OctopusRate> rates)> GetOctopusTariffRates(string tariffCode, DateTime startDate, 
                    DateTime endDate, CancellationToken token)
    {
        var rates = await GetOctopusRates(tariffCode, startDate, endDate, token);
        if( !rates.Any())
            logger.LogWarning("No rates returned for {T} between {S} and {E}", tariffCode, startDate, endDate);
        
        return (tariffCode, rates);
    }
    
    public async Task<IEnumerable<ConsumptionRecord>?> GetConsumptionForMeter(string apiKey, OctopusMeterPoints meterPoints, 
                            DateTime startDate, DateTime endDate, bool isExport, CancellationToken token)
    {
        var authToken = await GetAuthToken(apiKey);
        
        // https://api.octopus.energy/v1/electricity-meter-points/< MPAN >/meters/< meter serial number >/consumption/?
        //                  page_size=100&period_from=2023-03-29T00:00Z&period_to=2023-03-29T01:29Z&order_by=period

        ArgumentNullException.ThrowIfNull(meterPoints);

        // Which meter to use? Use the last one.
        var meter = meterPoints.meters.LastOrDefault();
        var serial = meter?.serial_number;

        var pageSize = ((endDate - startDate).TotalDays / 30) * 200;
        
        if (!string.IsNullOrEmpty(serial))
        {
            logger.LogInformation("  Requesting consumption data for {T} meter S/N {M}", isExport ? "export" : "import", serial);
            try
            {
                var url = "https://api.octopus.energy"
                    .WithOctopusAuth(authToken)
                    .WithHeader("User-Agent", userAgentProvider.UserAgent)
                    .AppendPathSegment("/v1/electricity-meter-points")
                    .AppendPathSegment(meterPoints.mpan)
                    .AppendPathSegment("meters")
                    .AppendPathSegment(serial)
                    .AppendPathSegment("consumption/")
                    .SetQueryParams(new
                    {
                        period_from = startDate,
                        period_to = endDate,
                        page_size = pageSize,
                        order_by = "period"
                    });
                    
                var result = await url.GetJsonAsync<Consumption>(cancellationToken:token);
                
                if (result != null)
                {
                    var results = new List<ConsumptionRecord>(result.results);

                    // Paginate
                    while (!string.IsNullOrEmpty(result?.next))
                    {
                        result = await result.next
                            .WithOctopusAuth(authToken)
                            .WithHeader("User-Agent", userAgentProvider.UserAgent)
                            .GetJsonAsync<Consumption?>(cancellationToken:token);

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
                    await Task.Delay(3000, token);
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
    
    private record OctopusPrices(int count, OctopusRate[] results, string? next);

    public record Consumption(int count, IEnumerable<ConsumptionRecord> results, string? next);

    public record ConsumptionRecord
    {
        public decimal consumption { get; set; }
        public DateTime interval_start { get; set; }
        public DateTime interval_end { get; set; }
        public string? tariff { get; set; }
        public decimal? dailyStandingCharge { get; set; }
        public decimal? price { get; set; }
    }
}
