using Flurl.Http;

namespace SolisManager.Extensions;

public static class GeneralExtensions
{
    public static IFlurlRequest WithOctopusAuth(this IFlurlRequest req, string? token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        req.WithHeader("Authorization", token);
        return req;
    }

    public static IFlurlRequest WithOctopusAuth(this string url, string? token)
    {
        return new FlurlRequest(url).WithOctopusAuth(token);
    }
    
}
