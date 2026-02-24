namespace SolisManager.Shared.Models;

public class ConsumptionResponse
{
    public IEnumerable<GroupedConsumption> ConsumptionData { get; set; } = [];
    public IEnumerable<GroupedConsumption> ComparisonConsumptionData { get; set; } = [];
}

public class RawConsumptionResponse
{
    public IEnumerable<OctopusConsumption> RawConsumptionData { get; set; } = [];
    public IEnumerable<OctopusConsumption> RawComparisonConsumptionData { get; set; } = [];
}