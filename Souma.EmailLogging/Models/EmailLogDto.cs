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
    /// Identificador único del registro de log. Se genera automáticamente al crear la instancia.
    /// </summary>
    public Guid MessageId { get; init; } = Guid.NewGuid();

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
}
