using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SolisManager.Shared;
using SolisManager.Shared.Interfaces;
using SolisManager.Shared.InverterConfigs;
using SolisManager.Shared.Models;

namespace SolisManager.Inverters.SolarEdge;

public class SolarEdgeAPI : InverterBase<InverterConfigSolarEdge>, IInverter
{
    public SolarEdgeAPI(SolisManagerConfig _config, IUserAgentProvider _userAgentProvider, ILogger _logger)
    {
        SetInverterConfig(_config);
    }
    
    public Task UpdateInverterTime(bool simulateOnly)
    {
        throw new NotImplementedException();
    }

    public Task SetCharge(DateTime? chargeStart, DateTime? chargeEnd, DateTime? dischargeStart, DateTime? dischargeEnd,
        bool holdCharge, bool simulateOnly)
    {
        throw new NotImplementedException();
    }

    public Task<bool> UpdateInverterState(SolisManagerState inverterState)
    {
        throw new NotImplementedException();
    }

    public Task<IEnumerable<InverterFiveMinData>?> GetHistoricData(int dayOffset = 0)
    {
        throw new NotImplementedException();
    }

    public Task<IEnumerable<InverterFiveMinData>?> GetHistoricData(DateTime dayToQuery)
    {
        throw new NotImplementedException();
    }
}