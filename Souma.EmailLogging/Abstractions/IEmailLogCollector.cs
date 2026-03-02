using Souma.EmailLogging.Models;

namespace Souma.EmailLogging.Abstractions;

/// <summary>
/// Interfaz principal del sistema de logging de emails.
/// Permite registrar, consultar y archivar logs de envío de email.
/// </summary>
/// <remarks>
/// <para>
/// Esta interfaz es el punto de integración para los microservicios existentes.
/// Después de cada intento de envío de email (exitoso o fallido), el microservicio
/// llama a <see cref="LogEmailAsync"/> con el resultado.
/// </para>
/// <para>
/// Los métodos de consulta (<c>GetLogsBy*</c>) son utilizados principalmente
/// por el dashboard Blazor (Souma.Tool) para visualizar métricas.
/// </para>
/// </remarks>
public interface IEmailLogCollector
{
    /// <summary>
    /// Registra un intento de envío de email en el archivo de log diario.
    /// </summary>
    /// <param name="log">DTO con los datos del envío a registrar.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <remarks>
    /// Este método NUNCA lanza excepciones al llamador. Si la escritura falla,
    /// se reintenta una vez, luego se intenta el directorio de fallback,
    /// y si todo falla, se registra internamente vía ILogger.
    /// </remarks>
    Task LogEmailAsync(EmailLogDto log, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene todos los logs de email para una fecha específica.
    /// </summary>
    /// <param name="date">Fecha de los logs a consultar.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>Lista de logs del día especificado. Lista vacía si no hay logs.</returns>
    Task<List<EmailLogDto>> GetLogsByDateAsync(DateOnly date, CancellationToken cancellationToken);

    /// <summary>
    /// Obtiene logs de email filtrados por microservicio de origen.
    /// </summary>
    /// <param name="microservice">Nombre del microservicio a filtrar.</param>
    /// <param name="date">Fecha opcional. Si es <c>null</c>, busca en todos los archivos disponibles.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>Lista de logs filtrados por microservicio.</returns>
    Task<List<EmailLogDto>> GetLogsByMicroserviceAsync(
        string microservice,
        DateOnly? date,
        CancellationToken cancellationToken);

    /// <summary>
    /// Obtiene logs de email filtrados por estado de envío.
    /// </summary>
    /// <param name="status">Estado de email a filtrar.</param>
    /// <param name="date">Fecha opcional. Si es <c>null</c>, busca en todos los archivos disponibles.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>Lista de logs filtrados por estado.</returns>
    Task<List<EmailLogDto>> GetLogsByStatusAsync(
        EmailStatus status,
        DateOnly? date,
        CancellationToken cancellationToken);

    /// <summary>
    /// Genera un resumen estadístico de los logs de email para un día específico.
    /// </summary>
    /// <param name="date">Fecha del resumen.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>Resumen con totales, promedios, top destinatarios y tasa de fallo.</returns>
    Task<EmailLogSummary> GetDailySummaryAsync(DateOnly date, CancellationToken cancellationToken);

    /// <summary>
    /// Archiva (comprime o elimina) archivos de log más antiguos que los días de retención configurados.
    /// </summary>
    /// <param name="retentionDays">Número de días a retener. Archivos más antiguos serán procesados.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <remarks>
    /// Si <c>EnableCompression</c> está habilitado en las opciones, los archivos se comprimen
    /// a formato GZip antes de eliminar el original. Si no, simplemente se eliminan.
    /// </remarks>
    Task ArchiveOldLogsAsync(int retentionDays, CancellationToken cancellationToken);
}
