using System.ComponentModel.DataAnnotations;

namespace Souma.EmailLogging.Options;

/// <summary>
/// Opciones de configuración para el sistema de logging de emails.
/// Se registra vía <c>IOptions&lt;EmailLoggingOptions&gt;</c> en el contenedor de DI.
/// </summary>
/// <example>
/// Configuración en appsettings.json:
/// <code>
/// {
///   "EmailLogging": {
///     "LogDirectory": "\\\\servidor\\logs\\email-logs\\",
///     "MaxFileSizeMb": 10,
///     "RetentionDays": 30,
///     "EnableCompression": false,
///     "FallbackDirectory": "C:\\logs\\email-fallback\\",
///     "PollingIntervalSeconds": 30
///   }
/// }
/// </code>
/// </example>
public sealed class EmailLoggingOptions
{
    /// <summary>
    /// Nombre de la sección de configuración en appsettings.json.
    /// </summary>
    public const string SectionName = "EmailLogging";

    /// <summary>
    /// Ruta del directorio donde se almacenan los archivos de log.
    /// Puede ser una ruta UNC para carpetas compartidas en red.
    /// Ejemplo: <c>\\servidor\logs\email-logs\</c>
    /// </summary>
    [Required(ErrorMessage = "LogDirectory es requerido.")]
    public string LogDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Tamaño máximo por archivo de log en megabytes.
    /// Cuando se excede, se crea un nuevo archivo con sufijo -partN.
    /// Valor por defecto: 10 MB.
    /// </summary>
    [Range(1, 100, ErrorMessage = "MaxFileSizeMb debe estar entre 1 y 100.")]
    public int MaxFileSizeMb { get; set; } = 10;

    /// <summary>
    /// Días de retención de archivos de log antes de ser archivados o eliminados.
    /// Valor por defecto: 30 días.
    /// </summary>
    [Range(1, 365, ErrorMessage = "RetentionDays debe estar entre 1 y 365.")]
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// Si es <c>true</c>, los archivos archivados se comprimen con GZip (.gz).
    /// Si es <c>false</c>, los archivos antiguos simplemente se eliminan.
    /// Valor por defecto: <c>false</c>.
    /// </summary>
    public bool EnableCompression { get; set; }

    /// <summary>
    /// Directorio alternativo donde escribir logs si el directorio principal falla.
    /// Útil cuando la ruta de red no está disponible temporalmente.
    /// Si es <c>null</c>, no se usa fallback y el error se registra internamente.
    /// </summary>
    public string? FallbackDirectory { get; set; }

    /// <summary>
    /// Intervalo en segundos para el polling del dashboard al leer nuevos archivos.
    /// Valor por defecto: 30 segundos.
    /// </summary>
    [Range(5, 300, ErrorMessage = "PollingIntervalSeconds debe estar entre 5 y 300.")]
    public int PollingIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Tamaño máximo del archivo en bytes (calculado desde <see cref="MaxFileSizeMb"/>).
    /// </summary>
    internal long MaxFileSizeBytes => MaxFileSizeMb * 1024L * 1024L;
}
