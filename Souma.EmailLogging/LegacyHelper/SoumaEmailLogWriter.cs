// =============================================================================
// SoumaEmailLogWriter.cs — Helper para .NET Framework 4.7.2+ (ASMX, WCF, etc.)
// =============================================================================
//
// INSTRUCCIONES DE USO:
//   1. Copiar ESTE ARCHIVO completo a tu proyecto .NET Framework
//   2. Agregar referencia a Newtonsoft.Json (ya lo tienes si usas Web API / ASMX)
//   3. Llamar a SoumaEmailLogWriter.LogEmail(...) despues de cada envio de email
//
// IMPORTANTE:
//   - Este archivo escribe al MISMO formato JSON que lee Souma.Tool (dashboard)
//   - No requiere referencia a Souma.EmailLogging.dll (es autocontenido)
//   - Compatible con .NET Framework 4.5+, .NET Standard 2.0+, y .NET 6+
//   - Incluye PipelineTracker para rastrear los 6 pasos del envio de correo
//
// =============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Souma.EmailLogging.Legacy
{
    #region Enums — Deben coincidir exactamente con Souma.EmailLogging.Models

    /// <summary>
    /// Estados posibles de un envio de email.
    /// Los valores string deben coincidir con EmailStatus en Souma.EmailLogging.
    /// </summary>
    public enum EmailStatus
    {
        Sent = 0,
        Failed = 1,
        Pending = 2,
        Retrying = 3
    }

    /// <summary>
    /// Ambientes de despliegue.
    /// </summary>
    public enum DeploymentEnvironment
    {
        DEV = 0,
        UAT = 1
    }

    /// <summary>
    /// Nivel de prioridad del email.
    /// </summary>
    public enum EmailPriority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Critical = 3
    }

    /// <summary>
    /// Tipo de contenido del cuerpo del email.
    /// </summary>
    public enum EmailContentType
    {
        PlainText = 0,
        Html = 1
    }

    /// <summary>
    /// Estado de ejecucion de un paso individual del pipeline de envio.
    /// </summary>
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

    #endregion

    #region DTOs — Deben coincidir exactamente con el schema de Souma.EmailLogging

    /// <summary>
    /// Representa un paso individual en el pipeline de envio de correo.
    /// Pasos: SubjectAndProvider → MailText → MailVars → MailImages → MailTo → MailAttachments.
    /// </summary>
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
        [JsonConverter(typeof(StringEnumConverter))]
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

    /// <summary>
    /// DTO de log de email. Los nombres de propiedades JSON DEBEN coincidir
    /// exactamente con los que produce Souma.EmailLogging (.NET 10) en camelCase.
    /// </summary>
    public class EmailLogEntry
    {
        // =================================================================
        // Campos base
        // =================================================================

        /// <summary>
        /// ID del mensaje retornado por la API al enviar exitosamente.
        /// Vacio cuando el envio fallo y no se obtuvo respuesta de la API.
        /// </summary>
        [JsonProperty("messageId")]
        public string MessageId { get; set; }

        [JsonProperty("correlationId", NullValueHandling = NullValueHandling.Ignore)]
        public string CorrelationId { get; set; }

        [JsonProperty("sourceMicroservice")]
        public string SourceMicroservice { get; set; }

        [JsonProperty("senderAddress")]
        public string SenderAddress { get; set; }

        [JsonProperty("recipientAddresses")]
        public List<string> RecipientAddresses { get; set; }

        [JsonProperty("ccAddresses", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> CcAddresses { get; set; }

        [JsonProperty("subject")]
        public string Subject { get; set; }

        [JsonProperty("status")]
        [JsonConverter(typeof(StringEnumConverter))]
        public EmailStatus Status { get; set; }

        [JsonProperty("statusMessage", NullValueHandling = NullValueHandling.Ignore)]
        public string StatusMessage { get; set; }

        [JsonProperty("sentAtUtc")]
        public DateTimeOffset SentAtUtc { get; set; }

        [JsonProperty("durationMs")]
        public long DurationMs { get; set; }

        [JsonProperty("retryCount")]
        public int RetryCount { get; set; }

        [JsonProperty("hasAttachments")]
        public bool HasAttachments { get; set; }

        [JsonProperty("environment")]
        [JsonConverter(typeof(StringEnumConverter))]
        public DeploymentEnvironment Environment { get; set; }

        // =================================================================
        // Campos extendidos — Fase 2 de trazabilidad profesional
        // =================================================================

        /// <summary>Nombre del servidor/instancia que proceso el envio.</summary>
        [JsonProperty("hostName", NullValueHandling = NullValueHandling.Ignore)]
        public string HostName { get; set; }

        /// <summary>Codigo de respuesta SMTP numerico (250=OK, 503=Unavailable, etc.).</summary>
        [JsonProperty("smtpStatusCode", NullValueHandling = NullValueHandling.Ignore)]
        public int? SmtpStatusCode { get; set; }

        /// <summary>Tamano total del mensaje en bytes (headers + body + adjuntos).</summary>
        [JsonProperty("emailSizeBytes", NullValueHandling = NullValueHandling.Ignore)]
        public long? EmailSizeBytes { get; set; }

        /// <summary>Cantidad exacta de archivos adjuntos.</summary>
        [JsonProperty("attachmentCount")]
        public int AttachmentCount { get; set; }

        /// <summary>Tipo de contenido del cuerpo del email.</summary>
        [JsonProperty("contentType", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(StringEnumConverter))]
        public EmailContentType? ContentType { get; set; }

        /// <summary>Tipo de transaccion bancaria: "OTP", "EstadoCuenta", "AlertaFraude", etc.</summary>
        [JsonProperty("transactionType", NullValueHandling = NullValueHandling.Ignore)]
        public string TransactionType { get; set; }

        /// <summary>Nivel de prioridad del email.</summary>
        [JsonProperty("priority")]
        [JsonConverter(typeof(StringEnumConverter))]
        public EmailPriority Priority { get; set; }

        /// <summary>Momento UTC en que la solicitud fue recibida/encolada.</summary>
        [JsonProperty("queuedAtUtc", NullValueHandling = NullValueHandling.Ignore)]
        public DateTimeOffset? QueuedAtUtc { get; set; }

        /// <summary>ID del usuario o proceso que inicio el envio (NO email sender).</summary>
        [JsonProperty("initiatedBy", NullValueHandling = NullValueHandling.Ignore)]
        public string InitiatedBy { get; set; }

        /// <summary>IP de la instancia/servicio que hizo la solicitud.</summary>
        [JsonProperty("requestOriginIp", NullValueHandling = NullValueHandling.Ignore)]
        public string RequestOriginIp { get; set; }

        // =================================================================
        // Pipeline — Pasos del envio con estado individual
        // =================================================================

        /// <summary>
        /// Pasos del pipeline de envio con su estado individual.
        /// Null para registros que no capturan pipeline.
        /// </summary>
        [JsonProperty("pipelineSteps", NullValueHandling = NullValueHandling.Ignore)]
        public List<PipelineStep> PipelineSteps { get; set; }

        /// <summary>
        /// Crea una nueva instancia con MessageId autogenerado.
        /// </summary>
        public EmailLogEntry()
        {
            MessageId = "";
            RecipientAddresses = new List<string>();
            Priority = EmailPriority.Normal;
        }
    }

    #endregion

    #region PipelineTracker — Rastreador de pasos del envio

    /// <summary>
    /// Rastreador de pipeline que registra cada paso del envio de correo
    /// con su duracion y resultado. Cuando un paso falla, los siguientes
    /// se marcan automaticamente como Skipped.
    /// </summary>
    /// <example>
    /// <code>
    /// var tracker = new PipelineTracker();
    /// var error = "";
    ///
    /// tracker.Execute(1, "SUBJECT_AND_PROVIDER", "Asunto y Proveedor", () =>
    /// {
    ///     mailObj = mailSetter.SubjectAndProvider(..., ref error);
    ///     if (error != "Exito!") throw new Exception(error);
    /// });
    ///
    /// tracker.Execute(2, "MAIL_TEXT", "Texto del Correo", () =>
    /// {
    ///     mailObj = mailSetter.MailText(..., ref error);
    ///     if (error != "Exito!") throw new Exception(error);
    /// });
    ///
    /// // ... mas pasos ...
    ///
    /// // Al final, asignar al DTO:
    /// entry.PipelineSteps = tracker.ToSteps();
    /// entry.Status = tracker.HasFailed ? EmailStatus.Failed : EmailStatus.Sent;
    /// entry.StatusMessage = tracker.HasFailed ? tracker.FailureMessage : null;
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
        /// Accion a ejecutar. Debe lanzar Exception si falla.
        /// Patron: if (error != "Exito!") throw new Exception(error);
        /// </param>
        public void Execute(int order, string code, string name, Action action)
        {
            DateTimeOffset inicioUtc = DateTimeOffset.Now;

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
                    ExecutedAtUtc = DateTimeOffset.Now
                });
                return;
            }

            // Si aplica, delegar a Execute normal
            Execute(order, code, name, action);
        }

        /// <summary>
        /// Retorna la lista de pasos para asignar a EmailLogEntry.PipelineSteps.
        /// </summary>
        public List<PipelineStep> ToSteps()
        {
            return _steps;
        }
    }

    /// <summary>
    /// Excepcion especial para indicar Warning en un paso del pipeline.
    /// El paso se registra como Warning (amarillo en el dashboard) pero
    /// el pipeline NO se detiene — el envio continua.
    /// Uso: throw new PipelineWarningException("Variable {Monto} = 0.00");
    /// </summary>
    public class PipelineWarningException : Exception
    {
        public PipelineWarningException(string message) : base(message) { }
    }

    #endregion

    #region SoumaEmailLogWriter — Escritor de logs

    /// <summary>
    /// Escritor de logs de email compatible con el formato de Souma.EmailLogging.
    /// Thread-safe, resiliente, y autocontenido para .NET Framework 4.7.2.
    /// </summary>
    /// <remarks>
    /// ARQUITECTURA:
    ///   Tu servicio ASMX --> escribe JSON --> \\servidor\logs\email-logs\
    ///   Souma.Tool (dashboard) --> lee JSON <-- misma carpeta
    ///
    /// Los archivos generados son identicos a los de Souma.EmailLogging,
    /// por lo que el dashboard los lee sin ninguna modificacion.
    /// </remarks>
    public static class SoumaEmailLogWriter
    {
        // =====================================================================
        // CONFIGURACION — Ajustar estos valores segun tu entorno
        // =====================================================================

        /// <summary>
        /// Ruta del directorio de logs. Puede ser UNC (red) o local.
        /// DEBE ser la MISMA ruta que usa Souma.Tool en su appsettings.json.
        /// </summary>
        public static string LogDirectory { get; set; } = @"\\servidor\logs\email-logs\";

        /// <summary>
        /// Directorio alternativo si LogDirectory no esta disponible.
        /// </summary>
        public static string FallbackDirectory { get; set; } = @"C:\logs\email-fallback\";

        /// <summary>
        /// Tamano maximo por archivo en bytes. Default: 10 MB.
        /// Cuando se excede, se crea email-log-YYYY-MM-DD-part2.json, etc.
        /// </summary>
        public static long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;

        /// <summary>
        /// Nombre del microservicio que aparecera en SourceMicroservice.
        /// Configurar UNA VEZ en Application_Start o Global.asax.
        /// </summary>
        public static string ServiceName { get; set; } = "MiServicioASMX";

        /// <summary>
        /// Ambiente actual.
        /// </summary>
        public static DeploymentEnvironment CurrentEnvironment { get; set; } = DeploymentEnvironment.DEV;

        // =====================================================================
        // INTERNOS
        // =====================================================================

        private const string FilePrefix = "email-log-";
        private const string FileExtension = ".json";

        // Lock para escritura thread-safe (equivalente al SemaphoreSlim de .NET 10)
        private static readonly object _writeLock = new object();

        private static readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        // =====================================================================
        // METODO PRINCIPAL — Llamar despues de cada envio de email
        // =====================================================================

        /// <summary>
        /// Registra un envio de email en el archivo de log diario.
        /// NUNCA lanza excepciones — si falla, intenta el fallback y luego ignora silenciosamente.
        /// </summary>
        /// <param name="entry">DTO con los datos del envio.</param>
        public static void LogEmail(EmailLogEntry entry)
        {
            try
            {
                EscribirConReintento(entry, LogDirectory);
            }
            catch (Exception)
            {
                // Primer intento fallo — intentar fallback
                try
                {
                    if (!string.IsNullOrEmpty(FallbackDirectory))
                    {
                        EscribirInterno(entry, FallbackDirectory);
                    }
                }
                catch (Exception)
                {
                    // Fallo total — no hacer nada, NUNCA propagar excepcion
                    // El envio de email NO debe fallar por el logging
                    System.Diagnostics.Trace.TraceError(
                        "[SoumaEmailLog] Fallo critico al escribir log para MessageId={0}",
                        entry.MessageId);
                }
            }
        }

        /// <summary>
        /// Version asincrona (para usar con async/await si tu servicio lo soporta).
        /// Internamente usa ThreadPool para no bloquear el hilo actual.
        /// </summary>
        public static void LogEmailAsync(EmailLogEntry entry)
        {
            ThreadPool.QueueUserWorkItem(_ => LogEmail(entry));
        }

        // =====================================================================
        // METODOS DE CONVENIENCIA — Para simplificar la llamada desde ASMX
        // =====================================================================

        /// <summary>
        /// Registra un envio exitoso de email CON pipeline.
        /// </summary>
        /// <param name="messageId">ID del mensaje retornado por la API de envio.</param>
        /// <param name="senderAddress">Direccion del remitente.</param>
        /// <param name="recipientAddresses">Lista de destinatarios.</param>
        /// <param name="subject">Asunto del correo.</param>
        /// <param name="durationMs">Duracion total en milisegundos.</param>
        /// <param name="tracker">PipelineTracker con los pasos ejecutados (puede ser null).</param>
        /// <param name="correlationId">ID de correlacion para vincular reintentos.</param>
        /// <param name="ccAddresses">Direcciones en copia (CC).</param>
        /// <param name="hasAttachments">Indica si tiene adjuntos.</param>
        /// <param name="retryCount">Numero de reintentos realizados.</param>
        /// <param name="transactionType">Tipo de transaccion: "OTP", "AlertaFraude", etc.</param>
        /// <param name="priority">Nivel de prioridad del email.</param>
        /// <param name="queuedAtUtc">Momento en que se encolo la solicitud.</param>
        /// <param name="initiatedBy">ID del usuario/proceso que inicio el envio.</param>
        public static void LogSuccess(
            string messageId,
            string senderAddress,
            List<string> recipientAddresses,
            string subject,
            long durationMs,
            PipelineTracker tracker = null,
            string correlationId = null,
            List<string> ccAddresses = null,
            bool hasAttachments = false,
            int retryCount = 0,
            string transactionType = null,
            EmailPriority priority = EmailPriority.Normal,
            DateTimeOffset? queuedAtUtc = null,
            string initiatedBy = null)
        {
            LogEmail(new EmailLogEntry
            {
                MessageId = messageId ?? "",
                CorrelationId = correlationId,
                SourceMicroservice = ServiceName,
                SenderAddress = senderAddress,
                RecipientAddresses = recipientAddresses,
                CcAddresses = ccAddresses,
                Subject = subject,
                Status = EmailStatus.Sent,
                StatusMessage = null,
                SentAtUtc = DateTimeOffset.Now,
                DurationMs = durationMs,
                RetryCount = retryCount,
                HasAttachments = hasAttachments,
                Environment = CurrentEnvironment,
                // Campos extendidos
                HostName = System.Environment.MachineName,
                ContentType = EmailContentType.Html,
                TransactionType = transactionType,
                Priority = priority,
                QueuedAtUtc = queuedAtUtc,
                InitiatedBy = initiatedBy,
                // Pipeline
                PipelineSteps = tracker != null ? tracker.ToSteps() : null
            });
        }

        /// <summary>
        /// Registra un envio fallido de email CON pipeline.
        /// </summary>
        /// <param name="senderAddress">Direccion del remitente.</param>
        /// <param name="recipientAddresses">Lista de destinatarios.</param>
        /// <param name="subject">Asunto del correo.</param>
        /// <param name="durationMs">Duracion total en milisegundos.</param>
        /// <param name="errorMessage">Mensaje de error descriptivo.</param>
        /// <param name="tracker">PipelineTracker con los pasos ejecutados (puede ser null).</param>
        /// <param name="correlationId">ID de correlacion para vincular reintentos.</param>
        /// <param name="ccAddresses">Direcciones en copia (CC).</param>
        /// <param name="hasAttachments">Indica si tiene adjuntos.</param>
        /// <param name="retryCount">Numero de reintentos realizados.</param>
        /// <param name="transactionType">Tipo de transaccion: "OTP", "AlertaFraude", etc.</param>
        /// <param name="priority">Nivel de prioridad del email.</param>
        /// <param name="queuedAtUtc">Momento en que se encolo la solicitud.</param>
        /// <param name="initiatedBy">ID del usuario/proceso que inicio el envio.</param>
        public static void LogFailure(
            string senderAddress,
            List<string> recipientAddresses,
            string subject,
            long durationMs,
            string errorMessage,
            PipelineTracker tracker = null,
            string correlationId = null,
            List<string> ccAddresses = null,
            bool hasAttachments = false,
            int retryCount = 0,
            string transactionType = null,
            EmailPriority priority = EmailPriority.Normal,
            DateTimeOffset? queuedAtUtc = null,
            string initiatedBy = null)
        {
            LogEmail(new EmailLogEntry
            {
                CorrelationId = correlationId,
                SourceMicroservice = ServiceName,
                SenderAddress = senderAddress,
                RecipientAddresses = recipientAddresses,
                CcAddresses = ccAddresses,
                Subject = subject,
                Status = EmailStatus.Failed,
                StatusMessage = errorMessage,
                SentAtUtc = DateTimeOffset.Now,
                DurationMs = durationMs,
                RetryCount = retryCount,
                HasAttachments = hasAttachments,
                Environment = CurrentEnvironment,
                // Campos extendidos
                HostName = System.Environment.MachineName,
                ContentType = EmailContentType.Html,
                TransactionType = transactionType,
                Priority = priority,
                QueuedAtUtc = queuedAtUtc,
                InitiatedBy = initiatedBy,
                // Pipeline
                PipelineSteps = tracker != null ? tracker.ToSteps() : null
            });
        }

        // =====================================================================
        // PRIVADOS — Logica de escritura
        // =====================================================================

        private static void EscribirConReintento(EmailLogEntry entry, string directorio)
        {
            try
            {
                EscribirInterno(entry, directorio);
            }
            catch (Exception)
            {
                // Esperar 100ms y reintentar una vez
                Thread.Sleep(100);
                EscribirInterno(entry, directorio);
            }
        }

        private static void EscribirInterno(EmailLogEntry entry, string directorio)
        {
            lock (_writeLock)
            {
                // Asegurar que el directorio existe
                if (!Directory.Exists(directorio))
                {
                    Directory.CreateDirectory(directorio);
                }

                string archivoDestino = DeterminarArchivo(directorio);

                // Leer logs existentes o crear lista nueva
                List<EmailLogEntry> logs;
                if (File.Exists(archivoDestino))
                {
                    string jsonExistente = File.ReadAllText(archivoDestino);
                    logs = string.IsNullOrWhiteSpace(jsonExistente)
                        ? new List<EmailLogEntry>()
                        : JsonConvert.DeserializeObject<List<EmailLogEntry>>(jsonExistente, _jsonSettings)
                          ?? new List<EmailLogEntry>();
                }
                else
                {
                    logs = new List<EmailLogEntry>();
                }

                // Agregar nuevo log
                logs.Add(entry);

                // Escritura atomica: tmp -> move
                string json = JsonConvert.SerializeObject(logs, _jsonSettings);
                string tempPath = archivoDestino + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Copy(tempPath, archivoDestino, overwrite: true);
                File.Delete(tempPath);
            }
        }

        private static string DeterminarArchivo(string directorio)
        {
            string fechaStr = DateTime.UtcNow.ToString("yyyy-MM-dd");
            string archivoPrincipal = Path.Combine(directorio, FilePrefix + fechaStr + FileExtension);

            // Si no existe o esta dentro del limite, usar el principal
            if (!File.Exists(archivoPrincipal) || new FileInfo(archivoPrincipal).Length < MaxFileSizeBytes)
            {
                return archivoPrincipal;
            }

            // Buscar la siguiente parte disponible
            int parte = 2;
            while (true)
            {
                string archivoParte = Path.Combine(
                    directorio,
                    string.Format("{0}{1}-part{2}{3}", FilePrefix, fechaStr, parte, FileExtension));

                if (!File.Exists(archivoParte) || new FileInfo(archivoParte).Length < MaxFileSizeBytes)
                {
                    return archivoParte;
                }

                parte++;
            }
        }
    }

    #endregion
}
