namespace Souma.MicroserviceExample.Contracts;

/// <summary>
/// Respuesta del endpoint POST /api/email/send.
/// </summary>
public sealed record SendEmailResponse
{
    /// <summary>
    /// Indica si el email se envió exitosamente.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Identificador único del registro de log generado.
    /// </summary>
    public required Guid MessageId { get; init; }

    /// <summary>
    /// Mensaje descriptivo del resultado.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Duración de la operación de envío en milisegundos.
    /// </summary>
    public required long DurationMs { get; init; }
}
