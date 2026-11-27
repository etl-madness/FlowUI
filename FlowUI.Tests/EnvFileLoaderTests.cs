using FlowUI.Config;

namespace FlowUI.Tests;

public class EnvFileLoaderTests
{
    private static readonly string[] ExpectedForwardedArgs = ["--urls", "http://127.0.0.1:5185", "--contentRoot=/opt/flow-ui/app"];
    private static readonly string[] ExpectedFallbackArgs = ["--urls", "http://localhost:5185"];
    [Fact]
    public void ParseLines_HandlesCommentsQuotesExportAndInlineComments()
    {
        var lines = new[]
        {
            "# Leading comment",
            "",
            "   ",
            "FLOW_API_URL=\"http://localhost:8080\"",
            "FLOW_UI_AUTH_MODE='Windows'",
            "export FLOW_UI_PORT=5185 # inline comment",
            "ASPNETCORE_URLS=http://0.0.0.0:5185",
            "MALFORMED_LINE_WITHOUT_EQUALS",
            "=EMPTY_KEY"
        };

        var parsed = EnvFileLoader.ParseLines(lines);

        Assert.Equal(4, parsed.Count);
        Assert.Equal("http://localhost:8080", parsed["FLOW_API_URL"]);
        Assert.Equal("Windows", parsed["FLOW_UI_AUTH_MODE"]);
        Assert.Equal("5185", parsed["FLOW_UI_PORT"]);
        Assert.Equal("http://0.0.0.0:5185", parsed["ASPNETCORE_URLS"]);
    }

    [Fact]
    public void ExtractHostArgs_ExtractsEnvFileAndServiceNameAndForwardsOthers()
    {
        var args = new[]
        {
            "--env-file", "/etc/flow-ui/flow-ui.env",
            "--service-name=CustomFlowUI",
            "--urls", "http://127.0.0.1:5185",
            "--contentRoot=/opt/flow-ui/app"
        };

        var forwarded = EnvFileLoader.ExtractHostArgs(args, out var envFile, out var serviceName);

        Assert.Equal("/etc/flow-ui/flow-ui.env", envFile);
        Assert.Equal("CustomFlowUI", serviceName);
        Assert.Equal(ExpectedForwardedArgs, forwarded);
    }

    [Fact]
    public void LoadFile_PreservesExistingEnvVarsByDefault()
    {
        var tempFile = Path.GetTempFileName();
        var keyExisting = "FLOWUI_TEST_EXISTING_" + Guid.NewGuid().ToString("N");
        var keyNew = "FLOWUI_TEST_NEW_" + Guid.NewGuid().ToString("N");

        try
        {
            Environment.SetEnvironmentVariable(keyExisting, "from_process");
            Environment.SetEnvironmentVariable(keyNew, null);

            File.WriteAllLines(tempFile, new[]
            {
                $"{keyExisting}=\"from_file\"",
                $"{keyNew}=\"loaded_value\""
            });

            var loaded = EnvFileLoader.LoadFile(tempFile, overwriteExisting: false);

            Assert.Equal(1, loaded);
            Assert.Equal("from_process", Environment.GetEnvironmentVariable(keyExisting));
            Assert.Equal("loaded_value", Environment.GetEnvironmentVariable(keyNew));
        }
        finally
        {
            Environment.SetEnvironmentVariable(keyExisting, null);
            Environment.SetEnvironmentVariable(keyNew, null);
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void LoadFile_ReturnsZeroWhenFileMissing()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), "nonexistent-" + Guid.NewGuid().ToString("N") + ".env");
        Assert.Equal(0, EnvFileLoader.LoadFile(missingPath));
    }

    [Theory]
    [InlineData(new[] { "--env-file=/a.env", "-service-name=SvcA" }, "/a.env", "SvcA")]
    [InlineData(new[] { "-env-file=/b.env", "--service-name", "SvcB" }, "/b.env", "SvcB")]
    [InlineData(new[] { "-env-file", "/c.env", "-service-name", "SvcC" }, "/c.env", "SvcC")]
    public void ExtractHostArgs_HandlesAllFlagVariants(string[] args, string expectedEnv, string expectedSvc)
    {
        var forwarded = EnvFileLoader.ExtractHostArgs(args, out var envFile, out var serviceName);
        Assert.Equal(expectedEnv, envFile);
        Assert.Equal(expectedSvc, serviceName);
        Assert.Empty(forwarded);
    }

    [Fact]
    public void ExtractHostArgs_FallsBackToEnvironmentVariable_AndLoadFileOverwritesWhenRequested()
    {
        var prev = Environment.GetEnvironmentVariable(EnvFileLoader.EnvFileVarName);
        var key = "FLOWUI_TEST_OVERWRITE_" + Guid.NewGuid().ToString("N");
        var tempFile = Path.GetTempFileName();
        try
        {
            Environment.SetEnvironmentVariable(EnvFileLoader.EnvFileVarName, " /from/env.env ");
            var forwarded = EnvFileLoader.ExtractHostArgs(ExpectedFallbackArgs, out var envFile, out var svc);
            Assert.Equal("/from/env.env", envFile);
            Assert.Null(svc);
            Assert.Equal(ExpectedFallbackArgs, forwarded);

            Environment.SetEnvironmentVariable(key, "old");
            File.WriteAllText(tempFile, $"{key}=new\n");
            Assert.Equal(1, EnvFileLoader.LoadFile(tempFile, overwriteExisting: true));
            Assert.Equal("new", Environment.GetEnvironmentVariable(key));
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvFileLoader.EnvFileVarName, prev);
            Environment.SetEnvironmentVariable(key, null);
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
