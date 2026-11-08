using FlowUI.Models;
using FlowUI.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace FlowUI.Tests;

public class DocsServiceTests
{
    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "FlowUI";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private readonly CatalogService _catalog = new();
    private readonly DocsService _docs;

    public DocsServiceTests()
    {
        _docs = new DocsService(_catalog, new FakeWebHostEnvironment());
    }

    [Theory]
    [InlineData("database", 2)]
    [InlineData("sql", 4)]
    [InlineData("template", 3)]
    [InlineData("json_path", 2)]
    [InlineData("variable", 2)]
    [InlineData("unknown_type", 0)]
    public void GetRelatedGuides_ReturnsExpectedGuideTopics(string nodeType, int expectedCount)
    {
        var guides = _docs.GetRelatedGuides(nodeType);
        Assert.Equal(expectedCount, guides.Count);
    }

    [Fact]
    public void RenderMarkdownToHtml_PrefixesHeadingIds_AndRendersMermaidBlocks()
    {
        const string markdown = """
            ## Overview
            ### Configuration

            ```mermaid
            graph LR
                A[Start] --> B[End]
            ```
            """;

        var html = _docs.RenderMarkdownToHtml(markdown, "hb-sql");
        Assert.Contains("id=\"hb-sql-overview\"", html);
        Assert.Contains("data-hb-heading=\"hb-sql-overview\"", html);
        Assert.Contains("id=\"hb-sql-configuration\"", html);
        Assert.Contains("class=\"mermaid\"", html);
    }

    [Fact]
    public void BuildCatalogMarkdown_GeneratesAttributeTableAndContentSection()
    {
        var comp = _catalog.GetByType("sql")!;
        var md = DocsService.BuildCatalogMarkdown(comp);

        Assert.Contains("# `SQL Query / Command` (`<sql>`)", md);
        Assert.Contains("## Supported Attributes", md);
        Assert.Contains("| `db` |", md);
        Assert.Contains("✅ Yes", md);
        Assert.Contains("## Element Body / Content", md);
    }

    [Fact]
    public void GetComponentGuideMarkdown_And_GetHandbookGroups_LoadDocsAndFallbackGracefully()
    {
        var sqlMd = _docs.GetComponentGuideMarkdown("sql");
        Assert.NotEmpty(sqlMd);

        var unknownMd = _docs.GetComponentGuideMarkdown("completely_unknown_component");
        Assert.Contains("No documentation found for component `completely_unknown_component`.", unknownMd);

        var groups = _docs.GetHandbookGroups();
        Assert.NotEmpty(groups);
        Assert.Equal("Architecture & Deployment", groups[0].Category);

        var allItems = _docs.GetAllHandbookItems();
        Assert.True(allItems.Count > _catalog.Components.Count);
        Assert.Contains(allItems, i => i.Key == "flowui_and_goflowapi" && i.IsSupplementalGuide);
        Assert.Contains(allItems, i => i.Key == "sql" && !i.IsSupplementalGuide);
    }
}
