using Xunit;
using SolisManager.APIWrappers;

namespace SolisManager.Tests;

/// <summary>
/// The URL guard that stands between an API-supplied link and the Kraken bearer token.
/// Every case here is a real shape the API can return, or a hostile one it should never
/// be able to make us follow.
/// </summary>
public class OctopusUrlNormalisationTests
{
    private const string Query = "?page=2&page_size=2&period_from=2026-01-01T00:00Z";

    [Fact]
    public void Current_host_passes_through_unchanged()
    {
        const string url = "https://api.oegb-kraken.energy/v1/products/";
        Assert.Equal(url, OctopusAPI.NormalizeOctopusUrl(url));
    }

    [Fact]
    public void Legacy_host_is_rewritten_and_path_and_query_survive()
    {
        var legacy =
            "https://api.octopus.energy/v1/products/AGILE-24-10-01/electricity-tariffs/" +
            "E-1R-AGILE-24-10-01-A/standard-unit-rates/" + Query;

        Assert.Equal(
            "https://api.oegb-kraken.energy/v1/products/AGILE-24-10-01/electricity-tariffs/" +
            "E-1R-AGILE-24-10-01-A/standard-unit-rates/" + Query,
            OctopusAPI.NormalizeOctopusUrl(legacy));
    }

    [Fact]
    public void Legacy_pagination_next_link_is_rewritten()
    {
        // The old host's own `next` values still point at the old host, so this is the case
        // that makes the migration complete rather than cosmetic.
        Assert.Equal(
            "https://api.oegb-kraken.energy/v1/products/X/standard-unit-rates/?page=3&page_size=200",
            OctopusAPI.NormalizeOctopusUrl(
                "https://api.octopus.energy/v1/products/X/standard-unit-rates/?page=3&page_size=200"));
    }

    [Theory]
    [InlineData("https://evil.example.com/v1/products/")]           // foreign host
    [InlineData("https://api.oegb-kraken.energy.evil.com/v1/")]     // lookalike suffix
    [InlineData("http://api.oegb-kraken.energy/v1/products/")]      // plaintext
    [InlineData("https://user:pass@api.oegb-kraken.energy/v1/")]    // userinfo
    [InlineData("https://api.oegb-kraken.energy:8443/v1/")]         // odd port
    [InlineData("/v1/products/")]                                   // relative
    [InlineData("")]                                                // empty
    [InlineData("https://api.backend.octopus.energy/v1/graphql/")]  // not migrated, never used here
    public void Anything_that_is_not_the_expected_host_scheme_port_is_refused(string url)
    {
        Assert.Null(OctopusAPI.NormalizeOctopusUrl(url));
    }

    [Fact]
    public void A_null_link_is_refused()
    {
        Assert.Null(OctopusAPI.NormalizeOctopusUrl(null));
    }

    [Fact]
    public void An_explicit_default_port_is_accepted()
    {
        Assert.Equal(
            "https://api.oegb-kraken.energy/v1/products/",
            OctopusAPI.NormalizeOctopusUrl("https://api.oegb-kraken.energy:443/v1/products/"));
    }

    [Fact]
    public void Host_comparison_is_case_insensitive()
    {
        Assert.Equal(
            "https://api.oegb-kraken.energy/v1/products/",
            OctopusAPI.NormalizeOctopusUrl("https://API.OEGB-KRAKEN.ENERGY/v1/products/"));
    }
}
