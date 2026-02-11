using Microsoft.AspNetCore.Components;
using SolisManager.Shared.Interfaces;

namespace SolisManager.Client.InverterConfigs;

public class InverterConfigComponentBase<T> : ComponentBase where T : InverterConfigBase, new()
{
    [Parameter]
    public InverterConfigBase? InverterConfig { get; set; }

    [Parameter]
    public EventCallback<InverterConfigBase?> InverterConfigChanged { get; set; }
    
    protected T? config;
    
    protected override void OnInitialized()
    {
        if (InverterConfig != null)
        {
            config = InverterConfig as T;
            ArgumentNullException.ThrowIfNull(config);
        }
        else
        {
            config = new T();
            InverterConfig = config;
        }
        
        base.OnInitialized();
    }

    protected virtual async Task ConfigChanged()
    {
        await InverterConfigChanged.InvokeAsync(InverterConfig);
    }
}