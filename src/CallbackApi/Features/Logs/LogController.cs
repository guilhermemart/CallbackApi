/**
 * ARQUIVO: LogController.cs
 * UTILIDADE: Define as "rotas" de API para receber logs via HTTP POST. 
 * É a ponte entre a internet e a lógica do sistema.
 */
using Microsoft.AspNetCore.Mvc;

namespace CallbackApi.Features.Logs;

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
        var timestamp = input.Timestamp ?? DateTime.UtcNow;
        var id = Guid.NewGuid();

        var result = new
        {
            id,
            input.Log,
            input.Source,
            Timestamp = timestamp
        };

        _logService.ProcessLog(result.Log, result.Source, result.Timestamp.ToString("o"));

        return Created($"/v1/log/{id}", result);
    }
}
