namespace Souma.MicroserviceExample.Contracts;

/// <summary>
/// Solicitud de envío de email recibida por el endpoint POST /api/email/send.
/// </summary>
public sealed record SendEmailRequest
{
    /// <summary>
    /// Identificador de correlación opcional para vincular reintentos del mismo email lógico.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Dirección de correo del remitente.
    /// </summary>
    public required string SenderAddress { get; init; }

    /// <summary>
    /// Lista de destinatarios principales.
    /// </summary>
    public required List<string> RecipientAddresses { get; init; }

    /// <summary>
    /// Lista opcional de destinatarios en copia (CC).
    /// </summary>
    public List<string>? CcAddresses { get; init; }

    /// <summary>
    /// Asunto del email.
    /// </summary>
    public required string Subject { get; init; }

    /// <summary>
    /// Cuerpo del email (no se persiste en el log por seguridad).
    /// </summary>
    public required string Body { get; init; }

    /// <summary>
    /// Indica si el email incluye archivos adjuntos.
    /// </summary>
    public bool HasAttachments { get; init; }

    /// <summary>
    /// Tipo de transacción bancaria que genera el email.
    /// Ejemplos: "OTP", "EstadoCuenta", "AlertaFraude", "AprobacionPrestamo".
    /// </summary>
    public string? TransactionType { get; init; }

    /// <summary>
    /// Nivel de prioridad del email.
    /// </summary>
    public string? Priority { get; init; }

    /// <summary>
    /// Tipo de contenido del cuerpo: "PlainText" o "Html".
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Cantidad de archivos adjuntos.
    /// </summary>
    public int AttachmentCount { get; init; }

    /// <summary>
    /// Tamaño estimado del email en bytes.
    /// </summary>
    public long? EmailSizeBytes { get; init; }

    /// <summary>
    /// Identificador del usuario o proceso que inició el envío (ID opaco, no PII).
    /// </summary>
    public string? InitiatedBy { get; init; }
}
