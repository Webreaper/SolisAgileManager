using Microsoft.Extensions.Logging;

namespace SolisManager.Shared.Interfaces;

public interface ILogViewService
{
    public record LogViewRequest(string searchText, IEnumerable<LogLevel> levelFilters, int pageNumber, int PageSize, string? LogFile, bool force = false);

    public record LogEntry()
    {
        public DateTimeOffset timestamp { get; set; }
        public LogLevel level { get; set; }
        public string logText { get; set; } = string.Empty;
    }
    public record LogViewResponse(string LogFileName, IEnumerable<LogEntry> LogEntries, int TotalItemCount, IEnumerable<string> logFiles);

    Task<LogViewResponse> GetLogs(LogViewRequest req, CancellationToken token);
}