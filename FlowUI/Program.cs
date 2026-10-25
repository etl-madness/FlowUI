using FlowUI.Components;
using FlowUI.Config;
using FlowUI.Services;
using FlowUI.Services.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Radzen;

var builder = WebApplication.CreateBuilder(args);

if (!string.IsNullOrWhiteSpace(AppConfig.UiPort) &&
    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    var inContainer = string.Equals(
        Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
        "true",
        StringComparison.OrdinalIgnoreCase);
    var bindHost = inContainer ? "0.0.0.0" : "localhost";
    builder.WebHost.UseUrls($"http://{bindHost}:{AppConfig.UiPort}");
}

// Add Razor Components (Interactive Server) & Radzen UI Services
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddRadzenComponents();
builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();

// Backend go-flowAPI Client (Scoped per Blazor circuit so all components share the logged-in user's Basic Auth token)
builder.Services.AddHttpClient("FlowApi", client =>
{
    if (!string.IsNullOrWhiteSpace(AppConfig.ApiBaseUrl))
    {
        client.BaseAddress = new Uri(AppConfig.ApiBaseUrl.TrimEnd('/') + "/");
    }
    client.Timeout = TimeSpan.FromMinutes(5);
});
builder.Services.AddScoped<FlowApiClient>(sp =>
{
    var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpFactory.CreateClient("FlowApi");
    var logger = sp.GetRequiredService<ILogger<FlowApiClient>>();
    return new FlowApiClient(httpClient, logger);
});

// Pluggable UI Authentication Providers (API Basic Auth, Windows Integrated, & Windows Credentials)
builder.Services.AddScoped<IUiAuthProvider, FlowApiPassthroughAuthProvider>();
builder.Services.AddScoped<IUiAuthProvider, WindowsIntegratedUiAuthProvider>();
builder.Services.AddScoped<IUiAuthProvider, WindowsCredentialsUiAuthProvider>();

// Central Authentication Service & Blazor AuthenticationStateProvider
builder.Services.AddScoped<UiAuthenticationService>();
builder.Services.AddScoped<IAuthenticationService>(sp => sp.GetRequiredService<UiAuthenticationService>());
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<UiAuthenticationService>());

// Flow Builder Studio Services
builder.Services.AddSingleton<CatalogService>();
builder.Services.AddSingleton<XmlGeneratorService>();
builder.Services.AddSingleton<XmlImporterService>();
builder.Services.AddSingleton<DocsService>();
builder.Services.AddSingleton<FlowCryptoService>();
builder.Services.AddScoped<StudioStateService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

// Allow 'unload' in Permissions-Policy so Visual Studio Browser Link does not log a console violation in Chromium
app.Use(async (context, next) =>
{
    context.Response.Headers["Permissions-Policy"] = "unload=(self)";
    await next();
});

app.UseAntiforgery();
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
