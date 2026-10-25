using System.Text;
using FlowUIv2.Models;

namespace FlowUIv2.Services;

/// <summary>
/// 1:1 port of go-flow/builder/xml_generator.go.
/// Generates valid Flow <pipeline> XML from the visual designer node tree.
/// </summary>
public sealed class XmlGeneratorService
{
    private static readonly string[] PreferredAttributeOrder =
    [
        "id", "name", "driver", "connection_string", "workload",
        "db", "database", "target_db", "target_database", "target_table", "table",
        "file", "path", "sheet", "source",
        "into", "output_var", "var", "equals", "variable",
        "transaction", "tx", "timeout",
        "stream", "buffer", "mode", "language",
        "batch_size", "tablock", "check_constraints", "fire_triggers", "keep_nulls",
        "header", "op", "bucket", "key", "value",
        "condition", "max_iterations", "max_threads", "threads", "max_concurrency", "concurrency",
        "on_error", "retry_count", "retry_interval", "description"
    ];

    public string GenerateXml(PipelineDraft draft)
    {
        if (string.Equals(draft.ArtifactType, "options", StringComparison.OrdinalIgnoreCase))
        {
            return GenerateOptionsXml(draft);
        }

        return GenerateXmlWithMetadata(
            draft.Name,
            draft.Description,
            draft.VariableNodes,
            draft.DatabaseNodes,
            draft.PreflightNodes,
            draft.FlowNodes,
            includeEmptyFlow: !string.Equals(draft.ArtifactType, "config", StringComparison.OrdinalIgnoreCase));
    }

    public string GeneratePipelineXml(PipelineDraft draft) => GenerateXml(draft);

    public static readonly HashSet<string> BooleanOptionElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "validate",
        "preflight",
        "debug",
        "encrypted",
        "verify-signature",
        "node-output"
    };

    public static readonly IReadOnlyList<(string Tag, string Type, string DefaultValue, string Description)> OptionsXsdCatalog =
    [
        ("vars", "string", "ENV=DEV", "Comma-separated key=value runtime variable overrides (-vars)"),
        ("node-output", "boolean", "true", "Record output of each node into pipeline_events.node_output (-node-output)"),
        ("format", "enum", "json", "Output formatting: csv, text, json, jsonpretty, markdown, summary, stream (-format)"),
        ("debug", "boolean", "true", "Enable detailed console execution logging (-debug)"),
        ("preflight", "boolean", "false", "Execute preflight validation nodes only (-preflight)"),
        ("validate", "boolean", "false", "Validate XML schema and AST structure without executing (-validate)"),
        ("config", "string", "", "Optional path/name to a CONFIG.xml file (-config)"),
        ("file", "string", "scripts.xml", "Path to pipeline XML file (-file)"),
        ("xsd", "string", "", "Optional path to XSD file for schema validation (-xsd)"),
        ("encrypted", "boolean", "false", "Decrypt encrypted resources using secure-key prior to execution (-encrypted)"),
        ("secure-key", "string", "", "Secret key or passphrase used to decrypt XML (-secure-key)"),
        ("verify-signature", "boolean", "false", "Verify digital signature before executing (-verify-signature)"),
        ("public-key", "string", "", "Path to PEM public key file for signature verification (-public-key)"),
        ("cert", "string", "", "Path to X.509 certificate file for signature verification (-cert)"),
        ("ca-cert", "string", "", "Path to root CA certificate file (-ca-cert)"),
        ("keyring", "string", "", "Path to OpenPGP armored public keyring (-keyring)"),
        ("signature", "string", "", "Path to detached digital signature file (-signature)"),
        ("gopath", "string", "", "GOPATH directory for dynamic interpreter imports (-gopath)"),
        ("xslt", "string", "", "Optional path to custom XSLT stylesheet (-xslt)"),
        ("out", "string", "", "Path to output file for XSLT transformed XML (-out)")
    ];

    public static string NormalizeOptionTagName(string? rawFlag)
    {
        if (string.IsNullOrWhiteSpace(rawFlag))
            return string.Empty;

        var trimmed = rawFlag.Trim().TrimStart('-').Trim().ToLowerInvariant();
        return trimmed switch
        {
            "var" => "vars",
            "l" or "log" => "debug",
            "preflight-only" => "preflight",
            "f" => "file",
            "c" => "config",
            "v" => "validate",
            _ => trimmed
        };
    }

    public string GenerateOptionsXml(PipelineDraft draft)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        sb.Append("<flow_cli_options>\n");
        sb.Append("  <options>\n");

        // options.xsd uses <xs:all> where each element has maxOccurs="1".
        // Merge multiple 'vars' entries with commas and deduplicate other option elements.
        var orderedTags = new List<string>();
        var valuesByTag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var opt in draft.OptionEntries)
        {
            var tag = NormalizeOptionTagName(opt.Flag);
            if (string.IsNullOrWhiteSpace(tag))
                continue;

            var rawVal = (opt.Value ?? string.Empty).Trim();
            if (BooleanOptionElements.Contains(tag))
            {
                if (string.IsNullOrEmpty(rawVal))
                    rawVal = "true";
                else if (string.Equals(rawVal, "1", StringComparison.OrdinalIgnoreCase))
                    rawVal = "true";
                else if (string.Equals(rawVal, "0", StringComparison.OrdinalIgnoreCase))
                    rawVal = "false";
                else
                    rawVal = rawVal.ToLowerInvariant();
            }

            if (!valuesByTag.ContainsKey(tag))
            {
                orderedTags.Add(tag);
                valuesByTag[tag] = rawVal;
            }
            else if (string.Equals(tag, "vars", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(rawVal))
            {
                valuesByTag[tag] = string.IsNullOrEmpty(valuesByTag[tag])
                    ? rawVal
                    : $"{valuesByTag[tag]},{rawVal}";
            }
            else
            {
                valuesByTag[tag] = rawVal;
            }
        }

        foreach (var tag in orderedTags)
        {
            var val = EscapeXmlAttr(valuesByTag[tag]);
            sb.Append($"    <{tag}>{val}</{tag}>\n");
        }

        sb.Append("  </options>\n");
        sb.Append("</flow_cli_options>\n");
        return sb.ToString();
    }

    public string GenerateXmlWithMetadata(
        string pipelineName,
        string description,
        IReadOnlyList<PipelineNode> varNodes,
        IReadOnlyList<PipelineNode> dbNodes,
        IReadOnlyList<PipelineNode> preflightNodes,
        IReadOnlyList<PipelineNode> flowNodes,
        bool includeEmptyFlow = true)
    {
        var sb = new StringBuilder();
        sb.Append("<pipeline xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"\n");
        sb.Append("          xsi:noNamespaceSchemaLocation=\"https://raw.githubusercontent.com/etl-madness/flow/main/xsd/pipeline.xsd\"");

        if (!string.IsNullOrWhiteSpace(pipelineName))
        {
            sb.Append($"\n          name=\"{EscapeXmlAttr(pipelineName)}\"");
            sb.Append($"\n          id=\"{EscapeXmlAttr(pipelineName)}\"");
        }
        if (!string.IsNullOrWhiteSpace(description))
        {
            sb.Append($"\n          description=\"{EscapeXmlAttr(description)}\"");
        }
        sb.Append(">\n\n");

        // 1. Variables
        if (varNodes.Count > 0)
        {
            sb.Append("    <variables>\n");
            foreach (var n in varNodes)
            {
                sb.Append(RenderNodeXml(n, "        "));
            }
            sb.Append("    </variables>\n\n");
        }

        // 2. Databases
        if (dbNodes.Count > 0)
        {
            sb.Append("    <databases>\n");
            foreach (var n in dbNodes)
            {
                sb.Append(RenderNodeXml(n, "        "));
            }
            sb.Append("    </databases>\n\n");
        }

        // 3. Preflight
        if (preflightNodes.Count > 0)
        {
            sb.Append("    <preflight>\n");
            foreach (var n in preflightNodes)
            {
                sb.Append(RenderNodeXml(n, "        "));
            }
            sb.Append("    </preflight>\n\n");
        }

        // 4. Flow
        if (flowNodes.Count > 0 || includeEmptyFlow)
        {
            sb.Append("    <flow>\n");
            foreach (var n in flowNodes)
            {
                sb.Append(RenderNodeXml(n, "        "));
            }
            sb.Append("    </flow>\n");
        }
        sb.Append("</pipeline>\n");

        return sb.ToString();
    }

    private static string RenderNodeXml(PipelineNode node, string indent)
    {
        var sb = new StringBuilder();
        var tag = node.NodeType;

        var keys = node.Attributes.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        var orderedKeys = new List<string>();

        foreach (var preferred in PreferredAttributeOrder)
        {
            foreach (var k in keys)
            {
                if (string.Equals(k, preferred, StringComparison.Ordinal))
                {
                    orderedKeys.Add(k);
                }
            }
        }

        foreach (var k in keys)
        {
            if (!orderedKeys.Contains(k, StringComparer.Ordinal))
            {
                orderedKeys.Add(k);
            }
        }

        var attrs = new List<string>();
        foreach (var k in orderedKeys)
        {
            if (node.Attributes.TryGetValue(k, out var v) && !string.IsNullOrEmpty(v))
            {
                attrs.Add($"{k}=\"{EscapeXmlAttr(v)}\"");
            }
        }

        var attrStr = attrs.Count > 0 ? " " + string.Join(" ", attrs) : string.Empty;
        var content = (node.ContentText ?? string.Empty).Trim();

        // Special handling for <if> conditional container
        if (string.Equals(tag, "if", StringComparison.OrdinalIgnoreCase))
        {
            PipelineNode? thenNode = null;
            PipelineNode? elseNode = null;
            var otherChildren = new List<PipelineNode>();

            foreach (var ch in node.Children)
            {
                if (string.Equals(ch.NodeType, "then", StringComparison.OrdinalIgnoreCase))
                    thenNode = ch;
                else if (string.Equals(ch.NodeType, "else", StringComparison.OrdinalIgnoreCase))
                    elseNode = ch;
                else
                    otherChildren.Add(ch);
            }

            bool hasThen = thenNode is not null && (thenNode.Children.Count > 0 || !string.IsNullOrWhiteSpace(thenNode.ContentText));
            bool hasElse = elseNode is not null && (elseNode.Children.Count > 0 || !string.IsNullOrWhiteSpace(elseNode.ContentText));
            bool hasOther = otherChildren.Count > 0;
            bool hasContent = !string.IsNullOrEmpty(content);

            if (!hasThen && !hasElse && !hasOther && !hasContent)
            {
                sb.Append($"{indent}<if{attrStr} />\n");
                return sb.ToString();
            }

            sb.Append($"{indent}<if{attrStr}>\n");
            if (hasContent)
            {
                sb.Append(FormatContent(content, indent));
            }
            foreach (var ch in otherChildren)
            {
                sb.Append(RenderNodeXml(ch, indent + "    "));
            }
            if (hasThen && thenNode is not null)
            {
                sb.Append($"{indent}    <then>\n");
                if (!string.IsNullOrWhiteSpace(thenNode.ContentText))
                {
                    sb.Append(FormatContent(thenNode.ContentText, indent + "    "));
                }
                foreach (var ch in thenNode.Children)
                {
                    sb.Append(RenderNodeXml(ch, indent + "        "));
                }
                sb.Append($"{indent}    </then>\n");
            }
            if (hasElse && elseNode is not null)
            {
                sb.Append($"{indent}    <else>\n");
                if (!string.IsNullOrWhiteSpace(elseNode.ContentText))
                {
                    sb.Append(FormatContent(elseNode.ContentText, indent + "    "));
                }
                foreach (var ch in elseNode.Children)
                {
                    sb.Append(RenderNodeXml(ch, indent + "        "));
                }
                sb.Append($"{indent}    </else>\n");
            }
            sb.Append($"{indent}</if>\n");
            return sb.ToString();
        }

        // Container nodes with children (group, parallel, foreach, while, etc.)
        if (node.Children.Count > 0)
        {
            sb.Append($"{indent}<{tag}{attrStr}>\n");
            if (!string.IsNullOrEmpty(content))
            {
                sb.Append(FormatContent(content, indent));
            }
            foreach (var ch in node.Children)
            {
                sb.Append(RenderNodeXml(ch, indent + "    "));
            }
            sb.Append($"{indent}</{tag}>\n");
            return sb.ToString();
        }

        if (string.IsNullOrEmpty(content) ||
            string.Equals(tag, "variable", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tag, "database", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append($"{indent}<{tag}{attrStr} />\n");
        }
        else
        {
            sb.Append($"{indent}<{tag}{attrStr}>\n");
            sb.Append(FormatContent(content, indent));
            sb.Append($"{indent}</{tag}>\n");
        }

        return sb.ToString();
    }

    private static string FormatContent(string content, string indent)
    {
        content = content.Trim();
        if (string.IsNullOrEmpty(content)) return string.Empty;

        var lines = content.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();

        if (content.StartsWith("<![CDATA[", StringComparison.Ordinal) &&
            content.EndsWith("]]>", StringComparison.Ordinal))
        {
            foreach (var l in lines)
            {
                sb.Append($"{indent}    {l}\n");
            }
            return sb.ToString();
        }

        if (content.IndexOfAny(['<', '>', '&']) >= 0)
        {
            sb.Append($"{indent}    <![CDATA[\n");
            foreach (var l in lines)
            {
                sb.Append($"{indent}        {l}\n");
            }
            sb.Append($"{indent}    ]]>\n");
            return sb.ToString();
        }

        foreach (var l in lines)
        {
            sb.Append($"{indent}    {l}\n");
        }
        return sb.ToString();
    }

    private static (bool IsEntity, int Length) IsXmlEntity(ReadOnlySpan<char> s)
    {
        if (s.IsEmpty || s[0] != '&') return (false, 0);

        ReadOnlySpan<string> named = ["&amp;", "&lt;", "&gt;", "&quot;", "&apos;"];
        foreach (var ent in named)
        {
            if (s.StartsWith(ent.AsSpan(), StringComparison.Ordinal))
                return (true, ent.Length);
        }

        if (s.StartsWith("&#".AsSpan(), StringComparison.Ordinal))
        {
            var semi = s.IndexOf(';');
            if (semi > 2 && semi <= 10)
            {
                var numPart = s[2..semi];
                if (numPart[0] == 'x' || numPart[0] == 'X')
                {
                    bool isHex = numPart.Length > 1;
                    for (int i = 1; i < numPart.Length; i++)
                    {
                        char c = numPart[i];
                        if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                        {
                            isHex = false;
                            break;
                        }
                    }
                    if (isHex) return (true, semi + 1);
                }
                else
                {
                    bool isDec = numPart.Length > 0;
                    foreach (var c in numPart)
                    {
                        if (c < '0' || c > '9')
                        {
                            isDec = false;
                            break;
                        }
                    }
                    if (isDec) return (true, semi + 1);
                }
            }
        }

        return (false, 0);
    }

    public static string EscapeXmlAttr(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length + 8);
        var span = s.AsSpan();
        int i = 0;
        while (i < span.Length)
        {
            var (isEnt, entLen) = IsXmlEntity(span[i..]);
            if (isEnt)
            {
                sb.Append(span.Slice(i, entLen));
                i += entLen;
                continue;
            }

            char c = span[i];
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default: sb.Append(c); break;
            }
            i++;
        }
        return sb.ToString();
    }
}
