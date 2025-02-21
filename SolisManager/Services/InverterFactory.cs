using Microsoft.Extensions.Caching.Memory;
using SolisManager.Inverters.Solis;
using SolisManager.Shared.Interfaces;
using SolisManager.Shared.InverterConfigs;
using SolisManager.Shared.Models;

namespace SolisManager.Services;

public class InverterFactory(SolisManagerConfig config, IMemoryCache _cache, IUserAgentProvider _userAgentProvider, ILogger<SolisAPI> _logger)
{
    public IInverter? GetInverter()
    {
        return config.InverterConfig switch
        {
            InverterConfigSolis _ => new SolisAPI(config, _cache, _userAgentProvider, _logger),
            _ => null
        };
    }
}