using Microsoft.JSInterop;

namespace Souma.Tool.Services;

/// <summary>
/// Servicio de interop con JavaScript para Chart.js.
/// Encapsula todas las llamadas JS para crear, actualizar y destruir gráficos.
/// </summary>
public sealed class ChartInteropService : IAsyncDisposable
{
    private readonly IJSRuntime _jsRuntime;

    public ChartInteropService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    /// <summary>Crea o actualiza un gráfico de línea (volumen diario).</summary>
    public async Task RenderLineChartAsync(
        string canvasId, string[] labels, int[] data, CancellationToken ct = default)
    {
        await _jsRuntime.InvokeVoidAsync("SoumaCharts.renderLineChart", ct, canvasId, labels, data);
    }

    /// <summary>Crea o actualiza un gráfico de dona (distribución de estado).</summary>
    public async Task RenderDoughnutChartAsync(
        string canvasId, string[] labels, int[] data, string[] colors, CancellationToken ct = default)
    {
        await _jsRuntime.InvokeVoidAsync("SoumaCharts.renderDoughnutChart", ct, canvasId, labels, data, colors);
    }

    /// <summary>Crea o actualiza un gráfico de barras (emails por microservicio).</summary>
    public async Task RenderBarChartAsync(
        string canvasId, string[] labels, int[] data, CancellationToken ct = default)
    {
        await _jsRuntime.InvokeVoidAsync("SoumaCharts.renderBarChart", ct, canvasId, labels, data);
    }

    /// <summary>Destruye un gráfico existente por su canvas ID.</summary>
    public async Task DestroyChartAsync(string canvasId, CancellationToken ct = default)
    {
        await _jsRuntime.InvokeVoidAsync("SoumaCharts.destroyChart", ct, canvasId);
    }

    public async ValueTask DisposeAsync()
    {
        // Chart.js limpia sus instancias en el navegador automáticamente
        await ValueTask.CompletedTask;
    }
}
