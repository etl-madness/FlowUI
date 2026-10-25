using FlowUI.Models;

namespace FlowUI.Services.Auth;

/// <summary>
/// Pluggable authentication provider contract for FlowUI.
/// Implement this interface to swap or add custom authentication mechanisms
/// (Database, Windows Integrated / Active Directory, API Basic Auth Passthrough, OIDC/JWT, etc.).
/// </summary>
public interface IUiAuthProvider
{
    /// <summary>
    /// Unique identifier for this provider (e.g. "Database", "Windows", "ApiBasic").
    /// Matches AppConfig.AuthMode / FLOW_UI_AUTH_MODE.
    /// </summary>
    string ProviderName
    {
        get;
    }

    /// <summary>
    /// Human-readable display name shown in the Studio UI.
    /// </summary>
    string DisplayName
    {
        get;
    }

    /// <summary>
    /// Description of how this authentication provider validates users.
    /// </summary>
    string Description
    {
        get;
    }

    /// <summary>
    /// Whether this provider requires interactive username/password input,
    /// or can auto-authenticate from ambient OS/HTTP context.
    /// </summary>
    bool SupportsAutoLogin
    {
        get;
    }

    /// <summary>
    /// True if the provider requires a password prompt (inverse of SupportsAutoLogin).
    /// </summary>
    bool RequiresPasswordPrompt => !SupportsAutoLogin;

    /// <summary>
    /// Authenticates the user request and returns an AuthResult with resolved CRUD permissions.
    /// </summary>
    Task<AuthResult> AuthenticateAsync(AuthRequest request, CancellationToken ct = default);
}
