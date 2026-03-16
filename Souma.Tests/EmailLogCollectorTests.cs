using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Souma.EmailLogging.Abstractions;
using Souma.EmailLogging.Models;
using Souma.EmailLogging.Options;
using Souma.EmailLogging.Services;

namespace Souma.Tests;

/// <summary>
/// Tests unitarios para EmailLogCollector.
/// Se usa Mock de IFileSystem para evitar operaciones reales de I/O.
/// </summary>
public sealed class EmailLogCollectorTests : IDisposable
{
    private readonly Mock<IFileSystem> _mockFileSystem;
    private readonly ILogger<EmailLogCollector> _logger;
    private readonly EmailLoggingOptions _options;
    private readonly EmailLogCollector _collector;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public EmailLogCollectorTests()
    {
        _mockFileSystem = new Mock<IFileSystem>();
        _logger = new LoggerFactory().CreateLogger<EmailLogCollector>();
        _options = new EmailLoggingOptions
        {
            LogDirectory = @"C:\logs\email-logs",
            MaxFileSizeMb = 10,
            RetentionDays = 30,
            FallbackDirectory = @"C:\logs\fallback"
        };

        // Simular que el directorio ya existe
        _mockFileSystem.Setup(fs => fs.DirectoryExists(It.IsAny<string>())).Returns(true);

        IOptions<EmailLoggingOptions> optionsWrapper = Options.Create(_options);
        _collector = new EmailLogCollector(_mockFileSystem.Object, _logger, optionsWrapper);
    }

    public void Dispose()
    {
        _collector.Dispose();
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static EmailLogDto CrearLogDePrueba(
        EmailStatus status = EmailStatus.Sent,
        string microservice = "TestService") => new()
    {
        SourceMicroservice = microservice,
        SenderAddress = "sender@davivienda.hn",
        RecipientAddresses = ["recipient@davivienda.hn"],
        Subject = "Test Email",
        Status = status,
        SentAtUtc = DateTimeOffset.UtcNow,
        DurationMs = 150,
        Environment = DeploymentEnvironment.DEV,
        Priority = EmailPriority.Normal,
        ContentType = EmailContentType.Html,
        TransactionType = "OTP",
        HostName = "SRV-EMAIL-01",
        AttachmentCount = 0,
        InitiatedBy = "TestRunner",
        SmtpStatusCode = status == EmailStatus.Sent ? 250 : 503,
        QueuedAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(-50),
        PipelineSteps = CrearPipelineSteps(status)
    };

    private static List<PipelineStep> CrearPipelineSteps(EmailStatus status)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (status == EmailStatus.Sent)
        {
            return
            [
                new() { StepOrder = 1, StepCode = "VALIDATE_RECIPIENTS", StepName = "Validar Destinatarios", StepStatus = PipelineStepStatus.OK, DurationMs = 5, ExecutedAtUtc = now },
                new() { StepOrder = 2, StepCode = "LOAD_TEMPLATE", StepName = "Cargar Plantilla", StepStatus = PipelineStepStatus.OK, DurationMs = 10, ExecutedAtUtc = now },
                new() { StepOrder = 3, StepCode = "PROCESS_VARIABLES", StepName = "Procesar Variables", StepStatus = PipelineStepStatus.OK, DurationMs = 8, ExecutedAtUtc = now },
                new() { StepOrder = 4, StepCode = "PREPARE_ATTACHMENTS", StepName = "Preparar Adjuntos", StepStatus = PipelineStepStatus.OK, DurationMs = 3, ExecutedAtUtc = now },
                new() { StepOrder = 5, StepCode = "SMTP_SEND", StepName = "Enviar por SMTP", StepStatus = PipelineStepStatus.OK, DurationMs = 120, ExecutedAtUtc = now }
            ];
        }

        return
        [
            new() { StepOrder = 1, StepCode = "VALIDATE_RECIPIENTS", StepName = "Validar Destinatarios", StepStatus = PipelineStepStatus.OK, DurationMs = 5, ExecutedAtUtc = now },
            new() { StepOrder = 2, StepCode = "LOAD_TEMPLATE", StepName = "Cargar Plantilla", StepStatus = PipelineStepStatus.Failed, Message = "Plantilla 'TestTemplate' no encontrada", DurationMs = 12, ExecutedAtUtc = now },
            new() { StepOrder = 3, StepCode = "PROCESS_VARIABLES", StepName = "Procesar Variables", StepStatus = PipelineStepStatus.Skipped, DurationMs = 0, ExecutedAtUtc = now },
            new() { StepOrder = 4, StepCode = "PREPARE_ATTACHMENTS", StepName = "Preparar Adjuntos", StepStatus = PipelineStepStatus.Skipped, DurationMs = 0, ExecutedAtUtc = now },
            new() { StepOrder = 5, StepCode = "SMTP_SEND", StepName = "Enviar por SMTP", StepStatus = PipelineStepStatus.Skipped, DurationMs = 0, ExecutedAtUtc = now }
        ];
    }

    private void ConfigurarArchivoExistente(string contenidoJson)
    {
        _mockFileSystem.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns(true);
        _mockFileSystem.Setup(fs => fs.GetFileSize(It.IsAny<string>())).Returns(100L);
        _mockFileSystem.Setup(fs => fs.ReadAllTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(contenidoJson);
    }

    private void ConfigurarArchivoNuevo()
    {
        _mockFileSystem.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns(false);
    }

    // =========================================================================
    // Test 1: Happy path — escribir un log exitosamente a archivo nuevo
    // =========================================================================
    [Fact]
    public async Task LogEmailAsync_ArchivoNuevo_CreaArchivoConUnRegistro()
    {
        // Arrange
        ConfigurarArchivoNuevo();
        EmailLogDto log = CrearLogDePrueba();
        string? contenidoEscrito = null;

        _mockFileSystem.Setup(fs => fs.WriteAllTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, content, _) => contenidoEscrito = content)
            .Returns(Task.CompletedTask);

        // Act
        await _collector.LogEmailAsync(log);

        // Assert
        Assert.NotNull(contenidoEscrito);
        List<EmailLogDto>? logsEscritos = JsonSerializer.Deserialize<List<EmailLogDto>>(contenidoEscrito, _jsonOptions);
        Assert.NotNull(logsEscritos);
        Assert.Single(logsEscritos);
        Assert.Equal(log.MessageId, logsEscritos[0].MessageId);
    }

    // =========================================================================
    // Test 2: Agregar a archivo existente
    // =========================================================================
    [Fact]
    public async Task LogEmailAsync_ArchivoExistente_AgregaRegistroAlArray()
    {
        // Arrange
        EmailLogDto logExistente = CrearLogDePrueba();
        string jsonExistente = JsonSerializer.Serialize(new List<EmailLogDto> { logExistente }, _jsonOptions);
        ConfigurarArchivoExistente(jsonExistente);

        EmailLogDto nuevoLog = CrearLogDePrueba(EmailStatus.Failed);
        string? contenidoEscrito = null;

        _mockFileSystem.Setup(fs => fs.WriteAllTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, content, _) => contenidoEscrito = content)
            .Returns(Task.CompletedTask);

        // Act
        await _collector.LogEmailAsync(nuevoLog);

        // Assert
        Assert.NotNull(contenidoEscrito);
        List<EmailLogDto>? logsEscritos = JsonSerializer.Deserialize<List<EmailLogDto>>(contenidoEscrito, _jsonOptions);
        Assert.NotNull(logsEscritos);
        Assert.Equal(2, logsEscritos.Count);
    }

    // =========================================================================
    // Test 3: Fallback — si directorio principal falla, escribe al fallback
    // =========================================================================
    [Fact]
    public async Task LogEmailAsync_FalloPrimario_EscribeAlFallback()
    {
        // Arrange
        ConfigurarArchivoNuevo();
        EmailLogDto log = CrearLogDePrueba();

        // Simular que la primera escritura SIEMPRE falla (directorio primario)
        int intentosEscritura = 0;
        _mockFileSystem.Setup(fs => fs.WriteAllTextAsync(
            It.Is<string>(p => p.StartsWith(_options.LogDirectory)),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .Callback(() => intentosEscritura++)
            .ThrowsAsync(new IOException("Red no disponible"));

        // Permitir escritura al fallback
        string? contenidoFallback = null;
        _mockFileSystem.Setup(fs => fs.WriteAllTextAsync(
            It.Is<string>(p => p.StartsWith(_options.FallbackDirectory!)),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, content, _) => contenidoFallback = content)
            .Returns(Task.CompletedTask);

        // Act
        await _collector.LogEmailAsync(log);

        // Assert — debe haber intentado 2 veces en primario (1 + 1 retry), luego fallback
        Assert.Equal(2, intentosEscritura);
        Assert.NotNull(contenidoFallback);
    }

    // =========================================================================
    // Test 4: Nunca lanza excepción — incluso si todo falla
    // =========================================================================
    [Fact]
    public async Task LogEmailAsync_TodoFalla_NuncaLanzaExcepcion()
    {
        // Arrange
        ConfigurarArchivoNuevo();
        EmailLogDto log = CrearLogDePrueba();

        _mockFileSystem.Setup(fs => fs.WriteAllTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Fallo total"));

        // Act & Assert — no debe lanzar excepción
        Exception? exception = await Record.ExceptionAsync(() => _collector.LogEmailAsync(log));
        Assert.Null(exception);
    }

    // =========================================================================
    // Test 5: GetLogsByDateAsync — retorna logs del día especificado
    // =========================================================================
    [Fact]
    public async Task GetLogsByDateAsync_ConDatos_RetornaLogsFiltrados()
    {
        // Arrange
        DateOnly hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        List<EmailLogDto> logsDelDia = [CrearLogDePrueba(), CrearLogDePrueba(EmailStatus.Failed)];
        string json = JsonSerializer.Serialize(logsDelDia, _jsonOptions);

        _mockFileSystem.Setup(fs => fs.GetFiles(It.IsAny<string>(), It.IsAny<string>()))
            .Returns([$@"C:\logs\email-logs\email-log-{hoy:yyyy-MM-dd}.json"]);
        _mockFileSystem.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        // Act
        List<EmailLogDto> resultado = await _collector.GetLogsByDateAsync(hoy, CancellationToken.None);

        // Assert
        Assert.Equal(2, resultado.Count);
    }

    // =========================================================================
    // Test 6: Archivo vacío — retorna lista vacía sin error
    // =========================================================================
    [Fact]
    public async Task GetLogsByDateAsync_ArchivoVacio_RetornaListaVacia()
    {
        // Arrange
        DateOnly hoy = DateOnly.FromDateTime(DateTime.UtcNow);

        _mockFileSystem.Setup(fs => fs.GetFiles(It.IsAny<string>(), It.IsAny<string>()))
            .Returns([@"C:\logs\email-log-2026-03-01.json"]);
        _mockFileSystem.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        // Act
        List<EmailLogDto> resultado = await _collector.GetLogsByDateAsync(hoy, CancellationToken.None);

        // Assert
        Assert.Empty(resultado);
    }

    // =========================================================================
    // Test 7: JSON malformado — retorna lista vacía sin lanzar excepción
    // =========================================================================
    [Fact]
    public async Task GetLogsByDateAsync_JsonMalformado_RetornaListaVaciaSinError()
    {
        // Arrange
        DateOnly hoy = DateOnly.FromDateTime(DateTime.UtcNow);

        _mockFileSystem.Setup(fs => fs.GetFiles(It.IsAny<string>(), It.IsAny<string>()))
            .Returns([@"C:\logs\email-log-2026-03-01.json"]);
        _mockFileSystem.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{ json roto sin sentido }}}");

        // Act
        List<EmailLogDto> resultado = await _collector.GetLogsByDateAsync(hoy, CancellationToken.None);

        // Assert
        Assert.Empty(resultado);
    }

    // =========================================================================
    // Test 8: GetDailySummaryAsync — calcula resumen correctamente
    // =========================================================================
    [Fact]
    public async Task GetDailySummaryAsync_CalculaMetricasCorrectamente()
    {
        // Arrange
        DateOnly hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        List<EmailLogDto> logs =
        [
            CrearLogDePrueba(EmailStatus.Sent) with { DurationMs = 100, RecipientAddresses = ["a@test.com"] },
            CrearLogDePrueba(EmailStatus.Sent) with { DurationMs = 200, RecipientAddresses = ["a@test.com"] },
            CrearLogDePrueba(EmailStatus.Failed) with { DurationMs = 300, RecipientAddresses = ["b@test.com"] },
            CrearLogDePrueba(EmailStatus.Pending) with { DurationMs = 400, RecipientAddresses = ["c@test.com"] }
        ];
        string json = JsonSerializer.Serialize(logs, _jsonOptions);

        _mockFileSystem.Setup(fs => fs.GetFiles(It.IsAny<string>(), It.IsAny<string>()))
            .Returns([$@"C:\logs\email-log-{hoy:yyyy-MM-dd}.json"]);
        _mockFileSystem.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        // Act
        EmailLogSummary summary = await _collector.GetDailySummaryAsync(hoy, CancellationToken.None);

        // Assert
        Assert.Equal(4, summary.TotalCount);
        Assert.Equal(2, summary.TotalSent);
        Assert.Equal(1, summary.TotalFailed);
        Assert.Equal(1, summary.TotalPending);
        Assert.Equal(250, summary.AverageDurationMs); // (100+200+300+400)/4
        Assert.Equal(25.0, summary.FailureRate); // 1/4 * 100
        Assert.Equal("a@test.com", summary.TopRecipients[0]); // 2 veces
    }

    // =========================================================================
    // Test 9: GetLogsByMicroserviceAsync — filtra por nombre correctamente
    // =========================================================================
    [Fact]
    public async Task GetLogsByMicroserviceAsync_FiltraCorrectamente()
    {
        // Arrange
        DateOnly hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        List<EmailLogDto> logs =
        [
            CrearLogDePrueba(microservice: "SoumaIntegration"),
            CrearLogDePrueba(microservice: "BankXmlMapper"),
            CrearLogDePrueba(microservice: "SoumaIntegration")
        ];
        string json = JsonSerializer.Serialize(logs, _jsonOptions);

        _mockFileSystem.Setup(fs => fs.GetFiles(It.IsAny<string>(), It.IsAny<string>()))
            .Returns([$@"C:\logs\email-log-{hoy:yyyy-MM-dd}.json"]);
        _mockFileSystem.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        // Act
        List<EmailLogDto> resultado = await _collector.GetLogsByMicroserviceAsync(
            "SoumaIntegration", hoy, CancellationToken.None);

        // Assert
        Assert.Equal(2, resultado.Count);
        Assert.All(resultado, l => Assert.Equal("SoumaIntegration", l.SourceMicroservice));
    }

    // =========================================================================
    // Test 10: Escritura concurrente — SemaphoreSlim protege contra race conditions
    // =========================================================================
    [Fact]
    public async Task LogEmailAsync_EscriturasConcurrentes_SinCorrupcion()
    {
        // Arrange
        ConfigurarArchivoNuevo();
        int escriturasCompletadas = 0;

        _mockFileSystem.Setup(fs => fs.WriteAllTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => Interlocked.Increment(ref escriturasCompletadas))
            .Returns(Task.CompletedTask);

        // Después de la primera escritura, el archivo ya "existe" con datos
        _mockFileSystem.Setup(fs => fs.ReadAllTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                // Simular que siempre se lee un array vacío (simplificación para el test)
                return "[]";
            });

        // Act — 10 escrituras concurrentes
        Task[] tareas = Enumerable.Range(0, 10)
            .Select(_ => _collector.LogEmailAsync(CrearLogDePrueba()))
            .ToArray();

        await Task.WhenAll(tareas);

        // Assert — todas las escrituras deben haberse completado
        Assert.Equal(10, escriturasCompletadas);
    }

    // =========================================================================
    // Test 11: ArchiveOldLogsAsync — elimina archivos antiguos
    // =========================================================================
    [Fact]
    public async Task ArchiveOldLogsAsync_EliminaArchivosAntiguos()
    {
        // Arrange
        DateOnly hace60Dias = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-60));
        DateOnly hoy = DateOnly.FromDateTime(DateTime.UtcNow);

        string archivoAntiguo = $@"C:\logs\email-logs\email-log-{hace60Dias:yyyy-MM-dd}.json";
        string archivoReciente = $@"C:\logs\email-logs\email-log-{hoy:yyyy-MM-dd}.json";

        _mockFileSystem.Setup(fs => fs.GetFiles(It.IsAny<string>(), It.IsAny<string>()))
            .Returns([archivoAntiguo, archivoReciente]);

        // Act
        await _collector.ArchiveOldLogsAsync(30, CancellationToken.None);

        // Assert — solo el archivo antiguo debe eliminarse
        _mockFileSystem.Verify(fs => fs.DeleteFile(archivoAntiguo), Times.Once);
        _mockFileSystem.Verify(fs => fs.DeleteFile(archivoReciente), Times.Never);
    }

    // =========================================================================
    // Test 12: GetLogsByStatusAsync — filtra por estado
    // =========================================================================
    [Fact]
    public async Task GetLogsByStatusAsync_FiltraPorEstadoCorrectamente()
    {
        // Arrange
        DateOnly hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        List<EmailLogDto> logs =
        [
            CrearLogDePrueba(EmailStatus.Sent),
            CrearLogDePrueba(EmailStatus.Failed),
            CrearLogDePrueba(EmailStatus.Failed),
            CrearLogDePrueba(EmailStatus.Pending)
        ];
        string json = JsonSerializer.Serialize(logs, _jsonOptions);

        _mockFileSystem.Setup(fs => fs.GetFiles(It.IsAny<string>(), It.IsAny<string>()))
            .Returns([$@"C:\logs\email-log-{hoy:yyyy-MM-dd}.json"]);
        _mockFileSystem.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        // Act
        List<EmailLogDto> resultado = await _collector.GetLogsByStatusAsync(
            EmailStatus.Failed, hoy, CancellationToken.None);

        // Assert
        Assert.Equal(2, resultado.Count);
        Assert.All(resultado, l => Assert.Equal(EmailStatus.Failed, l.Status));
    }

    // =========================================================================
    // Test 13: Serialización de campos nuevos y PipelineSteps
    // =========================================================================
    [Fact]
    public void SerializacionCamposNuevos_IncluyePipelineSteps()
    {
        // Arrange
        EmailLogDto log = CrearLogDePrueba(EmailStatus.Sent);

        // Act
        string json = JsonSerializer.Serialize(log, _jsonOptions);
        EmailLogDto? deserializado = JsonSerializer.Deserialize<EmailLogDto>(json, _jsonOptions);

        // Assert
        Assert.NotNull(deserializado);
        Assert.Equal(log.Priority, deserializado.Priority);
        Assert.Equal(log.ContentType, deserializado.ContentType);
        Assert.Equal(log.TransactionType, deserializado.TransactionType);
        Assert.Equal(log.HostName, deserializado.HostName);
        Assert.Equal(log.SmtpStatusCode, deserializado.SmtpStatusCode);
        Assert.Equal(log.InitiatedBy, deserializado.InitiatedBy);
        Assert.Equal(log.AttachmentCount, deserializado.AttachmentCount);
        Assert.NotNull(deserializado.QueuedAtUtc);
        Assert.NotNull(deserializado.PipelineSteps);
        Assert.Equal(5, deserializado.PipelineSteps.Count);
        Assert.All(deserializado.PipelineSteps, s => Assert.Equal(PipelineStepStatus.OK, s.StepStatus));
    }

    // =========================================================================
    // Test 14: Pipeline con paso fallido — pasos posteriores marcados Skipped
    // =========================================================================
    [Fact]
    public void PipelineConFallo_PasosPosterioresSonSkipped()
    {
        // Arrange
        EmailLogDto log = CrearLogDePrueba(EmailStatus.Failed);

        // Assert
        Assert.NotNull(log.PipelineSteps);
        Assert.Equal(5, log.PipelineSteps.Count);

        // Paso 1 OK
        Assert.Equal(PipelineStepStatus.OK, log.PipelineSteps[0].StepStatus);
        // Paso 2 Failed
        Assert.Equal(PipelineStepStatus.Failed, log.PipelineSteps[1].StepStatus);
        Assert.Contains("no encontrada", log.PipelineSteps[1].Message);
        // Pasos 3-5 Skipped
        Assert.Equal(PipelineStepStatus.Skipped, log.PipelineSteps[2].StepStatus);
        Assert.Equal(PipelineStepStatus.Skipped, log.PipelineSteps[3].StepStatus);
        Assert.Equal(PipelineStepStatus.Skipped, log.PipelineSteps[4].StepStatus);
    }

    // =========================================================================
    // Test 15: Retrocompatibilidad — JSON sin campos nuevos deserializa OK
    // =========================================================================
    [Fact]
    public void Retrocompatibilidad_JsonSinCamposNuevos_DeserializaSinError()
    {
        // Arrange — JSON con solo los campos originales (pre-v2)
        string jsonViejo = """
        {
            "messageId": "00000000-0000-0000-0000-000000000001",
            "correlationId": null,
            "sourceMicroservice": "LegacyService",
            "senderAddress": "old@davivienda.hn",
            "recipientAddresses": ["dest@davivienda.hn"],
            "ccAddresses": null,
            "subject": "Email viejo",
            "status": 0,
            "statusMessage": null,
            "sentAtUtc": "2026-01-15T10:30:00+00:00",
            "durationMs": 200,
            "retryCount": 0,
            "hasAttachments": false,
            "environment": 0
        }
        """;

        // Act
        EmailLogDto? log = JsonSerializer.Deserialize<EmailLogDto>(jsonViejo, _jsonOptions);

        // Assert — no debe lanzar, campos nuevos tienen defaults
        Assert.NotNull(log);
        Assert.Equal("LegacyService", log.SourceMicroservice);
        Assert.Equal(EmailPriority.Normal, log.Priority); // default
        Assert.Null(log.PipelineSteps); // null para logs viejos
        Assert.Null(log.HostName);
        Assert.Null(log.SmtpStatusCode);
        Assert.Equal(0, log.AttachmentCount);
        Assert.Null(log.TransactionType);
    }

    // =========================================================================
    // Test 16: Enums nuevos serializan/deserializan correctamente
    // =========================================================================
    [Theory]
    [InlineData(EmailPriority.Low)]
    [InlineData(EmailPriority.Normal)]
    [InlineData(EmailPriority.High)]
    [InlineData(EmailPriority.Critical)]
    public void EnumPriority_SerializaCorrectamente(EmailPriority priority)
    {
        // Arrange
        EmailLogDto log = CrearLogDePrueba() with { Priority = priority };

        // Act
        string json = JsonSerializer.Serialize(log, _jsonOptions);
        EmailLogDto? result = JsonSerializer.Deserialize<EmailLogDto>(json, _jsonOptions);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(priority, result.Priority);
    }

    // =========================================================================
    // Test 17: PipelineStep serializa StepCode y StepName
    // =========================================================================
    [Fact]
    public void PipelineStep_SerializaTodosLosCampos()
    {
        // Arrange
        PipelineStep step = new()
        {
            StepOrder = 1,
            StepCode = "VALIDATE_RECIPIENTS",
            StepName = "Validar Destinatarios",
            StepStatus = PipelineStepStatus.Warning,
            Message = "Dominio @test.com no verificado",
            DurationMs = 15,
            ExecutedAtUtc = DateTimeOffset.UtcNow
        };

        // Act
        string json = JsonSerializer.Serialize(step, _jsonOptions);
        PipelineStep? result = JsonSerializer.Deserialize<PipelineStep>(json, _jsonOptions);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.StepOrder);
        Assert.Equal("VALIDATE_RECIPIENTS", result.StepCode);
        Assert.Equal("Validar Destinatarios", result.StepName);
        Assert.Equal(PipelineStepStatus.Warning, result.StepStatus);
        Assert.Equal("Dominio @test.com no verificado", result.Message);
        Assert.Equal(15, result.DurationMs);
    }
}
