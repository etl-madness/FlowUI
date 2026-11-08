using FlowUI.Models;
using FlowUI.Services;

namespace FlowUI.Tests;

public class XmlImporterServiceTests
{
    private readonly XmlImporterService _importer = new();
    private readonly XmlGeneratorService _generator = new();

    [Fact]
    public void ImportFromXml_EmptyOrWhitespace_ReturnsEmptyDraftWithFallbacks()
    {
        var draft = _importer.ImportFromXml("   ", fallbackName: "my_pipe", fallbackDescription: "desc", artifactTypeHint: "config");
        Assert.Equal("my_pipe", draft.Name);
        Assert.Equal("desc", draft.Description);
        Assert.Equal("config", draft.ArtifactType);
        Assert.Empty(draft.FlowNodes);
    }

    [Fact]
    public void ImportFromXml_InvalidRootElement_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => _importer.ImportFromXml("<invalid_root />"));
    }

    [Fact]
    public void ImportFromXml_DtdDeclaration_IsProhibitedForSecurity()
    {
        const string xxeXml = "<?xml version=\"1.0\"?><!DOCTYPE foo [<!ELEMENT foo ANY >]><pipeline />";
        Assert.ThrowsAny<Exception>(() => _importer.ImportFromXml(xxeXml));
    }

    [Fact]
    public void ImportFromXml_OptionsXml_SupportsBothXsdElementsAndLegacyOptionTags()
    {
        const string optionsXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <flow_cli_options>
              <options>
                <vars>ENV=PROD</vars>
                <node-output></node-output>
                <option flag="--debug" />
                <param name="-f" value="job.xml" />
              </options>
            </flow_cli_options>
            """;

        var draft = _importer.ImportFromXml(optionsXml, fallbackName: "OPT_1");
        Assert.Equal("options", draft.ArtifactType);
        Assert.Equal("OPT_1", draft.Name);
        Assert.Equal(4, draft.OptionEntries.Count);

        Assert.Equal("vars", draft.OptionEntries[0].Flag);
        Assert.Equal("ENV=PROD", draft.OptionEntries[0].Value);

        Assert.Equal("node-output", draft.OptionEntries[1].Flag);
        Assert.Equal("true", draft.OptionEntries[1].Value);

        Assert.Equal("debug", draft.OptionEntries[2].Flag);
        Assert.Equal("true", draft.OptionEntries[2].Value);

        Assert.Equal("file", draft.OptionEntries[3].Flag);
        Assert.Equal("job.xml", draft.OptionEntries[3].Value);
    }

    [Fact]
    public void ImportFromXml_FullPipeline_ParsesAllSections_Containers_IfBranches_AndDedentsContent()
    {
        const string xml = """
            <pipeline xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                      xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd"
                      name="sample_etl"
                      description="Sample pipeline">
                <variables>
                    <variable name="env" value="dev" />
                </variables>
                <databases>
                    <database name="db1" driver="sqlite" connection_string=":memory:" />
                </databases>
                <preflight>
                    <assert id="a1" equals="1">
                        SELECT 1;
                    </assert>
                </preflight>
                <flow>
                    <if id="cond1" var="env" equals="dev">
                        <then>
                            <sql id="q1" db="db1">
                                <![CDATA[
                                    SELECT * FROM items
                                    WHERE id > 0;
                                ]]>
                            </sql>
                        </then>
                        <else>
                            <script id="s1" language="pwsh">Write-Output "skip"</script>
                        </else>
                        <sql id="direct_in_if" db="db1">SELECT 2;</sql>
                    </if>
                    <foreach id="loop1" db="db1">
                        SELECT id FROM items;
                        <sql id="inner_sql" db="db1">INSERT INTO log VALUES (1);</sql>
                    </foreach>
                </flow>
            </pipeline>
            """;

        var draft = _importer.ImportFromXml(xml);
        Assert.Equal("pipeline", draft.ArtifactType);
        Assert.Equal("sample_etl", draft.Name);
        Assert.Equal("Sample pipeline", draft.Description);

        Assert.Single(draft.VariableNodes);
        Assert.Equal("env", draft.VariableNodes[0].Attributes["name"]);
        Assert.False(draft.VariableNodes[0].Attributes.ContainsKey("xmlns"));

        Assert.Single(draft.DatabaseNodes);
        Assert.Equal("sqlite", draft.DatabaseNodes[0].Attributes["driver"]);

        Assert.Single(draft.PreflightNodes);
        Assert.Equal("SELECT 1;", draft.PreflightNodes[0].ContentText);

        Assert.Equal(2, draft.FlowNodes.Count);
        var ifNode = draft.FlowNodes[0];
        Assert.Equal("if", ifNode.NodeType);
        var thenBranch = ifNode.GetThenBranch();
        var elseBranch = ifNode.GetElseBranch();
        Assert.NotNull(thenBranch);
        Assert.NotNull(elseBranch);
        Assert.Equal(2, thenBranch.Children.Count); // <then> child + direct child in <if>
        Assert.Equal("SELECT * FROM items\nWHERE id > 0;", thenBranch.Children[0].ContentText);
        Assert.Single(elseBranch.Children);
        Assert.Equal("Write-Output \"skip\"", elseBranch.Children[0].ContentText);

        var foreachNode = draft.FlowNodes[1];
        Assert.Equal("foreach", foreachNode.NodeType);
        Assert.Equal("SELECT id FROM items;", foreachNode.ContentText);
        Assert.Single(foreachNode.Children);
        Assert.Equal("inner_sql", foreachNode.Children[0].Attributes["id"]);
    }

    [Fact]
    public void EnsureIfBranches_AddsMissingThenAndElseNodes()
    {
        var ifNode = new PipelineNode { Id = 10, Section = "flow", NodeType = "if" };
        _importer.EnsureIfBranches(ifNode);

        Assert.NotNull(ifNode.GetThenBranch());
        Assert.NotNull(ifNode.GetElseBranch());
        Assert.Equal(2, ifNode.Children.Count);

        // Calling again should be idempotent
        _importer.EnsureIfBranches(ifNode);
        Assert.Equal(2, ifNode.Children.Count);
    }

    [Fact]
    public void RoundTrip_GenerateAndImport_PreservesStructure()
    {
        var original = new PipelineDraft
        {
            ArtifactType = "pipeline",
            Name = "roundtrip_pipe",
            Description = "Roundtrip test"
        };
        original.VariableNodes.Add(new PipelineNode
        {
            Id = 1,
            Section = "variables",
            NodeType = "variable",
            Attributes = new(StringComparer.OrdinalIgnoreCase) { ["name"] = "v1", ["value"] = "123" }
        });
        original.FlowNodes.Add(new PipelineNode
        {
            Id = 2,
            Section = "flow",
            NodeType = "sql",
            Attributes = new(StringComparer.OrdinalIgnoreCase) { ["id"] = "step1", ["db"] = "default_db" },
            ContentText = "SELECT 1;"
        });

        var xml = _generator.GeneratePipelineXml(original);
        var reimported = _importer.ImportFromXml(xml);

        Assert.Equal(original.Name, reimported.Name);
        Assert.Equal(original.Description, reimported.Description);
        Assert.Single(reimported.VariableNodes);
        Assert.Equal("123", reimported.VariableNodes[0].Attributes["value"]);
        Assert.Single(reimported.FlowNodes);
        Assert.Equal("SELECT 1;", reimported.FlowNodes[0].ContentText);
    }
}
