using FlowUI.Config;

namespace FlowUI.Tests;

public class EnvFileLoaderTests
{
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
        Assert.Equal(new[] { "--urls", "http://127.0.0.1:5185", "--contentRoot=/opt/flow-ui/app" }, forwarded);
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
}
