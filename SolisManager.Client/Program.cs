using ApexCharts;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor;
using MudBlazor.Services;
using SolisManager.Client.Constants;
using SolisManager.Client.Services;
using SolisManager.Shared;

namespace SolisManager.Client;

class Program
{
    static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);

        builder.Services.AddScoped( x => new HttpClient
        {
            BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
        });

        builder.Services.AddScoped<ClientInverterService>();
        builder.Services.AddScoped<IInverterService>(x => x.GetRequiredService<ClientInverterService>());
        
        builder.Services.AddMudServices();
        builder.Services.AddApexCharts();
        builder.Services.AddBlazoredLocalStorage();

        builder.Services.AddMudServices(config =>
        {
            config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
            config.SnackbarConfiguration.SnackbarVariant = UIConstants.MudVariant;
        });

        await builder.Build().RunAsync();
    }
}