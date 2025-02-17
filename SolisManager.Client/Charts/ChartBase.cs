using ApexCharts;
using Microsoft.AspNetCore.Components;

namespace SolisManager.Client.Charts;

public abstract class ChartBase<T> : ComponentBase where T : class
{
    [CascadingParameter] public bool DarkMode { get; set; }

    protected ApexChartOptions<T>? Options => options;

    protected ApexChart<T>? chart;
    private ApexChartOptions<T>? options;

    protected override void OnInitialized()
    {
        InitOptions();

        base.OnInitialized();
    }

    protected override async Task OnParametersSetAsync()
    {
        await InitChart();
        
        await base.OnParametersSetAsync();
    }

    private async Task InitChart()
    {
        if (chart != null)
        {
            var newDarkMode = DarkMode ? Mode.Dark : Mode.Light;

            if (options != null && options.Theme.Mode != newDarkMode)
            {
                options.Theme.Mode = newDarkMode;
                await chart.UpdateOptionsAsync(false, false, false);
            }

            await GraphStateChanged();
        }
    }

    protected async Task GraphStateChanged()
    {
        if( chart != null )
            await chart.UpdateSeriesAsync();
    }
    
    protected abstract void SetOptions(ApexChartOptions<T> options);
    
    private void InitOptions() 
    {
        options = new ApexChartOptions<T>
        {
            Theme = new Theme
            { 
                Mode = DarkMode ? Mode.Dark : Mode.Light
            },
            Chart = new Chart
            { 
                Toolbar = new Toolbar
                {
                    Show = true,
                    Tools = new Tools
                    {
                        Download = false
                    }
                }
            },
        };
        
        SetOptions(options);
    }
}