using System.Net.Http.Json;
using SolisManager.Shared.Interfaces;

namespace SolisManager.Client.Services;

public class ClientLogViewService(HttpClient httpClient, ILogger<ClientLogViewService> logger) : ILogViewService
{
    public async Task<ILogViewService.LogViewResponse> GetLogs(ILogViewService.LogViewRequest req, CancellationToken token)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync("logs", req, token);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<ILogViewService.LogViewResponse?>(token);

            if (result != null)
                return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to retreive logs");
        }
        
        return new ILogViewService.LogViewResponse("unknown", []);
    }
}