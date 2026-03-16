namespace Souma.Tool.Models;

/// <summary>
/// Datos de comparación de tendencia entre el valor actual y el período anterior.
/// </summary>
public sealed record TrendData
{
    /// <summary>Valor del período actual.</summary>
    public double CurrentValue { get; init; }
    /// <summary>Valor del período anterior.</summary>
    public double PreviousValue { get; init; }
    /// <summary>Porcentaje de cambio (positivo = aumento, negativo = disminución).</summary>
    public double ChangePercent { get; init; }
    /// <summary>Dirección de la tendencia: "up", "down", o "neutral".</summary>
    public string Direction { get; init; } = "neutral";
}
