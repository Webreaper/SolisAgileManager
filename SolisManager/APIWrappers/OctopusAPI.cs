
using System.Net;
using System.Text.Json;
using Flurl.Http;
using Microsoft.Extensions.Caching.Memory;
using SolisManager.Shared;
using SolisManager.Shared.Interfaces;
using SolisManager.Shared.Models;

namespace SolisManager.APIWrappers;

public class OctopusAPI(IMemoryCache memoryCache, ILogger<OctopusAPI> logger, IUserAgentProvider userAgentProvider)
{
    private readonly MemoryCacheEntryOptions _productCacheOptions =
        new MemoryCacheEntryOptions()
                    .SetSize(1)
                    .SetAbsoluteExpiration(TimeSpan.FromDays(7));
    
    private readonly MemoryCacheEntryOptions _authTokenCacheOptions =
        new MemoryCacheEntryOptions()
            .SetSize(1)
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(45));

    public async Task<IEnumerable<OctopusPriceSlot>> GetOctopusRates(string tariffCode, DateTime? startTime = null)
    {
        if (startTime == null)
            startTime = DateTime.UtcNow;

        var from = startTime.Value;
        var to = startTime.Value.AddDays(5);

        var product = tariffCode.GetProductFromTariffCode();
        
        // https://api.octopus.energy/v1/products/AGILE-24-10-01/electricity-tariffs/E-1R-AGILE-24-10-01-A/standard-unit-rates/

        try
        {
            var result = await "https://api.octopus.energy"
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
                }).GetJsonAsync<OctopusPrices?>();

            if (result != null)
            {
                if (result.count != 0 && result.results != null)
                {
                    // Ensure they're in date order. Sometimes they come back in random order!!!
                    var orderedSlots = result.results!.OrderBy(x => x.valid_from).ToList();

                    var first = result.results.FirstOrDefault()?.valid_from;
                    var last = result.results.LastOrDefault()?.valid_to;
                    logger.LogInformation(
                        "Retrieved {C} rates from Octopus ({S:dd-MMM-yyyy HH:mm} - {E:dd-MMM-yyyy HH:mm}) for product {Code}",
                        result.count, first, last, tariffCode);

                    return SplitToHalfHourSlots(orderedSlots);
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
                logger.LogError("HTTP Exception getting octopus tariff rates: {E}", ex);
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
    private IEnumerable<OctopusPriceSlot> SplitToHalfHourSlots(IEnumerable<OctopusPriceSlot> slots)
    {
        List<OctopusPriceSlot> result = new();

        foreach (var slot in slots)
        {
            var slotLength = slot.valid_to - slot.valid_from;

            if (slotLength.TotalMinutes == 30)
            {
                result.Add(slot);
                continue;
            }

            var start = slot.valid_from;
            while (start < slot.valid_to)
            {
                var smallSlot = new OctopusPriceSlot
                {
                    valid_from = start,
                    valid_to = start.AddMinutes(30),
                    ActionReason = slot.ActionReason,
                    OverrideAction = slot.OverrideAction,
                    OverrideType = slot.OverrideType,
                    PlanAction = slot.PlanAction,
                    PriceType = slot.PriceType,
                    value_inc_vat = slot.value_inc_vat,
                    pv_est_kwh = slot.pv_est_kwh
                };
                
                if( smallSlot.valid_to > DateTime.UtcNow)
                    result.Add(smallSlot);
                
                start = start.AddMinutes(30);
            }
        }

        return result;
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
            .WithHeader("Authorization", token)
            .WithHeader("User-Agent", userAgentProvider.UserAgent)
            .AppendPathSegment("/v1/graphql/")
            .PostJsonAsync(payload)
            .ReceiveString();

        if (!string.IsNullOrEmpty(responseStr))
        {
            var response = JsonSerializer.Deserialize<KrakenDispatchResponse>(responseStr);

            if (response?.data?.plannedDispatches != null)
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
                .WithHeader("Authorization", token)
                .WithHeader("User-Agent", userAgentProvider.UserAgent)
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
    
    public async Task<string?> GetCurrentOctopusTariffCode(string apiKey, string accountNumber)
    {
        var accountDetails = await GetOctopusAccount(apiKey, accountNumber);
        
        if (accountDetails != null)
        {
            var now = DateTime.UtcNow;
            var currentProperty = accountDetails.properties.FirstOrDefault(x => x.moved_in_at < now && x.moved_out_at == null);

            if (currentProperty != null)
            {
                var exportMeter = currentProperty.electricity_meter_points.FirstOrDefault(x => !x.is_export);
                if (exportMeter != null)
                {
                    // Look for a contract with no end date.
                    var contract = exportMeter.agreements.FirstOrDefault(x => x.valid_from < now && x.valid_to == null);

                    // It's possible it has an end-date set, that's later than today. 
                    if( contract == null )
                        contract = exportMeter.agreements.FirstOrDefault(x => x.valid_from < now && x.valid_to != null && x.valid_to > now);

                    if (contract != null)
                    {
                        logger.LogInformation("Found Octopus Product/Contract: {P}, Starts {S:dd-MMM-yyyy}",
                            contract.tariff_code, contract.valid_from);
                        return contract.tariff_code;
                    }
                }
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
    
    public record OctopusAgreement(string tariff_code, DateTime? valid_from, DateTime? valid_to);
    public record OctopusMeter(string serial_number);
    public record OctopusMeterPoints(string mpan, OctopusMeter[] meters, OctopusAgreement[] agreements, bool is_export);
    public record OctopusProperty(int id, OctopusMeterPoints[] electricity_meter_points, DateTime? moved_in_at, DateTime? moved_out_at);
    public record OctopusAccountDetails(string number, OctopusProperty[] properties);
    
    private record OctopusPrices(int count, OctopusPriceSlot[] results);
}