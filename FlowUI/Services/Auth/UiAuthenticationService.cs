using System.Security.Claims;
using FlowUI.Config;
using FlowUI.Models;
using Microsoft.AspNetCore.Components.Authorization;

namespace FlowUI.Services.Auth;

/// <summary>
/// Default implementation of IAuthenticationService and Blazor AuthenticationStateProvider.
/// Allows seamless switching between go-flowAPI Basic Auth and Windows Integrated providers.
/// </summary>
public sealed class UiAuthenticationService : AuthenticationStateProvider, IAuthenticationService
{
    private readonly IReadOnlyList<IUiAuthProvider> _providers;
    private readonly FlowApiClient _apiClient;
    private IUiAuthProvider _activeProvider;
    private AppUser? _currentUser;

    public UiAuthenticationService(IEnumerable<IUiAuthProvider> providers, FlowApiClient apiClient)
    {
        _providers = providers.ToList();
        _apiClient = apiClient;

        _activeProvider = _providers.FirstOrDefault(p =>
            string.Equals(p.ProviderName, AppConfig.AuthMode, StringComparison.OrdinalIgnoreCase))
            ?? _providers.First();
    }

    public AppUser? CurrentUser => _currentUser;
    public bool IsAuthenticated => _currentUser is not null && _currentUser.IsActive;
    public CrudPermissions Permissions => _currentUser?.GetPermissions() ?? new CrudPermissions { CanRead = false };
    public CrudPermissions CurrentPermissions => Permissions;
    public string ActiveProviderName => _activeProvider.ProviderName;
    public IUiAuthProvider ActiveProvider => _activeProvider;
    public IReadOnlyList<IUiAuthProvider> AvailableProviders => _providers;
    public IReadOnlyList<IUiAuthProvider> GetAvailableProviders() => _providers;

    public bool CanCreate => IsAuthenticated && Permissions.CanCreate;
    public bool CanRead => IsAuthenticated && Permissions.CanRead;
    public bool CanUpdate => IsAuthenticated && Permissions.CanUpdate;
    public bool CanDelete => IsAuthenticated && Permissions.CanDelete;
    public bool CanExecute => IsAuthenticated && Permissions.CanExecute;
    public bool IsAdmin => IsAuthenticated && Permissions.IsAdmin;

    public event Action? OnAuthStateChanged;

    public void SetActiveProvider(string providerName)
    {
        var match = _providers.FirstOrDefault(p =>
            string.Equals(p.ProviderName, providerName, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            _activeProvider = match;
            NotifyStateChanged();
        }
    }

    public async Task<AuthResult> TryAutoLoginAsync(CancellationToken ct = default)
    {
        if (IsAuthenticated && _currentUser is not null)
        {
            return AuthResult.Success(_currentUser, _activeProvider.ProviderName);
        }

        if (_activeProvider.SupportsAutoLogin)
        {
            return await LoginInternalAsync(new AuthRequest(), _activeProvider, ct);
        }

        return AuthResult.Fail("Interactive login required.", _activeProvider.ProviderName);
    }

    public async Task<AuthResult> LoginAsync(
        string username,
        string password,
        string? providerOverride = null,
        CancellationToken ct = default)
    {
        var provider = _activeProvider;
        if (!string.IsNullOrWhiteSpace(providerOverride))
        {
            var found = _providers.FirstOrDefault(p =>
                string.Equals(p.ProviderName, providerOverride, StringComparison.OrdinalIgnoreCase));
            if (found is not null)
            {
                provider = found;
                _activeProvider = found;
            }
        }

        var req = new AuthRequest
        {
            Username = username?.Trim() ?? string.Empty,
            Password = password ?? string.Empty
        };

        return await LoginInternalAsync(req, provider, ct);
    }

    private async Task<AuthResult> LoginInternalAsync(
        AuthRequest req,
        IUiAuthProvider provider,
        CancellationToken ct)
    {
        var result = await provider.AuthenticateAsync(req, ct);

        if (result.Succeeded && result.User is not null)
        {
            _currentUser = result.User;
            NotifyStateChanged();
        }

        return result;
    }

    public Task LogoutAsync(CancellationToken ct = default)
    {
        _currentUser = null;
        _apiClient.ClearAuth();
        NotifyStateChanged();
        return Task.CompletedTask;
    }

    public async Task RefreshCurrentUserPermissionsAsync(CancellationToken ct = default)
    {
        if (_currentUser is null) return;
        try
        {
            var apiUsers = await _apiClient.GetApiUsersAsync(ct);
            var matched = apiUsers.FirstOrDefault(u =>
                string.Equals(u.Username, _currentUser.Username, StringComparison.OrdinalIgnoreCase));
            if (matched is not null)
            {
                _currentUser = FlowApiClient.MapApiPermissionToUser(
                    matched.Username,
                    matched.Permission,
                    _currentUser.AuthProvider,
                    matched.Description,
                    matched.IsActive);
                NotifyStateChanged();
            }
        }
        catch
        {
            // Ignore if current user is not an API admin
        }
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (_currentUser is null || !_currentUser.IsActive)
        {
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
        }

        var perms = _currentUser.GetPermissions();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, _currentUser.Username),
            new("auth_provider", _currentUser.AuthProvider),
            new("can_create", perms.CanCreate.ToString()),
            new("can_read", perms.CanRead.ToString()),
            new("can_update", perms.CanUpdate.ToString()),
            new("can_delete", perms.CanDelete.ToString()),
            new("can_execute", perms.CanExecute.ToString()),
            new("is_admin", perms.IsAdmin.ToString())
        };

        if (perms.IsAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        }

        var identity = new ClaimsIdentity(claims, _currentUser.AuthProvider);
        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
    }

    private void NotifyStateChanged()
    {
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        OnAuthStateChanged?.Invoke();
    }
}
