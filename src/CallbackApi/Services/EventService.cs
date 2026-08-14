using System.Text.Json;

namespace CallbackApi.Services;

public class EventService
{
    private readonly ILogger<EventService> _logger;

    public EventService(ILogger<EventService> logger)
    {
        _logger = logger;
    }

    public void ProcessEvent(string eventType, JsonElement payload, DateTime receivedAt)
    {
        _logger.LogInformation(
            "event_processed event_type={eventType} received_at={receivedAt} payload={payload}",
            eventType,
            receivedAt,
            payload.GetRawText());
    }
}
