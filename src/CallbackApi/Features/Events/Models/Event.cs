namespace CallbackApi.Features.Events.Models;

// Representa o evento que será persistido no PostgreSQL.
public class Event
{
    public Guid Id { get; set; }

    public string EventType { get; set; } = string.Empty;

    // JSON original do callback, preservado sem depender do fabricante.
    public string Payload { get; set; } = string.Empty;

    public DateTime ReceivedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }
}
