using System.Net;
using System.Reflection;
using System.Security.Claims;
using FlowUI.Config;
using FlowUI.Models;
using FlowUI.Services;
using FlowUI.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowUI.Tests;

internal sealed class FakeUiAuthProvider : IUiAuthProvider
{
    public string ProviderName { get; init; } = "ApiBasic";
    public string DisplayName { get; init; } = "Fake Provider";
    public string Description { get; init; } = "Fake auth provider for unit testing";
    public bool SupportsAutoLogin { get; init; }
    public Func<AuthRequest, Task<AuthResult>> Handler { get; set; } =
        req => Task.FromResult(AuthResult.Success(new AppUser
        {
            Username = string.IsNullOrEmpty(req.Username) ? "auto_user" : req.Username,
            IsAdmin = true,
            IsActive = true,
            AuthProvider = "ApiBasic"
        }, "ApiBasic"));

    public Task<AuthResult> AuthenticateAsync(AuthRequest request, CancellationToken ct = default) =>
        Handler(request);
}

public class AuthServicesTests
{
    private static FlowApiClient CreateApiClient(FakeHttpMessageHandler? handler = null)
    {
        handler ??= new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.JsonResponse("{}"));
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        return new FlowApiClient(http, NullLogger<FlowApiClient>.Instance);
    }

    [Fact]
    public async Task UiAuthenticationService_Login_Claims_SwitchProvider_Refresh_And_Logout_WorkCorrectly()
    {
        Assert.Throws<InvalidOperationException>(() => new UiAuthenticationService([], CreateApiClient()));

        var basicProvider = new FakeUiAuthProvider { ProviderName = "ApiBasic", SupportsAutoLogin = false };
        var winProvider = new FakeUiAuthProvider { ProviderName = "Windows", SupportsAutoLogin = true };

        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/v1/users")
            {
                return FakeHttpMessageHandler.JsonResponse("""
                    {"users":[{"username":"alice","permission":"execution-only","is_active":true,"description":"Updated perm"}]}
                    """);
            }
            return FakeHttpMessageHandler.JsonResponse("{}");
        });
        var apiClient = CreateApiClient(handler);
        var authService = new UiAuthenticationService([basicProvider, winProvider], apiClient);

        Assert.False(authService.IsAuthenticated);
        Assert.False(authService.CanRead);
        Assert.False(authService.CanCreate);
        Assert.False(authService.CanUpdate);
        Assert.False(authService.CanDelete);
        Assert.False(authService.CanExecute);
        Assert.False(authService.IsAdmin);
        Assert.Equal(2, authService.AvailableProviders.Count);
        Assert.Equal(2, authService.GetAvailableProviders().Count);
        Assert.NotNull(authService.ActiveProvider);
        Assert.NotNull(authService.CurrentPermissions);

        // Refresh when unauthenticated is a no-op
        await authService.RefreshCurrentUserPermissionsAsync();

        // Auto-login fails when active provider does not support auto-login
        var autoFail = await authService.TryAutoLoginAsync();
        Assert.False(autoFail.Succeeded);

        // Interactive login with providerOverride succeeds
        IAuthenticationService iAuth = authService;
        var loginRes = await iAuth.LoginAsync("alice", "pw123", "ApiBasic");
        Assert.True(loginRes.Succeeded);
        Assert.True(authService.IsAuthenticated);
        Assert.True(authService.IsAdmin);
        Assert.True(authService.CanCreate);
        Assert.True(authService.CanUpdate);
        Assert.True(authService.CanDelete);
        Assert.True(authService.CanExecute);

        // TryAutoLogin when already authenticated returns current user immediately
        var autoAlready = await authService.TryAutoLoginAsync();
        Assert.True(autoAlready.Succeeded);
        Assert.Equal("alice", autoAlready.User?.Username);

        var authState = await authService.GetAuthenticationStateAsync();
        Assert.Equal("alice", authState.User.Identity?.Name);
        Assert.True(authState.User.IsInRole("Admin"));
        Assert.Equal("True", authState.User.FindFirstValue("is_admin"));

        // Refresh permissions downgrades alice to execution-only from API
        await authService.RefreshCurrentUserPermissionsAsync();
        Assert.False(authService.IsAdmin);
        Assert.False(authService.CanCreate);
        Assert.True(authService.CanExecute);
        Assert.True(authService.CanRead);

        var nonAdminState = await authService.GetAuthenticationStateAsync();
        Assert.False(nonAdminState.User.IsInRole("Admin"));

        // Switch provider to Windows and test auto-login after LogoutAsync
        await iAuth.LogoutAsync();
        Assert.False(authService.IsAuthenticated);

        authService.SetActiveProvider("  ");
        authService.SetActiveProvider("NonExistent");
        authService.SetActiveProvider("Windows");
        Assert.Equal("Windows", authService.ActiveProviderName);
        var autoOk = await authService.TryAutoLoginAsync();
        Assert.True(autoOk.Succeeded);
        Assert.True(authService.IsAuthenticated);
    }

    [Fact]
    public async Task FlowApiPassthroughAuthProvider_HandlesAllResponsesAndErrors()
    {
        var apiClient = CreateApiClient();
        HttpResponseMessage nextResp = FakeHttpMessageHandler.JsonResponse("{}", HttpStatusCode.Unauthorized);
        bool throwOnSend = false;

        var factory = new ConfigurableHttpClientFactory(_ =>
        {
            if (throwOnSend) throw new HttpRequestException("Connection refused");
            return nextResp;
        });

        var provider = new FlowApiPassthroughAuthProvider(factory, apiClient);

        Assert.Equal("ApiBasic", provider.ProviderName);
        Assert.NotEmpty(provider.DisplayName);
        Assert.NotEmpty(provider.Description);
        Assert.False(provider.SupportsAutoLogin);
        Assert.True(((IUiAuthProvider)provider).RequiresPasswordPrompt);

        // Empty credentials
        var emptyRes = await provider.AuthenticateAsync(new AuthRequest { Username = "", Password = "" });
        Assert.False(emptyRes.Succeeded);
        Assert.Contains("Username and password are required", emptyRes.ErrorMessage);

        // 401 Unauthorized
        var unauthRes = await provider.AuthenticateAsync(new AuthRequest { Username = "bob", Password = "bad" });
        Assert.False(unauthRes.Succeeded);
        Assert.Contains("rejected credentials", unauthRes.ErrorMessage);

        // 200 OK but invalid JSON / missing fields
        nextResp = FakeHttpMessageHandler.JsonResponse("not-json", HttpStatusCode.OK);
        var invalidJsonRes = await provider.AuthenticateAsync(new AuthRequest { Username = "bob", Password = "pw" });
        Assert.False(invalidJsonRes.Succeeded);
        Assert.Contains("Invalid username or password", invalidJsonRes.ErrorMessage);

        // 200 OK with valid authenticated_as and permission
        nextResp = FakeHttpMessageHandler.JsonResponse("""{"authenticated_as":"bob","permission":"write"}""", HttpStatusCode.OK);
        var okRes = await provider.AuthenticateAsync(new AuthRequest { Username = "bob", Password = "pw" });
        Assert.True(okRes.Succeeded);
        Assert.Equal("bob", okRes.User?.Username);
        Assert.True(okRes.User?.CanCreate);

        // Network exception
        throwOnSend = true;
        var exRes = await provider.AuthenticateAsync(new AuthRequest { Username = "bob", Password = "pw" });
        Assert.False(exRes.Succeeded);
        Assert.Contains("Could not reach go-flowAPI", exRes.ErrorMessage);
    }

    [Fact]
    public async Task WindowsIntegratedAndCredentialsProviders_ResolveIdentity_Groups_AndAuthenticate()
    {
        string healthJson = """{"status":"ok","authenticated_as":"CORP\\alice","permission":"all"}""";
        HttpStatusCode healthStatus = HttpStatusCode.OK;

        var apiClient = CreateApiClient(new FakeHttpMessageHandler(_ =>
            FakeHttpMessageHandler.JsonResponse(healthJson, healthStatus)));
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };

        var winProvider = new WindowsIntegratedUiAuthProvider(apiClient, accessor);
        Assert.Equal("Windows", winProvider.ProviderName);
        Assert.NotEmpty(winProvider.DisplayName);
        Assert.NotEmpty(winProvider.Description);
        Assert.True(winProvider.SupportsAutoLogin);

        var credProvider = new WindowsCredentialsUiAuthProvider(apiClient, accessor);
        Assert.Equal("WindowsCredentials", credProvider.ProviderName);
        Assert.NotEmpty(credProvider.DisplayName);
        Assert.NotEmpty(credProvider.Description);
        Assert.False(credProvider.SupportsAutoLogin);

        // WindowsCredentials requires username & password
        var missingUser = await credProvider.AuthenticateAsync(new AuthRequest { Username = "", Password = "pw" });
        Assert.False(missingUser.Succeeded);
        Assert.Contains("Windows username", missingUser.ErrorMessage);

        var missingPw = await credProvider.AuthenticateAsync(new AuthRequest { Username = "CORP\\alice", Password = "" });
        Assert.False(missingPw.Succeeded);
        Assert.Contains("Windows password is required", missingPw.ErrorMessage);

        // Test GetDefaultDetectedIdentity with env vars and OS fallback
        var origWinUser = Environment.GetEnvironmentVariable("FLOW_WINDOWS_USER");
        var origHostDom = Environment.GetEnvironmentVariable("HOST_USERDOMAIN");
        var origHostUser = Environment.GetEnvironmentVariable("HOST_USERNAME");
        var origGroups = Environment.GetEnvironmentVariable("FLOW_WINDOWS_GROUPS");
        try
        {
            Environment.SetEnvironmentVariable("FLOW_WINDOWS_USER", "ENV_DOM\\env_user");
            Assert.Equal("ENV_DOM\\env_user", WindowsIntegratedUiAuthProvider.GetDefaultDetectedIdentity());

            Environment.SetEnvironmentVariable("FLOW_WINDOWS_USER", null);
            Environment.SetEnvironmentVariable("HOST_USERDOMAIN", "HOST_DOM");
            Environment.SetEnvironmentVariable("HOST_USERNAME", "host_user");
            Assert.Equal("HOST_DOM\\host_user", WindowsIntegratedUiAuthProvider.GetDefaultDetectedIdentity());

            Environment.SetEnvironmentVariable("HOST_USERDOMAIN", null);
            Assert.Equal("host_user", WindowsIntegratedUiAuthProvider.GetDefaultDetectedIdentity());

            Environment.SetEnvironmentVariable("HOST_USERNAME", null);
            var osIdentity = WindowsIntegratedUiAuthProvider.GetDefaultDetectedIdentity();
            Assert.NotEmpty(osIdentity);

            // Authenticate with ambient OS identity + FLOW_WINDOWS_GROUPS
            Environment.SetEnvironmentVariable("FLOW_WINDOWS_GROUPS", "CORP\\GroupA;CORP\\GroupB,GroupC");
            var ambientOk = await winProvider.AuthenticateAsync(new AuthRequest());
            Assert.True(ambientOk.Succeeded);
            Assert.Equal("CORP\\alice", ambientOk.User?.Username);
            Assert.True(ambientOk.User?.IsAdmin);

            // Authenticate with explicit DOMAIN\user + password (exercises LogonUser fallback + explicit username)
            var explicitOk = await credProvider.AuthenticateAsync(new AuthRequest
            {
                Username = "CORP\\alice",
                Password = "dummy_password_for_test"
            });
            Assert.True(explicitOk.Succeeded);

            // Unauthenticated response from API (missing authenticated_as)
            healthJson = """{"status":"ok"}""";
            var unauth = await winProvider.AuthenticateAsync(new AuthRequest { Username = "__AUTO_HOST__" });
            Assert.False(unauth.Succeeded);
            Assert.Contains("Host Windows Identity", unauth.ErrorMessage);

            // Offline API response
            healthStatus = HttpStatusCode.ServiceUnavailable;
            var offline = await winProvider.AuthenticateAsync(new AuthRequest { Username = "CORP\\alice" });
            Assert.False(offline.Succeeded);
            Assert.Contains("Could not reach go-flowAPI", offline.ErrorMessage);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FLOW_WINDOWS_USER", origWinUser);
            Environment.SetEnvironmentVariable("HOST_USERDOMAIN", origHostDom);
            Environment.SetEnvironmentVariable("HOST_USERNAME", origHostUser);
            Environment.SetEnvironmentVariable("FLOW_WINDOWS_GROUPS", origGroups);
        }
    }

    [Fact]
    public void AppConfig_ResolvesUrlsAndSchemesFromEnvironment()
    {
        var origUrl = Environment.GetEnvironmentVariable(AppConfig.EnvApiUrl);
        var origHost = Environment.GetEnvironmentVariable(AppConfig.EnvApiHost);
        var origPort = Environment.GetEnvironmentVariable(AppConfig.EnvApiPort);
        var origScheme = Environment.GetEnvironmentVariable(AppConfig.EnvUiScheme);

        var resolveUrlMethod = typeof(AppConfig).GetMethod("ResolveApiBaseUrl", BindingFlags.Static | BindingFlags.NonPublic)!;
        var resolveSchemeMethod = typeof(AppConfig).GetMethod("ResolveUiScheme", BindingFlags.Static | BindingFlags.NonPublic)!;

        try
        {
            Environment.SetEnvironmentVariable(AppConfig.EnvApiUrl, "http://api.local:9000/");
            Assert.Equal("http://api.local:9000", (string)resolveUrlMethod.Invoke(null, null)!);

            Environment.SetEnvironmentVariable(AppConfig.EnvApiUrl, null);
            Environment.SetEnvironmentVariable(AppConfig.EnvApiHost, "myhost");
            Environment.SetEnvironmentVariable(AppConfig.EnvApiPort, "8085");
            Assert.Equal("http://myhost:8085", (string)resolveUrlMethod.Invoke(null, null)!);

            Environment.SetEnvironmentVariable(AppConfig.EnvApiHost, "https://securehost/");
            Environment.SetEnvironmentVariable(AppConfig.EnvApiPort, "");
            Assert.Equal("https://securehost", (string)resolveUrlMethod.Invoke(null, null)!);

            Environment.SetEnvironmentVariable(AppConfig.EnvUiScheme, "http");
            Assert.Equal("http", (string)resolveSchemeMethod.Invoke(null, null)!);

            Environment.SetEnvironmentVariable(AppConfig.EnvUiScheme, "https");
            Assert.Equal("https", (string)resolveSchemeMethod.Invoke(null, null)!);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppConfig.EnvApiUrl, origUrl);
            Environment.SetEnvironmentVariable(AppConfig.EnvApiHost, origHost);
            Environment.SetEnvironmentVariable(AppConfig.EnvApiPort, origPort);
            Environment.SetEnvironmentVariable(AppConfig.EnvUiScheme, origScheme);
        }
    }

    private sealed class ConfigurableHttpClientFactory : IHttpClientFactory
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public ConfigurableHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public HttpClient CreateClient(string name) =>
            new(new FakeHttpMessageHandler(_responder))
            {
                BaseAddress = new Uri("http://localhost:8080/")
            };
    }
}
