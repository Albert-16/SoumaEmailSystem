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
//
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Souma.EmailLogging.Legacy
{
    #region DTOs — Deben coincidir exactamente con el schema de Souma.EmailLogging

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
    /// DTO de log de email. Los nombres de propiedades JSON DEBEN coincidir
    /// exactamente con los que produce Souma.EmailLogging (.NET 10) en camelCase.
    /// </summary>
    public class EmailLogEntry
    {
        [JsonProperty("messageId")]
        public Guid MessageId { get; set; }

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

        /// <summary>
        /// Crea una nueva instancia con MessageId autogenerado.
        /// </summary>
        public EmailLogEntry()
        {
            MessageId = Guid.NewGuid();
            RecipientAddresses = new List<string>();
        }
    }

    #endregion

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
        /// Registra un envio exitoso de email.
        /// </summary>
        public static void LogSuccess(
            string senderAddress,
            List<string> recipientAddresses,
            string subject,
            long durationMs,
            string correlationId = null,
            List<string> ccAddresses = null,
            bool hasAttachments = false,
            int retryCount = 0)
        {
            LogEmail(new EmailLogEntry
            {
                CorrelationId = correlationId,
                SourceMicroservice = ServiceName,
                SenderAddress = senderAddress,
                RecipientAddresses = recipientAddresses,
                CcAddresses = ccAddresses,
                Subject = subject,
                Status = EmailStatus.Sent,
                StatusMessage = null,
                SentAtUtc = DateTimeOffset.UtcNow,
                DurationMs = durationMs,
                RetryCount = retryCount,
                HasAttachments = hasAttachments,
                Environment = CurrentEnvironment
            });
        }

        /// <summary>
        /// Registra un envio fallido de email.
        /// </summary>
        public static void LogFailure(
            string senderAddress,
            List<string> recipientAddresses,
            string subject,
            long durationMs,
            string errorMessage,
            string correlationId = null,
            List<string> ccAddresses = null,
            bool hasAttachments = false,
            int retryCount = 0)
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
                SentAtUtc = DateTimeOffset.UtcNow,
                DurationMs = durationMs,
                RetryCount = retryCount,
                HasAttachments = hasAttachments,
                Environment = CurrentEnvironment
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
}
