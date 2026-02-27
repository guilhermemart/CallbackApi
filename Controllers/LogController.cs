using Microsoft.AspNetCore.Mvc;
using CallbackApi.Models;
using CallbackApi.Services;

namespace CallbackApi.Controllers;

[ApiController]
[Route("v1/log")]
public class LogController : ControllerBase
{
    private readonly LogService _logService;

    public LogController(LogService logService)
    {
        _logService = logService;
    }

    [HttpPost]
    public IActionResult Receive([FromBody] LogInput input)
    {
        var timestamp = input.Timestamp?.ToString("o") ?? "unknown";
        var source = input.Source ?? "unknown";

        _logService.ProcessLog(input.Log, source, timestamp);

        return Created(string.Empty, new
        {
            action = $"Log received {DateTime.UtcNow:o}",
            callback = input
        });
    }
}