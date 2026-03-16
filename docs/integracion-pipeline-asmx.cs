// =============================================================================
// GUIA DE INTEGRACION — MailSender ASMX .NET 4.7.2
// =============================================================================
//
// Tu servicio ya referencia Souma.EmailLogging y usa logSuccess/logFailure.
// Esta guia muestra como agregar el PipelineTracker a tu flujo existente
// para capturar los 6 pasos y alimentar el dashboard con trazabilidad completa.
//
// ANTES (sin pipeline):
//   try { enviarCorreo(); logSuccess(dto); }
//   catch { logFailure(dto); }
//
// DESPUES (con pipeline):
//   var tracker = new PipelineTracker();
//   tracker.Execute(1, "SUBJECT_AND_PROVIDER", ...);
//   tracker.Execute(2, "MAIL_TEXT", ...);
//   ...
//   dto.PipelineSteps = tracker.ToSteps();  // <-- agregar esto
//   if (tracker.HasFailed) logFailure(dto); else logSuccess(dto);
//
// =============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Souma.EmailLogging.Models;

namespace TuServicioCorreo.Helpers
{
    // =========================================================================
    // PipelineTracker — Agregar a tu carpeta Helpers
    // =========================================================================

    /// <summary>
    /// Rastreador de pipeline que registra cada paso con su duracion y resultado.
    /// Cuando un paso falla, los siguientes se marcan automaticamente como Skipped.
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
        /// Ejecuta un paso del pipeline. Si un paso anterior fallo, este se marca como Skipped.
        /// </summary>
        public void Execute(int order, string code, string name, Action action)
        {
            DateTimeOffset inicioUtc = DateTimeOffset.UtcNow;

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
                // Warning NO detiene el pipeline
            }
            catch (Exception ex)
            {
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
        /// Ejecuta un paso OPCIONAL (MailImages, MailAttachments).
        /// Si no aplica, se registra como OK con mensaje informativo.
        /// </summary>
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
        /// Retorna la lista de pasos para asignar al EmailLogDto.PipelineSteps.
        /// </summary>
        public List<PipelineStep> ToSteps() => _steps;
    }

    /// <summary>
    /// Excepcion para indicar Warning (el paso continua pero con advertencia).
    /// </summary>
    public class PipelineWarningException : Exception
    {
        public PipelineWarningException(string message) : base(message) { }
    }
}

// =============================================================================
// EJEMPLO: Tu MailSender actual con pipeline integrado
// =============================================================================

namespace TuServicioCorreo
{
    using TuServicioCorreo.Helpers;
    using Souma.EmailLogging.Models;
    using Souma.EmailLogging.Abstractions;

    public class MailSender
    {
        private readonly IEmailLogCollector _logCollector; // ya lo tienes en tu helper

        public MailSender(IEmailLogCollector logCollector)
        {
            _logCollector = logCollector;
        }

        /// <summary>
        /// Tu metodo de envio — ahora con pipeline instrumentado.
        /// </summary>
        public void EnviarCorreo(/* tus parametros actuales */)
        {
            // NUEVO: Crear el tracker al inicio
            PipelineTracker tracker = new PipelineTracker();
            Stopwatch totalSw = Stopwatch.StartNew();
            DateTimeOffset queuedAtUtc = DateTimeOffset.UtcNow;

            string textoCorreo = null;

            // =================================================================
            // PASO 1: SubjectAndProvider
            // Donde validas el subject y el proveedor.
            // Envuelve tu logica existente con tracker.Execute()
            // =================================================================
            tracker.Execute(1, "SUBJECT_AND_PROVIDER", "Asunto y Proveedor", () =>
            {
                // >>> TU CODIGO EXISTENTE AQUI <<<
                // if (string.IsNullOrEmpty(subject))
                //     throw new Exception("Campo 'Subject' es requerido");
                // if (!EsProveedorPermitido(dominio))
                //     throw new Exception($"Proveedor @{dominio} no permitido");
            });

            // =================================================================
            // PASO 2: MailText
            // Donde cargas la plantilla del gestor.
            // =================================================================
            tracker.Execute(2, "MAIL_TEXT", "Texto del Correo", () =>
            {
                // >>> TU CODIGO EXISTENTE AQUI <<<
                // textoCorreo = gestorPlantillas.Obtener(templateName);
                // if (textoCorreo == null)
                //     throw new Exception($"Plantilla '{templateName}' no encontrada");
                // if (!plantilla.Activa)
                //     throw new Exception($"Plantilla '{templateName}' inactiva");
            });

            // =================================================================
            // PASO 3: MailVars
            // Donde reemplazas {NombreCliente}, {Monto}, etc.
            // =================================================================
            tracker.Execute(3, "MAIL_VARS", "Variables de Plantilla", () =>
            {
                // >>> TU CODIGO EXISTENTE AQUI <<<
                // foreach (var v in variables)
                //     textoCorreo = textoCorreo.Replace("{" + v.Key + "}", v.Value);
                //
                // Si una variable no tiene valor:
                //     throw new Exception("Variable {NombreCliente} no tiene valor");
                //
                // Si un monto es 0 (advertencia, no error):
                //     throw new PipelineWarningException("Variable {Monto} = 0.00");
            });

            // =================================================================
            // PASO 4: MailImages (OPCIONAL)
            // Si no hay imagenes, pasa OK automaticamente.
            // =================================================================
            bool tieneImagenes = false; // tu logica: request.Imagenes?.Count > 0
            tracker.ExecuteOptional(4, "MAIL_IMAGES", "Imagenes del Correo",
                applies: tieneImagenes,
                action: () =>
                {
                    // >>> TU CODIGO EXISTENTE AQUI <<<
                    // foreach (var img in imagenes)
                    //     if (!repo.Existe(img))
                    //         throw new Exception($"Imagen '{img}' no encontrada");
                },
                skipMessage: "Sin imagenes — paso completado sin accion"
            );

            // =================================================================
            // PASO 5: MailTo
            // Donde estableces los destinatarios.
            // =================================================================
            tracker.Execute(5, "MAIL_TO", "Destinatarios", () =>
            {
                // >>> TU CODIGO EXISTENTE AQUI <<<
                // if (destinatarios.Count == 0)
                //     throw new Exception("Lista de destinatarios vacia");
                // foreach (var d in destinatarios) message.To.Add(d);
            });

            // =================================================================
            // PASO 6: MailAttachments (OPCIONAL)
            // Si no hay adjuntos, pasa OK automaticamente.
            // =================================================================
            bool tieneAdjuntos = false; // tu logica: request.Adjuntos?.Count > 0
            tracker.ExecuteOptional(6, "MAIL_ATTACHMENTS", "Adjuntos",
                applies: tieneAdjuntos,
                action: () =>
                {
                    // >>> TU CODIGO EXISTENTE AQUI <<<
                    // foreach (var adj in adjuntos)
                    //     if (adj.Size > 10MB)
                    //         throw new Exception("Adjunto excede 10MB");
                    //     if (ext == ".exe")
                    //         throw new Exception("Formato .exe no permitido");
                },
                skipMessage: "Sin adjuntos — paso completado sin accion"
            );

            totalSw.Stop();

            // =================================================================
            // LOGGING — Igual que antes, pero con PipelineSteps
            // =================================================================

            EmailLogDto logEntry = new()
            {
                SourceMicroservice = "TuServicioCorreo",
                SenderAddress = "noreply@davivienda.hn",
                RecipientAddresses = new List<string> { /* tus destinatarios */ },
                Subject = "...",
                Status = tracker.HasFailed ? EmailStatus.Failed : EmailStatus.Sent,
                StatusMessage = tracker.HasFailed ? tracker.FailureMessage : null,
                SentAtUtc = DateTimeOffset.UtcNow,
                DurationMs = totalSw.ElapsedMilliseconds,
                HasAttachments = tieneAdjuntos,
                Environment = DeploymentEnvironment.DEV,

                // --- Campos extendidos ---
                HostName = System.Environment.MachineName,
                TransactionType = "NotificacionTransferencia",
                Priority = EmailPriority.Normal,
                QueuedAtUtc = queuedAtUtc,
                InitiatedBy = "SistemaX",
                AttachmentCount = 0,
                ContentType = EmailContentType.Html,

                // >>> ESTO ES LO NUEVO <<<
                PipelineSteps = tracker.ToSteps()
            };

            // Llamar como ya lo haces — logSuccess / logFailure
            _logCollector.LogEmailAsync(logEntry).Wait();
        }
    }
}

// =============================================================================
// RESUMEN — QUE CAMBIAR EN TU CODIGO EXISTENTE
// =============================================================================
//
// 1. AGREGAR: PipelineTracker.cs + PipelineWarningException.cs a Helpers/
//
// 2. MODIFICAR tu EnviarCorreo():
//    - Crear PipelineTracker al inicio
//    - Envolver cada seccion con tracker.Execute() o tracker.ExecuteOptional()
//    - Agregar PipelineSteps = tracker.ToSteps() al EmailLogDto
//    - Usar tracker.HasFailed para decidir logSuccess vs logFailure
//
// 3. NO CAMBIAR: Tus metodos internos (ValidarSubject, CargarPlantilla, etc.)
//    Solo deben lanzar Exception cuando algo falla.
//    El tracker las captura automaticamente.
//
// 4. NO CAMBIAR: logSuccess / logFailure
//    Siguen funcionando igual — el DTO ahora incluye PipelineSteps.
//
// 5. PARA WARNING (advertencia sin detener pipeline):
//    throw new PipelineWarningException("Variable {Monto} = 0.00");
//    Se muestra en amarillo en el dashboard, pero el envio continua.
//
// =============================================================================
