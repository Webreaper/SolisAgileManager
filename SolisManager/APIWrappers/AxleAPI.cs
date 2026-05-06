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

    public IEnumerable<AxleEvent> GetAxleEventsAsync()
    {
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

    public async Task QueryForAxleEvents()
    {
        if (string.IsNullOrEmpty(config.AxleAPIKey))
            return;

        var url = "https://api.axle.energy"
            .WithHeader("User-Agent", userAgentProvider.UserAgent)
            .WithOAuthBearerToken(config.AxleAPIKey)
            .AppendPathSegment("vpp")
            .AppendPathSegment("home-assistant")
            .AppendPathSegment("event");

        try
        {
            logger.LogInformation("Querying Axle API for VPP events...");
            
            var responseData = await url.GetJsonAsync<AxleEventResponse>();

            axleEvents.Clear();
            
            if (responseData?.events != null && responseData.events.Any())
            {
                logger.LogInformation("Axle API succeeded: {F} events retrieved", responseData.events.Count());
                axleEvents.AddRange(responseData.events);

                LogAxleEvents();
            }
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

    private record AxleEventResponse
    {
        public IEnumerable<AxleEvent>? events { get; set; } = [];
    }
    
    public record AxleEvent
    {
        public DateTime start_time { get; set; }
        public DateTime end_time { get; set; }
        public string? import_export { get; set; }
        public decimal pence_per_kwh { get; set; }
        public DateTime updated_at { get; set; }

        public override string ToString()
        {
            return $"{start_time} - {end_time} {import_export} {pence_per_kwh}p/kWh Updated: {updated_at}";
        }
    }
}