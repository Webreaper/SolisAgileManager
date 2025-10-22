using Flurl.Http;

namespace SolisManager.Extensions;

public static class GeneralExtensions
{
    public static DateTime RoundToHalfHour(this DateTime dateTime)
    {
        return new DateTime(dateTime.Year, dateTime.Month,
            dateTime.Day, dateTime.Hour, (dateTime.Minute / 30) * 30, 0);
    }

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
    
    public static DateTime StartOfWeek(this DateTime dt, DayOfWeek startOfWeek)
    {
        int diff = (7 + (dt.DayOfWeek - startOfWeek)) % 7;
        return dt.AddDays(-1 * diff).Date;
    }
}
