using CallbackApi.Models;
using CallbackApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace CallbackApi.Controllers;

[ApiController]
[Route("v1/[controller]")]
public class EventController : ControllerBase
{
    private readonly EventService _eventService;

    public EventController(EventService eventService)
    {
        _eventService = eventService;
    }

    [HttpPost]
    public IActionResult Receive([FromBody] EventInput input)
    {
        var id = Guid.NewGuid();
        var receivedAt = DateTime.UtcNow;

        _eventService.ProcessEvent(input.EventType, input.Payload, receivedAt);

        var result = new
        {
            id,
            event_type = input.EventType,
            payload = input.Payload,
            received_at = receivedAt
        };

        return Created($"/v1/event/{id}", result);
    }
}
