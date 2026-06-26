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
public class AxleApi(SolisManagerConfig config, IUserAgentProvider userAgentProvider, ILogger<AxleApi> logger)
{
    private readonly List<AxleEvent> axleEvents = new();
    private bool checkedEvents = false;

    public async Task<IEnumerable<AxleEvent>> GetAxleEventsAsync()
    {
        if (!checkedEvents)
        {
            // First time through we always query. After that it gets
            // done asynchronously on the schedule
            await QueryForAxleEvent();
        }
        
        return axleEvents;
    }

    private void LogAxleEvents()
    {
        if (axleEvents.Any())
        {
            var msg = "Axle Events: ";
            msg += string.Join("\n  ", axleEvents);
            logger.LogInformation(msg);
        }
        else
        {
            logger.LogInformation("No axle events found");
        }
    }

    private AxleEvent GetDummyAxleEvent()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return new AxleEvent
        {
            start_time = new DateTime(today, new TimeOnly(07, 30, 00)),
            end_time = new DateTime(today, new TimeOnly(08, 30, 00)),
            import_export = "export", 
            updated_at = DateTime.Now
        };
    }
    
    public async Task QueryForAxleEvent()
    {
        
        if (string.IsNullOrEmpty(config.AxleAPIKey))
            return;

        checkedEvents = true;

        var url = "https://api.axle.energy"
            .WithHeader("User-Agent", userAgentProvider.UserAgent)
            .WithOAuthBearerToken(config.AxleAPIKey)
            .AppendPathSegment("vpp")
            .AppendPathSegment("home-assistant")
            .AppendPathSegment("event");

        try
        {
            logger.LogInformation("Querying Axle API for VPP events...");

            AxleEvent axleEvent;
            
            // if (config.Simulate)
            // {
            //     axleEvent = GetDummyAxleEvent();
            // }
            // else
            {
                axleEvent =await url.GetJsonAsync<AxleEvent>();
            }

            axleEvents.Clear();
            
            if (axleEvent is { start_time: not null, end_time: not null, import_export: not null })
            {
                logger.LogInformation("Axle API succeeded: event retrieved: {Event}", axleEvent);
                axleEvents.Add(axleEvent);

                LogAxleEvents();
            }
            else 
                logger.LogInformation("Axle API: event retrieved but had no data, so ignoring");
        }
        catch (FlurlHttpException ex)
        {
            if (ex.StatusCode == (int)HttpStatusCode.TooManyRequests)
            {
                logger.LogWarning(
                    "Axle API failed - too many requests. Will try again at next scheduled update");
            }
            else
                logger.LogError("HTTP Exception getting Axle events: {E}", ex);
        }
        catch (Exception ex)
        {
            logger.LogError("Exception getting Axle events: {E}", ex);
        }
    }
    
    public record AxleEvent
    {
        public DateTime? start_time { get; set; }
        public DateTime? end_time { get; set; }
        public string? import_export { get; set; }
        public decimal? pence_per_kwh { get; set; }
        public DateTime? updated_at { get; set; }

        public override string ToString()
        {
            var price = string.Empty;
            if (pence_per_kwh != null)
                price = $"{pence_per_kwh}p/kWh ";
            
            return $"{start_time} - {end_time} {import_export} {price} Updated: {updated_at}";
        }
    }
}