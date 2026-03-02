using Souma.EmailLogging.Models;

namespace Souma.Tool.Models;

/// <summary>
/// Modelo de filtros aplicables al dashboard de trazabilidad de emails.
/// </summary>
public sealed class DashboardFilter
{
    /// <summary>Fecha inicial del rango.</summary>
    public DateOnly? DateFrom { get; set; }

    /// <summary>Fecha final del rango.</summary>
    public DateOnly? DateTo { get; set; }

    /// <summary>Microservicios seleccionados para filtrar.</summary>
    public List<string> SelectedMicroservices { get; set; } = [];

    /// <summary>Estados seleccionados para filtrar.</summary>
    public List<EmailStatus> SelectedStatuses { get; set; } = [];

    /// <summary>Texto para filtrar por remitente.</summary>
    public string? SenderFilter { get; set; }

    /// <summary>Texto para filtrar por destinatario.</summary>
    public string? RecipientFilter { get; set; }

    /// <summary>Indica si hay filtros activos.</summary>
    public bool HasActiveFilters =>
        DateFrom.HasValue || DateTo.HasValue ||
        SelectedMicroservices.Count > 0 || SelectedStatuses.Count > 0 ||
        !string.IsNullOrWhiteSpace(SenderFilter) || !string.IsNullOrWhiteSpace(RecipientFilter);

    /// <summary>Limpia todos los filtros.</summary>
    public void Clear()
    {
        DateFrom = null;
        DateTo = null;
        SelectedMicroservices = [];
        SelectedStatuses = [];
        SenderFilter = null;
        RecipientFilter = null;
    }
}
