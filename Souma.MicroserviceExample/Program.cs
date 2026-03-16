using System.Diagnostics;
using Souma.EmailLogging.Abstractions;
using Souma.EmailLogging.Extensions;
using Souma.EmailLogging.Models;
using Souma.MicroserviceExample.Contracts;
using Souma.MicroserviceExample.Services;

var builder = WebApplication.CreateBuilder(args);

// ============================================================================
// INTEGRACIÓN CON SOUMA.EMAILLOGGING
// Estas son las únicas líneas que necesitas agregar a tu microservicio existente
// para habilitar trazabilidad de emails.
// ============================================================================

// 1. Registrar el servicio de logging de emails vía DI
builder.Services.AddEmailLogging(options =>
{
    options.LogDirectory = builder.Configuration["EmailLogging:LogDirectory"]
        ?? Path.Combine(AppContext.BaseDirectory, "email-logs");
    options.FallbackDirectory = builder.Configuration["EmailLogging:FallbackDirectory"];
    options.MaxFileSizeMb = builder.Configuration.GetValue("EmailLogging:MaxFileSizeMb", 10);
    options.RetentionDays = builder.Configuration.GetValue("EmailLogging:RetentionDays", 30);
});

// Registro del sender fake (en tu microservicio real, aquí va tu implementación de IEmailSender)
builder.Services.AddSingleton<IEmailSender, FakeEmailSender>();

var app = builder.Build();

// ============================================================================
// ENDPOINT: POST /api/email/send
// Simula el envío de un email y registra el resultado en el log de trazabilidad.
// ============================================================================
app.MapPost("/api/email/send", async (
    SendEmailRequest request,
    IEmailSender emailSender,
    IEmailLogCollector logCollector, // 2. Inyectar IEmailLogCollector
    ILogger<Program> logger,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    // Capturar timestamp de recepción para calcular tiempo en cola
    DateTimeOffset queuedAtUtc = DateTimeOffset.UtcNow;

    // Medir duración de la operación de envío con Stopwatch
    Stopwatch cronometro = Stopwatch.StartNew();
    EmailSendResult resultado;

    try
    {
        resultado = await emailSender.SendAsync(request, cancellationToken);
    }
    catch (Exception ex)
    {
        // Si el sender lanza excepción, capturar como resultado fallido
        resultado = new EmailSendResult
        {
            Success = false,
            Message = $"{ex.GetType().Name}: {ex.Message}"
        };
    }

    cronometro.Stop();

    // Parsear enums del request (vienen como string para compatibilidad con ASMX .NET 4.7.2)
    EmailPriority prioridad = Enum.TryParse<EmailPriority>(request.Priority, true, out EmailPriority p)
        ? p
        : EmailPriority.Normal;

    EmailContentType? tipoContenido = Enum.TryParse<EmailContentType>(request.ContentType, true, out EmailContentType ct)
        ? ct
        : null;

    // Capturar IP del servicio solicitante
    string? requestOriginIp = httpContext.Connection.RemoteIpAddress?.ToString();

    // ==========================================================================
    // 3. Crear el DTO de log con TODOS los campos de trazabilidad y llamar a
    //    LogEmailAsync — esto es lo único que necesitas agregar a tu flujo
    //    existente de envío de emails.
    // ==========================================================================
    EmailLogDto logEntry = new()
    {
        CorrelationId = request.CorrelationId,
        SourceMicroservice = "Souma.MicroserviceExample",
        SenderAddress = request.SenderAddress,
        RecipientAddresses = request.RecipientAddresses,
        CcAddresses = request.CcAddresses,
        Subject = request.Subject,
        Status = resultado.Success ? EmailStatus.Sent : EmailStatus.Failed,
        StatusMessage = resultado.Success ? null : resultado.Message,
        SentAtUtc = DateTimeOffset.UtcNow,
        DurationMs = cronometro.ElapsedMilliseconds,
        HasAttachments = request.HasAttachments || request.AttachmentCount > 0,
        Environment = DeploymentEnvironment.DEV,
        // --- Campos extendidos de trazabilidad ---
        HostName = System.Environment.MachineName,
        SmtpStatusCode = resultado.SmtpStatusCode,
        EmailSizeBytes = request.EmailSizeBytes,
        AttachmentCount = request.AttachmentCount,
        ContentType = tipoContenido,
        TransactionType = request.TransactionType,
        Priority = prioridad,
        QueuedAtUtc = queuedAtUtc,
        InitiatedBy = request.InitiatedBy,
        RequestOriginIp = requestOriginIp,
        PipelineSteps = resultado.PipelineSteps
    };

    // Llamar al logger de trazabilidad — NUNCA falla ni lanza excepción al caller
    await logCollector.LogEmailAsync(logEntry, cancellationToken);

    // Identificar paso fallido del pipeline para el log estructurado
    string pipelineResult = resultado.PipelineSteps?
        .FirstOrDefault(s => s.StepStatus == PipelineStepStatus.Failed)?.StepName ?? "OK";

    logger.LogInformation(
        "Email {Estado} hacia {Destinatarios} en {DuracionMs}ms — MessageId: {MessageId} Pipeline: {PipelineResult}",
        logEntry.Status,
        string.Join(", ", request.RecipientAddresses),
        cronometro.ElapsedMilliseconds,
        logEntry.MessageId,
        pipelineResult);

    // Retornar respuesta tipada
    SendEmailResponse response = new()
    {
        Success = resultado.Success,
        MessageId = logEntry.MessageId,
        Message = resultado.Message,
        DurationMs = cronometro.ElapsedMilliseconds
    };

    return resultado.Success
        ? Results.Ok(response)
        : Results.UnprocessableEntity(response);
});

// Health check simple
app.MapGet("/health/live", () => Results.Ok(new { status = "alive", timestamp = DateTimeOffset.UtcNow }));

// Endpoint para consultar logs (útil para verificación rápida)
app.MapGet("/api/email/logs/{fecha}", async (
    string fecha,
    IEmailLogCollector logCollector,
    CancellationToken cancellationToken) =>
{
    if (!DateOnly.TryParseExact(fecha, "yyyy-MM-dd", out DateOnly fechaParsed))
    {
        return Results.BadRequest(new { error = "Formato de fecha inválido. Use yyyy-MM-dd." });
    }

    List<EmailLogDto> logs = await logCollector.GetLogsByDateAsync(fechaParsed, cancellationToken);
    return Results.Ok(logs);
});

// Endpoint para resumen diario
app.MapGet("/api/email/summary/{fecha}", async (
    string fecha,
    IEmailLogCollector logCollector,
    CancellationToken cancellationToken) =>
{
    if (!DateOnly.TryParseExact(fecha, "yyyy-MM-dd", out DateOnly fechaParsed))
    {
        return Results.BadRequest(new { error = "Formato de fecha inválido. Use yyyy-MM-dd." });
    }

    EmailLogSummary summary = await logCollector.GetDailySummaryAsync(fechaParsed, cancellationToken);
    return Results.Ok(summary);
});

app.Run();

// Necesario para que los tests de integración puedan acceder a Program
public partial class Program;
