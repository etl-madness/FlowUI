using FlowUI.Models;
using FlowUI.Services;

namespace FlowUI.Tests;

public class XmlGeneratorServiceTests
{
    private readonly XmlGeneratorService _generator = new();

    [Theory]
    [InlineData("--var", "vars")]
    [InlineData("-l", "debug")]
    [InlineData("--log", "debug")]
    [InlineData("--preflight-only", "preflight")]
    [InlineData("-f", "file")]
    [InlineData("-c", "config")]
    [InlineData("-v", "validate")]
    [InlineData("--node-output", "node-output")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void NormalizeOptionTagName_NormalizesLegacyAndShortFlags(string? rawFlag, string expected)
    {
        Assert.Equal(expected, XmlGeneratorService.NormalizeOptionTagName(rawFlag));
    }

    [Fact]
    public void EscapeXmlAttr_EscapesSpecialCharsAndPreservesValidEntities()
    {
        Assert.Equal(string.Empty, XmlGeneratorService.EscapeXmlAttr(""));
        Assert.Equal("a &amp; b &lt; c &gt; &quot;d&quot;", XmlGeneratorService.EscapeXmlAttr("a & b < c > \"d\""));
        // Existing valid entities should not be double-escaped
        Assert.Equal("&amp; &lt; &gt; &quot; &apos; &#10; &#x0A; &#X1f;",
            XmlGeneratorService.EscapeXmlAttr("&amp; &lt; &gt; &quot; &apos; &#10; &#x0A; &#X1f;"));
        // Invalid entity sequences get their '&' escaped
        Assert.Equal("&amp;invalid; &amp;#12g; &amp;#xZZ;",
            XmlGeneratorService.EscapeXmlAttr("&invalid; &#12g; &#xZZ;"));
    }

    [Fact]
    public void GenerateOptionsXml_MergesVars_NormalizesBooleans_AndDeduplicates()
    {
        var draft = new PipelineDraft
        {
            ArtifactType = "options",
            OptionEntries =
            [
                new FlowOptionEntry { Id = 1, Flag = "--var", Value = "ENV=DEV" },
                new FlowOptionEntry { Id = 2, Flag = "vars", Value = "BATCH=100" },
                new FlowOptionEntry { Id = 3, Flag = "debug", Value = "1" },
                new FlowOptionEntry { Id = 4, Flag = "preflight", Value = "0" },
                new FlowOptionEntry { Id = 5, Flag = "validate", Value = "" },
                new FlowOptionEntry { Id = 6, Flag = "format", Value = "text" },
                new FlowOptionEntry { Id = 7, Flag = "format", Value = "json" }
            ]
        };

        var xml = _generator.GenerateXml(draft);
        Assert.Contains("<flow_cli_options>", xml);
        Assert.Contains("<vars>ENV=DEV,BATCH=100</vars>", xml);
        Assert.Contains("<debug>true</debug>", xml);
        Assert.Contains("<preflight>false</preflight>", xml);
        Assert.Contains("<validate>true</validate>", xml);
        Assert.Contains("<format>json</format>", xml);
        Assert.DoesNotContain("<format>text</format>", xml);
    }

    [Fact]
    public void GenerateXml_Pipeline_RendersAllSections_PreferredAttrOrder_IfBranches_AndCdata()
    {
        var draft = new PipelineDraft
        {
            ArtifactType = "pipeline",
            Name = "etl_job",
            Description = "Daily ETL & Sync"
        };

        draft.VariableNodes.Add(new PipelineNode
        {
            Id = 1,
            Section = "variables",
            NodeType = "variable",
            Attributes = new(StringComparer.OrdinalIgnoreCase)
            {
                ["value"] = "prod",
                ["name"] = "env",
                ["custom_attr"] = "custom_val"
            }
        });

        draft.DatabaseNodes.Add(new PipelineNode
        {
            Id = 2,
            Section = "databases",
            NodeType = "database",
            Attributes = new(StringComparer.OrdinalIgnoreCase)
            {
                ["connection_string"] = "Server=localhost;",
                ["driver"] = "sqlserver",
                ["name"] = "main_db"
            }
        });

        draft.PreflightNodes.Add(new PipelineNode
        {
            Id = 3,
            Section = "preflight",
            NodeType = "assert",
            Attributes = new(StringComparer.OrdinalIgnoreCase) { ["id"] = "chk1" },
            ContentText = "SELECT 1 WHERE 1 < 2;"
        });

        var ifNode = new PipelineNode
        {
            Id = 4,
            Section = "flow",
            NodeType = "if",
            Attributes = new(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = "branch_check",
                ["var"] = "env",
                ["equals"] = "prod"
            }
        };
        var thenBranch = new PipelineNode { Id = 5, ParentNodeId = 4, NodeType = "then" };
        thenBranch.Children.Add(new PipelineNode
        {
            Id = 6,
            ParentNodeId = 5,
            NodeType = "sql",
            Attributes = new(StringComparer.OrdinalIgnoreCase) { ["id"] = "q_prod", ["db"] = "main_db" },
            ContentText = "SELECT 'prod';"
        });
        var elseBranch = new PipelineNode { Id = 7, ParentNodeId = 4, NodeType = "else" };
        elseBranch.Children.Add(new PipelineNode
        {
            Id = 8,
            ParentNodeId = 7,
            NodeType = "script",
            Attributes = new(StringComparer.OrdinalIgnoreCase) { ["id"] = "s_dev", ["language"] = "pwsh" },
            ContentText = "<![CDATA[Write-Host 'already cdata']]>"
        });
        ifNode.Children.Add(thenBranch);
        ifNode.Children.Add(elseBranch);
        draft.FlowNodes.Add(ifNode);

        // Also test empty <if />
        draft.FlowNodes.Add(new PipelineNode
        {
            Id = 9,
            Section = "flow",
            NodeType = "if",
            Attributes = new(StringComparer.OrdinalIgnoreCase) { ["id"] = "empty_if" }
        });

        var xml = _generator.GeneratePipelineXml(draft);

        Assert.Contains("name=\"etl_job\"", xml);
        Assert.Contains("description=\"Daily ETL &amp; Sync\"", xml);
        // Preferred order: name before value before custom_attr
        Assert.Contains("<variable name=\"env\" value=\"prod\" custom_attr=\"custom_val\" />", xml);
        // Preferred order: name, driver, connection_string
        Assert.Contains("<database name=\"main_db\" driver=\"sqlserver\" connection_string=\"Server=localhost;\" />", xml);
        // Content with '<' auto-wrapped in CDATA
        Assert.Contains("<![CDATA[\n                SELECT 1 WHERE 1 < 2;\n            ]]>", xml);
        // If/then/else rendered properly
        Assert.Contains("<then>", xml);
        Assert.Contains("<else>", xml);
        Assert.Contains("<if id=\"empty_if\" />", xml);
    }

    [Fact]
    public void GenerateXml_ConfigArtifact_OmitsEmptyFlowSection()
    {
        var draft = new PipelineDraft
        {
            ArtifactType = "config",
            Name = "CFG_TEST",
            Description = "Config only"
        };
        draft.VariableNodes.Add(new PipelineNode
        {
            Id = 1,
            Section = "variables",
            NodeType = "variable",
            Attributes = new(StringComparer.OrdinalIgnoreCase) { ["name"] = "ENV", ["value"] = "STAGE" }
        });

        var xml = _generator.GenerateXml(draft);
        Assert.Contains("<variables>", xml);
        Assert.DoesNotContain("<flow>", xml);
    }
}
