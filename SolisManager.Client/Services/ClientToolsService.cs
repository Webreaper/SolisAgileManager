using System.Net.Http.Json;
using System.Text.Json;
using SolisManager.Shared;
using SolisManager.Shared.Models;

namespace SolisManager.Client.Services;

public class ClientToolsService( HttpClient httpClient ) : IToolsService
{
    public async Task RestartApplication()
    {
        await httpClient.GetAsync("inverter/restartapplication");
    }
}