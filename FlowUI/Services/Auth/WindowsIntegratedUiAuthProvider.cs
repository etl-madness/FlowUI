using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using FlowUI.Models;

namespace FlowUI.Services.Auth;

/// <summary>
/// Windows Integrated authentication provider.
/// Resolves the active Windows / OS identity (DOMAIN\user) and all AD / Windows group memberships (DOMAIN\group),
/// and authenticates against go-flowAPI ([dbo].[flow_api_users]). Also supports optional Windows password entry.
/// </summary>
public class WindowsIntegratedUiAuthProvider : IUiAuthProvider
{
    private const int LOGON32_LOGON_NETWORK = 3;
    private const int LOGON32_PROVIDER_DEFAULT = 0;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LogonUser(
        string lpszUsername,
        string? lpszDomain,
        string lpszPassword,
        int dwLogonType,
        int dwLogonProvider,
        out IntPtr phToken);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    protected readonly FlowApiClient _apiClient;
    protected readonly IHttpContextAccessor _httpContextAccessor;

    public WindowsIntegratedUiAuthProvider(FlowApiClient apiClient, IHttpContextAccessor httpContextAccessor)
    {
        _apiClient = apiClient;
        _httpContextAccessor = httpContextAccessor;
    }

    public virtual string ProviderName => "Windows";
    public virtual string DisplayName => "Windows Integrated Identity (OS / AD User or Group)";
    public virtual string Description => "Uses the active Windows identity (DOMAIN\\User) and AD/Windows group memberships (DOMAIN\\Group) to authenticate against [dbo].[flow_api_users] via go-flowAPI.";
    public virtual bool SupportsAutoLogin => true;

    public async Task<AuthResult> AuthenticateAsync(AuthRequest request, CancellationToken ct = default)
    {
        if (!SupportsAutoLogin && string.IsNullOrWhiteSpace(request.Username))
        {
            return AuthResult.Fail("Windows username (e.g. DOMAIN\\user) is required.", ProviderName);
        }

        if (!SupportsAutoLogin && string.IsNullOrEmpty(request.Password))
        {
            return AuthResult.Fail("Windows password is required.", ProviderName);
        }

        var (resolvedUser, groups, logonError) = ResolveWindowsIdentityAndGroups(request.Username, request.Password);
        if (!string.IsNullOrWhiteSpace(logonError))
        {
            return AuthResult.Fail(logonError, ProviderName);
        }

        if (string.IsNullOrWhiteSpace(resolvedUser))
        {
            return AuthResult.Fail("Unable to determine Windows OS identity.", ProviderName);
        }

        // Configure the scoped FlowApiClient to send the Windows user, groups, and optional password to go-flowAPI
        _apiClient.SetWindowsIntegratedAuth(resolvedUser, groups, request.Password);

        var (isOnline, authenticatedAs, tokenPerm, statusSummary) = await _apiClient.GetAuthenticatedIdentityAsync(ct);
        if (!isOnline)
        {
            _apiClient.ClearAuth();
            return AuthResult.Fail($"Could not reach go-flowAPI ({statusSummary}).", ProviderName);
        }

        if (string.IsNullOrWhiteSpace(authenticatedAs) || string.IsNullOrWhiteSpace(tokenPerm))
        {
            _apiClient.ClearAuth();
            var displayTarget = string.Equals(resolvedUser, "__AUTO_HOST__", StringComparison.OrdinalIgnoreCase)
                ? "Host Windows Identity (ensure go-flowAPI is rebuilt/restarted or enter DOMAIN\\user below)"
                : resolvedUser;
            return AuthResult.Fail(
                $"Windows identity '{displayTarget}' (or one of its DOMAIN\\group memberships) was not found, had invalid credentials, or is inactive in [dbo].[flow_api_users].",
                ProviderName);
        }

        // Lock in the resolved canonical Windows username returned by go-flowAPI
        _apiClient.SetWindowsIntegratedAuth(authenticatedAs, groups, request.Password);

        var mappedUser = FlowApiClient.MapApiPermissionToUser(
            authenticatedAs,
            tokenPerm,
            ProviderName,
            $"{DisplayName} ({authenticatedAs}) — API permission: {tokenPerm}");

        return AuthResult.Success(mappedUser, ProviderName);
    }

    public static string GetDefaultDetectedIdentity()
    {
        var envWinUser = Environment.GetEnvironmentVariable("FLOW_WINDOWS_USER");
        if (!string.IsNullOrWhiteSpace(envWinUser))
        {
            return envWinUser.Trim();
        }

        var hostDomain = Environment.GetEnvironmentVariable("HOST_USERDOMAIN");
        var hostUser = Environment.GetEnvironmentVariable("HOST_USERNAME");
        if (!string.IsNullOrWhiteSpace(hostUser))
        {
            return !string.IsNullOrWhiteSpace(hostDomain)
                ? $"{hostDomain.Trim()}\\{hostUser.Trim()}"
                : hostUser.Trim();
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var currentWinIdentity = WindowsIdentity.GetCurrent();
                if (!string.IsNullOrWhiteSpace(currentWinIdentity?.Name))
                {
                    return currentWinIdentity.Name.Trim();
                }
            }
            catch
            {
                // Ignore
            }

            var domain = Environment.UserDomainName;
            var user = Environment.UserName;
            if (!string.IsNullOrWhiteSpace(user))
            {
                return !string.IsNullOrWhiteSpace(domain) ? $"{domain}\\{user}" : user;
            }
        }

        return string.Empty;
    }

    private (string User, List<string> Groups, string? Error) ResolveWindowsIdentityAndGroups(string? explicitUsername, string? password)
    {
        var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var envGroups = Environment.GetEnvironmentVariable("FLOW_WINDOWS_GROUPS");
        if (!string.IsNullOrWhiteSpace(envGroups))
        {
            foreach (var g in envGroups.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(g))
                {
                    groups.Add(g);
                }
            }
        }

        // When running on Windows and a Windows username + password are provided, attempt native Windows LogonUser
        // to verify AD/Windows credentials and extract token group SIDs.
        if (OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(explicitUsername) && !string.IsNullOrEmpty(password))
        {
            var trimmedExplicit = explicitUsername.Trim();
            string? domain = null;
            string userPart = trimmedExplicit;
            var slashIdx = trimmedExplicit.IndexOf('\\');
            if (slashIdx > 0 && slashIdx + 1 < trimmedExplicit.Length)
            {
                domain = trimmedExplicit[..slashIdx];
                userPart = trimmedExplicit[(slashIdx + 1)..];
            }

            if (LogonUser(userPart, domain, password, LOGON32_LOGON_NETWORK, LOGON32_PROVIDER_DEFAULT, out var tokenHandle) &&
                tokenHandle != IntPtr.Zero)
            {
                try
                {
                    using var logonIdentity = new WindowsIdentity(tokenHandle);
                    var canonicalName = !string.IsNullOrWhiteSpace(logonIdentity.Name)
                        ? logonIdentity.Name.Trim()
                        : trimmedExplicit;
                    ExtractWindowsGroups(logonIdentity, groups);
                    return (canonicalName, groups.ToList(), null);
                }
                finally
                {
                    CloseHandle(tokenHandle);
                }
            }
        }

        string resolvedUser = string.Empty;

        if (OperatingSystem.IsWindows())
        {
            try
            {
                var httpWinIdentity = _httpContextAccessor.HttpContext?.User?.Identity as WindowsIdentity;
                using var currentWinIdentity = httpWinIdentity is null ? WindowsIdentity.GetCurrent() : null;
                var activeWinIdentity = httpWinIdentity ?? currentWinIdentity;

                if (activeWinIdentity is not null)
                {
                    if (!string.IsNullOrWhiteSpace(activeWinIdentity.Name))
                    {
                        resolvedUser = activeWinIdentity.Name.Trim();
                    }

                    if (string.IsNullOrWhiteSpace(explicitUsername) ||
                        string.Equals(explicitUsername.Trim(), resolvedUser, StringComparison.OrdinalIgnoreCase))
                    {
                        ExtractWindowsGroups(activeWinIdentity, groups);
                    }
                }
            }
            catch
            {
                // Fallback to HttpContext or Environment identity below
            }

            if (!string.IsNullOrWhiteSpace(explicitUsername))
            {
                resolvedUser = explicitUsername.Trim();
            }
            else if (string.IsNullOrWhiteSpace(resolvedUser))
            {
                var domain = Environment.UserDomainName;
                var user = Environment.UserName;
                resolvedUser = !string.IsNullOrWhiteSpace(domain)
                    ? $"{domain}\\{user}"
                    : user;
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(explicitUsername))
            {
                resolvedUser = explicitUsername.Trim();
            }
            else
            {
                var defaultIdentity = GetDefaultDetectedIdentity();
                if (!string.IsNullOrWhiteSpace(defaultIdentity))
                {
                    resolvedUser = defaultIdentity;
                }
                else
                {
                    var httpIdentity = _httpContextAccessor.HttpContext?.User?.Identity;
                    if (httpIdentity is { IsAuthenticated: true } && !string.IsNullOrWhiteSpace(httpIdentity.Name))
                    {
                        resolvedUser = httpIdentity.Name.Trim();
                    }
                    else
                    {
                        resolvedUser = "__AUTO_HOST__";
                    }
                }
            }
        }

        return (resolvedUser, groups.ToList(), null);
    }

    [SupportedOSPlatform("windows")]
    private static void ExtractWindowsGroups(WindowsIdentity identity, HashSet<string> groups)
    {
        if (identity.Groups is null) return;
        foreach (var sid in identity.Groups)
        {
            try
            {
                var ntAccount = sid.Translate(typeof(NTAccount))?.Value;
                if (!string.IsNullOrWhiteSpace(ntAccount))
                {
                    var trimmedGroup = ntAccount.Trim();
                    groups.Add(trimmedGroup);

                    var slashIdx = trimmedGroup.LastIndexOf('\\');
                    if (slashIdx >= 0 && slashIdx + 1 < trimmedGroup.Length)
                    {
                        groups.Add(trimmedGroup[(slashIdx + 1)..]);
                    }
                }
            }
            catch
            {
                // Some built-in or orphaned SIDs cannot be translated to NTAccount; ignore
            }
        }
    }
}

/// <summary>
/// Interactive Windows / Active Directory Credentials provider (DOMAIN\user + password).
/// Prompts explicitly for a Windows username and password and resolves permissions against [dbo].[flow_api_users].
/// </summary>
public sealed class WindowsCredentialsUiAuthProvider : WindowsIntegratedUiAuthProvider
{
    public WindowsCredentialsUiAuthProvider(FlowApiClient apiClient, IHttpContextAccessor httpContextAccessor)
        : base(apiClient, httpContextAccessor)
    {
    }

    public override string ProviderName => "WindowsCredentials";
    public override string DisplayName => "Windows / AD Credentials (DOMAIN\\User + Password)";
    public override string Description => "Prompts for a Windows username (DOMAIN\\user) and password, resolving user and DOMAIN\\group permissions against [dbo].[flow_api_users] via go-flowAPI.";
    public override bool SupportsAutoLogin => false;
}
