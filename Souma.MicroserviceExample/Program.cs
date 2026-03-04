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
// Simula el envío de un email con reintentos de login (hasta 3 intentos).
// Cada intento fallido intermedio se registra como Retrying en el log.
// El resultado final (Sent o Failed) se registra al terminar el ciclo.
// ============================================================================
app.MapPost("/api/email/send", async (
    SendEmailRequest request,
    IEmailSender emailSender,
    IEmailLogCollector logCollector, // 2. Inyectar IEmailLogCollector
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    const int MaxReintentos = 3;

    EmailSendResult resultado = new() { Success = false, Message = "Sin intentos realizados" };
    int retryCount = 0;
    long lastDurationMs = 0;

    // Ciclo de reintentos de login: hasta MaxReintentos intentos
    for (int intento = 1; intento <= MaxReintentos; intento++)
    {
        Stopwatch cronometro = Stopwatch.StartNew();

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
        lastDurationMs = cronometro.ElapsedMilliseconds;

        // Si el envío fue exitoso, salir del ciclo de reintentos
        if (resultado.Success)
            break;

        // Envío fallido: si quedan más intentos, logar como Retrying y esperar
        bool hayMasIntentos = intento < MaxReintentos;
        if (hayMasIntentos)
        {
            retryCount = intento;

            // =================================================================
            // CLAVE: Logar el intento fallido como Retrying (NO como Failed),
            // ya que aún hay más intentos disponibles. El CorrelationId vincula
            // todos los intentos de este mismo correo en el dashboard.
            // =================================================================
            EmailLogDto logReintento = new()
            {
                CorrelationId = request.CorrelationId,
                SourceMicroservice = "Souma.MicroserviceExample",
                SenderAddress = request.SenderAddress,
                RecipientAddresses = request.RecipientAddresses,
                CcAddresses = request.CcAddresses,
                Subject = request.Subject,
                Status = EmailStatus.Retrying,
                StatusMessage = $"Reintentos de Login: {intento}/{MaxReintentos} - Detail Why: {resultado.Message}",
                SentAtUtc = DateTimeOffset.UtcNow,
                DurationMs = lastDurationMs,
                RetryCount = intento,
                HasAttachments = request.HasAttachments,
                Environment = DeploymentEnvironment.DEV
            };

            await logCollector.LogEmailAsync(logReintento, cancellationToken);

            logger.LogWarning(
                "Reintento {Intento}/{Max} hacia {Destinatarios} — CorrelationId: {CorrelationId}",
                intento, MaxReintentos,
                string.Join(", ", request.RecipientAddresses),
                request.CorrelationId);

            // Espera breve antes del siguiente intento (backoff simple)
            await Task.Delay(500, cancellationToken);
        }
    }

    // ==========================================================================
    // 3. Logar el resultado final del ciclo de reintentos:
    //    - Si algún intento fue exitoso → Status = Sent
    //    - Si todos los intentos fallaron → Status = Failed
    //    RetryCount indica cuántos reintentos intermedios hubo antes del resultado final.
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
        DurationMs = lastDurationMs,
        RetryCount = retryCount,
        HasAttachments = request.HasAttachments,
        Environment = DeploymentEnvironment.DEV
    };

    // Llamar al logger de trazabilidad — NUNCA falla ni lanza excepción al caller
    await logCollector.LogEmailAsync(logEntry, cancellationToken);

    logger.LogInformation(
        "Email {Estado} hacia {Destinatarios} en {DuracionMs}ms — MessageId: {MessageId}",
        logEntry.Status,
        string.Join(", ", request.RecipientAddresses),
        lastDurationMs,
        logEntry.MessageId);

    // Retornar respuesta tipada
    SendEmailResponse response = new()
    {
        Success = resultado.Success,
        MessageId = logEntry.MessageId,
        Message = resultado.Message,
        DurationMs = lastDurationMs
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
