/**
 * ARQUIVO: HealthController.cs
 * UTILIDADE: Fornece um endpoint simples para verificar se a API está online (Health Check).
 */
using Microsoft.AspNetCore.Mvc;

namespace CallbackApi.Controllers;

[ApiController]
[Route("v1/health")]
public class HealthController : ControllerBase
{
    // Responde a requisições GET. Usado para verificar se a API está "viva" e funcionando.
    [HttpGet]
    public IActionResult Get()
    {
        // Retorna status 200 (OK) com um objeto JSON simples.
        return Ok(new { status = "ok", service = "callback-api" });
    }
}
