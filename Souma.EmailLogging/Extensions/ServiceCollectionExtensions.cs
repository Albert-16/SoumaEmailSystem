using Microsoft.Extensions.DependencyInjection;
using Souma.EmailLogging.Abstractions;
using Souma.EmailLogging.Options;
using Souma.EmailLogging.Services;

namespace Souma.EmailLogging.Extensions;

/// <summary>
/// Métodos de extensión para registrar los servicios de Souma.EmailLogging
/// en el contenedor de inyección de dependencias.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra los servicios de logging de email en el contenedor de DI.
    /// </summary>
    /// <param name="services">Colección de servicios.</param>
    /// <param name="configure">Acción para configurar las opciones de logging.</param>
    /// <returns>La misma colección de servicios para encadenamiento fluido.</returns>
    /// <example>
    /// <code>
    /// // En Program.cs del microservicio:
    /// builder.Services.AddEmailLogging(options =>
    /// {
    ///     options.LogDirectory = builder.Configuration["EmailLogging:LogDirectory"]!;
    ///     options.FallbackDirectory = builder.Configuration["EmailLogging:FallbackDirectory"];
    ///     options.MaxFileSizeMb = 10;
    ///     options.RetentionDays = 30;
    /// });
    ///
    /// // Luego inyectar IEmailLogCollector en cualquier servicio:
    /// public class MiServicioDeEmail(IEmailLogCollector logCollector)
    /// {
    ///     // Usar logCollector.LogEmailAsync(...) después de cada envío
    /// }
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException">Si <paramref name="services"/> o <paramref name="configure"/> son null.</exception>
    public static IServiceCollection AddEmailLogging(
        this IServiceCollection services,
        Action<EmailLoggingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        // Registrar y validar opciones de configuración
        services.Configure(configure);

        // Registrar la abstracción del sistema de archivos (Singleton: es stateless)
        services.AddSingleton<IFileSystem, DefaultFileSystem>();

        // Registrar el collector como Singleton:
        // - Mantiene el SemaphoreSlim durante toda la vida de la aplicación
        // - Las opciones y logger se inyectan una sola vez
        // - Thread-safe por diseño (SemaphoreSlim interno)
        services.AddSingleton<IEmailLogCollector, EmailLogCollector>();

        return services;
    }
}
