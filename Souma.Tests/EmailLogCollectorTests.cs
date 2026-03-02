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
        Environment = DeploymentEnvironment.DEV
    };

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
}
