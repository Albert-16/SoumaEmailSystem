using Souma.MicroserviceExample.Contracts;

namespace Souma.MicroserviceExample.Services;

/// <summary>
/// Interfaz para el envío de emails.
/// En producción, esto conectaría con el API externo real (SendGrid, SMTP, etc.).
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Envía un email y retorna el resultado.
    /// </summary>
    /// <param name="request">Datos del email a enviar.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>Resultado del envío con éxito/fallo y mensaje descriptivo.</returns>
    Task<EmailSendResult> SendAsync(SendEmailRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Resultado del intento de envío de email.
/// </summary>
public sealed record EmailSendResult
{
    /// <summary>
    /// Indica si el envío fue exitoso.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Mensaje descriptivo del resultado (ej: "OK", "HTTP 503 Service Unavailable").
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Código de respuesta SMTP numérico. Null si no aplica (ej: error de conexión).
    /// Valores comunes: 250 (OK), 421 (Service not available), 450 (Mailbox busy),
    /// 503 (Bad sequence), 550 (Mailbox not found).
    /// </summary>
    public int? SmtpStatusCode { get; init; }

    /// <summary>
    /// Pasos del pipeline ejecutados durante el envío.
    /// </summary>
    public List<Souma.EmailLogging.Models.PipelineStep>? PipelineSteps { get; init; }
}
