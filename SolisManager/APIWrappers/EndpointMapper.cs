using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SolisManager.Client.Pages;
using SolisManager.Services;
using SolisManager.Shared;
using SolisManager.Shared.Models;

namespace SolisManager.APIWrappers;

public static class EndpointMapper
{
    public static IEndpointRouteBuilder ConfigureAPIEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup("inverter")
            .MapInverterAPI()
            .MapGetConfigAPI()
            .MapToolsAPI()
            .MapSaveConfigAPI()
            .MapProductsApi();
        
        return app;
    }

    private static RouteGroupBuilder MapInverterAPI(this RouteGroupBuilder group)
    {
        group.MapGet("refreshinverterdata",
            async ([FromServices] IInverterService service) =>
            {
                await service.RefreshInverterState();
                return TypedResults.Ok(service.InverterState);
            });
        
        group.MapGet("versioninfo",
            async ([FromServices] IInverterService service) =>
            {
                var info = await service.GetVersionInfo();
                return TypedResults.Ok(info);
            });
        
        group.MapGet("history",
            async ([FromServices] IInverterService service) =>
            {
                var history = await service.GetHistory();
                return TypedResults.Ok(history);
            });

        group.MapGet("testcharge",
            async ([FromServices] IInverterService service) =>
            {
                await service.TestCharge();
                return TypedResults.Ok();
            });

        group.MapPost("overrideslotaction",
            async (ChangeSlotActionRequest req, 
                [FromServices] IInverterService service) =>
            {
                await service.OverrideSlotAction(req);
                return TypedResults.Ok();
            });


        group.MapGet("tariffcomparison/{tariffA}/{tariffB}",
            async (string tariffA, string tariffB, 
                [FromServices] IInverterService service) =>
            {
                var result = await service.GetTariffComparisonData(tariffA, tariffB);
                return TypedResults.Ok(result);
            });

        return group;
    }

    private static RouteGroupBuilder MapGetConfigAPI(this RouteGroupBuilder group)
    {
        group.MapGet("getconfig",
           async  ([FromServices] IInverterService service) =>
            {
                var config = await service.GetConfig();
                return TypedResults.Ok(config);
            });

        return group;
    }

    private static RouteGroupBuilder MapToolsAPI(this RouteGroupBuilder group)
    {
        group.MapGet("chargebattery",
            async  ([FromServices] IInverterService service) =>
            {
                await service.ChargeBattery();
                return TypedResults.Ok();
            });

        group.MapGet("dischargebattery",
            async  ([FromServices] IInverterService service) =>
            {
                await service.DischargeBattery();
                return TypedResults.Ok();
            });

        group.MapGet("dumpandchargebattery",
            async  ([FromServices] IInverterService service) =>
            {
                await service.DumpAndChargeBattery();
                return TypedResults.Ok();
            });

        group.MapGet("clearoverrides",
            async  ([FromServices] IInverterService service) =>
            {
                await service.ClearManualOverrides();
                return TypedResults.Ok();
            });

        group.MapGet("advancesimulation",
            async  ([FromServices] IInverterService service) =>
            {
                await service.AdvanceSimulation();
                return TypedResults.Ok();
            });

        group.MapGet("resetsimulation",
            async  ([FromServices] IInverterService service) =>
            {
                await service.ResetSimulation();
                return TypedResults.Ok();
            });

        group.MapGet("restartapplication",
            ([FromServices] RestartService service) =>
            {
                service.RestartApplication();
                return TypedResults.Ok();
            });
        return group;
    }

    private static RouteGroupBuilder MapProductsApi(this RouteGroupBuilder group)
    {
        group.MapGet("octopusproducts",
            async ([FromServices] OctopusAPI api) =>
            {
                var result = await api.GetOctopusProducts();
                return TypedResults.Ok(result);
            });

        group.MapGet("octopustariffs/{product}",
            async (string product, 
                [FromServices] OctopusAPI api) =>
            {
                var result = await api.GetOctopusTariffs(product);
                return TypedResults.Ok(result);
            });

        return group;
    }

    private static RouteGroupBuilder MapSaveConfigAPI(this RouteGroupBuilder group)
    {
        group.MapPost("saveconfig",
            async (string configJson, 
                [FromServices] IInverterService inverterService) =>
            {
                var configToSave = JsonSerializer.Deserialize<SolisManagerConfig>(configJson);
                ArgumentNullException.ThrowIfNull(configToSave);
                var result = await inverterService.SaveConfig(configToSave);
                return TypedResults.Ok(result);
            });

        return group;
    }
}