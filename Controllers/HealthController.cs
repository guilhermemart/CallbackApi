using Microsoft.AspNetCore.Mvc;

namespace CallbackApi.Controllers;

[ApiController]
[Route("v1/health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new { status = "ok", service = "callback-api" });
    }
}