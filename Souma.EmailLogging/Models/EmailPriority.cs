using System.Text.Json.Serialization;

namespace Souma.EmailLogging.Models;

/// <summary>
/// Nivel de prioridad asignado al email.
/// Permite priorizar investigación de fallos en emails críticos (OTP, AlertaFraude)
/// sobre emails de baja prioridad (reportes mensuales, notificaciones informativas).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EmailPriority
{
    /// <summary>Prioridad baja — reportes no urgentes, notificaciones informativas.</summary>
    Low = 0,

    /// <summary>Prioridad normal — flujo estándar de negocio.</summary>
    Normal = 1,

    /// <summary>Prioridad alta — operaciones que requieren atención oportuna.</summary>
    High = 2,

    /// <summary>Prioridad crítica — OTP, alertas de fraude, bloqueos de cuenta.</summary>
    Critical = 3
}
