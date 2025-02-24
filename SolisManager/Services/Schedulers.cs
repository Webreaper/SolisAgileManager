using Coravel.Invocable;
using SolisManager.APIWrappers;
using SolisManager.Shared;

namespace SolisManager.Services;

public class RatesScheduler( IInverterRefreshService service, ILogger<RatesScheduler> logger ) : IInvocable
{
    public async Task Invoke()
    {
        logger.LogDebug("Executing Rates scheduler");
        await service.RefreshAgileRates();
    }
}

public class BatteryScheduler( IInverterRefreshService service, ILogger<BatteryScheduler> logger) : IInvocable
{
    public async Task Invoke()
    {
        logger.LogDebug("Executing Battery scheduler");
        await service.UpdateInverterState();
    }
}

public class SolcastScheduler( SolcastAPI solcastService, ILogger<SolcastScheduler> logger ) : IInvocable
{
    public async Task Invoke()
    {
        logger.LogDebug("Executing Solcast scheduler");
        await solcastService.GetNewSolcastForecasts();
    }
}

public class SolcastExtraScheduler( SolcastAPI solcastService, IInverterService invService, ILogger<SolcastExtraScheduler> logger ) : IInvocable
{
    public async Task Invoke()
    {
        var config = await invService.GetConfig();

        if (config.SolcastExtraUpdates)
        {
            logger.LogDebug("Executing Extra Solcast scheduler");
            await solcastService.GetNewSolcastForecasts();
        }
    }
}

public class InverterTimeAdjustScheduler( IInverterRefreshService inverterRefresh, ILogger<InverterTimeAdjustScheduler> logger ) : IInvocable
{
    public async Task Invoke()
    {
        logger.LogDebug("Executing Inverter Time Adjustment");
        await inverterRefresh.UpdateInverterTime();
    }
}

public class TariffScheduler( IInverterRefreshService inverterRefresh, ILogger<TariffScheduler> logger ) : IInvocable
{
    public async Task Invoke()
    {
        logger.LogDebug("Executing Tariff scheduler");
        await inverterRefresh.RefreshTariff();
    }
}

public class VersionCheckScheduler( InverterManager service, ILogger<BatteryScheduler> logger) : IInvocable
{
    public async Task Invoke()
    {
        logger.LogDebug("Executing version check scheduler");
        await service.CheckForNewVersion();
    }
}
