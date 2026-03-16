using System.Text.Json.Serialization;

namespace Souma.EmailLogging.Models;

/// <summary>
/// Registro inmutable que representa un intento de envío de email con todos sus metadatos.
/// Se serializa a JSON para almacenamiento en archivos diarios de trazabilidad.
/// </summary>
/// <remarks>
/// Se usa <c>record</c> para garantizar inmutabilidad del log una vez creado.
/// <c>MessageId</c> se genera automáticamente; <c>CorrelationId</c> permite vincular
/// reintentos del mismo email lógico entre sí.
/// Se utiliza <see cref="DateTimeOffset"/> en lugar de <c>DateTime</c> para evitar
/// ambigüedad de zona horaria, siguiendo las mejores prácticas de .NET.
/// </remarks>
public sealed record EmailLogDto
{
    /// <summary>
    /// Identificador del mensaje retornado por la API de envío.
    /// Vacío cuando el envío falló y no se obtuvo respuesta de la API.
    /// </summary>
    public string MessageId { get; init; } = "";

    /// <summary>
    /// Identificador de correlación para vincular reintentos del mismo email lógico.
    /// Cuando un email falla y se reintenta, cada intento genera un <see cref="MessageId"/>
    /// diferente, pero comparten el mismo <see cref="CorrelationId"/>.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Nombre del microservicio que originó el envío del email.
    /// Ejemplo: "SoumaIntegration", "BankXmlMapper".
    /// </summary>
    public required string SourceMicroservice { get; init; }

    /// <summary>
    /// Dirección de correo del remitente.
    /// </summary>
    public required string SenderAddress { get; init; }

    /// <summary>
    /// Lista de direcciones de los destinatarios principales.
    /// </summary>
    public required List<string> RecipientAddresses { get; init; }

    /// <summary>
    /// Lista opcional de direcciones en copia (CC).
    /// </summary>
    public List<string>? CcAddresses { get; init; }

    /// <summary>
    /// Asunto del email.
    /// </summary>
    public required string Subject { get; init; }

    /// <summary>
    /// Estado del envío del email.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required EmailStatus Status { get; init; }

    /// <summary>
    /// Mensaje descriptivo del estado, especialmente útil en caso de fallo.
    /// Puede contener: código HTTP, mensaje de excepción, cuerpo de respuesta del API, etc.
    /// </summary>
    public string? StatusMessage { get; init; }

    /// <summary>
    /// Fecha y hora UTC en que se realizó el intento de envío.
    /// Se utiliza <see cref="DateTimeOffset"/> para evitar ambigüedad de zona horaria.
    /// </summary>
    public required DateTimeOffset SentAtUtc { get; init; }

    /// <summary>
    /// Duración total de la operación de envío en milisegundos.
    /// Medido con <see cref="System.Diagnostics.Stopwatch"/> en el microservicio origen.
    /// </summary>
    public required long DurationMs { get; init; }

    /// <summary>
    /// Número de reintentos realizados para este envío. Por defecto es 0 (primer intento).
    /// </summary>
    public int RetryCount { get; init; }

    /// <summary>
    /// Indica si el email incluye archivos adjuntos.
    /// Útil para diagnóstico sin exponer el contenido de los adjuntos.
    /// </summary>
    public bool HasAttachments { get; init; }

    /// <summary>
    /// Ambiente de despliegue donde se ejecuta el microservicio.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required DeploymentEnvironment Environment { get; init; }

    // =========================================================================
    // Campos extendidos — Fase 2 de trazabilidad profesional
    // Todos son opcionales/nullable para retrocompatibilidad con JSON existentes.
    // =========================================================================

    /// <summary>
    /// Nombre del servidor/instancia que procesó el envío.
    /// Crítico cuando hay múltiples instancias detrás de un balanceador.
    /// Se captura automáticamente con <c>System.Environment.MachineName</c>.
    /// </summary>
    public string? HostName { get; init; }

    /// <summary>
    /// Código de respuesta SMTP numérico (250=OK, 503=Service Unavailable, 550=Mailbox not found).
    /// Permite filtrar y agrupar errores por tipo, a diferencia de <see cref="StatusMessage"/> que es texto libre.
    /// </summary>
    public int? SmtpStatusCode { get; init; }

    /// <summary>
    /// Tamaño total del mensaje en bytes (headers + body + adjuntos).
    /// Permite detectar emails anómalamente grandes que afectan rendimiento.
    /// </summary>
    public long? EmailSizeBytes { get; init; }

    /// <summary>
    /// Cantidad exacta de archivos adjuntos. Complementa <see cref="HasAttachments"/>
    /// con granularidad numérica para análisis de impacto en rendimiento.
    /// </summary>
    public int AttachmentCount { get; init; }

    /// <summary>
    /// Tipo de contenido del cuerpo del email (PlainText o Html).
    /// Útil para diagnosticar problemas de renderizado.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EmailContentType? ContentType { get; init; }

    /// <summary>
    /// Tipo de transacción bancaria que generó el email.
    /// Ejemplos: "OTP", "EstadoCuenta", "AlertaFraude", "AprobacionPrestamo".
    /// Permite agrupar emails por flujo de negocio, no solo por microservicio.
    /// </summary>
    public string? TransactionType { get; init; }

    /// <summary>
    /// Nivel de prioridad del email. Emails de tipo "AlertaFraude" o "OTP" son Critical;
    /// "EstadoCuenta" mensual es Normal. Permite priorizar investigación de fallos.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EmailPriority Priority { get; init; } = EmailPriority.Normal;

    /// <summary>
    /// Momento UTC en que la solicitud fue recibida/encolada, separado de <see cref="SentAtUtc"/>.
    /// La diferencia <c>SentAtUtc - QueuedAtUtc</c> revela el tiempo de espera en cola.
    /// </summary>
    public DateTimeOffset? QueuedAtUtc { get; init; }

    /// <summary>
    /// Identificador del usuario o proceso que inició el envío (NO el email sender).
    /// Ejemplo: "Sistema", "USR-45612", "BATCH-Nocturno".
    /// Solo IDs opacos — nunca nombre real ni información personal.
    /// </summary>
    public string? InitiatedBy { get; init; }

    /// <summary>
    /// Dirección IP de la instancia/servicio que hizo la solicitud HTTP al endpoint de envío.
    /// Permite correlacionar con logs de infraestructura y detectar solicitudes anómalas.
    /// </summary>
    public string? RequestOriginIp { get; init; }

    /// <summary>
    /// Pasos del pipeline de envío con su estado individual.
    /// Cada paso registra si fue exitoso, falló o fue omitido.
    /// Null para registros antiguos que no capturaron el pipeline.
    /// </summary>
    public List<PipelineStep>? PipelineSteps { get; init; }
}
