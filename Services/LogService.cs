using Microsoft.Extensions.Logging;

namespace CallbackApi.Services;

public class LogService
{
    private readonly ILogger<LogService> _logger;

    public LogService(ILogger<LogService> logger)
    {
        _logger = logger;
    }

    public void ProcessLog(string log, string source, string timestamp)
    {
        _logger.LogInformation("log_processed source={source} timestamp={timestamp} log={log}",
            source, timestamp, log);
    }
}