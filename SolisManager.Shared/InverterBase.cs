using System.Diagnostics.CodeAnalysis;
using SolisManager.Shared.Interfaces;
using SolisManager.Shared.Models;

namespace SolisManager.Shared;

public class InverterBase<T> where T : InverterConfigBase
{
    protected T? inverterConfig;
    
    public void SetInverterConfig(SolisManagerConfig newConfig)
    {
        var solisConfig = newConfig.InverterConfig as T;
        ArgumentNullException.ThrowIfNull(solisConfig);
        inverterConfig = solisConfig;
    }
}