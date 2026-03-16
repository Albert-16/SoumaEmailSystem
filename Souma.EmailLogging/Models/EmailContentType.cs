using System.Text.Json.Serialization;

namespace Souma.EmailLogging.Models;

/// <summary>
/// Tipo de contenido del cuerpo del email.
/// Útil para diagnosticar problemas de renderizado reportados por destinatarios.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EmailContentType
{
    /// <summary>Texto plano sin formato.</summary>
    PlainText = 0,

    /// <summary>Contenido HTML con formato y estilos.</summary>
    Html = 1
}
