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

        if (!string.IsNullOrWhiteSpace(filter.TransactionType))
        {
            query = query.Where(l =>
                string.Equals(l.TransactionType, filter.TransactionType, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(filter.SelectedPriority) &&
            Enum.TryParse<EmailPriority>(filter.SelectedPriority, true, out EmailPriority priority))
        {
            query = query.Where(l => l.Priority == priority);
        }

        if (!string.IsNullOrWhiteSpace(filter.SelectedHostName))
        {
            query = query.Where(l =>
                string.Equals(l.HostName, filter.SelectedHostName, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.MinDurationMs.HasValue)
        {
            query = query.Where(l => l.DurationMs >= filter.MinDurationMs.Value);
        }

        if (filter.HasPipelineErrors == true)
        {
            query = query.Where(l =>
                l.PipelineSteps != null &&
                l.PipelineSteps.Any(s => s.StepStatus == PipelineStepStatus.Failed));
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

    /// <summary>Calcula percentiles de duración.</summary>
    public PerformanceMetrics GetPerformanceMetrics(List<EmailLogDto> logs, int slaThresholdMs = 2000)
    {
        if (logs.Count == 0)
            return new PerformanceMetrics();

        long[] duraciones = logs.Select(l => l.DurationMs).OrderBy(d => d).ToArray();
        int count = duraciones.Length;

        double p50 = duraciones[(int)(count * 0.50)];
        double p95 = duraciones[Math.Min((int)(count * 0.95), count - 1)];
        double p99 = duraciones[Math.Min((int)(count * 0.99), count - 1)];

        double slaRate = count > 0
            ? (double)duraciones.Count(d => d <= slaThresholdMs) / count * 100.0
            : 0;

        return new PerformanceMetrics
        {
            P50 = Math.Round(p50, 2),
            P95 = Math.Round(p95, 2),
            P99 = Math.Round(p99, 2),
            SlaComplianceRate = Math.Round(slaRate, 2)
        };
    }

    /// <summary>Genera datos para el heatmap de actividad por hora (7 días x 24 horas).</summary>
    public int[,] GetHourlyHeatmapData(List<EmailLogDto> logs)
    {
        // [día de semana 0=Lun..6=Dom, hora 0..23]
        int[,] heatmap = new int[7, 24];

        foreach (EmailLogDto log in logs)
        {
            // Convertir DayOfWeek (0=Sun) a 0=Lun
            int diaSemana = ((int)log.SentAtUtc.DayOfWeek + 6) % 7;
            int hora = log.SentAtUtc.Hour;
            heatmap[diaSemana, hora]++;
        }

        return heatmap;
    }

    /// <summary>Obtiene distribución por tipo de transacción.</summary>
    public Dictionary<string, int> GetTransactionTypeBreakdown(List<EmailLogDto> logs)
    {
        return logs
            .Where(l => !string.IsNullOrWhiteSpace(l.TransactionType))
            .GroupBy(l => l.TransactionType!)
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>Obtiene distribución por prioridad.</summary>
    public Dictionary<string, int> GetPriorityBreakdown(List<EmailLogDto> logs)
    {
        return logs
            .GroupBy(l => l.Priority.ToString())
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>Calcula tiempo promedio en cola en milisegundos.</summary>
    public double GetAverageQueueTime(List<EmailLogDto> logs)
    {
        var conCola = logs.Where(l => l.QueuedAtUtc.HasValue).ToList();
        if (conCola.Count == 0) return 0;

        return Math.Round(conCola.Average(l => (l.SentAtUtc - l.QueuedAtUtc!.Value).TotalMilliseconds), 2);
    }

    /// <summary>Calcula tendencia comparando hoy vs ayer.</summary>
    public TrendData GetVolumeTrend()
    {
        List<EmailLogDto> allLogs = GetAllLogs();
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        DateOnly yesterday = today.AddDays(-1);

        DateTimeOffset todayStart = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        DateTimeOffset todayEnd = today.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
        DateTimeOffset yesterdayStart = yesterday.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        DateTimeOffset yesterdayEnd = yesterday.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        double todayCount = allLogs.Count(l => l.SentAtUtc >= todayStart && l.SentAtUtc <= todayEnd);
        double yesterdayCount = allLogs.Count(l => l.SentAtUtc >= yesterdayStart && l.SentAtUtc <= yesterdayEnd);

        double changePercent = yesterdayCount > 0
            ? Math.Round((todayCount - yesterdayCount) / yesterdayCount * 100.0, 1)
            : 0;

        string direction = changePercent > 0 ? "up" : changePercent < 0 ? "down" : "neutral";

        return new TrendData
        {
            CurrentValue = todayCount,
            PreviousValue = yesterdayCount,
            ChangePercent = changePercent,
            Direction = direction
        };
    }

    /// <summary>Obtiene todos los intentos correlacionados por CorrelationId.</summary>
    public List<EmailLogDto> GetCorrelatedAttempts(string correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId)) return [];

        return GetAllLogs()
            .Where(l => string.Equals(l.CorrelationId, correlationId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(l => l.SentAtUtc)
            .ToList();
    }

    /// <summary>Obtiene distribución de fallos por paso del pipeline.</summary>
    public Dictionary<string, int> GetPipelineFailureBreakdown(List<EmailLogDto> logs)
    {
        return logs
            .Where(l => l.PipelineSteps is not null)
            .SelectMany(l => l.PipelineSteps!)
            .Where(s => s.StepStatus == PipelineStepStatus.Failed)
            .GroupBy(s => s.StepName)
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>Obtiene la lista de hostnames únicos en los logs.</summary>
    public List<string> GetAvailableHostNames()
    {
        return GetAllLogs()
            .Where(l => !string.IsNullOrWhiteSpace(l.HostName))
            .Select(l => l.HostName!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(h => h)
            .ToList();
    }

    /// <summary>Obtiene la lista de tipos de transacción únicos en los logs.</summary>
    public List<string> GetAvailableTransactionTypes()
    {
        return GetAllLogs()
            .Where(l => !string.IsNullOrWhiteSpace(l.TransactionType))
            .Select(l => l.TransactionType!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t)
            .ToList();
    }
}
