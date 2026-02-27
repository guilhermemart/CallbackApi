using System.ComponentModel.DataAnnotations;

namespace CallbackApi.Models;

public class LogInput
{
    [Required]
    public string Log { get; set; } = string.Empty;

    public DateTime? Timestamp { get; set; }

    public string? Source { get; set; }
}