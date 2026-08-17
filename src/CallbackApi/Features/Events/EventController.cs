using Microsoft.AspNetCore.Mvc;

namespace CallbackApi.Features.Events;

[ApiController]
[Route("v1/[controller]")]
public class EventController : ControllerBase
{
    private readonly EventService _eventService;

    public EventController(EventService eventService)
    {
        _eventService = eventService;
    }

    [HttpPost("/v1/event/save")]
    public async Task<IActionResult> Receive([FromBody] EventInput input, CancellationToken cancellationToken)
    {
        var @event = await _eventService.QueueAsync(input, cancellationToken);

        var result = new
        {
            id = @event.Id,
            event_type = @event.EventType,
            payload = input.Payload,
            received_at = @event.ReceivedAt,
            updated_at = @event.UpdatedAt,
            deleted_at = @event.DeletedAt,
            status = "queued"
        };

        return Accepted($"/v1/events/{@event.Id}", result);
    }

    [HttpGet("/v1/events/list")]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var events = await _eventService.ListAsync(cancellationToken);
        return Ok(events);
    }

    [HttpGet("/v1/event/get/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var @event = await _eventService.GetAsync(id, cancellationToken);

        return @event is not null ? Ok(@event) : NotFound();
    }

    [HttpPost("/v1/event/update/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] EventInput input, CancellationToken cancellationToken)
    {
        var @event = await _eventService.UpdateAsync(id, input, cancellationToken);

        return @event is not null ? Accepted($"/v1/events/{id}") : NotFound();
    }

    [HttpPatch("/v1/events/softdelete/{id:guid}")]
    public async Task<IActionResult> SoftDelete(Guid id, CancellationToken cancellationToken)
    {
        var wasSoftDeleted = await _eventService.SoftDeleteAsync(id, cancellationToken);

        return wasSoftDeleted ? Accepted() : NotFound();
    }

    [HttpDelete("/v1/events/delete/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var wasDeleted = await _eventService.DeleteAsync(id, cancellationToken);

        return wasDeleted ? Accepted() : NotFound();
    }
}
