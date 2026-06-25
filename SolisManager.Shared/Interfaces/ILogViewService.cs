using Microsoft.Extensions.Logging;

namespace SolisManager.Shared.Interfaces;

public interface ILogViewService
{
    public record LogViewRequest(string searchText, LogLevel? levelFilter, int pageNumber, int PageSize, string? LogFile, bool force = false);
    public record LogEntry(DateTime timestamp, LogLevel level, string logText);
    public record LogViewResponse(string LogFileName, IEnumerable<LogEntry> LogEntries, int TotalItemCount, IEnumerable<string> logFiles);

    Task<LogViewResponse> GetLogs(LogViewRequest req, CancellationToken token);
}