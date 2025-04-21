using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Flurl;
using Flurl.Http;
using SolisManager.Extensions;
using SolisManager.Shared;
using SolisManager.Shared.Interfaces;
using SolisManager.Shared.Models;
using static SolisManager.Extensions.EnergyExtensions;
namespace SolisManager.APIWrappers;

/// <summary>
/// https://docs.solcast.com.au/#api-authentication
/// https://docs.solcast.com.au/#155071c9-3457-47ea-a689-88fa894b0f51
/// </summary>
public class SolcastAPI(SolisManagerConfig config, IUserAgentProvider userAgentProvider, ILogger<SolcastAPI> logger)
{
    public DateTime? lastAPIUpdate
    {
        get
        {
            if (responseCache?.sites == null || !responseCache.sites.Any())
                return null;

            var lastUpdate = responseCache?.sites.SelectMany(x => x.updates).Max(x => x.lastUpdate);

            if( lastUpdate != null ) 
                return lastUpdate.Value.ToLocalTime();

            return null;
        }
    }

    private SolcastResponseCache? responseCache = null;

    private string DiskCachePath => Path.Combine(Program.ConfigFolder, $"Solcast-cache.json");

    public async Task InitialiseSolcastCache()
    {
        // Not set up yet
        if (!config.SolcastValid())
            return;
        
        await LoadCachedSolcastDataFromDisk();

        if (responseCache == null || !responseCache.sites.SelectMany(x => x.updates).Any() || lastAPIUpdate?.Date != DateTime.Now.Date)
        {
            logger.LogInformation("Solcast startup - no cache available so running one-off update...");
            await GetNewSolcastForecasts();
        }
    }
    
    private async Task LoadCachedSolcastDataFromDisk()
    {
        if (responseCache == null)
        {
            var file = DiskCachePath;

            if (File.Exists(file))
            {
                var json = await File.ReadAllTextAsync(file);
                logger.LogInformation("Loaded cached Solcast data from {F}", file);
                
                responseCache = JsonSerializer.Deserialize<SolcastResponseCache>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }

        }

        responseCache ??= new();
    }

    private async Task CacheSolcastResponse(string siteId, SolcastResponse response)
    {
        ArgumentNullException.ThrowIfNull(responseCache);
        
        var site = responseCache.sites.FirstOrDefault(x => x.siteId == siteId);
        if (site == null)
        {
            site = new SolcastResponseCacheEntry { siteId = siteId };
            responseCache.sites.Add(site);
        }

        if (!site.updates.Exists(x => x.lastUpdate == response.lastUpdate))
        {
            logger.LogInformation("Caching Solcast Response with {E} entries", response.forecasts?.Count() ?? 0);
            
            // Save the response
            site.updates.Add(response);
            // Update the date
            responseCache.date = DateOnly.FromDateTime(response.lastUpdate.ToLocalTime());
        }
        else
            logger.LogInformation("No new forecast entries received from Solcast");

        if (site.updates.Count > 3)
            logger.LogError("Unexpected solcast response cache count = {C}", site.updates.Count);

        var json = JsonSerializer.Serialize(responseCache, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(DiskCachePath, json);
    }

    private void InvalidateCacheOnNewDay()
    {
        // Check we have an active cache object. 
        var today = DateOnly.FromDateTime(DateTime.Now.Date);
        if (responseCache != null && responseCache.date != today)
        {
            logger.LogInformation("New day - discarding solcast cache for {D}", responseCache.date);

            // It's a new day.
            responseCache = null;
        }
        
        if (responseCache == null )
        {
            // If we didn't have one, or today is a different date to the date in the
            // existing cache, then throw everything away and start again. It's a new
            // day, it's a new dawn, etc etc.
            responseCache = new SolcastResponseCache{ date = today };
        }
    }
    
    public async Task GetNewSolcastForecasts()
    {
        try
        {
            InvalidateCacheOnNewDay();
            
            var siteIdentifiers = GetSolcastSites(config.SolcastSiteIdentifier);

            if (siteIdentifiers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != siteIdentifiers.Length)
                logger.LogWarning("Same Solcast site ID specified twice in config. Ignoring the second one");

            // Only ever take the first 2
            foreach (var siteIdentifier in siteIdentifiers.Take(2))
            {
                // Use WhenAll here?
                await GetNewSolcastForecast(siteIdentifier);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting new solcast forecasts");
        }
    }

    private SolcastResponse GetFakeSolcastResponse(string siteId)
    {
        var rnd = new Random();
        var start = DateTime.UtcNow.RoundToHalfHour();
        var fakeForecasts = Enumerable.Range(0, 97).Select(x => new SolcastForecast
        {
            period_end = start.AddMinutes(30 * x),
            pv_estimate = (decimal)(rnd.NextDouble() * 5.0),

        });
        return new SolcastResponse { lastUpdate = DateTime.UtcNow, forecasts = fakeForecasts.ToList() };
    }
    
    private async Task GetNewSolcastForecast(string siteIdentifier)
    {
        // First, check we've got the cache initialised
        await LoadCachedSolcastDataFromDisk();
        
        var url = "https://api.solcast.com.au"
            .WithHeader("User-Agent", userAgentProvider.UserAgent)
            .AppendPathSegment("rooftop_sites")
            .AppendPathSegment(siteIdentifier)
            .AppendPathSegment("forecasts")
            .SetQueryParams(new
            {
                format = "json",
                api_key = config.SolcastAPIKey
            });

        try
        {
            logger.LogInformation("Querying Solcast API for forecast (site ID: {ID})...", siteIdentifier);
            
            var responseData = await url.GetJsonAsync<SolcastResponse>();

            if (responseData != null)
            {
                if( responseData.forecasts != null && responseData.forecasts.Any() )
                    logger.LogInformation("Solcast API succeeded: {F} forecasts retrieved", responseData.forecasts.Count());

                // We got one. Add it to the cache
                await CacheSolcastResponse(siteIdentifier, responseData);
            }
        }
        catch (FlurlHttpException ex)
        {
            if (ex.StatusCode == (int)HttpStatusCode.TooManyRequests)
            {
                logger.LogWarning(
                    "Solcast API failed - too many requests. Will try again at next scheduled update");
            }
            else
                logger.LogError("HTTP Exception getting solcast data: {E}", ex);
        }
        catch (Exception ex)
        {
            logger.LogError("Exception getting solcast data: {E}", ex);
        }
    }

    public static string[] GetSolcastSites(string siteIdList)
    {
        string[] siteIdentifiers;

        // We support up to 2 site IDs for people with multiple strings
        if (siteIdList.Contains(','))
            siteIdentifiers = siteIdList.Split(',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        else
            siteIdentifiers = [siteIdList];

        return siteIdentifiers;
    }

    private IEnumerable<SolarForecast>? GetSolcastDataFromCache()
    {
        if (!config.SolcastValid() || responseCache == null)
            return null;

        Dictionary<DateTime, decimal> data = new();

        foreach (var site in responseCache.sites)
        {
            var siteData = AggregateSiteData(site);

            // Add it to the overall total
            foreach (var pair in siteData)
            {
                if( ! data.ContainsKey(pair.Start))
                    data[pair.Start] = pair.energy;
                else
                    data[pair.Start] += pair.energy;
            }
        }

        if (data.Values.Count != 0)
        {
            return data.Select( x => new SolarForecast
            {  
                PeriodStart = x.Key, 
                ForecastkWh = x.Value
            }).OrderBy(x => x.PeriodStart)
                .ToList();
        }

        return null;
    }

    private List<(DateTime Start, decimal energy)> AggregateSiteData(SolcastResponseCacheEntry siteData)
    {
        Dictionary<DateTime, decimal> data = new();

        // Iterate through the updates, starting oldest first
        foreach (var update in siteData.updates.OrderBy(x => x.lastUpdate))
        {
            if (update.forecasts == null)
                continue;
            
            foreach (var datapoint in update.forecasts)
            {
                var start = datapoint.period_end.AddMinutes(-30);

                // Divide the kW figure by 2 to get the power, and save into 
                // the dict, overwriting anything that came before.
                data[start] = (datapoint.pv_estimate / 2.0M); 
            }
        }
        
        return data.Select(x => (x.Key, x.Value))
            .OrderBy(x => x.Key)
            .ToList();
    }

    public IEnumerable<SolarForecast>? GetSolcastForecast()
    {
        try
        {
            return GetSolcastDataFromCache();
        }
        catch (FlurlHttpException ex)
        {
            if (ex.StatusCode == (int)HttpStatusCode.TooManyRequests)
            {
                logger.LogWarning("Solcast API failed - too many requests. Will try again at next scheduled update");
            }
            else
                logger.LogError("HTTP Exception getting solcast data: {E}", ex);
        }
        catch (Exception ex)
        {
            logger.LogError("Exception getting solcast data: {E}", ex);
        }

        return null;
    }
    
    private record SolcastResponseCache
    {
        public DateOnly? date { get; set; }
        public List<SolcastResponseCacheEntry> sites { get; init; } = [];
    };

    private record SolcastResponseCacheEntry
    {
        public string siteId { get; set; } = string.Empty;
        public List<SolcastResponse> updates { get; init; } = [];
    };

    private record SolcastResponse
    {
        public DateTime lastUpdate { get; set; } = DateTime.UtcNow;
        public IEnumerable<SolcastForecast>? forecasts { get; set; } = [];
    }
    
    private record SolcastForecast
    {
        public decimal pv_estimate { get; set;  }
        public decimal pv_estimate10 { get; set;  }
        public decimal pv_estimate90 { get; set;  }
        public DateTime period_end { get; set; }

        public override string ToString()
        {
            return $"{period_end} = {pv_estimate}kW";
        }
    }
}