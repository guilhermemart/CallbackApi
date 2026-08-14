using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CallbackApi.Models;

// DTO que representa o envelope genérico recebido de sistemas externos.
public class EventInput
{
    [Required]
    [JsonPropertyName("event_type")]
    public string EventType { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("payload")]
    public JsonElement Payload { get; set; }
}
