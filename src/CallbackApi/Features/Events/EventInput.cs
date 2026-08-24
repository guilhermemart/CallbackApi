using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CallbackApi.Features.Events;

// DTO que representa o envelope genérico recebido de sistemas externos.
public class EventInput : IValidatableObject
{
    [Required]
    [JsonPropertyName("event_type")]
    public string EventType { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("payload")]
    public JsonElement Payload { get; set; }

    public DateTime GetCreatedAt()
    {
        var createdAt = Payload.GetProperty("created_at").GetString();
        return DateTime.Parse(
            createdAt!,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind).ToUniversalTime();
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Payload.ValueKind != JsonValueKind.Object)
        {
            yield return new ValidationResult(
                "payload must be a JSON object.",
                [nameof(Payload)]);
            yield break;
        }

        if (!HasNonEmptyArray("source"))
        {
            yield return MissingOrInvalid("source must be a non-empty JSON array.");
        }

        if (!HasNonEmptyString("source_id"))
        {
            yield return MissingOrInvalid("source_id must be a non-empty string.");
        }

        if (!Payload.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            yield return MissingOrInvalid("data must be a JSON object.");
        }

        if (!HasValidDateTime("created_at"))
        {
            yield return MissingOrInvalid("created_at must be an ISO 8601 date and time.");
        }

        if (!HasNonEmptyString("created_by"))
        {
            yield return MissingOrInvalid("created_by must be a non-empty string.");
        }
    }

    private bool HasNonEmptyArray(string propertyName)
    {
        return Payload.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Array
            && value.GetArrayLength() > 0;
    }

    private bool HasNonEmptyString(string propertyName)
    {
        return Payload.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString());
    }

    private bool HasValidDateTime(string propertyName)
    {
        return Payload.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
            && DateTime.TryParse(
                value.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out _);
    }

    private static ValidationResult MissingOrInvalid(string message)
    {
        return new ValidationResult(message, [nameof(Payload)]);
    }
}
