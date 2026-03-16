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

    /// <summary>
    /// Percentil 50 (mediana) de duración de envío en milisegundos.
    /// </summary>
    public double PercentileP50 { get; init; }

    /// <summary>
    /// Percentil 95 de duración de envío en milisegundos.
    /// Indicador clave de rendimiento — revela outliers que el promedio oculta.
    /// </summary>
    public double PercentileP95 { get; init; }

    /// <summary>
    /// Percentil 99 de duración de envío en milisegundos.
    /// </summary>
    public double PercentileP99 { get; init; }

    /// <summary>
    /// Porcentaje de emails enviados dentro del umbral SLA (0.0 a 100.0).
    /// Un email cumple SLA si su <c>DurationMs</c> es menor o igual al umbral configurado.
    /// </summary>
    public double SlaComplianceRate { get; init; }
}
