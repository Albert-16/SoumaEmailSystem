namespace Souma.Tool.Models;

/// <summary>
/// Métricas de rendimiento calculadas sobre los logs de email.
/// </summary>
public sealed record PerformanceMetrics
{
    /// <summary>Percentil 50 (mediana) de duración en ms.</summary>
    public double P50 { get; init; }
    /// <summary>Percentil 95 de duración en ms.</summary>
    public double P95 { get; init; }
    /// <summary>Percentil 99 de duración en ms.</summary>
    public double P99 { get; init; }
    /// <summary>Tasa de cumplimiento SLA (0.0 a 100.0).</summary>
    public double SlaComplianceRate { get; init; }
}
