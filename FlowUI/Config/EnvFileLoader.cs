namespace FlowUI.Config;

/// <summary>
/// Loads KEY=VALUE environment files (such as /etc/flow-ui/flow-ui.env or
/// C:\ProgramData\ETL Madness\Flow UI\flow-ui.env) prior to initializing
/// <see cref="AppConfig"/> and ASP.NET Core hosting.
/// </summary>
public static class EnvFileLoader
{
    public const string EnvFileVarName = "FLOW_UI_ENV_FILE";

    /// <summary>
    /// Extracts FlowUI-specific CLI flags (--env-file / -env-file, --service-name / -service-name)
    /// and returns the remaining arguments to forward to ASP.NET Core.
    /// </summary>
    public static string[] ExtractHostArgs(
        string[] args,
        out string? envFilePath,
        out string? serviceName)
    {
        envFilePath = null;
        serviceName = null;
        var forwarded = new List<string>(args.Length);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "--env-file" or "-env-file")
            {
                if (i + 1 < args.Length)
                {
                    envFilePath = args[++i];
                }
                continue;
            }
            if (arg.StartsWith("--env-file=", StringComparison.Ordinal))
            {
                envFilePath = arg["--env-file=".Length..];
                continue;
            }
            if (arg.StartsWith("-env-file=", StringComparison.Ordinal))
            {
                envFilePath = arg["-env-file=".Length..];
                continue;
            }
            if (arg is "--service-name" or "-service-name")
            {
                if (i + 1 < args.Length)
                {
                    serviceName = args[++i];
                }
                continue;
            }
            if (arg.StartsWith("--service-name=", StringComparison.Ordinal))
            {
                serviceName = arg["--service-name=".Length..];
                continue;
            }
            if (arg.StartsWith("-service-name=", StringComparison.Ordinal))
            {
                serviceName = arg["-service-name=".Length..];
                continue;
            }

            forwarded.Add(arg);
        }

        if (string.IsNullOrWhiteSpace(envFilePath))
        {
            var fromEnv = Environment.GetEnvironmentVariable(EnvFileVarName);
            if (!string.IsNullOrWhiteSpace(fromEnv))
            {
                envFilePath = fromEnv.Trim();
            }
        }

        return forwarded.ToArray();
    }

    /// <summary>
    /// Parses KEY=VALUE lines into a dictionary. Supports blank lines, # comments,
    /// optional leading "export ", and single- or double-quoted values.
    /// </summary>
    public static Dictionary<string, string> ParseLines(IEnumerable<string> lines)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line["export ".Length..].TrimStart();
            }

            var eqIndex = line.IndexOf('=');
            if (eqIndex <= 0)
            {
                continue;
            }

            var key = line[..eqIndex].Trim();
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            var value = line[(eqIndex + 1)..].Trim();
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') ||
                 (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }
            else
            {
                var commentIdx = value.IndexOf(" #", StringComparison.Ordinal);
                if (commentIdx >= 0)
                {
                    value = value[..commentIdx].TrimEnd();
                }
            }

            result[key] = value;
        }

        return result;
    }

    /// <summary>
    /// Loads variables from <paramref name="filePath"/> into the current process environment.
    /// Existing non-empty environment variables are preserved unless <paramref name="overwriteExisting"/> is true.
    /// </summary>
    public static int LoadFile(string filePath, bool overwriteExisting = false)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return 0;
        }

        var parsed = ParseLines(File.ReadLines(filePath));
        var loaded = 0;

        foreach (var kvp in parsed)
        {
            if (!overwriteExisting && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(kvp.Key)))
            {
                continue;
            }

            Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
            loaded++;
        }

        return loaded;
    }
}
