using System.Net.Http.Json;
using System.Text.Json;
using SolisManager.Shared;
using SolisManager.Shared.Interfaces;
using SolisManager.Shared.Models;

namespace SolisManager.Client.Services;

public class ClientInverterManagerService( HttpClient httpClient, ILogger<ClientInverterManagerService> logger ) : IInverterManagerService
{
    public SolisManagerState InverterState { get; private set; } = new();

    public async Task RefreshInverterState()
    {
        // Load the data from the server
        var state = await httpClient.GetFromJsonAsync<SolisManagerState?>("inverter/refreshinverterdata");
        if (state != null)
            InverterState = state;
    }

    public async Task<TariffComparison> GetTariffComparisonData(string tariffA, string tariffB, CancellationToken token)
    {
        var url = $"inverter/tariffcomparison/{tariffA}/{tariffB}";
        var result = await httpClient.GetFromJsonAsync<TariffComparison>(url, token);
        if (result != null)
            return result;

        return new TariffComparison();
    }

    public async Task<ConsumptionResponse?> GetConsumption(ConsumptionRequest req, CancellationToken token)
    {
        var url = $"inverter/consumption";

        try
        {
            var response = await httpClient.PostAsJsonAsync(url, req, token);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<ConsumptionResponse>(token);

            if (result != null)
                return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to retreive consumption");
        }

        return null;
    }

    public async Task OverrideSlotAction(ManualOverrideRequest request)
    {
        await httpClient.PostAsJsonAsync("inverter/overrideslotaction", request);
    }

    public async Task<List<HistoryEntry>> GetHistory()
    {
        var result = await httpClient.GetFromJsonAsync<List<HistoryEntry>>("inverter/history");
        if (result != null)
            return result;

        return [];
    }

    public async Task<SolisManagerConfig> GetConfig()
    {
        var result = await httpClient.GetFromJsonAsync<SolisManagerConfig>("inverter/getconfig");
        
        ArgumentNullException.ThrowIfNull(result);
        
        return result;
    }

    public async Task<ConfigSaveResponse> SaveConfig(SolisManagerConfig config)
    {
        // TODO - investigate why passing the object directly, rather than the json
        // as a queryparam, doesn't work. 
        string json;
        try
        {
            json = JsonSerializer.Serialize(config);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to serialize config - did you add the JsonDerived on InverterConfigBase?");
            throw;
        }
        var response = await httpClient.PostAsync($"inverter/saveconfig?configJson={json}", null);

        if (response.IsSuccessStatusCode)
        {
            var errResponse = await response.Content.ReadFromJsonAsync<ConfigSaveResponse?>();
            if( errResponse != null )
                return errResponse;
        }

        return new ConfigSaveResponse { Success = false, Message = "Unknown error saving config." };
    }

    public async Task ClearManualOverrides()
    {
        await httpClient.GetAsync("inverter/clearoverrides");
    }

    public async Task AdvanceSimulation()
    {
        await httpClient.GetAsync("inverter/advancesimulation");
    }

    public async Task ResetSimulation()
    {
        await httpClient.GetAsync("inverter/resetsimulation");
    }

    public async Task TestCharge()
    {
        await httpClient.GetAsync("inverter/testcharge");
    }
    
    public async Task ChargeBattery()
    {
        await httpClient.GetAsync("inverter/chargebattery");
    }

    public async Task DischargeBattery()
    {
        await httpClient.GetAsync("inverter/dischargebattery");
    }

    public async Task DumpAndChargeBattery()
    {
        await httpClient.GetAsync("inverter/dumpandchargebattery");
    }
    
    public async Task<NewVersionResponse> GetVersionInfo()
    {
        var result = await httpClient.GetFromJsonAsync<NewVersionResponse>("inverter/versioninfo");
        ArgumentNullException.ThrowIfNull(result);
        return result;
    }

    public async Task RestartApplication()
    {
        await httpClient.GetAsync("inverter/restartapplication");
    }

    public async Task<OctopusProductResponse?> GetOctopusProducts()
    {
        var result = await httpClient.GetFromJsonAsync<OctopusProductResponse>("inverter/octopusproducts");
        return result;
    }

    public async Task<OctopusTariffResponse?> GetOctopusTariffs(string product)
    {
        var result = await httpClient.GetFromJsonAsync<OctopusTariffResponse>($"inverter/octopustariffs/{product}");
        return result;
    }
}