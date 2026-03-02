using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Souma.EmailLogging.Abstractions;
using Souma.EmailLogging.Models;
using Souma.EmailLogging.Options;

namespace Souma.EmailLogging.Services;

/// <summary>
/// Implementación principal de <see cref="IEmailLogCollector"/>.
/// Gestiona la escritura y lectura de logs de email en archivos JSON diarios.
/// </summary>
/// <remarks>
/// <para><strong>Decisión de diseño — SemaphoreSlim vs Channel&lt;T&gt;:</strong></para>
/// <para>
/// Se eligió <see cref="SemaphoreSlim"/> (1,1) sobre <c>Channel&lt;T&gt;</c> por las siguientes razones:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///       <strong>Correctitud:</strong> Cuando <c>LogEmailAsync</c> completa su Task, el log
///       YA fue escrito a disco. Con Channel, el Task completaría al encolar, no al escribir,
///       lo que genera incertidumbre sobre si el log realmente se persistió.
///     </description>
///   </item>
///   <item>
///     <description>
///       <strong>Bajo throughput:</strong> Los envíos de email son de baja frecuencia
///       (decenas por minuto, no miles por segundo). La contención del semáforo es mínima.
///     </description>
///   </item>
///   <item>
///     <description>
///       <strong>Simplicidad:</strong> No requiere un hilo consumidor en background ni
///       lógica de disposal compleja. Menos partes móviles = menos puntos de fallo.
///     </description>
///   </item>
///   <item>
///     <description>
///       <strong>Resiliencia:</strong> Los errores de I/O se manejan directamente en el
///       contexto de la llamada, facilitando retry y fallback inmediatos.
///     </description>
///   </item>
/// </list>
/// <para>
/// Channel&lt;T&gt; sería apropiado si necesitáramos: fire-and-forget puro, throughput
/// de miles de escrituras por segundo, o buffering en memoria ante picos de carga.
/// Ninguno de esos escenarios aplica para logging de email en esta escala.
/// </para>
/// </remarks>
internal sealed partial class EmailLogCollector : IEmailLogCollector, IDisposable
{
    // Prefijo estándar para los archivos de log diarios
    private const string FilePrefix = "email-log-";
    private const string FileExtension = ".json";

    private readonly IFileSystem _fileSystem;
    private readonly ILogger<EmailLogCollector> _logger;
    private readonly EmailLoggingOptions _options;

    // Semáforo para garantizar escritura secuencial dentro del mismo proceso.
    // Solo permite 1 hilo a la vez en la sección crítica de escritura.
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    // Opciones de serialización JSON reutilizables (thread-safe una vez creadas)
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Inicializa una nueva instancia de <see cref="EmailLogCollector"/>.
    /// </summary>
    /// <param name="fileSystem">Abstracción del sistema de archivos.</param>
    /// <param name="logger">Logger para diagnóstico interno.</param>
    /// <param name="options">Opciones de configuración del logging.</param>
    public EmailLogCollector(
        IFileSystem fileSystem,
        ILogger<EmailLogCollector> logger,
        IOptions<EmailLoggingOptions> options)
    {
        _fileSystem = fileSystem;
        _logger = logger;
        _options = options.Value;

        // Asegurar que el directorio de logs existe al inicializar
        AsegurarDirectorioExiste(_options.LogDirectory);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Estrategia de resiliencia:
    /// 1. Intentar escribir al directorio principal
    /// 2. Si falla → reintentar UNA vez
    /// 3. Si falla de nuevo → escribir al directorio de fallback (si está configurado)
    /// 4. Si todo falla → registrar error vía ILogger, NUNCA lanzar excepción al llamador
    /// </remarks>
    public async Task LogEmailAsync(EmailLogDto log, CancellationToken cancellationToken = default)
    {
        try
        {
            // Primer intento: directorio principal
            await EscribirLogConReintento(log, _options.LogDirectory, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Fallo al escribir log de email {MessageId} al directorio principal '{Directorio}'. Intentando fallback...",
                log.MessageId,
                _options.LogDirectory);

            // Intentar directorio de fallback si está configurado
            if (!string.IsNullOrWhiteSpace(_options.FallbackDirectory))
            {
                try
                {
                    AsegurarDirectorioExiste(_options.FallbackDirectory);
                    await EscribirLogInterno(log, _options.FallbackDirectory, cancellationToken);

                    _logger.LogInformation(
                        "Log de email {MessageId} escrito exitosamente al directorio de fallback '{Directorio}'.",
                        log.MessageId,
                        _options.FallbackDirectory);
                }
                catch (Exception fallbackEx)
                {
                    // Último recurso: solo registrar internamente, NUNCA propagar
                    _logger.LogError(
                        fallbackEx,
                        "Fallo crítico: no se pudo escribir el log de email {MessageId} ni al directorio principal ni al fallback. " +
                        "Datos del log — Microservicio: {Microservicio}, Destinatarios: {Destinatarios}, Estado: {Estado}",
                        log.MessageId,
                        log.SourceMicroservice,
                        string.Join(", ", log.RecipientAddresses),
                        log.Status);
                }
            }
            else
            {
                // Sin fallback configurado, solo registrar internamente
                _logger.LogError(
                    ex,
                    "Fallo al escribir log de email {MessageId}. No hay directorio de fallback configurado. " +
                    "Datos del log — Microservicio: {Microservicio}, Destinatarios: {Destinatarios}, Estado: {Estado}",
                    log.MessageId,
                    log.SourceMicroservice,
                    string.Join(", ", log.RecipientAddresses),
                    log.Status);
            }
        }
    }

    /// <inheritdoc/>
    public async Task<List<EmailLogDto>> GetLogsByDateAsync(
        DateOnly date,
        CancellationToken cancellationToken)
    {
        return await LeerLogsPorFecha(date, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<List<EmailLogDto>> GetLogsByMicroserviceAsync(
        string microservice,
        DateOnly? date,
        CancellationToken cancellationToken)
    {
        List<EmailLogDto> logs = await ObtenerLogsBase(date, cancellationToken);

        return logs
            .Where(l => string.Equals(l.SourceMicroservice, microservice, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<List<EmailLogDto>> GetLogsByStatusAsync(
        EmailStatus status,
        DateOnly? date,
        CancellationToken cancellationToken)
    {
        List<EmailLogDto> logs = await ObtenerLogsBase(date, cancellationToken);

        return logs
            .Where(l => l.Status == status)
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<EmailLogSummary> GetDailySummaryAsync(
        DateOnly date,
        CancellationToken cancellationToken)
    {
        List<EmailLogDto> logs = await LeerLogsPorFecha(date, cancellationToken);

        int totalCount = logs.Count;
        int totalSent = logs.Count(l => l.Status == EmailStatus.Sent);
        int totalFailed = logs.Count(l => l.Status == EmailStatus.Failed);
        int totalPending = logs.Count(l => l.Status == EmailStatus.Pending);
        int totalRetrying = logs.Count(l => l.Status == EmailStatus.Retrying);

        double averageDurationMs = totalCount > 0
            ? logs.Average(l => l.DurationMs)
            : 0.0;

        // Top 5 destinatarios: aplanar todas las listas de destinatarios y contar frecuencia
        List<string> topRecipients = logs
            .SelectMany(l => l.RecipientAddresses)
            .GroupBy(r => r, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => g.Key)
            .ToList();

        double failureRate = totalCount > 0
            ? (double)totalFailed / totalCount * 100.0
            : 0.0;

        return new EmailLogSummary
        {
            Date = date,
            TotalSent = totalSent,
            TotalFailed = totalFailed,
            TotalPending = totalPending,
            TotalRetrying = totalRetrying,
            AverageDurationMs = Math.Round(averageDurationMs, 2),
            TopRecipients = topRecipients,
            FailureRate = Math.Round(failureRate, 2),
            TotalCount = totalCount
        };
    }

    /// <inheritdoc/>
    public async Task ArchiveOldLogsAsync(int retentionDays, CancellationToken cancellationToken)
    {
        DateOnly cutoffDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-retentionDays));
        string[] archivos = _fileSystem.GetFiles(_options.LogDirectory, $"{FilePrefix}*{FileExtension}");

        foreach (string archivo in archivos)
        {
            cancellationToken.ThrowIfCancellationRequested();

            DateOnly? fechaArchivo = ExtraerFechaDeNombreArchivo(Path.GetFileName(archivo));
            if (fechaArchivo is null || fechaArchivo >= cutoffDate)
            {
                continue;
            }

            try
            {
                if (_options.EnableCompression)
                {
                    string archivoComprimido = $"{archivo}.gz";
                    await _fileSystem.CompressFileAsync(archivo, archivoComprimido, cancellationToken);

                    _logger.LogInformation(
                        "Archivo de log archivado y comprimido: {ArchivoOriginal} → {ArchivoComprimido}",
                        archivo,
                        archivoComprimido);
                }

                _fileSystem.DeleteFile(archivo);

                _logger.LogInformation(
                    "Archivo de log eliminado por retención ({RetentionDays} días): {Archivo}",
                    retentionDays,
                    archivo);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "No se pudo archivar/eliminar el archivo de log: {Archivo}",
                    archivo);
            }
        }
    }

    /// <summary>
    /// Libera los recursos del semáforo de escritura.
    /// </summary>
    public void Dispose()
    {
        _writeLock.Dispose();
    }

    #region Métodos privados — Escritura

    /// <summary>
    /// Intenta escribir el log con un reintento automático si la primera escritura falla.
    /// </summary>
    private async Task EscribirLogConReintento(
        EmailLogDto log,
        string directorio,
        CancellationToken cancellationToken)
    {
        try
        {
            await EscribirLogInterno(log, directorio, cancellationToken);
        }
        catch (Exception primeraEx)
        {
            _logger.LogWarning(
                primeraEx,
                "Primer intento de escritura falló para {MessageId}. Reintentando...",
                log.MessageId);

            // Esperar brevemente antes de reintentar (backoff mínimo)
            await Task.Delay(100, cancellationToken);

            // Segundo intento — si falla, la excepción se propaga al caller (LogEmailAsync)
            await EscribirLogInterno(log, directorio, cancellationToken);
        }
    }

    /// <summary>
    /// Escribe un log al archivo JSON correspondiente, protegido por SemaphoreSlim.
    /// </summary>
    /// <remarks>
    /// Flujo de escritura:
    /// 1. Adquirir semáforo (exclusión mutua dentro del proceso)
    /// 2. Determinar archivo de destino (basado en fecha y tamaño)
    /// 3. Leer archivo existente o crear array vacío
    /// 4. Agregar nuevo log al array
    /// 5. Serializar y escribir de vuelta (escritura atómica vía archivo temporal)
    /// </remarks>
    private async Task EscribirLogInterno(
        EmailLogDto log,
        string directorio,
        CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            string archivoDestino = DeterminarArchivoDestino(directorio, log.SentAtUtc);
            List<EmailLogDto> logsExistentes = await LeerArchivoJson(archivoDestino, cancellationToken);

            logsExistentes.Add(log);

            string json = JsonSerializer.Serialize(logsExistentes, _jsonOptions);
            await _fileSystem.WriteAllTextAsync(archivoDestino, json, cancellationToken);

            _logger.LogDebug(
                "Log de email {MessageId} escrito exitosamente a {Archivo}. Total logs en archivo: {Total}",
                log.MessageId,
                archivoDestino,
                logsExistentes.Count);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Determina el archivo de destino basándose en la fecha y el tamaño actual del archivo.
    /// Si el archivo principal excede el tamaño máximo, crea un archivo partN.
    /// </summary>
    private string DeterminarArchivoDestino(string directorio, DateTimeOffset fecha)
    {
        string fechaStr = fecha.UtcDateTime.ToString("yyyy-MM-dd");
        string archivoPrincipal = Path.Combine(directorio, $"{FilePrefix}{fechaStr}{FileExtension}");

        // Si el archivo principal no existe o está dentro del límite, usarlo
        if (!_fileSystem.FileExists(archivoPrincipal) ||
            _fileSystem.GetFileSize(archivoPrincipal) < _options.MaxFileSizeBytes)
        {
            return archivoPrincipal;
        }

        // Buscar la siguiente parte disponible
        int parte = 2;
        while (true)
        {
            string archivoParte = Path.Combine(
                directorio,
                $"{FilePrefix}{fechaStr}-part{parte}{FileExtension}");

            if (!_fileSystem.FileExists(archivoParte) ||
                _fileSystem.GetFileSize(archivoParte) < _options.MaxFileSizeBytes)
            {
                return archivoParte;
            }

            parte++;
        }
    }

    #endregion

    #region Métodos privados — Lectura

    /// <summary>
    /// Lee todos los logs de una fecha, combinando el archivo principal y todas sus partes.
    /// </summary>
    private async Task<List<EmailLogDto>> LeerLogsPorFecha(
        DateOnly date,
        CancellationToken cancellationToken)
    {
        string fechaStr = date.ToString("yyyy-MM-dd");

        // Buscar archivo principal y todas las partes: email-log-2024-03-01*.json
        string[] archivos = _fileSystem.GetFiles(
            _options.LogDirectory,
            $"{FilePrefix}{fechaStr}*{FileExtension}");

        List<EmailLogDto> todosLosLogs = [];

        foreach (string archivo in archivos.OrderBy(f => f))
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<EmailLogDto> logsDelArchivo = await LeerArchivoJson(archivo, cancellationToken);
            todosLosLogs.AddRange(logsDelArchivo);
        }

        return todosLosLogs;
    }

    /// <summary>
    /// Obtiene logs base: si se especifica fecha, filtra por esa fecha;
    /// si no, carga todos los archivos disponibles.
    /// </summary>
    private async Task<List<EmailLogDto>> ObtenerLogsBase(
        DateOnly? date,
        CancellationToken cancellationToken)
    {
        if (date.HasValue)
        {
            return await LeerLogsPorFecha(date.Value, cancellationToken);
        }

        // Sin fecha específica: leer TODOS los archivos de log disponibles
        string[] archivos = _fileSystem.GetFiles(
            _options.LogDirectory,
            $"{FilePrefix}*{FileExtension}");

        List<EmailLogDto> todosLosLogs = [];

        foreach (string archivo in archivos.OrderBy(f => f))
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<EmailLogDto> logsDelArchivo = await LeerArchivoJson(archivo, cancellationToken);
            todosLosLogs.AddRange(logsDelArchivo);
        }

        return todosLosLogs;
    }

    /// <summary>
    /// Lee y deserializa un archivo JSON de logs. Maneja archivos inexistentes,
    /// vacíos y con JSON malformado de forma segura.
    /// </summary>
    private async Task<List<EmailLogDto>> LeerArchivoJson(
        string rutaArchivo,
        CancellationToken cancellationToken)
    {
        if (!_fileSystem.FileExists(rutaArchivo))
        {
            return [];
        }

        try
        {
            string json = await _fileSystem.ReadAllTextAsync(rutaArchivo, cancellationToken);

            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            List<EmailLogDto>? logs = JsonSerializer.Deserialize<List<EmailLogDto>>(json, _jsonOptions);
            return logs ?? [];
        }
        catch (JsonException jsonEx)
        {
            // JSON malformado — registrar advertencia y continuar sin perder el resto de datos
            _logger.LogWarning(
                jsonEx,
                "Archivo de log con JSON malformado, será ignorado: {Archivo}",
                rutaArchivo);

            return [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Error al leer archivo de log: {Archivo}",
                rutaArchivo);

            return [];
        }
    }

    #endregion

    #region Métodos privados — Utilidades

    /// <summary>
    /// Asegura que un directorio existe, creándolo si es necesario.
    /// </summary>
    private void AsegurarDirectorioExiste(string directorio)
    {
        if (!_fileSystem.DirectoryExists(directorio))
        {
            _fileSystem.CreateDirectory(directorio);

            _logger.LogInformation(
                "Directorio de logs creado: {Directorio}",
                directorio);
        }
    }

    /// <summary>
    /// Extrae la fecha de un nombre de archivo con formato email-log-YYYY-MM-DD[-partN].json.
    /// Retorna null si el nombre no coincide con el patrón esperado.
    /// </summary>
    private static DateOnly? ExtraerFechaDeNombreArchivo(string nombreArchivo)
    {
        // Patrón: email-log-2024-03-01.json o email-log-2024-03-01-part2.json
        Match match = PatronFechaArchivo().Match(nombreArchivo);

        if (!match.Success)
        {
            return null;
        }

        string fechaStr = match.Groups["fecha"].Value;
        return DateOnly.TryParseExact(fechaStr, "yyyy-MM-dd", out DateOnly fecha)
            ? fecha
            : null;
    }

    /// <summary>
    /// Expresión regular compilada para extraer la fecha del nombre del archivo de log.
    /// </summary>
    [GeneratedRegex(@"^email-log-(?<fecha>\d{4}-\d{2}-\d{2})(-part\d+)?\.json$")]
    private static partial Regex PatronFechaArchivo();

    #endregion
}
