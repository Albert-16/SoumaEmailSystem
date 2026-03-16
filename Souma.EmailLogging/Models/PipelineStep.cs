using System.Text.Json.Serialization;

namespace Souma.EmailLogging.Models;

/// <summary>
/// Representa un paso individual en el pipeline de envío de email.
/// Cada email pasa por una secuencia de 6 pasos: SubjectAndProvider → MailText →
/// MailVars → MailImages → MailTo → MailAttachments.
/// Si un paso falla, los siguientes se marcan como <see cref="PipelineStepStatus.Skipped"/>.
/// MailImages y MailAttachments son opcionales — si no aplican, continúan sin error.
/// </summary>
/// <remarks>
/// Códigos de paso estándar:
/// <list type="bullet">
///   <item><c>SUBJECT_AND_PROVIDER</c> — Validar asunto y proveedor de correo</item>
///   <item><c>MAIL_TEXT</c> — Cargar y preparar el texto/plantilla del correo</item>
///   <item><c>MAIL_VARS</c> — Reemplazar variables de la plantilla con valores reales</item>
///   <item><c>MAIL_IMAGES</c> — Cargar imágenes inline (opcional, continúa si no hay)</item>
///   <item><c>MAIL_TO</c> — Validar y establecer destinatarios</item>
///   <item><c>MAIL_ATTACHMENTS</c> — Preparar adjuntos (opcional, continúa si no hay)</item>
/// </list>
/// </remarks>
public sealed record PipelineStep
{
    /// <summary>
    /// Orden secuencial del paso dentro del pipeline (1, 2, 3...).
    /// </summary>
    public required int StepOrder { get; init; }

    /// <summary>
    /// Código único del paso. Ejemplo: "VALIDATE_RECIPIENTS", "LOAD_TEMPLATE".
    /// Permite agrupar fallos por tipo de paso en el dashboard.
    /// </summary>
    public required string StepCode { get; init; }

    /// <summary>
    /// Nombre legible del paso para mostrar en la UI.
    /// Ejemplo: "Validar Destinatarios", "Cargar Plantilla".
    /// </summary>
    public required string StepName { get; init; }

    /// <summary>
    /// Estado de ejecución del paso.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required PipelineStepStatus StepStatus { get; init; }

    /// <summary>
    /// Detalle del resultado o mensaje de error.
    /// Ejemplo: "Proveedor @hotmail.com no permitido", "Plantilla 'AlertaFraude_v2' no encontrada".
    /// Null cuando el paso fue exitoso y no hay detalle adicional.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Duración de este paso individual en milisegundos.
    /// </summary>
    public required long DurationMs { get; init; }

    /// <summary>
    /// Timestamp UTC de cuándo se ejecutó este paso.
    /// </summary>
    public required DateTimeOffset ExecutedAtUtc { get; init; }
}
