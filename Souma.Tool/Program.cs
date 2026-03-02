using Souma.EmailLogging.Extensions;
using Souma.Tool.Components;
using Souma.Tool.Services;

var builder = WebApplication.CreateBuilder(args);

// Servicios Blazor
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Registrar Souma.EmailLogging (reutiliza la configuración compartida)
builder.Services.AddEmailLogging(options =>
{
    options.LogDirectory = builder.Configuration["EmailLogging:LogDirectory"]
        ?? Path.Combine(AppContext.BaseDirectory, "email-logs");
    options.PollingIntervalSeconds = builder.Configuration.GetValue("EmailLogging:PollingIntervalSeconds", 30);
});

// Servicios del dashboard
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<EmailLogReaderService>();
builder.Services.AddHostedService<EmailLogPollingService>();
builder.Services.AddScoped<ChartInteropService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
