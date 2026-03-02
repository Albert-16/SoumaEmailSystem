using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Souma.EmailLogging.Models;
using Souma.EmailLogging.Options;
using Souma.Tool.Models;

namespace Souma.Tool.Services;

/// <summary>
/// Servicio de lectura de logs de email desde archivos JSON.
/// Mantiene un caché en memoria que se refresca vía polling.
/// </summary>
public sealed class EmailLogReaderService
{
    private const string CacheKeyLogs = "dashboard_all_logs";
    private readonly IMemoryCache _cache;
    private readonly EmailLoggingOptions _options;
    private readonly ILogger<EmailLogReaderService> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Evento que se dispara cuando hay datos nuevos disponibles
    public event Action? OnDataRefreshed;

    // Último conteo de logs conocido (para detectar nuevos logs en el polling)
    private int _lastKnownCount;

    /// <summary>Indica si hay datos nuevos desde el último refresh.</summary>
    public bool HasNewData { get; private set; }

    public EmailLogReaderService(
        IMemoryCache cache,
        IOptions<EmailLoggingOptions> options,
        ILogger<EmailLogReaderService> logger)
    {
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Refresca el caché leyendo todos los archivos JSON del directorio de logs.
    /// Llamado periódicamente por <see cref="EmailLogPollingService"/>.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            List<EmailLogDto> todosLosLogs = [];

            if (!Directory.Exists(_options.LogDirectory))
            {
                _logger.LogWarning("El directorio de logs no existe: {Directorio}", _options.LogDirectory);
                return;
            }

            string[] archivos = Directory.GetFiles(_options.LogDirectory, "email-log-*.json");

            foreach (string archivo in archivos.OrderBy(f => f))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    string json = await File.ReadAllTextAsync(archivo, cancellationToken);
                    if (string.IsNullOrWhiteSpace(json)) continue;

                    List<EmailLogDto>? logs = JsonSerializer.Deserialize<List<EmailLogDto>>(json, _jsonOptions);
                    if (logs is not null)
                    {
                        todosLosLogs.AddRange(logs);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "JSON malformado en archivo: {Archivo}", archivo);
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "Error de I/O al leer: {Archivo}", archivo);
                }
            }

            // Detectar si hay logs nuevos
            int nuevoCount = todosLosLogs.Count;
            HasNewData = nuevoCount > _lastKnownCount && _lastKnownCount > 0;
            _lastKnownCount = nuevoCount;

            // Guardar en caché con TTL igual al intervalo de polling
            MemoryCacheEntryOptions cacheOptions = new()
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_options.PollingIntervalSeconds + 5)
            };
            _cache.Set(CacheKeyLogs, todosLosLogs, cacheOptions);

            _logger.LogDebug("Caché refrescado: {Total} logs cargados desde {Archivos} archivos",
                todosLosLogs.Count, archivos.Length);

            // Notificar a los componentes suscritos
            OnDataRefreshed?.Invoke();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error al refrescar caché de logs");
        }
    }

    /// <summary>Obtiene todos los logs cacheados.</summary>
    public List<EmailLogDto> GetAllLogs()
    {
        return _cache.TryGetValue(CacheKeyLogs, out List<EmailLogDto>? logs)
            ? logs ?? []
            : [];
    }

    /// <summary>Aplica los filtros del dashboard a los logs cacheados.</summary>
    public List<EmailLogDto> GetFilteredLogs(DashboardFilter filter)
    {
        IEnumerable<EmailLogDto> query = GetAllLogs();

        if (filter.DateFrom.HasValue)
        {
            DateTimeOffset desde = filter.DateFrom.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(l => l.SentAtUtc >= desde);
        }

        if (filter.DateTo.HasValue)
        {
            DateTimeOffset hasta = filter.DateTo.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            query = query.Where(l => l.SentAtUtc <= hasta);
        }

        if (filter.SelectedMicroservices.Count > 0)
        {
            query = query.Where(l =>
                filter.SelectedMicroservices.Contains(l.SourceMicroservice, StringComparer.OrdinalIgnoreCase));
        }

        if (filter.SelectedStatuses.Count > 0)
        {
            query = query.Where(l => filter.SelectedStatuses.Contains(l.Status));
        }

        if (!string.IsNullOrWhiteSpace(filter.SenderFilter))
        {
            query = query.Where(l =>
                l.SenderAddress.Contains(filter.SenderFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(filter.RecipientFilter))
        {
            query = query.Where(l =>
                l.RecipientAddresses.Any(r =>
                    r.Contains(filter.RecipientFilter, StringComparison.OrdinalIgnoreCase)));
        }

        return query.OrderByDescending(l => l.SentAtUtc).ToList();
    }

    /// <summary>Genera resumen de una lista de logs.</summary>
    public EmailLogSummary GetSummary(List<EmailLogDto> logs, DateOnly date)
    {
        int totalCount = logs.Count;
        int totalSent = logs.Count(l => l.Status == EmailStatus.Sent);
        int totalFailed = logs.Count(l => l.Status == EmailStatus.Failed);
        int totalPending = logs.Count(l => l.Status == EmailStatus.Pending);
        int totalRetrying = logs.Count(l => l.Status == EmailStatus.Retrying);

        double avgDuration = totalCount > 0 ? logs.Average(l => l.DurationMs) : 0;

        List<string> topRecipients = logs
            .SelectMany(l => l.RecipientAddresses)
            .GroupBy(r => r, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => g.Key)
            .ToList();

        double failureRate = totalCount > 0 ? (double)totalFailed / totalCount * 100.0 : 0;

        return new EmailLogSummary
        {
            Date = date,
            TotalSent = totalSent,
            TotalFailed = totalFailed,
            TotalPending = totalPending,
            TotalRetrying = totalRetrying,
            AverageDurationMs = Math.Round(avgDuration, 2),
            TopRecipients = topRecipients,
            FailureRate = Math.Round(failureRate, 2),
            TotalCount = totalCount
        };
    }

    /// <summary>Obtiene la lista de microservicios únicos en los logs.</summary>
    public List<string> GetAvailableMicroservices()
    {
        return GetAllLogs()
            .Select(l => l.SourceMicroservice)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(m => m)
            .ToList();
    }

    /// <summary>Calcula la tasa de fallo en los últimos N minutos.</summary>
    public double GetRecentFailureRate(int minutes)
    {
        DateTimeOffset desde = DateTimeOffset.UtcNow.AddMinutes(-minutes);
        List<EmailLogDto> recientes = GetAllLogs()
            .Where(l => l.SentAtUtc >= desde)
            .ToList();

        if (recientes.Count == 0) return 0;

        int fallidos = recientes.Count(l => l.Status == EmailStatus.Failed);
        return (double)fallidos / recientes.Count * 100.0;
    }

    /// <summary>Marca que los datos nuevos ya fueron vistos por el usuario.</summary>
    public void AcknowledgeNewData()
    {
        HasNewData = false;
    }
}
