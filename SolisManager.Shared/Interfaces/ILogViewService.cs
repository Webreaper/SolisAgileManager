using Microsoft.Extensions.Logging;

namespace SolisManager.Shared.Interfaces;

public interface ILogViewService
{
    public record LogViewRequest(string searchText, int pageNumber, int PageSize);
    public record LogEntry(DateTime timestamp, LogLevel level, string logText);
    public record LogViewResponse(string LogFileName, IEnumerable<LogEntry> LogEntries);

    Task<LogViewResponse> GetLogs(LogViewRequest req, CancellationToken token);
}