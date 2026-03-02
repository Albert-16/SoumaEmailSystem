# Souma Email Traceability System

Sistema de trazabilidad de emails para Davivienda Honduras.
Logging basado en archivos JSON diarios, sin base de datos. Dashboard Blazor Server para visualizar metricas.

---

## Integracion desde ASMX / .NET Framework 4.7.2 (Escritura Directa)

> **Tu servicio ASMX no puede referenciar Souma.EmailLogging** (.NET 10).
> En su lugar, se usa una clase helper autocontenida que escribe al mismo formato JSON.

### Paso 1 — Copiar el archivo helper a tu proyecto

Copiar `Souma.EmailLogging/LegacyHelper/SoumaEmailLogWriter.cs` a tu proyecto ASMX.
Requiere: `Newtonsoft.Json` (ya lo tienes si usas Web API o ASMX con JSON).

### Paso 2 — Configurar en Global.asax o Application_Start

```csharp
// Global.asax.cs
protected void Application_Start(object sender, EventArgs e)
{
    // Configurar UNA VEZ al iniciar la aplicacion
    SoumaEmailLogWriter.LogDirectory = @"\\servidor\logs\email-logs\";
    SoumaEmailLogWriter.FallbackDirectory = @"C:\logs\email-fallback\";
    SoumaEmailLogWriter.ServiceName = "ServicioCorreoASMX";
    SoumaEmailLogWriter.CurrentEnvironment = DeploymentEnvironment.DEV;
}
```

### Paso 3 — Integrar en tu metodo de envio de email

Ejemplo real dentro de un servicio ASMX que llama a un API externo para enviar correo:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Web.Services;
using Souma.EmailLogging.Legacy;

[WebService(Namespace = "http://davivienda.hn/correo")]
public class ServicioCorreo : WebService
{
    private static readonly HttpClient _httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    [WebMethod]
    public ResultadoEnvio EnviarCorreo(
        string remitente,
        string[] destinatarios,
        string[] copias,
        string asunto,
        string cuerpo,
        bool tieneAdjuntos)
    {
        // ──────────────────────────────────────────
        // MOMENTO 1: ANTES — iniciar cronometro
        // ──────────────────────────────────────────
        var sw = Stopwatch.StartNew();
        var listaDestinatarios = new List<string>(destinatarios);
        var listaCopias = copias != null ? new List<string>(copias) : null;

        try
        {
            // ──────────────────────────────────────────
            // MOMENTO 2: DURANTE — tu logica existente
            // (esta parte NO se modifica)
            // ──────────────────────────────────────────
            var payload = new
            {
                from = remitente,
                to = destinatarios,
                cc = copias,
                subject = asunto,
                body = cuerpo
            };

            var response = _httpClient.PostAsJsonAsync(
                "https://api.correo-externo.com/send", payload).Result;

            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                // ──────────────────────────────────────
                // MOMENTO 3a: EXITO — registrar log
                // ──────────────────────────────────────
                SoumaEmailLogWriter.LogSuccess(
                    senderAddress:      remitente,
                    recipientAddresses: listaDestinatarios,
                    subject:            asunto,
                    durationMs:         sw.ElapsedMilliseconds,
                    ccAddresses:        listaCopias,
                    hasAttachments:     tieneAdjuntos
                );

                return new ResultadoEnvio { Exito = true, Mensaje = "Enviado" };
            }
            else
            {
                string error = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";

                // ──────────────────────────────────────
                // MOMENTO 3b: FALLO HTTP — registrar log
                // ──────────────────────────────────────
                SoumaEmailLogWriter.LogFailure(
                    senderAddress:      remitente,
                    recipientAddresses: listaDestinatarios,
                    subject:            asunto,
                    durationMs:         sw.ElapsedMilliseconds,
                    errorMessage:       error,
                    ccAddresses:        listaCopias,
                    hasAttachments:     tieneAdjuntos
                );

                return new ResultadoEnvio { Exito = false, Mensaje = error };
            }
        }
        catch (Exception ex)
        {
            sw.Stop();

            // ──────────────────────────────────────────
            // MOMENTO 3c: EXCEPCION — registrar log
            // ──────────────────────────────────────────
            SoumaEmailLogWriter.LogFailure(
                senderAddress:      remitente,
                recipientAddresses: listaDestinatarios,
                subject:            asunto,
                durationMs:         sw.ElapsedMilliseconds,
                errorMessage:       $"{ex.GetType().Name}: {ex.Message}",
                ccAddresses:        listaCopias,
                hasAttachments:     tieneAdjuntos
            );

            return new ResultadoEnvio { Exito = false, Mensaje = ex.Message };
        }
    }
}

public class ResultadoEnvio
{
    public bool Exito { get; set; }
    public string Mensaje { get; set; }
}
```

### Paso 4 — Con reintentos y CorrelationId

```csharp
[WebMethod]
public ResultadoEnvio EnviarConReintentos(
    string remitente, string[] destinatarios, string asunto, string cuerpo)
{
    // Un CorrelationId unico vincula TODOS los intentos del mismo email
    string correlationId = Guid.NewGuid().ToString("N");
    var listaDestinatarios = new List<string>(destinatarios);
    int maxReintentos = 3;

    for (int intento = 0; intento <= maxReintentos; intento++)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // ... tu logica de envio ...
            var response = _httpClient.PostAsJsonAsync("https://api.correo.com/send",
                new { from = remitente, to = destinatarios, subject = asunto, body = cuerpo }).Result;

            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                SoumaEmailLogWriter.LogSuccess(
                    senderAddress:      remitente,
                    recipientAddresses: listaDestinatarios,
                    subject:            asunto,
                    durationMs:         sw.ElapsedMilliseconds,
                    correlationId:      correlationId,  // <-- vincula todos los intentos
                    retryCount:         intento
                );
                return new ResultadoEnvio { Exito = true };
            }

            // Fallo HTTP — registrar como Retrying si quedan intentos
            SoumaEmailLogWriter.LogEmail(new EmailLogEntry
            {
                CorrelationId      = correlationId,
                SourceMicroservice = SoumaEmailLogWriter.ServiceName,
                SenderAddress      = remitente,
                RecipientAddresses = listaDestinatarios,
                Subject            = asunto,
                Status             = intento < maxReintentos ? EmailStatus.Retrying : EmailStatus.Failed,
                StatusMessage      = $"HTTP {(int)response.StatusCode}",
                SentAtUtc          = DateTimeOffset.UtcNow,
                DurationMs         = sw.ElapsedMilliseconds,
                RetryCount         = intento,
                Environment        = SoumaEmailLogWriter.CurrentEnvironment
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            SoumaEmailLogWriter.LogEmail(new EmailLogEntry
            {
                CorrelationId      = correlationId,
                SourceMicroservice = SoumaEmailLogWriter.ServiceName,
                SenderAddress      = remitente,
                RecipientAddresses = listaDestinatarios,
                Subject            = asunto,
                Status             = intento < maxReintentos ? EmailStatus.Retrying : EmailStatus.Failed,
                StatusMessage      = $"{ex.GetType().Name}: {ex.Message}",
                SentAtUtc          = DateTimeOffset.UtcNow,
                DurationMs         = sw.ElapsedMilliseconds,
                RetryCount         = intento,
                Environment        = SoumaEmailLogWriter.CurrentEnvironment
            });
        }

        // Backoff exponencial: 1s, 2s, 4s
        if (intento < maxReintentos)
            Thread.Sleep((int)Math.Pow(2, intento) * 1000);
    }

    return new ResultadoEnvio { Exito = false, Mensaje = "Agotados los reintentos" };
}
```

### Resumen: Que servicio usa que

| Servicio | Runtime | Integra con |
|----------|---------|-------------|
| Tu ASMX existente | .NET Framework 4.7.2 | `SoumaEmailLogWriter.cs` (copiar archivo) |
| Microservicios nuevos .NET | .NET 10 | `Souma.EmailLogging` (referencia de proyecto) |
| Dashboard | .NET 10 | `Souma.Tool` (lee los mismos archivos JSON) |

Ambos escriben al **mismo directorio** y con el **mismo formato JSON**, y el dashboard los lee sin distinguir quien escribio cada registro.

---

## Integracion en Microservicio .NET 10 (Referencia Directa)

### Paso 1 — Agregar referencia al proyecto

```xml
<!-- En tu .csproj -->
<ItemGroup>
  <ProjectReference Include="..\Souma.EmailLogging\Souma.EmailLogging.csproj" />
</ItemGroup>
```

### Paso 2 — Registrar en Program.cs (DI)

```csharp
using Souma.EmailLogging.Extensions;

var builder = WebApplication.CreateBuilder(args);

// ========================================================
// UNICA LINEA DE CONFIGURACION NECESARIA
// ========================================================
builder.Services.AddEmailLogging(options =>
{
    // Ruta donde se guardan los logs (puede ser UNC de red)
    options.LogDirectory = @"\\servidor\logs\email-logs\";

    // Opcional: directorio de respaldo si la red falla
    options.FallbackDirectory = @"C:\logs\email-fallback\";

    // Opcional: tamano maximo por archivo (default 10 MB)
    options.MaxFileSizeMb = 10;

    // Opcional: dias de retencion (default 30)
    options.RetentionDays = 30;
});
```

O usando `appsettings.json`:

```json
{
  "EmailLogging": {
    "LogDirectory": "\\\\servidor\\logs\\email-logs\\",
    "FallbackDirectory": "C:\\logs\\email-fallback\\",
    "MaxFileSizeMb": 10,
    "RetentionDays": 30,
    "PollingIntervalSeconds": 30
  }
}
```

```csharp
builder.Services.AddEmailLogging(options =>
{
    options.LogDirectory = builder.Configuration["EmailLogging:LogDirectory"]!;
    options.FallbackDirectory = builder.Configuration["EmailLogging:FallbackDirectory"];
});
```

### Paso 3 — Inyectar `IEmailLogCollector` en tu servicio

```csharp
using Souma.EmailLogging.Abstractions;
using Souma.EmailLogging.Models;

public sealed class MiServicioDeEmail
{
    private readonly IEmailLogCollector _logCollector;
    private readonly ILogger<MiServicioDeEmail> _logger;
    private readonly HttpClient _httpClient; // o tu cliente SMTP, SendGrid, etc.

    public MiServicioDeEmail(
        IEmailLogCollector logCollector,  // <-- inyectar esto
        ILogger<MiServicioDeEmail> logger,
        HttpClient httpClient)
    {
        _logCollector = logCollector;
        _logger = logger;
        _httpClient = httpClient;
    }
}
```

### Paso 4 — Registrar el resultado de cada envio

Hay **3 momentos clave** donde se construye el DTO:

```csharp
public async Task<bool> EnviarEmailAsync(
    string remitente,
    List<string> destinatarios,
    List<string>? copias,
    string asunto,
    string cuerpo,
    bool tieneAdjuntos,
    string? correlationId,
    CancellationToken cancellationToken)
{
    // ──────────────────────────────────────────────────────
    // MOMENTO 1: ANTES del envio — preparar Stopwatch
    // ──────────────────────────────────────────────────────
    Stopwatch cronometro = Stopwatch.StartNew();
    EmailStatus status;
    string? mensajeError = null;

    // ──────────────────────────────────────────────────────
    // MOMENTO 2: DURANTE el envio — capturar resultado
    // ──────────────────────────────────────────────────────
    try
    {
        // === Tu logica de envio existente (NO se modifica) ===
        HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
            "https://api.correo.com/send",
            new { from = remitente, to = destinatarios, subject = asunto, body = cuerpo },
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            status = EmailStatus.Sent;
        }
        else
        {
            status = EmailStatus.Failed;
            mensajeError = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
        }
    }
    catch (TaskCanceledException)
    {
        status = EmailStatus.Failed;
        mensajeError = "TimeoutException: La operacion excedio el tiempo de espera";
    }
    catch (HttpRequestException ex)
    {
        status = EmailStatus.Failed;
        mensajeError = $"HttpRequestException: {ex.Message}";
    }
    catch (Exception ex)
    {
        status = EmailStatus.Failed;
        mensajeError = $"{ex.GetType().Name}: {ex.Message}";
    }

    cronometro.Stop();

    // ──────────────────────────────────────────────────────
    // MOMENTO 3: DESPUES del envio — construir DTO y loguear
    // ──────────────────────────────────────────────────────
    EmailLogDto logEntry = new()
    {
        // Correlacion: vincula reintentos del mismo email logico
        CorrelationId = correlationId,

        // Identifica QUE microservicio envio el email
        SourceMicroservice = "MiMicroservicio",

        // Datos del email
        SenderAddress      = remitente,
        RecipientAddresses = destinatarios,
        CcAddresses        = copias,
        Subject            = asunto,

        // Resultado del envio
        Status        = status,
        StatusMessage = mensajeError,  // null si fue exitoso

        // Metricas de rendimiento
        SentAtUtc  = DateTimeOffset.UtcNow,
        DurationMs = cronometro.ElapsedMilliseconds,

        // Metadata adicional
        HasAttachments = tieneAdjuntos,
        RetryCount     = 0,
        Environment    = DeploymentEnvironment.DEV
    };

    // ESTA LLAMADA NUNCA FALLA — no necesitas try/catch aqui
    await _logCollector.LogEmailAsync(logEntry, cancellationToken);

    return status == EmailStatus.Sent;
}
```

### Paso 5 — Manejo de reintentos con CorrelationId

Si tu microservicio reintenta emails fallidos, usa `CorrelationId` para vincularlos:

```csharp
public async Task<bool> EnviarConReintentosAsync(
    string remitente,
    List<string> destinatarios,
    string asunto,
    string cuerpo,
    CancellationToken cancellationToken)
{
    // Generar un ID de correlacion unico para TODOS los intentos de este email
    string correlationId = Guid.NewGuid().ToString("N");
    int maxReintentos = 3;

    for (int intento = 0; intento <= maxReintentos; intento++)
    {
        Stopwatch cronometro = Stopwatch.StartNew();
        EmailStatus status;
        string? mensajeError = null;

        try
        {
            // ... tu logica de envio ...
            await EnviarViaApiExterna(remitente, destinatarios, asunto, cuerpo, cancellationToken);
            status = EmailStatus.Sent;
        }
        catch (Exception ex)
        {
            status = intento < maxReintentos ? EmailStatus.Retrying : EmailStatus.Failed;
            mensajeError = $"{ex.GetType().Name}: {ex.Message}";
        }

        cronometro.Stop();

        // Cada intento genera su propio MessageId,
        // pero TODOS comparten el mismo CorrelationId
        await _logCollector.LogEmailAsync(new EmailLogDto
        {
            CorrelationId      = correlationId,    // <-- mismo para todos los intentos
            SourceMicroservice = "MiMicroservicio",
            SenderAddress      = remitente,
            RecipientAddresses = destinatarios,
            Subject            = asunto,
            Status             = status,
            StatusMessage      = mensajeError,
            SentAtUtc          = DateTimeOffset.UtcNow,
            DurationMs         = cronometro.ElapsedMilliseconds,
            RetryCount         = intento,          // <-- 0, 1, 2, 3
            Environment        = DeploymentEnvironment.DEV
        }, cancellationToken);

        // Si fue exitoso, no reintentar
        if (status == EmailStatus.Sent)
            return true;

        // Backoff exponencial entre reintentos
        if (intento < maxReintentos)
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, intento)), cancellationToken);
    }

    return false;
}
```

### Paso 6 — Ejemplo minimo en Minimal API

```csharp
// Program.cs — solo las lineas relevantes

using Souma.EmailLogging.Abstractions;
using Souma.EmailLogging.Extensions;
using Souma.EmailLogging.Models;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEmailLogging(options =>
{
    options.LogDirectory = "./email-logs";
});

var app = builder.Build();

app.MapPost("/api/email/send", async (
    SendRequest request,
    IEmailLogCollector logCollector,
    CancellationToken ct) =>
{
    Stopwatch sw = Stopwatch.StartNew();

    // ... tu logica de envio real aqui ...
    bool exito = await EnviarEmailReal(request, ct);

    sw.Stop();

    // Una sola llamada despues del envio — esto es TODO lo que necesitas
    await logCollector.LogEmailAsync(new EmailLogDto
    {
        SourceMicroservice = "MiApi",
        SenderAddress      = request.From,
        RecipientAddresses = request.To,
        Subject            = request.Subject,
        Status             = exito ? EmailStatus.Sent : EmailStatus.Failed,
        SentAtUtc          = DateTimeOffset.UtcNow,
        DurationMs         = sw.ElapsedMilliseconds,
        Environment        = DeploymentEnvironment.DEV
    }, ct);

    return exito ? Results.Ok() : Results.StatusCode(502);
});

app.Run();
```

---

## Arquitectura

```
Tu Microservicio                    Souma.Tool (Dashboard)
+-----------------+                +---------------------+
|  Envia email    |                |  Blazor Server      |
|       |         |                |       |             |
|  LogEmailAsync()|                |  Polling cada 10s   |
|       |         |                |       |             |
|       v         |                |       v             |
|  IEmailLog      |                |  Lee archivos JSON  |
|  Collector      |                |       |             |
+-------+---------+                |       v             |
        |                          |  Metricas + Graficos|
        v                          +---------------------+
  \\servidor\logs\email-logs\                ^
  +----------------------+                   |
  | email-log-2026-03-02 |-------------------+
  | .json                |   (comparten solo la carpeta y el schema JSON)
  +----------------------+
```

Los dos sistemas estan **completamente desacoplados** — comparten unicamente una ruta de carpeta y el formato JSON.

---

## Despliegue en IIS (Produccion / UAT)

> **Solo se publica `Souma.Tool`** (el dashboard Blazor Server).
> Tu servicio ASMX ya corre en IIS — solo necesitas agregarle `SoumaEmailLogWriter.cs`.

### Arquitectura en IIS

```
Servidor IIS (Windows Server)
|
+-- Sitio: "ServicioCorreoASMX" (ya existe)     <-- .NET Framework 4.7.2
|   |   App Pool: "DefaultAppPool" (CLR v4.0)
|   |
|   +-- SoumaEmailLogWriter.cs                  <-- Copiar este archivo
|   |   Escribe JSON a \\servidor\logs\email-logs\
|   |
|   +-- Tu WebMethod existente
|       (agregar las 3 lineas de logging)
|
+-- Sitio: "SoumaDashboard" (NUEVO)             <-- .NET 10 Blazor Server
|   |   App Pool: "SoumaDashboardPool" (No Managed Code)
|   |
|   +-- C:\inetpub\SoumaDashboard\              <-- dotnet publish aqui
|       Lee JSON de \\servidor\logs\email-logs\
|
+-- Carpeta compartida de logs
    \\servidor\logs\email-logs\
    +-- email-log-2026-03-01.json
    +-- email-log-2026-03-02.json
    +-- ...
```

### Prerequisitos en el servidor

1. **Instalar .NET 10 Hosting Bundle** (incluye ASP.NET Core Module para IIS):
   ```
   https://dotnet.microsoft.com/download/dotnet/10.0
   → Descargar "Hosting Bundle" (no solo el Runtime)
   → Reiniciar IIS despues de instalar: iisreset
   ```

2. **Crear la carpeta de logs** (si no existe):
   ```cmd
   mkdir \\servidor\logs\email-logs
   ```

3. **Crear la carpeta de publicacion**:
   ```cmd
   mkdir C:\inetpub\SoumaDashboard
   ```

### Paso 1 — Publicar Souma.Tool

Desde tu maquina de desarrollo, ejecutar:

```cmd
cd C:\Users\MSI MAG\SoumaMail

dotnet publish Souma.Tool/Souma.Tool.csproj ^
  --configuration Release ^
  --output ./publish/SoumaDashboard ^
  --self-contained false
```

> **`--self-contained false`** = requiere el Hosting Bundle en el servidor (mas liviano).
> Si prefieres no instalar nada en el servidor, usa `--self-contained true` (genera ~80 MB pero no requiere runtime).

El resultado estara en `./publish/SoumaDashboard/`. Copiar **todo el contenido** de esa carpeta al servidor:

```cmd
xcopy /E /Y .\publish\SoumaDashboard\* \\servidor\C$\inetpub\SoumaDashboard\
```

### Paso 2 — Configurar `appsettings.Production.json` en el servidor

Editar `C:\inetpub\SoumaDashboard\appsettings.Production.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Microsoft.AspNetCore": "Warning",
      "Souma": "Information"
    }
  },
  "AllowedHosts": "*",
  "EmailLogging": {
    "LogDirectory": "\\\\servidor\\logs\\email-logs\\",
    "PollingIntervalSeconds": 30
  }
}
```

> **IMPORTANTE:** La ruta `LogDirectory` debe ser la **misma** que configuraste
> en `SoumaEmailLogWriter.LogDirectory` del servicio ASMX.

### Paso 3 — Crear Application Pool en IIS

1. Abrir **IIS Manager** (`inetmgr`)
2. Click derecho en **Application Pools** → **Add Application Pool...**
3. Configurar:
   - **Name:** `SoumaDashboardPool`
   - **.NET CLR version:** `No Managed Code` (obligatorio para .NET 10)
   - **Managed pipeline mode:** `Integrated`
4. Click derecho en el pool → **Advanced Settings...**
   - **Start Mode:** `AlwaysRunning` (para que el BackgroundService de polling inicie con IIS)
   - **Identity:** Cuenta que tenga permisos de **lectura** en `\\servidor\logs\email-logs\`

### Paso 4 — Crear el sitio en IIS

1. Click derecho en **Sites** → **Add Website...**
2. Configurar:
   - **Site name:** `SoumaDashboard`
   - **Application pool:** `SoumaDashboardPool`
   - **Physical path:** `C:\inetpub\SoumaDashboard`
   - **Binding:**
     - Type: `http`
     - Port: `8080` (o el que prefieras, no uses 80 si ya esta ocupado)
     - Host name: (dejar vacio para acceder por IP, o poner `souma.davivienda.hn`)
3. Click **OK**

### Paso 5 — Verificar permisos

El Application Pool identity necesita estos permisos:

| Carpeta | Permiso | Motivo |
|---------|---------|--------|
| `C:\inetpub\SoumaDashboard` | Lectura + Ejecucion | Archivos de la aplicacion |
| `C:\inetpub\SoumaDashboard\logs` | Escritura | stdout logs de IIS |
| `\\servidor\logs\email-logs\` | **Lectura** | Leer archivos JSON de email |

```cmd
:: Dar permisos al pool identity (si usa ApplicationPoolIdentity)
icacls "C:\inetpub\SoumaDashboard" /grant "IIS AppPool\SoumaDashboardPool":(OI)(CI)RX
icacls "C:\inetpub\SoumaDashboard\logs" /grant "IIS AppPool\SoumaDashboardPool":(OI)(CI)M
icacls "\\servidor\logs\email-logs" /grant "IIS AppPool\SoumaDashboardPool":(OI)(CI)R
```

### Paso 6 — Verificar que funciona

1. Navegar a `http://servidor:8080` (o el puerto que configuraste)
2. Debe cargar el dashboard dark theme
3. Si el ASMX ya esta escribiendo logs, deben aparecer en ~30 segundos (polling interval)

### Troubleshooting

| Problema | Solucion |
|----------|----------|
| Error 500.19 | Falta el .NET 10 Hosting Bundle. Instalar + `iisreset` |
| Error 502.5 | El `web.config` apunta a un DLL que no existe. Verificar el publish |
| Dashboard carga pero sin datos | Verificar que `LogDirectory` en appsettings.Production.json coincide con la ruta del ASMX |
| "Access denied" en logs | El App Pool identity no tiene permisos de lectura en la carpeta de logs |
| Charts no cargan | Verificar que el servidor tiene acceso a internet para CDN de Chart.js, o copiar chart.js localmente |

### Actualizaciones futuras

Para actualizar el dashboard sin downtime:

```cmd
:: 1. Publicar nueva version
dotnet publish Souma.Tool/Souma.Tool.csproj -c Release -o ./publish/SoumaDashboard

:: 2. Detener el sitio en IIS
%windir%\system32\inetsrv\appcmd stop site "SoumaDashboard"

:: 3. Copiar archivos (NO sobreescribir appsettings.Production.json)
xcopy /E /Y /EXCLUDE:exclude.txt .\publish\SoumaDashboard\* \\servidor\C$\inetpub\SoumaDashboard\

:: 4. Reiniciar el sitio
%windir%\system32\inetsrv\appcmd start site "SoumaDashboard"
```

> **Nota:** El `appsettings.Production.json` del servidor NO se sobreescribe — asi conservas la configuracion de rutas sin tener que reconfigurar cada vez.

---

## Ejecucion Local

```bash
# Terminal 1 — Microservicio (genera logs)
cd Souma.MicroserviceExample
dotnet run --urls "http://localhost:5100"

# Terminal 2 — Dashboard (lee y muestra logs)
cd Souma.Tool
dotnet run --urls "http://localhost:5200"

# Terminal 3 — Enviar emails de prueba
curl -X POST http://localhost:5100/api/email/send \
  -H "Content-Type: application/json" \
  -d '{
    "senderAddress": "noreply@davivienda.hn",
    "recipientAddresses": ["usuario@banco.hn"],
    "subject": "Prueba de envio",
    "body": "Contenido del email"
  }'
```

## Tests

```bash
dotnet test Souma.Tests/Souma.Tests.csproj
# Resultado esperado: 12/12 tests pasan
```

---

## Estructura del Proyecto

```
SoumaEmailSystem/
+-- Souma.EmailLogging/          # Class Library — nucleo del sistema
|   +-- Abstractions/            #   IEmailLogCollector, IFileSystem
|   +-- Extensions/              #   AddEmailLogging() para DI
|   +-- Models/                  #   EmailLogDto, EmailLogSummary, enums
|   +-- Options/                 #   EmailLoggingOptions
|   +-- Services/                #   EmailLogCollector, DefaultFileSystem
|
+-- Souma.MicroserviceExample/   # Minimal API — ejemplo de integracion
|   +-- Contracts/               #   SendEmailRequest, SendEmailResponse
|   +-- Services/                #   IEmailSender, FakeEmailSender
|
+-- Souma.Tool/                  # Blazor Server — dashboard
|   +-- Components/Dashboard/    #   Cards, tabla, filtros, modal, charts
|   +-- Services/                #   Reader, Polling, ChartInterop, CSV
|
+-- Souma.Tests/                 # xUnit + Moq — 12 test cases
```

## Formato del Archivo de Log

Archivo: `email-log-YYYY-MM-DD.json` (overflow: `-part2.json`, `-part3.json`...)

```json
[
  {
    "messageId": "04f8e3e4-ca11-4e5b-925e-281d1bb0f16e",
    "correlationId": "batch-abc123",
    "sourceMicroservice": "SoumaIntegration",
    "senderAddress": "noreply@davivienda.hn",
    "recipientAddresses": ["usuario@banco.hn"],
    "ccAddresses": ["audit@davivienda.hn"],
    "subject": "Notificacion de transaccion",
    "status": "Sent",
    "statusMessage": null,
    "sentAtUtc": "2026-03-02T02:11:03+00:00",
    "durationMs": 413,
    "retryCount": 0,
    "hasAttachments": false,
    "environment": "DEV"
  }
]
```
