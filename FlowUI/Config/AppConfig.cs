namespace FlowUI.Config;

/// <summary>
/// Application configuration constants and environment-derived settings.
/// FlowUI communicates exclusively with go-flowAPI (no direct database connection).
/// </summary>
public static class AppConfig
{
    // Compile-time default constants
    public const string DefaultApiSchemeAndHost = "";
    public const string DefaultApiPort = "";
    public const string DefaultApiBaseUrl = "";
    public const string DefaultApiBasicAuth = "";
    public const string DefaultUiPort = "";

    public const string DefaultAuthMode = "ApiBasic"; // Options: ApiBasic, Windows
    public const string DefaultAdminUser = "";
    public const string DefaultAdminPassword = "";

    // Environment variable names
    public const string EnvApiUrl = "FLOW_API_URL";
    public const string EnvApiHost = "FLOW_API_HOST";
    public const string EnvApiPort = "FLOW_API_PORT";
    public const string EnvApiBasicAuth = "FLOW_API_BASIC_AUTH";
    public const string EnvUiPort = "FLOW_UI_PORT";
    public const string EnvUiAuthMode = "FLOW_UI_AUTH_MODE";
    public const string EnvUiAdminUser = "FLOW_UI_ADMIN_USER";
    public const string EnvUiAdminPassword = "FLOW_UI_ADMIN_PASSWORD";

    // Environment-derived readonly values
    public static readonly string ApiPort = ResolveEnv(EnvApiPort, DefaultApiPort);

    public static readonly string UiPort = ResolveEnv(EnvUiPort, DefaultUiPort);

    public static readonly string ApiHost = ResolveEnv(EnvApiHost, DefaultApiSchemeAndHost).TrimEnd('/');

    public static readonly string ApiBaseUrl = ResolveApiBaseUrl();

    public static readonly string ApiBasicAuthToken = ResolveEnv(EnvApiBasicAuth, DefaultApiBasicAuth);

    public static readonly string AuthMode = ResolveEnv(EnvUiAuthMode, DefaultAuthMode);

    public static readonly string AdminUser = ResolveEnv(EnvUiAdminUser, DefaultAdminUser);

    public static readonly string AdminPassword = ResolveEnv(EnvUiAdminPassword, DefaultAdminPassword);

    private static string ResolveEnv(string envName, string fallback)
    {
        var val = Environment.GetEnvironmentVariable(envName);
        return string.IsNullOrWhiteSpace(val) ? fallback : val.Trim();
    }

    private static string ResolveApiBaseUrl()
    {
        var explicitUrl = Environment.GetEnvironmentVariable(EnvApiUrl);
        if (!string.IsNullOrWhiteSpace(explicitUrl))
        {
            return explicitUrl.Trim().TrimEnd('/');
        }

        var host = ResolveEnv(EnvApiHost, DefaultApiSchemeAndHost).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(host))
        {
            return DefaultApiBaseUrl;
        }

        if (!host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            host = "http://" + host;
        }

        var port = ResolveEnv(EnvApiPort, DefaultApiPort);
        return string.IsNullOrWhiteSpace(port) ? host : $"{host}:{port}";
    }
}
