using System.Reflection;
using FlowUI.Components;
using FlowUI.Config;
using FlowUI.Services;
using FlowUI.Services.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Hosting.WindowsServices;
using Radzen;

if (args.Any(a => a is "--version" or "-v"))
{
    var ver = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "1.3.1";
    var plusIdx = ver.IndexOf('+');
    if (plusIdx > 0)
    {
        ver = ver[..plusIdx];
    }
    Console.WriteLine($"FlowUI {ver}");
    return;
}

if (args.Any(a => a is "--help" or "-h" or "-?"))
{
    Console.WriteLine("""
        ETL Madness FlowUI (Flow Builder Studio)

        Usage:
          flow-ui [options]

        Options:
          --env-file <path>       Load KEY=VALUE environment configuration file before startup
          --service-name <name>   Windows Service name when hosted under Windows SCM (default: FlowUI)
          --urls <urls>           Semicolon-separated listen URLs (e.g., http://0.0.0.0:5185)
          --contentRoot <path>    Override ASP.NET Core content root directory
          -v, --version           Print version information and exit
          -h, --help              Show this help message and exit

        Key Environment Variables:
          FLOW_API_URL            Full go-flowAPI base URL (e.g., http://localhost:8080)
          FLOW_API_HOST           go-flowAPI host (used if FLOW_API_URL is unset)
          FLOW_API_PORT           go-flowAPI port (used with FLOW_API_HOST)
          FLOW_UI_PORT            UI listen port (ignored if ASPNETCORE_URLS is set)
          FLOW_UI_SCHEME          UI scheme when using FLOW_UI_PORT (https or http)
          FLOW_UI_AUTH_MODE       UI authentication mode: ApiBasic (default) or Windows
          ASPNETCORE_URLS         Explicit Kestrel listen URLs (e.g., http://0.0.0.0:5185)
          FLOW_UI_ENV_FILE        Default path to KEY=VALUE environment file
        """);
    return;
}

var hostArgs = EnvFileLoader.ExtractHostArgs(args, out var envFilePath, out var serviceName);
if (!string.IsNullOrWhiteSpace(envFilePath))
{
    EnvFileLoader.LoadFile(envFilePath);
}

var hasExplicitContentRoot = hostArgs.Any(a =>
    a.Equals("--contentRoot", StringComparison.OrdinalIgnoreCase) ||
    a.StartsWith("--contentRoot=", StringComparison.OrdinalIgnoreCase));

var contentRootPath = !hasExplicitContentRoot &&
    (WindowsServiceHelpers.IsWindowsService() ||
     Directory.Exists(Path.Combine(AppContext.BaseDirectory, "wwwroot")))
    ? AppContext.BaseDirectory
    : null;

var webAppOptions = new WebApplicationOptions
{
    Args = hostArgs,
    ContentRootPath = contentRootPath
};

var builder = WebApplication.CreateBuilder(webAppOptions);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = string.IsNullOrWhiteSpace(serviceName) ? "FlowUI" : serviceName;
});
builder.Services.AddSystemd();

if (!string.IsNullOrWhiteSpace(AppConfig.UiPort) &&
    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    var inContainer = string.Equals(
        Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
        "true",
        StringComparison.OrdinalIgnoreCase);
    var bindHost = inContainer ? "0.0.0.0" : "localhost";
    builder.WebHost.UseUrls($"{AppConfig.UiScheme}://{bindHost}:{AppConfig.UiPort}");
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

await app.RunAsync();
