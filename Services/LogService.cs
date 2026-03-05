/**
 * ARQUIVO: LogService.cs
 * UTILIDADE: Contém a lógica de processamento de logs, isolada das rotas (controllers).
 */
using Microsoft.Extensions.Logging;

namespace CallbackApi.Services;

// Serviços contêm a "lógica de negócio" da aplicação, mantendo os Controllers limpos.
public class LogService
{
    private readonly ILogger<LogService> _logger;

    // Recebe o sistema de logs do ASP.NET via Injeção de Dependência.
    public LogService(ILogger<LogService> logger)
    {
        _logger = logger;
    }

    public void ProcessLog(string log, string source, string timestamp)
    {
        // Registra uma informação no console/arquivo de log.
        // O uso de {source} etc. é uma boa prática chamada "Structured Logging".
        _logger.LogInformation("log_processed source={source} timestamp={timestamp} log={log}",
            source, timestamp, log);
    }
}
