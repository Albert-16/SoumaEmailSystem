using Souma.MicroserviceExample.Contracts;

namespace Souma.MicroserviceExample.Services;

/// <summary>
/// Implementación simulada de <see cref="IEmailSender"/> para pruebas.
/// Simula latencia de red y falla aleatoriamente (~20% de las veces).
/// </summary>
/// <remarks>
/// En producción, esta clase se reemplaza por la implementación real
/// que conecta con el API de correo externo (SendGrid, SMTP relay, etc.).
/// </remarks>
internal sealed class FakeEmailSender : IEmailSender
{
    private static readonly Random _random = new();

    /// <inheritdoc/>
    public async Task<EmailSendResult> SendAsync(SendEmailRequest request, CancellationToken cancellationToken)
    {
        // Simular latencia de red entre 50ms y 500ms
        int latenciaMs = _random.Next(50, 500);
        await Task.Delay(latenciaMs, cancellationToken);

        // Simular fallo aleatorio (~20% de las veces) para probar resiliencia
        bool falla = _random.Next(1, 6) == 1; // 1 de cada 5

        if (falla)
        {
            // Simular diferentes tipos de error que podrían ocurrir con un API real
            string[] errores =
            [
                "HTTP 503 Service Unavailable - El servicio de correo no responde",
                "HTTP 429 Too Many Requests - Límite de envío excedido",
                "HTTP 400 Bad Request - Dirección de destinatario inválida",
                "TimeoutException - La operación excedió el tiempo de espera (30s)",
                "SocketException - No se pudo establecer conexión con el servidor SMTP"
            ];

            string errorSeleccionado = errores[_random.Next(errores.Length)];

            return new EmailSendResult
            {
                Success = false,
                Message = errorSeleccionado
            };
        }

        return new EmailSendResult
        {
            Success = true,
            Message = $"Email enviado exitosamente a {string.Join(", ", request.RecipientAddresses)}"
        };
    }
}
