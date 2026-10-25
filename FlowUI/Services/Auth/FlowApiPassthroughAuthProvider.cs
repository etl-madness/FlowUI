using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FlowUI.Config;
using FlowUI.Models;

namespace FlowUI.Services.Auth;

/// <summary>
/// Authenticates UI users directly against go-flowAPI using HTTP Basic Auth (/api/v1/health)
/// and maps their go-flowAPI permission ("read-only", "execution-only", "upload", "write", "all")
/// to UI CRUD + Execute permissions.
/// </summary>
public sealed class FlowApiPassthroughAuthProvider : IUiAuthProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FlowApiClient _apiClient;

    public FlowApiPassthroughAuthProvider(IHttpClientFactory httpClientFactory, FlowApiClient apiClient)
    {
        _httpClientFactory = httpClientFactory;
        _apiClient = apiClient;
    }

    public string ProviderName => "ApiBasic";
    public string DisplayName => "go-flowAPI Basic Auth";
    public string Description => "Validates username/password directly against go-flowAPI (/api/v1/health) via HTTP Basic Auth and maps API permissions.";
    public bool SupportsAutoLogin => false;

    public async Task<AuthResult> AuthenticateAsync(AuthRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            return AuthResult.Fail("Username and password are required for API Basic Auth.", ProviderName);
        }

        var username = request.Username.Trim();
        var rawCreds = $"{username}:{request.Password}";
        var base64Token = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawCreds));

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(AppConfig.ApiBaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64Token);

            // Verify credentials against GET /api/v1/health which returns {"authenticated_as": "...", "permission": "..."}
            using var response = await client.GetAsync("/api/v1/health", ct);
            if (!response.IsSuccessStatusCode)
            {
                return AuthResult.Fail($"go-flowAPI rejected credentials (HTTP {(int)response.StatusCode}).", ProviderName);
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            string? apiPerm = null;
            string? authenticatedAs = null;

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("permission", out var permElem) && permElem.ValueKind == JsonValueKind.String)
                {
                    apiPerm = permElem.GetString();
                }
                if (doc.RootElement.TryGetProperty("authenticated_as", out var authElem) && authElem.ValueKind == JsonValueKind.String)
                {
                    authenticatedAs = authElem.GetString();
                }
            }
            catch
            {
                // Fallback if health response has no JSON permission field
            }

            if (string.IsNullOrWhiteSpace(authenticatedAs) || string.IsNullOrWhiteSpace(apiPerm))
            {
                return AuthResult.Fail("Invalid username or password rejected by go-flowAPI.", ProviderName);
            }

            // Configure the scoped FlowApiClient to use the authenticated user's Basic Auth token for all API calls
            _apiClient.SetBasicAuthToken(base64Token);

            var apiUser = FlowApiClient.MapApiPermissionToUser(
                authenticatedAs,
                apiPerm,
                ProviderName,
                $"Authenticated via go-flowAPI Basic Auth (permission: {apiPerm})");

            return AuthResult.Success(apiUser, ProviderName);
        }
        catch (Exception ex)
        {
            return AuthResult.Fail($"Could not reach go-flowAPI at {AppConfig.ApiBaseUrl}: {ex.Message}", ProviderName);
        }
    }
}
