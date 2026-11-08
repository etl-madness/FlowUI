using FlowUI.Models;
using FlowUI.Services;

namespace FlowUI.Tests;

public class CatalogServiceTests
{
    private readonly CatalogService _catalog = new();

    [Fact]
    public void Catalog_HasExpectedCategoriesAndComponents()
    {
        Assert.Equal(9, _catalog.Categories.Count);
        Assert.Equal(26, _catalog.Components.Count);
        Assert.Contains("Pipeline Setup", _catalog.Categories);
        Assert.Contains("Control Flow", _catalog.Categories);
        Assert.Contains("Database & SQL", _catalog.Categories);
    }

    [Theory]
    [InlineData("variable", "Pipeline Setup", "variables", false)]
    [InlineData("database", "Pipeline Setup", "databases", false)]
    [InlineData("script", "Control Flow", "flow", true)]
    [InlineData("sql", "Database & SQL", "flow", true)]
    [InlineData("sql_bulk", "Database & SQL", "flow", true)]
    [InlineData("http_client", "Integration & Web", "flow", true)]
    public void GetByType_And_GetComponent_ReturnExpectedMetadata(
        string type, string expectedCategory, string expectedSection, bool expectedHasContent)
    {
        var comp = _catalog.GetByType(type.ToUpperInvariant());
        Assert.NotNull(comp);
        Assert.Equal(type, comp.Type);
        Assert.Equal(expectedCategory, comp.Category);
        Assert.Equal(expectedSection, comp.Section);
        Assert.Equal(expectedHasContent, comp.HasContent);
        Assert.NotEmpty(comp.Attributes);

        Assert.Same(comp, _catalog.GetComponent(type));
    }

    [Fact]
    public void GetByType_UnknownType_ReturnsNull()
    {
        Assert.Null(_catalog.GetByType("non_existent_component"));
    }

    [Fact]
    public void IsAllowedInConfig_And_ConfigComponents_FilterCorrectly()
    {
        Assert.True(_catalog.IsAllowedInConfig("variable"));
        Assert.True(_catalog.IsAllowedInConfig("  database  "));
        Assert.True(_catalog.IsAllowedInConfig("assert"));
        Assert.True(_catalog.IsAllowedInConfig("sql"));

        Assert.False(_catalog.IsAllowedInConfig("sql_bulk"));
        Assert.False(_catalog.IsAllowedInConfig("foreach"));
        Assert.False(_catalog.IsAllowedInConfig("while"));
        Assert.False(_catalog.IsAllowedInConfig(""));
        Assert.False(_catalog.IsAllowedInConfig((ComponentMeta)null!));

        var configComps = _catalog.ConfigComponents;
        Assert.NotEmpty(configComps);
        Assert.DoesNotContain(configComps, c => c.Type == "sql_bulk");
        Assert.Contains(configComps, c => c.Type == "variable");
    }

    [Fact]
    public void GetByCategory_And_GetGroupedByCategory_SupportSearchAndConfigFilter()
    {
        var dbAll = _catalog.GetByCategory("Database & SQL").ToList();
        Assert.Equal(2, dbAll.Count);

        var dbConfigOnly = _catalog.GetByCategory("Database & SQL", configOnly: true).ToList();
        Assert.Single(dbConfigOnly);
        Assert.Equal("sql", dbConfigOnly[0].Type);

        var searchBulk = _catalog.GetByCategory("Database & SQL", searchFilter: "bulk").ToList();
        Assert.Single(searchBulk);
        Assert.Equal("sql_bulk", searchBulk[0].Type);

        var grouped = _catalog.GetGroupedByCategory(searchFilter: "excel", configOnly: false);
        Assert.Single(grouped);
        Assert.Equal("File & Storage", grouped[0].Category);
        Assert.Equal(2, grouped[0].Items.Count);
    }
}
