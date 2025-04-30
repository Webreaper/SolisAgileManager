namespace SolisManager.Shared.Models;

public class TariffComparison
{
    public string TariffA { get; set; } = string.Empty;
    public IEnumerable<OctopusRate> TariffAPrices { get; set; } = [];

    public string TariffB { get; set; } = string.Empty;
    public IEnumerable<OctopusRate> TariffBPrices { get; set; } = [];
}