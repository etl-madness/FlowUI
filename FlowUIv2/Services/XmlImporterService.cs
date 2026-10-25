using System.Xml;
using System.Xml.Linq;
using FlowUIv2.Models;

namespace FlowUIv2.Services;

/// <summary>
/// 1:1 port of go-flow/builder/xml_importer.go.
/// Parses Flow <pipeline> XML into a PipelineDraft tree for the visual designer.
/// </summary>
public sealed class XmlImporterService
{
    private long _nextNodeId = 1000;

    public long NextNodeId() => Interlocked.Increment(ref _nextNodeId);

    public PipelineDraft ImportFromXml(
        string xmlContent,
        Func<long>? idGenerator = null,
        string? fallbackName = null,
        string? fallbackDescription = null,
        string? artifactTypeHint = null) =>
        ParsePipelineXml(xmlContent, fallbackName, fallbackDescription, artifactTypeHint, idGenerator);

    public PipelineDraft ParsePipelineXml(
        string xmlContent,
        string? fallbackName = null,
        string? fallbackDescription = null,
        string? artifactTypeHint = null,
        Func<long>? idGenerator = null)
    {
        var genId = idGenerator ?? NextNodeId;

        if (string.IsNullOrWhiteSpace(xmlContent))
        {
            var empty = CreateEmptyDraft(fallbackName ?? "default_pipeline", fallbackDescription ?? "");
            if (!string.IsNullOrWhiteSpace(artifactTypeHint))
                empty.ArtifactType = artifactTypeHint;
            return empty;
        }

        var xmlSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 10 * 1024 * 1024 // 10 MB limit
        };
        using var stringReader = new StringReader(xmlContent);
        using var xmlReader = XmlReader.Create(stringReader, xmlSettings);
        var doc = XDocument.Load(xmlReader, LoadOptions.PreserveWhitespace);
        var root = doc.Root ?? throw new InvalidOperationException("XML document has no root element.");
        var rootName = root.Name.LocalName;

        // Support <flow_cli_options> or <options> for Options artifacts (options.xsd compliant + legacy <option> fallback)
        if (string.Equals(rootName, "flow_cli_options", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rootName, "options", StringComparison.OrdinalIgnoreCase))
        {
            var optDraft = new PipelineDraft
            {
                ArtifactType = "options",
                Name = fallbackName ?? "OPTIONS_DEFAULT",
                Description = fallbackDescription ?? "Flow CLI Options",
                UpdatedAt = DateTime.UtcNow
            };

            var optionsContainer = string.Equals(rootName, "flow_cli_options", StringComparison.OrdinalIgnoreCase)
                ? (root.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, "options", StringComparison.OrdinalIgnoreCase)) ?? root)
                : root;

            foreach (var elem in optionsContainer.Elements())
            {
                var tag = elem.Name.LocalName;
                if (string.Equals(tag, "option", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tag, "param", StringComparison.OrdinalIgnoreCase))
                {
                    // Legacy <option flag="--node-output">true</option> fallback
                    var rawFlag = elem.Attribute("flag")?.Value
                               ?? elem.Attribute("name")?.Value
                               ?? "vars";
                    var normTag = XmlGeneratorService.NormalizeOptionTagName(rawFlag);
                    var val = elem.Attribute("value")?.Value
                           ?? elem.Value?.Trim()
                           ?? string.Empty;
                    if (XmlGeneratorService.BooleanOptionElements.Contains(normTag) && string.IsNullOrEmpty(val))
                    {
                        val = "true";
                    }
                    optDraft.OptionEntries.Add(new FlowOptionEntry
                    {
                        Id = genId(),
                        Flag = normTag,
                        Value = val
                    });
                }
                else
                {
                    // options.xsd compliant element: <node-output>true</node-output>, <format>json</format>, <vars>ENV=DEV</vars>, etc.
                    var normTag = XmlGeneratorService.NormalizeOptionTagName(tag);
                    var val = elem.Value?.Trim() ?? string.Empty;
                    if (XmlGeneratorService.BooleanOptionElements.Contains(normTag) && string.IsNullOrEmpty(val))
                    {
                        val = "true";
                    }
                    optDraft.OptionEntries.Add(new FlowOptionEntry
                    {
                        Id = genId(),
                        Flag = normTag,
                        Value = val
                    });
                }
            }

            return optDraft;
        }

        if (!string.Equals(rootName, "pipeline", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(rootName, "config", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Root XML element must be <pipeline>, <config>, or <flow_cli_options>.");
        }

        var name = root.Attribute("name")?.Value?.Trim()
                ?? root.Attribute("id")?.Value?.Trim()
                ?? fallbackName
                ?? "imported_pipeline";

        var desc = root.Attribute("description")?.Value?.Trim()
                ?? fallbackDescription
                ?? string.Empty;

        var draft = new PipelineDraft
        {
            ArtifactType = !string.IsNullOrWhiteSpace(artifactTypeHint)
                ? artifactTypeHint
                : (string.Equals(rootName, "config", StringComparison.OrdinalIgnoreCase) ? "config" : "pipeline"),
            Name = name,
            Description = desc,
            UpdatedAt = DateTime.UtcNow
        };

        var varSection = root.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, "variables", StringComparison.OrdinalIgnoreCase));
        if (varSection is not null)
        {
            int seq = 1;
            foreach (var elem in varSection.Elements())
            {
                draft.VariableNodes.Add(ParseElementTree("variables", elem, null, seq++, genId));
            }
        }

        var dbSection = root.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, "databases", StringComparison.OrdinalIgnoreCase));
        if (dbSection is not null)
        {
            int seq = 1;
            foreach (var elem in dbSection.Elements())
            {
                draft.DatabaseNodes.Add(ParseElementTree("databases", elem, null, seq++, genId));
            }
        }

        var preSection = root.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, "preflight", StringComparison.OrdinalIgnoreCase));
        if (preSection is not null)
        {
            int seq = 1;
            foreach (var elem in preSection.Elements())
            {
                draft.PreflightNodes.Add(ParseElementTree("preflight", elem, null, seq++, genId));
            }
        }

        var flowSection = root.Elements().FirstOrDefault(e =>
            string.Equals(e.Name.LocalName, "flow", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.Name.LocalName, "scripts", StringComparison.OrdinalIgnoreCase));

        if (flowSection is not null)
        {
            int seq = 1;
            foreach (var elem in flowSection.Elements())
            {
                draft.FlowNodes.Add(ParseElementTree("flow", elem, null, seq++, genId));
            }
        }

        return draft;
    }

    public PipelineDraft CreateEmptyDraft(string name, string description) => new()
    {
        Name = string.IsNullOrWhiteSpace(name) ? "new_pipeline" : name.Trim(),
        Description = description ?? string.Empty,
        UpdatedAt = DateTime.UtcNow
    };

    public void EnsureIfBranches(PipelineNode ifNode, Func<long>? idGenerator = null)
    {
        var genId = idGenerator ?? NextNodeId;
        if (!string.Equals(ifNode.NodeType, "if", StringComparison.OrdinalIgnoreCase))
            return;

        var thenBranch = ifNode.GetThenBranch();
        if (thenBranch is null)
        {
            ifNode.Children.Insert(0, new PipelineNode
            {
                Id = genId(),
                ParentNodeId = ifNode.Id,
                Section = ifNode.Section,
                NodeType = "then",
                SequenceOrder = 1
            });
        }

        var elseBranch = ifNode.GetElseBranch();
        if (elseBranch is null)
        {
            ifNode.Children.Add(new PipelineNode
            {
                Id = genId(),
                ParentNodeId = ifNode.Id,
                Section = ifNode.Section,
                NodeType = "else",
                SequenceOrder = 2
            });
        }
    }

    private PipelineNode ParseElementTree(string section, XElement elem, long? parentId, int sequenceOrder, Func<long> genId)
    {
        var tag = elem.Name.LocalName;
        var nodeId = genId();
        var attrs = ExtractAttributes(elem);

        if (string.Equals(tag, "if", StringComparison.OrdinalIgnoreCase))
        {
            var ifNode = new PipelineNode
            {
                Id = nodeId,
                ParentNodeId = parentId,
                Section = section,
                NodeType = "if",
                SequenceOrder = sequenceOrder,
                Attributes = attrs
            };

            var thenNode = new PipelineNode
            {
                Id = genId(),
                ParentNodeId = ifNode.Id,
                Section = section,
                NodeType = "then",
                SequenceOrder = 1
            };

            var elseNode = new PipelineNode
            {
                Id = genId(),
                ParentNodeId = ifNode.Id,
                Section = section,
                NodeType = "else",
                SequenceOrder = 2
            };

            ifNode.Children.Add(thenNode);
            ifNode.Children.Add(elseNode);

            int thenSeq = 1;
            int elseSeq = 1;

            foreach (var ch in elem.Elements())
            {
                var chTag = ch.Name.LocalName;
                if (string.Equals(chTag, "then", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var tch in ch.Elements())
                    {
                        thenNode.Children.Add(ParseElementTree(section, tch, thenNode.Id, thenSeq++, genId));
                    }
                }
                else if (string.Equals(chTag, "else", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var ech in ch.Elements())
                    {
                        elseNode.Children.Add(ParseElementTree(section, ech, elseNode.Id, elseSeq++, genId));
                    }
                }
                else
                {
                    thenNode.Children.Add(ParseElementTree(section, ch, thenNode.Id, thenSeq++, genId));
                }
            }

            return ifNode;
        }

        bool isContainerTag =
            string.Equals(tag, "group", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tag, "parallel", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tag, "foreach", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tag, "while", StringComparison.OrdinalIgnoreCase);

        var childElements = elem.Elements().ToList();
        var directText = ExtractDirectText(elem);

        if (isContainerTag || childElements.Count > 0)
        {
            var containerNode = new PipelineNode
            {
                Id = nodeId,
                ParentNodeId = parentId,
                Section = section,
                NodeType = tag,
                SequenceOrder = sequenceOrder,
                Attributes = attrs,
                ContentText = string.Equals(tag, "foreach", StringComparison.OrdinalIgnoreCase) ? directText : string.Empty
            };

            int childSeq = 1;
            foreach (var ch in childElements)
            {
                containerNode.Children.Add(ParseElementTree(section, ch, containerNode.Id, childSeq++, genId));
            }

            return containerNode;
        }

        return new PipelineNode
        {
            Id = nodeId,
            ParentNodeId = parentId,
            Section = section,
            NodeType = tag,
            SequenceOrder = sequenceOrder,
            Attributes = attrs,
            ContentText = ExtractLeafContent(elem)
        };
    }

    private static Dictionary<string, string> ExtractAttributes(XElement elem)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in elem.Attributes())
        {
            if (a.IsNamespaceDeclaration) continue;
            var local = a.Name.LocalName;
            var ns = a.Name.NamespaceName;
            if (local.StartsWith("xmlns", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(local, "noNamespaceSchemaLocation", StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrEmpty(ns) && ns.Contains("XMLSchema-instance", StringComparison.OrdinalIgnoreCase)) continue;

            dict[local] = a.Value;
        }
        return dict;
    }

    private static string ExtractDirectText(XElement elem)
    {
        var parts = new List<string>();
        foreach (var node in elem.Nodes())
        {
            if (node is XCData cdata)
            {
                var txt = cdata.Value.Trim();
                if (!string.IsNullOrEmpty(txt)) parts.Add(txt);
            }
            else if (node is XText textNode)
            {
                var txt = textNode.Value.Trim();
                if (!string.IsNullOrEmpty(txt)) parts.Add(txt);
            }
        }
        return string.Join("\n", parts);
    }

    private static string ExtractLeafContent(XElement elem)
    {
        // Preserve raw value if CDATA or text
        var text = elem.Value ?? string.Empty;
        return DedentLines(text);
    }

    private static string DedentLines(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var lines = raw.Replace("\r\n", "\n").Split('\n').ToList();

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
            lines.RemoveAt(0);
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
            lines.RemoveAt(lines.Count - 1);

        if (lines.Count == 0) return string.Empty;

        int minIndent = int.MaxValue;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            int spaces = 0;
            while (spaces < line.Length && (line[spaces] == ' ' || line[spaces] == '\t'))
                spaces++;
            if (spaces < minIndent) minIndent = spaces;
        }

        if (minIndent > 0 && minIndent < int.MaxValue)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Length >= minIndent)
                    lines[i] = lines[i][minIndent..];
            }
        }

        return string.Join("\n", lines);
    }
}
