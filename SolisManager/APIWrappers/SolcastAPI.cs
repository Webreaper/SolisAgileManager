using System.Net;
using System.Text.Json;
using Flurl.Http;
using SolisManager.Extensions;
using SolisManager.Shared.Interfaces;
using SolisManager.Shared.Models;
namespace SolisManager.APIWrappers;

/// <summary>
/// https://docs.solcast.com.au/#api-authentication
/// https://docs.solcast.com.au/#155071c9-3457-47ea-a689-88fa894b0f51
/// </summary>
public class SolcastAPI(SolisManagerConfig config, IUserAgentProvider userAgentProvider, ILogger<SolcastAPI> logger)
{
    public DateTime? LastAPIUpdateUTC
    {
        get
        {
            if (solcastCache?.sites == null || !solcastCache.sites.Any())
                return null;

            return solcastCache.sites.Max( x => x.lastSolcastUpdate);
        }
    }

    private readonly SolcastResponseCache solcastCache = new ();

    private string DiskCachePath => Path.Combine(Program.ConfigFolder, $"Solcast-cache.json");

    public async Task InitialiseSolcastCache()
    {
        // Not set up yet
        if (!config.SolcastValid())
            return;
        
        await LoadCachedSolcastDataFromDisk();
        
        // If we haven't got any updates, or the max cached value is in the past, we need to do an update
        if (solcastCache.sites.Any( x => x.UpdateIsStale))
        {
            logger.LogInformation("Solcast startup - no cache available so running one-off update...");
            await GetNewSolcastForecasts();
        }
    }
    
    private async Task LoadCachedSolcastDataFromDisk()
    {
        if (!solcastCache.sites.Any())
        {
            var file = DiskCachePath;

            if (File.Exists(file))
            {
                var json = await File.ReadAllTextAsync(file);
                logger.LogInformation("Loaded cached Solcast data from {F}", file);

                var loadedResponseCache = JsonSerializer.Deserialize<SolcastResponseCache>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (loadedResponseCache != null)
                    solcastCache.sites.AddRange(loadedResponseCache.sites);
            }
        }
    }

    private async Task CacheSolcastResponse(string siteId, SolcastResponse response)
    {
        ArgumentNullException.ThrowIfNull(solcastCache);

        if (response.forecasts == null || !response.forecasts.Any())
        {
            logger.LogInformation("No new forecast entries received from Solcast for Site {S}", siteId);
            return;
        }

        var cachedSite = solcastCache.sites.FirstOrDefault(x => x.siteId == siteId);
        if (cachedSite == null)
        {
            cachedSite = new SolcastResponseCacheEntry { siteId = siteId };
            solcastCache.sites.Add(cachedSite);
        }

        // Update the solcast last update entry
        cachedSite.lastSolcastUpdate = response.lastUpdate;
        
        foreach (var forecast in response.forecasts)
        {
            // Add or Overwrite the existing forecast with the new ones.
            cachedSite.forecasts[forecast.period_end] = forecast.pv_estimate;
        }

        // Now, clear out any entries older than 2 days
        cachedSite.forecasts.RemoveWhere(x => x.Date <= DateTime.UtcNow.AddDays(-2).Date);
        
        logger.LogInformation("Caching Solcast Response with {E} entries", response.forecasts?.Count() ?? 0);
        
        var json = JsonSerializer.Serialize(solcastCache, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(DiskCachePath, json);
    }
    
    public async Task GetNewSolcastForecasts()
    {
        try
        {
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
        if (!config.SolcastValid())
            return null;

        Dictionary<DateTime, decimal> data = new();

        foreach (var site in solcastCache.sites)
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
                PeriodStartUtc = x.Key, 
                ForecastkWh = x.Value
            }).OrderBy(x => x.PeriodStartUtc)
                .ToList();
        }

        return null;
    }

    private List<(DateTime Start, decimal energy)> AggregateSiteData(SolcastResponseCacheEntry siteData)
    {
        Dictionary<DateTime, decimal> data = new();

        // Iterate through the updates, starting oldest first
        foreach (var forecast in siteData.forecasts.OrderBy(x => x.Key))
        {
            // Value is the period_end, so convert to period-start
            var start = forecast.Key.AddMinutes(-30);

            // Divide the kW figure by 2 to get the power, and save into 
            // the dict, overwriting anything that came before.
            data[start] = (forecast.Value / 2.0M); 
        }
        
        return data.Select(x => (x.Key, x.Value))
            .OrderBy(x => x.Key)
            .ToList();
    }

    public IEnumerable<SolarForecast>? GetSolcastForecasts()
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
        public List<SolcastResponseCacheEntry> sites { get; init; } = [];
    };

    private record SolcastResponseCacheEntry
    {
        public string siteId { get; set; } = string.Empty;
        public DateTime? lastSolcastUpdate { get; set; } = null;
        public Dictionary<DateTime, decimal> forecasts { get; set; } = [];

        public bool UpdateIsStale => lastSolcastUpdate == null || !forecasts.Any() ||
                                     (DateTime.Now  - lastSolcastUpdate.Value).TotalHours > 16;
    };

    private record SolcastResponse
    {
        public DateTime lastUpdate { get; set; } = DateTime.UtcNow;
        public IEnumerable<SolcastForecast>? forecasts { get; set; } = [];
    }
    
    private record SolcastForecast
    {
        public decimal pv_estimate { get; set;  }
        public DateTime period_end { get; set; }

        public override string ToString()
        {
            return $"{period_end} = {pv_estimate}kW";
        }
    }
}