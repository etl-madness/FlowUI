using FlowUIv2.Models;

namespace FlowUIv2.Services.Auth;

/// <summary>
/// Central UI authentication and RBAC authorization service.
/// Coordinates pluggable IUiAuthProvider implementations, manages active session state,
/// enforces CRUD + Execute permissions, and logs authentication events to dbo.flow_ui_events.
/// </summary>
public interface IAuthenticationService
{
    AppUser? CurrentUser { get; }
    bool IsAuthenticated { get; }
    CrudPermissions Permissions { get; }
    CrudPermissions CurrentPermissions => Permissions;
    string ActiveProviderName { get; }
    IUiAuthProvider ActiveProvider { get; }
    IReadOnlyList<IUiAuthProvider> AvailableProviders { get; }
    IReadOnlyList<IUiAuthProvider> GetAvailableProviders() => AvailableProviders;

    bool CanCreate { get; }
    bool CanRead { get; }
    bool CanUpdate { get; }
    bool CanDelete { get; }
    bool CanExecute { get; }
    bool IsAdmin { get; }

    event Action? OnAuthStateChanged;

    void SetActiveProvider(string providerName);
    Task<AuthResult> LoginAsync(string username, string password, string? providerOverride = null, CancellationToken ct = default);
    Task<AuthResult> TryAutoLoginAsync(CancellationToken ct = default);
    Task LogoutAsync(CancellationToken ct = default);
    Task RefreshCurrentUserPermissionsAsync(CancellationToken ct = default);
}
