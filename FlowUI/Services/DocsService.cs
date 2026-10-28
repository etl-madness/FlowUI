using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FlowUI.Models;
using Markdig;

namespace FlowUI.Services;

public sealed record DocTopicOption(string TopicKey, string Label);

public sealed record HandbookHeading(string Id, string Text, int Level);

public sealed record HandbookItem(
    string Key,
    string Tag,
    string Title,
    string Category,
    string Section,
    string Description,
    bool IsSupplementalGuide,
    ComponentMeta? Metadata,
    string MarkdownContent,
    string HtmlContent,
    IReadOnlyList<HandbookHeading> Headings);

public sealed record HandbookCategoryGroup(
    string Category,
    IReadOnlyList<HandbookItem> Items);

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
            "sql" or "sql_bulk" or "sql_and_sql_bulk" or "db_select_template_stdio" =>
            [
                new("sql", "📖 <sql> Guide"),
                new("sql_bulk", "📖 <sql_bulk> Guide"),
                new("sql_and_sql_bulk", "📚 SQL & SQL Bulk Deep Dive"),
                new("db_select_template_stdio", "📚 DB Select to Template & Stdio")
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

    private static readonly Regex HeadingTagRegex = new(
        @"<h([23])\s+id=""([^""]+)""([^>]*)>([\s\S]*?)</h\1>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex HtmlTagStripRegex = new(
        @"<[^>]+>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> AllowedMermaidHtmlTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "br", "b", "i", "u", "s", "strong", "em", "code", "pre", "span", "small", "sub", "sup", "p", "div"
    };

    private static readonly HashSet<string> AliasComponentsSharingPrimaryMarkdown = new(StringComparer.OrdinalIgnoreCase)
    {
        "kv_bulk",
        "html_template",
        "xml_path"
    };

    private IReadOnlyList<HandbookCategoryGroup>? _cachedHandbookGroups;

    public string GetComponentGuideHtml(string nodeType)
    {
        var md = GetComponentGuideMarkdown(nodeType);
        return RenderMarkdownToHtml(md);
    }

    public string GetGuideHtml(string nodeType) => GetComponentGuideHtml(nodeType);

    public string RenderMarkdownToHtml(string md, string? headingIdPrefix = null)
    {
        var html = Markdown.ToHtml(md ?? string.Empty, _pipeline);

        if (!string.IsNullOrWhiteSpace(headingIdPrefix))
        {
            var safePrefix = headingIdPrefix.Trim();
            html = HeadingTagRegex.Replace(html, match =>
            {
                var level = match.Groups[1].Value;
                var origId = match.Groups[2].Value;
                var restAttrs = match.Groups[3].Value;
                var innerHtml = match.Groups[4].Value;
                var newId = $"{safePrefix}-{origId}";
                return $"<h{level} id=\"{newId}\" data-hb-heading=\"{newId}\"{restAttrs}>{innerHtml}</h{level}>";
            });
        }

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

    private static IReadOnlyList<HandbookHeading> ExtractHeadingsFromHtml(string html)
    {
        var list = new List<HandbookHeading>();
        foreach (Match match in HeadingTagRegex.Matches(html))
        {
            if (!int.TryParse(match.Groups[1].Value, out var level))
            {
                level = 2;
            }
            var id = match.Groups[2].Value;
            var rawInner = match.Groups[4].Value;
            var cleanText = WebUtility.HtmlDecode(HtmlTagStripRegex.Replace(rawInner, string.Empty)).Trim();
            if (!string.IsNullOrWhiteSpace(cleanText))
            {
                list.Add(new HandbookHeading(id, cleanText, level));
            }
        }
        return list;
    }

    public IReadOnlyList<HandbookCategoryGroup> GetHandbookGroups()
    {
        if (_cachedHandbookGroups is not null)
        {
            return _cachedHandbookGroups;
        }

        var groups = new List<HandbookCategoryGroup>();

        foreach (var category in _catalog.Categories)
        {
            var items = new List<HandbookItem>();
            var categoryComponents = _catalog.GetByCategory(category).ToList();

            foreach (var comp in categoryComponents)
            {
                var md = GetComponentGuideMarkdown(comp.Type);
                var prefix = $"hb-{comp.Type}";
                var html = RenderMarkdownToHtml(md, prefix);
                var headings = ExtractHeadingsFromHtml(html);

                items.Add(new HandbookItem(
                    Key: comp.Type,
                    Tag: comp.Tag,
                    Title: comp.Name,
                    Category: comp.Category,
                    Section: comp.Section,
                    Description: comp.Description,
                    IsSupplementalGuide: false,
                    Metadata: comp,
                    MarkdownContent: md,
                    HtmlContent: html,
                    Headings: headings));

                // Insert supplemental deep-dive guides right after their primary component
                if (string.Equals(comp.Type, "variable", StringComparison.OrdinalIgnoreCase))
                {
                    items.Add(BuildSupplementalItem(
                        key: "variables",
                        tag: "variables",
                        title: "Variables & Scoping Guide",
                        category: category,
                        section: "variables",
                        description: "Deep-dive guide on pipeline variable scoping, thread-safe registry operations, type conversions, and dynamic interpolation."));
                }
                else if (string.Equals(comp.Type, "database", StringComparison.OrdinalIgnoreCase))
                {
                    items.Add(BuildSupplementalItem(
                        key: "database_connection_strings",
                        tag: "connection_strings",
                        title: "Connection Strings & Driver Reference",
                        category: category,
                        section: "databases",
                        description: "Comprehensive reference of all supported SQL and Key-Value database drivers, DSN formats, TLS settings, and pool tuning profiles."));
                }
                else if (string.Equals(comp.Type, "sql_bulk", StringComparison.OrdinalIgnoreCase))
                {
                    items.Add(BuildSupplementalItem(
                        key: "sql_and_sql_bulk",
                        tag: "sql + sql_bulk",
                        title: "SQL & SQL Bulk Deep Dive",
                        category: category,
                        section: "flow",
                        description: "Architecture and performance guide comparing <sql> and <sql_bulk> for relational queries, batching, and streaming replication."));

                    items.Add(BuildSupplementalItem(
                        key: "db_select_template_stdio",
                        tag: "os.stdio",
                        title: "DB Query to Template & Standard I/O",
                        category: category,
                        section: "flow",
                        description: "End-to-end guide on querying a database, formatting rows with <template>, and streaming output to standard I/O (os.stdio)."));
                }
                else if (string.Equals(comp.Type, "html_template", StringComparison.OrdinalIgnoreCase))
                {
                    items.Add(BuildSupplementalItem(
                        key: "template_and_template_html",
                        tag: "template + html",
                        title: "Template & HTML Overview",
                        category: category,
                        section: "flow",
                        description: "Side-by-side comparison and usage patterns for <template> (text/template) and <template_html> (html/template)."));
                }
                else if (string.Equals(comp.Type, "json_path", StringComparison.OrdinalIgnoreCase))
                {
                    items.Add(BuildSupplementalItem(
                        key: "json_path_guide",
                        tag: "json_path",
                        title: "JSON Path Deep Dive",
                        category: category,
                        section: "flow",
                        description: "Extended reference and expression patterns for JSONPath filtering, array projection, and structured extraction."));
                }
            }

            if (items.Count > 0)
            {
                groups.Add(new HandbookCategoryGroup(category, items));
            }
        }

        _cachedHandbookGroups = groups;
        return groups;
    }

    public IReadOnlyList<HandbookItem> GetAllHandbookItems() =>
        GetHandbookGroups().SelectMany(g => g.Items).ToList();

    private HandbookItem BuildSupplementalItem(
        string key,
        string tag,
        string title,
        string category,
        string section,
        string description)
    {
        var md = GetComponentGuideMarkdown(key);
        var prefix = $"hb-{key}";
        var html = RenderMarkdownToHtml(md, prefix);
        var headings = ExtractHeadingsFromHtml(html);

        return new HandbookItem(
            Key: key,
            Tag: tag,
            Title: title,
            Category: category,
            Section: section,
            Description: description,
            IsSupplementalGuide: true,
            Metadata: null,
            MarkdownContent: md,
            HtmlContent: html,
            Headings: headings);
    }

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

        return BuildCatalogMarkdown(meta);
    }

    public static string BuildCatalogMarkdown(ComponentMeta meta)
    {
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
