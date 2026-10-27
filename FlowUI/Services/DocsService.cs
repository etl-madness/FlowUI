using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;

namespace FlowUI.Services;

public sealed record DocTopicOption(string TopicKey, string Label);

/// <summary>
/// Loads component markdown guides from bundled Docs/, embedded assembly resources, or ../go-flow/docs
/// (falling back to CatalogService metadata if a custom component has no markdown file)
/// and renders them to HTML using Markdig.
/// </summary>
public sealed class DocsService
{
    private static readonly Dictionary<string, string> DocMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["assert"] = "assert.md",
        ["database"] = "database.md",
        ["connection_strings"] = "database_connection_strings.md",
        ["database_connection_strings"] = "database_connection_strings.md",
        ["db_select_template_stdio"] = "db_select_template_stdio.md",
        ["excel_read"] = "excel_read.md",
        ["excel_write"] = "excel_write.md",
        ["file_read"] = "file_read.md",
        ["file_save"] = "file_save.md",
        ["foreach"] = "foreach.md",
        ["loop"] = "foreach.md",
        ["group"] = "group.md",
        ["http_client"] = "http_client.md",
        ["http"] = "http_client.md",
        ["if"] = "if_then_else.md",
        ["if_then_else"] = "if_then_else.md",
        ["if-then-else"] = "if_then_else.md",
        ["json_path"] = "json_path.md",
        ["json_path_guide"] = "json_path_guide.md",
        ["kv"] = "kv.md",
        ["kv_bulk"] = "kv.md",
        ["parallel"] = "parallel.md",
        ["preflight"] = "preflight.md",
        ["script"] = "script.md",
        ["sql"] = "sql.md",
        ["sql_bulk"] = "sql_bulk.md",
        ["sql_and_sql_bulk"] = "sql_and_sql_bulk.md",
        ["template"] = "template.md",
        ["template_html"] = "template_html.md",
        ["html_template"] = "template_html.md",
        ["template_and_template_html"] = "template_and_template_html.md",
        ["variable"] = "variable.md",
        ["variables"] = "variables.md",
        ["while"] = "while.md",
        ["xml_path"] = "xml_path.md",
        ["xml_xpath"] = "xml_path.md",
        ["yaml_path"] = "yaml_path.md"
    };

    private readonly CatalogService _catalog;
    private readonly MarkdownPipeline _pipeline;
    private readonly string[] _docsSearchRoots;

    public DocsService(CatalogService catalog, IWebHostEnvironment env)
    {
        _catalog = catalog;
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .DisableHtml()
            .Build();

        _docsSearchRoots = new[]
        {
            Path.GetFullPath(Path.Combine(env.ContentRootPath, "Docs")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Docs")),
            Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "..", "go-flow", "docs")),
            Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "go-flow", "docs"))
        }
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    }

    public IReadOnlyList<DocTopicOption> GetRelatedGuides(string nodeType)
    {
        var key = (nodeType ?? string.Empty).Trim().ToLowerInvariant();
        return key switch
        {
            "database" or "connection_strings" or "database_connection_strings" =>
            [
                new("database", "📖 <database> Component Guide"),
                new("connection_strings", "🔗 Connection Strings Reference")
            ],
            "sql" or "sql_bulk" or "sql_and_sql_bulk" =>
            [
                new("sql", "📖 <sql> Guide"),
                new("sql_bulk", "📖 <sql_bulk> Guide"),
                new("sql_and_sql_bulk", "📚 SQL & SQL Bulk Deep Dive")
            ],
            "template" or "template_html" or "html_template" or "template_and_template_html" =>
            [
                new("template", "📖 <template> Guide"),
                new("template_html", "📖 <template_html> Guide"),
                new("template_and_template_html", "📚 Template & HTML Overview")
            ],
            "json_path" or "json_path_guide" =>
            [
                new("json_path", "📖 <json_path> Guide"),
                new("json_path_guide", "📚 JSON Path Deep Dive")
            ],
            "variable" or "variables" =>
            [
                new("variable", "📖 <variable> Guide"),
                new("variables", "📚 Variables & Scoping Guide")
            ],
            _ => []
        };
    }

    private static readonly Regex MermaidBlockRegex = new(
        @"<div class=""mermaid"">([\s\S]*?)</div>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex XmlTagInMermaidRegex = new(
        @"<(\/?([a-zA-Z_][\w-]*)(?:\s[^<>]*)?\/?)>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> AllowedMermaidHtmlTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "br", "b", "i", "u", "s", "strong", "em", "code", "pre", "span", "small", "sub", "sup", "p", "div"
    };

    public string GetComponentGuideHtml(string nodeType)
    {
        var md = GetComponentGuideMarkdown(nodeType);
        var html = Markdown.ToHtml(md, _pipeline);
        return MermaidBlockRegex.Replace(html, static match =>
        {
            var rawMermaid = WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
            var sanitizedMermaid = XmlTagInMermaidRegex.Replace(rawMermaid, static tagMatch =>
            {
                var tagName = tagMatch.Groups[2].Value;
                if (AllowedMermaidHtmlTags.Contains(tagName))
                {
                    return tagMatch.Value;
                }

                return $"&lt;{tagMatch.Groups[1].Value}&gt;";
            });

            var encodedAttr = WebUtility.HtmlEncode(sanitizedMermaid);
            return $"<div class=\"mermaid\" data-mermaid=\"{encodedAttr}\">{encodedAttr}</div>";
        });
    }

    public string GetGuideHtml(string nodeType) => GetComponentGuideHtml(nodeType);

    public string GetComponentGuideMarkdown(string nodeType)
    {
        var safeNodeType = Path.GetFileName((nodeType ?? string.Empty).Trim());
        if (!string.IsNullOrWhiteSpace(safeNodeType))
        {
            var candidateFileNames = new List<string>();
            if (DocMap.TryGetValue(safeNodeType, out var mappedFile))
            {
                candidateFileNames.Add(mappedFile);
            }

            candidateFileNames.Add($"{safeNodeType}.md");
            candidateFileNames.Add($"{safeNodeType.Replace("_", "-")}.md");
            candidateFileNames.Add($"{safeNodeType.Replace("-", "_")}.md");

            // 1. Try disk search roots (bundled FlowUI/Docs, output Docs, or sibling go-flow/docs)
            foreach (var root in _docsSearchRoots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;

                foreach (var fileName in candidateFileNames.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var pathsToTry = new[]
                    {
                        Path.GetFullPath(Path.Combine(root, fileName)),
                        Path.GetFullPath(Path.Combine(root, "components", fileName))
                    };

                    foreach (var filePath in pathsToTry)
                    {
                        if (filePath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(filePath))
                        {
                            try
                            {
                                return File.ReadAllText(filePath);
                            }
                            catch
                            {
                                // Continue to next candidate or embedded resource
                            }
                        }
                    }
                }
            }

            // 2. Try embedded assembly resources
            var assembly = Assembly.GetExecutingAssembly();
            var resourceNames = assembly.GetManifestResourceNames();
            foreach (var fileName in candidateFileNames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var suffix = $".Docs.{fileName}";
                var match = resourceNames.FirstOrDefault(r =>
                    r.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(r, fileName, StringComparison.OrdinalIgnoreCase));

                if (match is not null)
                {
                    try
                    {
                        using var stream = assembly.GetManifestResourceStream(match);
                        if (stream is not null)
                        {
                            using var reader = new StreamReader(stream, Encoding.UTF8);
                            return reader.ReadToEnd();
                        }
                    }
                    catch
                    {
                        // Fallback to catalog generator
                    }
                }
            }
        }

        // 3. Fallback: generate structured documentation from CatalogService
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
            if (attr.Options is { Count: > 0 })
            {
                help += $" (Options: {string.Join(", ", attr.Options.Select(o => $"`{o}`"))})";
            }
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
