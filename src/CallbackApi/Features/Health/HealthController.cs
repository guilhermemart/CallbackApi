/**
 * ARQUIVO: HealthController.cs
 * UTILIDADE: Fornece um endpoint simples para verificar se a API está online (Health Check).
 */
using Microsoft.AspNetCore.Mvc;
using CallbackApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace CallbackApi.Features.Health;

[ApiController]
[Route("v1/health")]
public class HealthController : ControllerBase
{
    private readonly CallbackDbContext _database;
    private readonly IConnectionMultiplexer _redis;

    public HealthController(CallbackDbContext database, IConnectionMultiplexer redis)
    {
        _database = database;
        _redis = redis;
    }

    // Responde a requisições GET. Usado para verificar se a API está "viva" e funcionando.
    [HttpGet]
    public IActionResult Get()
    {
        // Retorna status 200 (OK) com um objeto JSON simples.
        return Ok(new { status = "ok", service = "callback-api" });
    }

    [HttpGet("ready")]
    public async Task<IActionResult> Ready(CancellationToken cancellationToken)
    {
        try
        {
            await _database.Database.OpenConnectionAsync(cancellationToken);

            try
            {
                await using var command = _database.Database.GetDbConnection().CreateCommand();
                command.CommandText = "SELECT 1";

                var result = await command.ExecuteScalarAsync(cancellationToken);
                if (Convert.ToInt32(result) != 1)
                {
                    return StatusCode(StatusCodes.Status503ServiceUnavailable,
                        new { status = "not_ready", database = "unavailable", redis = "unknown" });
                }
            }
            finally
            {
                await _database.Database.CloseConnectionAsync();
            }
        }
        catch (Exception)
        {
            // A resposta não expõe detalhes da infraestrutura ao cliente.
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { status = "not_ready", database = "unavailable", redis = "unknown" });
        }

        try
        {
            await _redis.GetDatabase().PingAsync().WaitAsync(cancellationToken);
            return Ok(new { status = "ready", database = "ok", redis = "ok" });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { status = "not_ready", database = "ok", redis = "unavailable" });
        }
    }
}
