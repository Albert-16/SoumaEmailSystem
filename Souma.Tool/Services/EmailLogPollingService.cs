using Microsoft.Extensions.Options;
using Souma.EmailLogging.Options;

namespace Souma.Tool.Services;

/// <summary>
/// BackgroundService que refresca periódicamente el caché de logs.
/// Usa polling en lugar de FileSystemWatcher porque este último
/// es poco confiable sobre rutas UNC/red entre diferentes servidores.
/// </summary>
public sealed class EmailLogPollingService : BackgroundService
{
    private readonly EmailLogReaderService _readerService;
    private readonly ILogger<EmailLogPollingService> _logger;
    private readonly int _intervalSeconds;

    public EmailLogPollingService(
        EmailLogReaderService readerService,
        IOptions<EmailLoggingOptions> options,
        ILogger<EmailLogPollingService> logger)
    {
        _readerService = readerService;
        _logger = logger;
        _intervalSeconds = options.Value.PollingIntervalSeconds;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Servicio de polling iniciado. Intervalo: {Intervalo}s",
            _intervalSeconds);

        // Carga inicial inmediata
        await _readerService.RefreshAsync(stoppingToken);

        using PeriodicTimer timer = new(TimeSpan.FromSeconds(_intervalSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await _readerService.RefreshAsync(stoppingToken);
        }
    }
}
