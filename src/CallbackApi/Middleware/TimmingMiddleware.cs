/**
 * ARQUIVO: TimingMiddleware.cs
 * UTILIDADE: Intercepta todas as requisições para medir e registrar quanto tempo 
 * cada uma demorou para ser processada.
 */
using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CallbackApi.Middleware;

// Middleware: Um componente que intercepta toda requisição que entra e toda resposta que sai.
public class TimingMiddleware
{
    private readonly RequestDelegate _next; // O próximo passo no pipeline.
    private readonly ILogger<TimingMiddleware> _logger;

    public TimingMiddleware(RequestDelegate next, ILogger<TimingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    // O método Invoke é chamado automaticamente para cada requisição HTTP.
    public async Task Invoke(HttpContext context)
    {
        // Inicia um cronômetro.
        var sw = Stopwatch.StartNew();

        // Passa a requisição para o próximo Middleware no pipeline (ex: o Controller).
        await _next(context);

        // Quando o resto da aplicação termina, paramos o cronômetro.
        sw.Stop();

        // Registra quanto tempo a requisição demorou no total.
        _logger.LogInformation("request_completed method={method} path={path} status={status} duration_ms={duration}",
            context.Request.Method,
            context.Request.Path.Value,
            context.Response.StatusCode,
            sw.ElapsedMilliseconds);
    }
}
