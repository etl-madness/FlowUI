using System.Text;
using Markdig;

namespace FlowUIv2.Services;

/// <summary>
/// Loads component markdown guides from ../go-flow/docs (or generates detailed reference guides from CatalogService)
/// and renders them to HTML using Markdig.
/// </summary>
public sealed class DocsService
{
    private readonly CatalogService _catalog;
    private readonly MarkdownPipeline _pipeline;
    private readonly string _docsRootPath;

    public DocsService(CatalogService catalog, IWebHostEnvironment env)
    {
        _catalog = catalog;
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .DisableHtml()
            .Build();
        _docsRootPath = Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "..", "go-flow", "docs"));
    }

    public string GetComponentGuideHtml(string nodeType)
    {
        var md = GetComponentGuideMarkdown(nodeType);
        return Markdown.ToHtml(md, _pipeline);
    }

    public string GetGuideHtml(string nodeType) => GetComponentGuideHtml(nodeType);

    public string GetComponentGuideMarkdown(string nodeType)
    {
        var safeNodeType = Path.GetFileName(nodeType ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(safeNodeType) && Directory.Exists(_docsRootPath))
        {
            var candidates = new[]
            {
                Path.GetFullPath(Path.Combine(_docsRootPath, $"{safeNodeType}.md")),
                Path.GetFullPath(Path.Combine(_docsRootPath, "components", $"{safeNodeType}.md")),
                Path.GetFullPath(Path.Combine(_docsRootPath, $"{safeNodeType.Replace("_", "-")}.md"))
            };

            foreach (var file in candidates)
            {
                if (file.StartsWith(_docsRootPath, StringComparison.OrdinalIgnoreCase) && File.Exists(file))
                {
                    try
                    {
                        return File.ReadAllText(file);
                    }
                    catch
                    {
                        // Fallback to catalog generator
                    }
                }
            }
        }

        var meta = _catalog.GetByType(safeNodeType);
        if (meta is null)
        {
            return $"# `<{safeNodeType}>`\n\nNo documentation found for component `{safeNodeType}`.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# `{meta.Name}` (`<{meta.Tag}>`)");
        sb.AppendLine();
        sb.AppendLine($"**Category:** `{meta.Category}` | **Pipeline Section:** `<{meta.Section}>`");
        sb.AppendLine();
        sb.AppendLine(meta.Description);
        sb.AppendLine();
        sb.AppendLine("## Supported Attributes");
        sb.AppendLine();
        sb.AppendLine("| Attribute | Label | Type | Required | Default | Description |");
        sb.AppendLine("| :--- | :--- | :--- | :---: | :--- | :--- |");

        foreach (var attr in meta.Attributes)
        {
            var req = attr.Required ? "✅ Yes" : "No";
            var def = string.IsNullOrEmpty(attr.Default) ? "-" : $"`{attr.Default}`";
            var help = string.IsNullOrEmpty(attr.Help) ? attr.Label : attr.Help;
            sb.AppendLine($"| `{attr.Name}` | {attr.Label} | `{attr.Type}` | {req} | {def} | {help} |");
        }

        if (meta.HasContent)
        {
            sb.AppendLine();
            sb.AppendLine("## Element Body / Content");
            sb.AppendLine();
            sb.AppendLine(meta.ContentLabel);
        }

        return sb.ToString();
    }
}
