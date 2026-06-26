using System.Text;
using SolisManager.Shared.Interfaces;

namespace SolisManager.Services;

public class ServerLogViewService(ILogger<ServerLogViewService> _logger) : ILogViewService
{
    private static string[] logLines = [];
    
    public Task<ILogViewService.LogViewResponse> GetLogs(ILogViewService.LogViewRequest req, CancellationToken token)
    {
        var logDir = new DirectoryInfo(Program.ConfigFolder);
        var files = logDir.GetFiles("*.log")
            .OrderByDescending(x => x.LastWriteTimeUtc)
            .ToList();

        ILogViewService.LogViewResponse response = new("unknown", [], 0, 
                        logFiles: files.Select(x => x.Name).ToArray());

        var file = files.FirstOrDefault(x => x.Name.Equals(req.LogFile));

        if (file == null)
            file = files.FirstOrDefault();
        
        if ( file != null )
        {
            try
            {
                var dateStr = Path.GetFileNameWithoutExtension(file.Name).Split('-', 2)[1];

                if (DateOnly.TryParseExact(dateStr, "yyyyMMdd", out var logFileDate))
                {
                    if (!logLines.Any() || req.force)
                    {
                        using var fs = File.Open(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        logLines = ReadLines(fs, Encoding.UTF8).ToArray();
                    }
                    
                    var entries = logLines
                        .Reverse()
                        .Skip(req.pageNumber * req.PageSize)
                        .Take(req.PageSize)
                        .Select(x => CreateLogEntry(logFileDate, x))
                        .Where(x => x != null)
                        .Where(x => string.IsNullOrEmpty(req.searchText) || x.logText.Contains(req.searchText, StringComparison.OrdinalIgnoreCase))
                        .Where(x => req.levelFilter == LogLevel.None || x.level == req.levelFilter)
                        .ToArray();

                    response = response with
                    {
                        LogFileName = file.Name,
                        LogEntries = entries,
                        TotalItemCount = logLines.Length
                    };
                }
                else 
                    _logger.LogError("Unexpected log file name date format: {N}", file.Name);
            }
            catch ( Exception ex )
            {
                _logger.LogError($"Exception reading logs: {ex}");
            }
        }

        return Task.FromResult(response);
    }

    private IEnumerable<string> ReadLines(Stream stream, Encoding encoding)
    {
        using (var reader = new StreamReader(stream, encoding))
        {
            while (reader.ReadLine() is { } line)
            {
                yield return line;
            }
        }
    }
    // TODO: Use a regex here
    private ILogViewService.LogEntry? CreateLogEntry(DateOnly logFileDate, string s)
    {
        if ( !string.IsNullOrWhiteSpace(s) && s.StartsWith('[') )
        {
            try
            {
                var parts = s.Split(']', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if ( parts.Length == 2 )
                {
                    var header = parts[0];
                    var message = parts[1];
                    
                    var headerParts = parts[0].Substring(1).Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

                    var timeStr = headerParts[0];
                    var level = headerParts[1] switch
                    {
                        "ERR" => LogLevel.Error,
                        "WRN" => LogLevel.Warning,
                        "TRC" => LogLevel.Trace,
                        _ => LogLevel.Information
                    };

                    if (TimeOnly.TryParseExact(timeStr, "HH:mm:ss.fff", out var logEntryTime))
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