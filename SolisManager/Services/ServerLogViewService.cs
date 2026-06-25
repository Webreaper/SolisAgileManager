using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SolisManager.Shared.Interfaces;
using SolisManager.Utils;

namespace SolisManager.Services;

public class ServerLogViewService(ILogger<ServerLogViewService> _logger) : ILogViewService
{
    public Task<ILogViewService.LogViewResponse> GetLogs(ILogViewService.LogViewRequest req, CancellationToken token)
    {
        ILogViewService.LogViewResponse response = new("unknown", []);
        var logDir = new DirectoryInfo(Program.ConfigFolder);
        var file = logDir.GetFiles("*.log")
            .OrderByDescending(x => x.LastWriteTimeUtc)
            .FirstOrDefault();

        if ( file != null )
        {
            try
            {
                var dateStr = file.Name.Split('-', 2)[1];
                if (DateOnly.TryParseExact(dateStr, "YYYYMMDD", out var logFileDate))
                {
                    var reader = new ReverseLineReader(file.FullName);

                    var entries = reader.Skip(req.pageNumber * req.PageSize)
                        .Take(req.PageSize)
                        .Select(x => CreateLogEntry(logFileDate, x))
                        .Where(x => x != null)
                        .ToList();

                    response = response with
                    {
                        LogFileName = file.Name,
                        LogEntries = entries
                    };
                }
            }
            catch ( Exception ex )
            {
                _logger.LogError($"Exception reading logs: {ex}");
            }
        }

        return Task.FromResult(response);
    }

    // TODO: Use a regex here
    private ILogViewService.LogEntry? CreateLogEntry(DateOnly logFileDate, string s)
    {
        if ( !string.IsNullOrWhiteSpace(s) && s.StartsWith('[') )
        {
            try
            {
                var parts = s.Split(']', 2);
                if ( parts.Length == 2 )
                {
                    var header = parts[0];
                    var message = parts[1];
                    
                    var headerParts = parts[0].Substring(1).Split('-');

                    var timeStr = headerParts[0];
                    var level = headerParts[1] switch
                    {
                        "ERR" => LogLevel.Error,
                        "WRN" => LogLevel.Warning,
                        _ => LogLevel.Information
                    };

                    if (TimeOnly.TryParseExact(timeStr, "HH:MM:dd:fff", out var logEntryTime))
                    {
                        DateTime timestamp = new DateTime(logFileDate, logEntryTime);
                        return new ILogViewService.LogEntry(timestamp, level, message);
                    }
                }
            }
            catch ( Exception )
            {
                // Don't log - we'll get an infinite loop!
            }
        }

        return null;
    }
}