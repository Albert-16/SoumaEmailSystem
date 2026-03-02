namespace Souma.EmailLogging.Models;

/// <summary>
/// Representa los posibles estados de un envío de email.
/// </summary>
public enum EmailStatus
{
    /// <summary>
    /// El email fue enviado exitosamente al proveedor de correo.
    /// </summary>
    Sent = 0,

    /// <summary>
    /// El envío falló por un error (ver StatusMessage para detalle).
    /// </summary>
    Failed = 1,

    /// <summary>
    /// El email está pendiente de envío (encolado para procesamiento posterior).
    /// </summary>
    Pending = 2,

    /// <summary>
    /// El email falló previamente y se está reintentando.
    /// </summary>
    Retrying = 3
}
