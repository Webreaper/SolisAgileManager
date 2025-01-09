using Coravel.Invocable;
using SolisManager.Shared;

namespace SolisManager.Services;

public class RatesScheduler( IInverterRefreshService service, ILogger<RatesScheduler> logger ) : IInvocable
{
    public async Task Invoke()
    {
        logger.LogInformation("Executing Rates scheduler");
        await service.RefreshAgileRates();
    }
}

public class BatteryScheduler( IInverterRefreshService service, ILogger<BatteryScheduler> logger) : IInvocable
{
    public async Task Invoke()
    {
        logger.LogInformation("Executing Battery scheduler");
        await service.RefreshBatteryState();
    }
}

public class SolcastScheduler( IInverterRefreshService service, ILogger<SolcastScheduler> logger ) : IInvocable
{
    public async Task Invoke()
    {
        if (System.Diagnostics.Debugger.IsAttached)
        {
            logger.LogInformation("Debugging, so skipping Solcast execution");
            return;
        }
        
        logger.LogInformation("Executing Solcast scheduler");
        await service.RefreshSolcastData();
    }
}