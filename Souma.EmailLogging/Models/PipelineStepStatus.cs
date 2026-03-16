using System.Text.Json.Serialization;

namespace Souma.EmailLogging.Models;

/// <summary>
/// Estado de ejecución de un paso individual del pipeline de envío de email.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PipelineStepStatus
{
    /// <summary>El paso se ejecutó exitosamente.</summary>
    OK = 0,

    /// <summary>El paso falló — los pasos subsiguientes se marcan como Skipped.</summary>
    Failed = 1,

    /// <summary>El paso no se ejecutó porque un paso anterior falló.</summary>
    Skipped = 2,

    /// <summary>El paso se completó con advertencias que no impiden el envío.</summary>
    Warning = 3
}
