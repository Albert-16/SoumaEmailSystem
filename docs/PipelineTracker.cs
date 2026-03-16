// =========================================================================
// PipelineTracker.cs — Copiar a tu carpeta Helpers/
// Compatible con .NET Framework 4.7.2
// Requiere: referencia a Souma.EmailLogging (ya la tienes)
// =========================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Souma.EmailLogging.Models;

namespace TuServicioCorreo.Helpers
{
    /// <summary>
    /// Rastreador de pipeline que registra cada paso del envio de correo
    /// con su duracion y resultado. Cuando un paso falla, los siguientes
    /// se marcan automaticamente como Skipped.
    /// </summary>
    public class PipelineTracker
    {
        private readonly List<PipelineStep> _steps = new List<PipelineStep>();
        private bool _hasFailed;
        private string _failureMessage;
        private string _failedStepCode;

        /// <summary>Indica si algun paso del pipeline fallo.</summary>
        public bool HasFailed => _hasFailed;

        /// <summary>Mensaje de error del primer paso que fallo.</summary>
        public string FailureMessage => _failureMessage;

        /// <summary>Codigo del paso que fallo.</summary>
        public string FailedStepCode => _failedStepCode;

        /// <summary>
        /// Ejecuta un paso OBLIGATORIO del pipeline.
        /// Si un paso anterior ya fallo, este se marca como Skipped automaticamente.
        /// </summary>
        /// <param name="order">Orden secuencial (1, 2, 3...)</param>
        /// <param name="code">Codigo unico: SUBJECT_AND_PROVIDER, MAIL_TEXT, etc.</param>
        /// <param name="name">Nombre legible: "Asunto y Proveedor", "Texto del Correo", etc.</param>
        /// <param name="action">Accion a ejecutar. Si lanza Exception, el paso se marca como Failed.</param>
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
                // Fallo — los pasos siguientes seran Skipped
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
        /// <param name="skipMessage">Mensaje cuando applies=false</param>
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

            Execute(order, code, name, action);
        }

        /// <summary>
        /// Retorna la lista de pasos para asignar a EmailLogDto.PipelineSteps.
        /// Llamar al final del pipeline: dto.PipelineSteps = tracker.ToSteps();
        /// </summary>
        public List<PipelineStep> ToSteps() => _steps;
    }

    /// <summary>
    /// Excepcion especial para indicar Warning en un paso del pipeline.
    /// El paso se registra como Warning pero el pipeline NO se detiene.
    /// Uso: throw new PipelineWarningException("Variable {Monto} = 0.00");
    /// </summary>
    public class PipelineWarningException : Exception
    {
        public PipelineWarningException(string message) : base(message) { }
    }
}
