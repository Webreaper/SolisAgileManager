namespace SolisManager.Shared.Extensions;

public static class DateExtensions
{
    public static DateTime RoundToHalfHour(this DateTime dateTime)
    {
        return new DateTime(dateTime.Year, dateTime.Month,
            dateTime.Day, dateTime.Hour, (dateTime.Minute / 30) * 30, 0);
    }

    public static DateTime StartOfWeek(this DateTime dt, DayOfWeek startOfWeek)
    {
        int diff = (7 + (dt.DayOfWeek - startOfWeek)) % 7;
        return dt.AddDays(-1 * diff).Date;
    }
}