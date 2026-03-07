/**
 * ARQUIVO: LogInput.cs
 * UTILIDADE: Define o formato dos dados (modelo) que a API espera receber do cliente.
 */
using System.ComponentModel.DataAnnotations;

namespace CallbackApi.Models;

// Data Transfer Object (DTO): Uma classe simples usada apenas para carregar dados entre o cliente e o servidor.
public class LogInput
{
    // [Required] garante que o campo 'Log' não pode ser enviado vazio.
    [Required]
    public string Log { get; set; } = string.Empty;

    // O '?' indica que este campo é opcional (pode ser nulo).
    public DateTime? Timestamp { get; set; }

    [Required]
    public string Source { get; set; } = string.Empty;
}
