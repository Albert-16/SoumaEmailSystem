using Souma.EmailLogging.Models;
using Souma.MicroserviceExample.Contracts;

namespace Souma.MicroserviceExample.Services;

/// <summary>
/// Implementación simulada de <see cref="IEmailSender"/> para pruebas.
/// Simula latencia de red, pipeline de 6 pasos y falla aleatoriamente (~20% de las veces)
/// en diferentes pasos del pipeline para probar la trazabilidad completa.
/// </summary>
/// <remarks>
/// Pipeline real del servicio ASMX .NET 4.7.2:
/// SubjectAndProvider → MailText → MailVars → MailImages → MailTo → MailAttachments
/// MailImages y MailAttachments son opcionales — si no aplican, continúan sin error.
/// </remarks>
internal sealed class FakeEmailSender : IEmailSender
{
    private static readonly Random _random = new();

    /// <inheritdoc/>
    public async Task<EmailSendResult> SendAsync(SendEmailRequest request, CancellationToken cancellationToken)
    {
        List<PipelineStep> pasos = [];
        bool pipelineFallo = false;
        string mensajeFallo = "";
        int? smtpCode = null;

        // === Paso 1: SubjectAndProvider — Validar asunto y proveedor ===
        PipelineStep paso1 = await SimularPaso(
            1, "SUBJECT_AND_PROVIDER", "Asunto y Proveedor",
            pipelineFallo, cancellationToken,
            () =>
            {
                // Simular fallo ~5%: asunto vacio o proveedor no permitido
                int resultado = _random.Next(1, 21);
                if (resultado == 1)
                {
                    return (PipelineStepStatus.Failed,
                        "Campo 'Subject' es requerido y no puede estar vacio");
                }
                if (resultado == 2)
                {
                    string destinatario = request.RecipientAddresses.FirstOrDefault() ?? "desconocido";
                    return (PipelineStepStatus.Failed,
                        $"Proveedor @{destinatario.Split('@').LastOrDefault()} no permitido por politica de seguridad");
                }
                return (PipelineStepStatus.OK, null);
            });
        pasos.Add(paso1);
        if (paso1.StepStatus == PipelineStepStatus.Failed)
        {
            pipelineFallo = true;
            mensajeFallo = paso1.Message!;
        }

        // === Paso 2: MailText — Cargar texto/plantilla del correo ===
        PipelineStep paso2 = await SimularPaso(
            2, "MAIL_TEXT", "Texto del Correo",
            pipelineFallo, cancellationToken,
            () =>
            {
                // Simular fallo ~5%: plantilla no encontrada o inactiva
                int resultado = _random.Next(1, 21);
                if (resultado == 1)
                    return (PipelineStepStatus.Failed, "Plantilla 'AlertaFraude_v2' no encontrada en el gestor");
                if (resultado == 2)
                    return (PipelineStepStatus.Failed, "Plantilla 'NotificacionTransferencia' esta marcada como inactiva");
                return (PipelineStepStatus.OK, null);
            });
        pasos.Add(paso2);
        if (!pipelineFallo && paso2.StepStatus == PipelineStepStatus.Failed)
        {
            pipelineFallo = true;
            mensajeFallo = paso2.Message!;
        }

        // === Paso 3: MailVars — Procesar variables de plantilla ===
        PipelineStep paso3 = await SimularPaso(
            3, "MAIL_VARS", "Variables de Plantilla",
            pipelineFallo, cancellationToken,
            () =>
            {
                // Simular fallo ~3%: variable sin valor
                if (_random.Next(1, 34) == 1)
                    return (PipelineStepStatus.Failed, "Variable {NombreCliente} no tiene valor asignado");
                // Simular warning ~5%: variable con valor sospechoso
                if (_random.Next(1, 21) == 1)
                    return (PipelineStepStatus.Warning, "Variable {Monto} tiene valor 0.00 — verificar");
                return (PipelineStepStatus.OK, null);
            });
        pasos.Add(paso3);
        if (!pipelineFallo && paso3.StepStatus == PipelineStepStatus.Failed)
        {
            pipelineFallo = true;
            mensajeFallo = paso3.Message!;
        }

        // === Paso 4: MailImages — Cargar imagenes inline (OPCIONAL) ===
        // Si no hay imagenes, el paso es OK y continua — no todos los correos tienen imagenes
        PipelineStep paso4 = await SimularPaso(
            4, "MAIL_IMAGES", "Imagenes del Correo",
            pipelineFallo, cancellationToken,
            () =>
            {
                // ~60% de los correos no tienen imagenes — paso OK sin accion
                if (_random.Next(1, 11) > 4)
                    return (PipelineStepStatus.OK, "Sin imagenes — paso completado sin accion");

                // Los que si tienen imagenes: ~10% fallan
                if (_random.Next(1, 11) == 1)
                    return (PipelineStepStatus.Failed, "Imagen 'banner_header.png' no encontrada en el repositorio de imagenes");

                return (PipelineStepStatus.OK, "2 imagenes inline cargadas correctamente");
            });
        pasos.Add(paso4);
        if (!pipelineFallo && paso4.StepStatus == PipelineStepStatus.Failed)
        {
            pipelineFallo = true;
            mensajeFallo = paso4.Message!;
        }

        // === Paso 5: MailTo — Validar y establecer destinatarios ===
        PipelineStep paso5 = await SimularPaso(
            5, "MAIL_TO", "Destinatarios",
            pipelineFallo, cancellationToken,
            () =>
            {
                // Simular fallo ~3%: destinatario invalido
                int resultado = _random.Next(1, 34);
                if (resultado == 1)
                    return (PipelineStepStatus.Failed, "Direccion 'usuario@dominio-invalido' no es una direccion de correo valida");
                if (resultado == 2)
                    return (PipelineStepStatus.Failed, "Lista de destinatarios esta vacia — se requiere al menos un destinatario");
                return (PipelineStepStatus.OK, $"{request.RecipientAddresses.Count} destinatario(s) configurado(s)");
            });
        pasos.Add(paso5);
        if (!pipelineFallo && paso5.StepStatus == PipelineStepStatus.Failed)
        {
            pipelineFallo = true;
            mensajeFallo = paso5.Message!;
        }

        // === Paso 6: MailAttachments — Preparar adjuntos (OPCIONAL) ===
        // Si no hay adjuntos, el paso es OK y continua
        PipelineStep paso6 = await SimularPaso(
            6, "MAIL_ATTACHMENTS", "Adjuntos",
            pipelineFallo, cancellationToken,
            () =>
            {
                if (!request.HasAttachments && request.AttachmentCount == 0)
                    return (PipelineStepStatus.OK, "Sin adjuntos — paso completado sin accion");

                // Simular fallo ~5%: adjunto invalido
                int resultado = _random.Next(1, 21);
                if (resultado == 1)
                    return (PipelineStepStatus.Failed, "Archivo adjunto excede el limite de 10MB");
                if (resultado == 2)
                    return (PipelineStepStatus.Failed, "Formato .exe no permitido como adjunto");

                smtpCode = 250;
                return (PipelineStepStatus.OK, $"{request.AttachmentCount} adjunto(s) preparado(s)");
            });
        pasos.Add(paso6);
        if (!pipelineFallo && paso6.StepStatus == PipelineStepStatus.Failed)
        {
            pipelineFallo = true;
            mensajeFallo = paso6.Message!;
        }

        if (pipelineFallo)
        {
            return new EmailSendResult
            {
                Success = false,
                MessageId = "",
                Message = mensajeFallo,
                SmtpStatusCode = smtpCode,
                PipelineSteps = pasos
            };
        }

        smtpCode = 250;
        return new EmailSendResult
        {
            Success = true,
            MessageId = $"MSG-{Guid.NewGuid():N}",
            Message = $"Email enviado exitosamente a {string.Join(", ", request.RecipientAddresses)}",
            SmtpStatusCode = 250,
            PipelineSteps = pasos
        };
    }

    /// <summary>
    /// Simula la ejecución de un paso del pipeline con latencia aleatoria.
    /// Si el pipeline ya falló en un paso anterior, retorna Skipped.
    /// </summary>
    private static async Task<PipelineStep> SimularPaso(
        int orden,
        string codigo,
        string nombre,
        bool pipelineYaFallo,
        CancellationToken cancellationToken,
        Func<(PipelineStepStatus status, string? mensaje)> ejecutar)
    {
        DateTimeOffset inicioUtc = DateTimeOffset.UtcNow;

        if (pipelineYaFallo)
        {
            return new PipelineStep
            {
                StepOrder = orden,
                StepCode = codigo,
                StepName = nombre,
                StepStatus = PipelineStepStatus.Skipped,
                Message = "Paso omitido — un paso anterior fallo",
                DurationMs = 0,
                ExecutedAtUtc = inicioUtc
            };
        }

        // Simular latencia del paso (10ms a 100ms)
        int latenciaMs = _random.Next(10, 100);
        await Task.Delay(latenciaMs, cancellationToken);

        (PipelineStepStatus status, string? mensaje) = ejecutar();

        return new PipelineStep
        {
            StepOrder = orden,
            StepCode = codigo,
            StepName = nombre,
            StepStatus = status,
            Message = mensaje,
            DurationMs = latenciaMs,
            ExecutedAtUtc = inicioUtc
        };
    }
}
