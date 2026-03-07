/**
 * ARQUIVO: LogController.cs
 * UTILIDADE: Define as "rotas" de API para receber logs via HTTP POST. 
 * É a ponte entre a internet e a lógica do sistema.
 */
using Microsoft.AspNetCore.Mvc;
using CallbackApi.Models;
using CallbackApi.Services;

namespace CallbackApi.Controllers;

// [ApiController] indica que esta classe serve para responder requisições HTTP (API).
// [Route] define a URL base para este controller (ex: http://localhost/v1/log).
[ApiController]
[Route("v1/[controller]")]
public class LogController : ControllerBase
{
    private readonly LogService _logService;

    // O construtor recebe o LogService automaticamente via Injeção de Dependência.
    public LogController(LogService logService)
    {
        _logService = logService;
    }

    // [HttpPost] significa que este método responde a envios de dados (POST).
    // [FromBody] diz para o ASP.NET pegar os dados do corpo da requisição e transformar no objeto LogInput.
    [HttpPost]
    public IActionResult Receive([FromBody] LogInput input)
    {
        // Garante que temos valores mesmo se vierem vazios.
        input.Timestamp = input.Timestamp ?? DateTime.UtcNow;
        var timestampString = input.Timestamp.Value.ToString("o");

        // Chama o serviço para processar a lógica de negócio (salvar o log).
        _logService.ProcessLog(input.Log, input.Source, timestampString);

        // Retorna um status 201 (Created) com uma mensagem de sucesso.
        return Created("/v1/log", new
        {
            action = $"Log received {DateTime.UtcNow:o}",
            callback = input
        });
    }
}
