using Xunit;
using Flurl.Http.Testing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using SolisManager.APIWrappers;
using SolisManager.Shared.Interfaces;

namespace SolisManager.Tests;

/// <summary>
/// Request formation for the tariff pagination path, with the network faked by
/// Flurl.Http.Testing. These assert the URL actually requested, not the string we meant to
/// build — the difference is what a host migration gets wrong.
/// </summary>
public class OctopusPaginationRequestTests : IDisposable
{
    private readonly HttpTest _http = new();

    private static OctopusAPI Api() => new(
        new MemoryCache(new MemoryCacheOptions()),
        NullLogger<OctopusAPI>.Instance,
        new StubUserAgentProvider());

    private static readonly DateTime From = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

    private const string Rates =
        "/v1/products/AGILE-24-10-01/electricity-tariffs/E-1R-AGILE-24-10-01-A/standard-unit-rates/";

    [Fact]
    public async Task A_legacy_product_link_is_requested_on_the_new_host()
    {
        _http.RespondWithJson(new { count = 0, results = Array.Empty<object>() });

        await Api().GetPagedTariffPrices(
            "https://api.octopus.energy" + Rates, From, To, CancellationToken.None);

        // Flurl 4 dropped HttpTest.AssertRequested, so assert against the call log directly.
        Assert.Single(_http.CallLog);
        var call = _http.CallLog.Single();
        Assert.Equal("api.oegb-kraken.energy", call.Request.Url.Host);
        Assert.Equal(Rates, call.Request.Url.Path);
        Assert.DoesNotContain(_http.CallLog, c => c.Request.Url.ToString().Contains("api.octopus.energy"));
    }

    [Fact]
    public async Task Query_parameters_are_preserved_through_the_rewrite()
    {
        _http.RespondWithJson(new { count = 0, results = Array.Empty<object>() });

        await Api().GetPagedTariffPrices(
            "https://api.octopus.energy" + Rates, From, To, CancellationToken.None);

        var requested = _http.CallLog.Single().Request.Url;
        Assert.Equal("api.oegb-kraken.energy", requested.Host);
        Assert.Contains("page_size=200", requested.Query);
        Assert.Contains("period_from=", requested.Query);
    }

    [Fact]
    public async Task A_foreign_pagination_link_is_not_followed_at_all()
    {
        _http.RespondWithJson(new
        {
            count = 1,
            results = Array.Empty<object>(),
            next = "https://evil.example.com/steal"
        });

        await Api().GetPagedTariffPrices(
            "https://api.oegb-kraken.energy" + Rates, From, To, CancellationToken.None);

        Assert.Single(_http.CallLog);
        Assert.DoesNotContain(_http.CallLog, c => c.Request.Url.ToString().Contains("evil.example.com"));
    }

    [Fact]
    public async Task A_repeating_next_link_terminates_instead_of_looping()
    {
        // Same `next` every time: without the cycle guard this would spin until the process
        // was killed.
        _http.RespondWithJson(new
        {
            count = 1,
            results = Array.Empty<object>(),
            next = "https://api.oegb-kraken.energy" + Rates + "?page=2&page_size=200"
        });

        var task = Api().GetPagedTariffPrices(
            "https://api.oegb-kraken.energy" + Rates, From, To, CancellationToken.None);

        var finished = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(task, finished);

        // Two calls: the first page, then the repeated next once. Not a third.
        Assert.Equal(2, _http.CallLog.Count);
    }

    [Fact]
    public async Task A_legacy_next_link_is_followed_on_the_new_host()
    {
        _http.RespondWithJson(new { count = 0, results = Array.Empty<object>() });
        _http.RespondWithJson(new { count = 0, results = Array.Empty<object>() });

        await Api().GetPagedTariffPrices(
            "https://api.oegb-kraken.energy" + Rates + "?page=1", From, To, CancellationToken.None);

        // Only one page has no next, so this asserts the first request; the rewrite of a
        // legacy next is covered by OctopusUrlNormalisationTests.
        Assert.Equal("api.oegb-kraken.energy", _http.CallLog.Single().Request.Url.Host);
    }

    public void Dispose() => _http.Dispose();

    private sealed class StubUserAgentProvider : IUserAgentProvider
    {
        public string UserAgent => "SolisManager-tests/1.0";
    }
}
