// =========================================================================
// PipelineTracker.cs — Copiar a tu carpeta Helpers/ del servicio de correo
// Compatible con .NET Framework 4.7.2
// NO requiere referencia a Souma.EmailLogging — todo esta autocontenido
// Usa Newtonsoft.Json (que ya tienes en tu proyecto 4.7.2)
// =========================================================================
// El JSON generado es 100% compatible con el dashboard Souma.Tool (.NET 10)
// porque los nombres de propiedades y valores de enum coinciden exactamente.
// =========================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace TuServicioCorreo.Helpers
{
    // =====================================================================
    // ENUM: PipelineStepStatus
    // =====================================================================

    /// <summary>
    /// Estado de ejecucion de un paso individual del pipeline de envio.
    /// Los valores deben coincidir con Souma.EmailLogging.Models.PipelineStepStatus
    /// para que el dashboard los interprete correctamente.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum PipelineStepStatus
    {
        /// <summary>El paso se ejecuto exitosamente.</summary>
        OK = 0,

        /// <summary>El paso fallo — los pasos siguientes se marcan como Skipped.</summary>
        Failed = 1,

        /// <summary>El paso no se ejecuto porque un paso anterior fallo.</summary>
        Skipped = 2,

        /// <summary>El paso se completo con advertencias que no impiden el envio.</summary>
        Warning = 3
    }

    // =====================================================================
    // MODELO: PipelineStep
    // =====================================================================

    /// <summary>
    /// Representa un paso individual en el pipeline de envio de correo.
    /// Pasos estandar: SubjectAndProvider, MailText, MailVars, MailImages, MailTo, MailAttachments.
    /// </summary>
    /// <remarks>
    /// Equivalente a Souma.EmailLogging.Models.PipelineStep pero como clase
    /// compatible con .NET Framework 4.7.2 (sin record, sin required, sin init).
    /// Los nombres de propiedad JSON coinciden exactamente con el dashboard.
    /// </remarks>
    public class PipelineStep
    {
        /// <summary>Orden secuencial del paso (1, 2, 3...)</summary>
        [JsonProperty("stepOrder")]
        public int StepOrder { get; set; }

        /// <summary>Codigo unico: SUBJECT_AND_PROVIDER, MAIL_TEXT, etc.</summary>
        [JsonProperty("stepCode")]
        public string StepCode { get; set; }

        /// <summary>Nombre legible: "Asunto y Proveedor", "Texto del Correo", etc.</summary>
        [JsonProperty("stepName")]
        public string StepName { get; set; }

        /// <summary>Estado de ejecucion del paso.</summary>
        [JsonProperty("stepStatus")]
        public PipelineStepStatus StepStatus { get; set; }

        /// <summary>Detalle del resultado o mensaje de error. Null si fue exitoso.</summary>
        [JsonProperty("message", NullValueHandling = NullValueHandling.Ignore)]
        public string Message { get; set; }

        /// <summary>Duracion de este paso en milisegundos.</summary>
        [JsonProperty("durationMs")]
        public long DurationMs { get; set; }

        /// <summary>Timestamp UTC de cuando se ejecuto este paso.</summary>
        [JsonProperty("executedAtUtc")]
        public DateTimeOffset ExecutedAtUtc { get; set; }
    }

    // =====================================================================
    // CLASE: PipelineTracker
    // =====================================================================

    /// <summary>
    /// Rastreador de pipeline que registra cada paso del envio de correo
    /// con su duracion y resultado. Cuando un paso falla, los siguientes
    /// se marcan automaticamente como Skipped.
    /// </summary>
    /// <example>
    /// Uso basico:
    /// <code>
    /// var tracker = new PipelineTracker();
    /// tracker.Execute(1, "SUBJECT_AND_PROVIDER", "Asunto y Proveedor", () =>
    /// {
    ///     mailObj = mailSetter.SubjectAndProvider(..., ref error);
    ///     if (error != "Exito!") throw new Exception(error);
    /// });
    /// // ... mas pasos ...
    /// dto.PipelineSteps = tracker.ToSteps();
    /// </code>
    /// </example>
    public class PipelineTracker
    {
        private readonly List<PipelineStep> _steps = new List<PipelineStep>();
        private bool _hasFailed;
        private string _failureMessage;
        private string _failedStepCode;

        /// <summary>Indica si algun paso del pipeline fallo.</summary>
        public bool HasFailed
        {
            get { return _hasFailed; }
        }

        /// <summary>Mensaje de error del primer paso que fallo.</summary>
        public string FailureMessage
        {
            get { return _failureMessage; }
        }

        /// <summary>Codigo del paso que fallo.</summary>
        public string FailedStepCode
        {
            get { return _failedStepCode; }
        }

        /// <summary>
        /// Ejecuta un paso OBLIGATORIO del pipeline.
        /// Si un paso anterior ya fallo, este se marca como Skipped automaticamente.
        /// </summary>
        /// <param name="order">Orden secuencial (1, 2, 3...)</param>
        /// <param name="code">Codigo unico: SUBJECT_AND_PROVIDER, MAIL_TEXT, etc.</param>
        /// <param name="name">Nombre legible: "Asunto y Proveedor", "Texto del Correo", etc.</param>
        /// <param name="action">
        /// Accion a ejecutar. Debe lanzar Exception si algo falla.
        /// Patron tipico: if (error != "Exito!") throw new Exception(error);
        /// </param>
        public void Execute(int order, string code, string name, Action action)
        {
            DateTimeOffset inicioUtc = DateTimeOffset.UtcNow;

            // Si ya fallo un paso anterior, marcar como Skipped y salir
            if (_hasFailed)
            {
                _steps.Add(new PipelineStep
                {
                    StepOrder = order,
                    StepCode = code,
                    StepName = name,
                    StepStatus = PipelineStepStatus.Skipped,
                    Message = "Paso omitido — un paso anterior fallo",
                    DurationMs = 0,
                    ExecutedAtUtc = inicioUtc
                });
                return;
            }

            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                action();
                sw.Stop();

                _steps.Add(new PipelineStep
                {
                    StepOrder = order,
                    StepCode = code,
                    StepName = name,
                    StepStatus = PipelineStepStatus.OK,
                    DurationMs = sw.ElapsedMilliseconds,
                    ExecutedAtUtc = inicioUtc
                });
            }
            catch (PipelineWarningException wex)
            {
                // Warning NO detiene el pipeline — el envio continua
                sw.Stop();
                _steps.Add(new PipelineStep
                {
                    StepOrder = order,
                    StepCode = code,
                    StepName = name,
                    StepStatus = PipelineStepStatus.Warning,
                    Message = wex.Message,
                    DurationMs = sw.ElapsedMilliseconds,
                    ExecutedAtUtc = inicioUtc
                });
            }
            catch (Exception ex)
            {
                // Fallo real — los pasos siguientes seran Skipped
                sw.Stop();
                _steps.Add(new PipelineStep
                {
                    StepOrder = order,
                    StepCode = code,
                    StepName = name,
                    StepStatus = PipelineStepStatus.Failed,
                    Message = ex.Message,
                    DurationMs = sw.ElapsedMilliseconds,
                    ExecutedAtUtc = inicioUtc
                });

                _hasFailed = true;
                _failureMessage = ex.Message;
                _failedStepCode = code;
            }
        }

        /// <summary>
        /// Ejecuta un paso OPCIONAL del pipeline (MailImages, MailAttachments).
        /// Si <paramref name="applies"/> es false, se registra como OK con mensaje informativo.
        /// Si un paso anterior fallo, se registra como Skipped independientemente.
        /// </summary>
        /// <param name="order">Orden secuencial (1, 2, 3...)</param>
        /// <param name="code">Codigo unico: MAIL_IMAGES, MAIL_ATTACHMENTS</param>
        /// <param name="name">Nombre legible: "Imagenes del Correo", "Adjuntos"</param>
        /// <param name="applies">true si hay datos que procesar, false si no aplica</param>
        /// <param name="action">Accion a ejecutar cuando applies=true</param>
        /// <param name="skipMessage">Mensaje informativo cuando applies=false</param>
        public void ExecuteOptional(int order, string code, string name,
            bool applies, Action action, string skipMessage = "Sin accion — paso completado")
        {
            if (!applies)
            {
                _steps.Add(new PipelineStep
                {
                    StepOrder = order,
                    StepCode = code,
                    StepName = name,
                    StepStatus = _hasFailed ? PipelineStepStatus.Skipped : PipelineStepStatus.OK,
                    Message = _hasFailed ? "Paso omitido — un paso anterior fallo" : skipMessage,
                    DurationMs = 0,
                    ExecutedAtUtc = DateTimeOffset.UtcNow
                });
                return;
            }

            // Si aplica, delegar a Execute normal
            Execute(order, code, name, action);
        }

        /// <summary>
        /// Retorna la lista de pasos registrados.
        /// Asignar al campo PipelineSteps de tu DTO/JSON de log.
        /// </summary>
        public List<PipelineStep> ToSteps()
        {
            return _steps;
        }
    }

    // =====================================================================
    // CLASE: PipelineWarningException
    // =====================================================================

    /// <summary>
    /// Excepcion especial para indicar Warning en un paso del pipeline.
    /// El paso se registra como Warning (amarillo en el dashboard) pero
    /// el pipeline NO se detiene — el envio continua.
    /// </summary>
    /// <example>
    /// <code>
    /// // Ejemplo: variable con valor sospechoso pero valido
    /// if (monto == 0)
    ///     throw new PipelineWarningException("Variable {Monto} = 0.00");
    /// </code>
    /// </example>
    public class PipelineWarningException : Exception
    {
        public PipelineWarningException(string message) : base(message) { }
    }
}
