namespace Souma.EmailLogging.Models;

/// <summary>
/// Representa el ambiente de despliegue donde se ejecuta el microservicio.
/// </summary>
public enum DeploymentEnvironment
{
    /// <summary>
    /// Ambiente de desarrollo.
    /// </summary>
    DEV = 0,

    /// <summary>
    /// Ambiente de pruebas de aceptación de usuario.
    /// </summary>
    UAT = 1
}
