namespace Souma.EmailLogging.Models;

/// <summary>
/// Resumen estadístico de los logs de email para un día específico.
/// Utilizado por el dashboard para mostrar métricas agregadas.
/// </summary>
public sealed record EmailLogSummary
{
    /// <summary>
    /// Fecha del resumen.
    /// </summary>
    public required DateOnly Date { get; init; }

    /// <summary>
    /// Total de emails enviados exitosamente.
    /// </summary>
    public required int TotalSent { get; init; }

    /// <summary>
    /// Total de emails que fallaron.
    /// </summary>
    public required int TotalFailed { get; init; }

    /// <summary>
    /// Total de emails pendientes de envío.
    /// </summary>
    public required int TotalPending { get; init; }

    /// <summary>
    /// Total de emails en proceso de reintento.
    /// </summary>
    public required int TotalRetrying { get; init; }

    /// <summary>
    /// Duración promedio de envío en milisegundos (sobre todos los intentos del día).
    /// </summary>
    public required double AverageDurationMs { get; init; }

    /// <summary>
    /// Los 5 destinatarios que más emails recibieron en el día.
    /// </summary>
    public required List<string> TopRecipients { get; init; }

    /// <summary>
    /// Tasa de fallo como porcentaje (0.0 a 100.0).
    /// Fórmula: (TotalFailed / TotalGeneral) * 100.
    /// </summary>
    public required double FailureRate { get; init; }

    /// <summary>
    /// Total general de registros de email en el día.
    /// </summary>
    public required int TotalCount { get; init; }
}
