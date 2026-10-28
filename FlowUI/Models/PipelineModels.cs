using System.Text.Json.Serialization;

namespace FlowUI.Models;

/// <summary>
/// Represents a single node in the visual pipeline canvas tree.
/// Modeled after builder.PipelineNode in go-flow/builder.
/// </summary>
public sealed class PipelineNode
{
    public long Id
    {
        get; set;
    }
    public long? ParentNodeId
    {
        get; set;
    }
    public string Section { get; set; } = "flow"; // variables, databases, preflight, flow
    public string NodeType { get; set; } = string.Empty;
    public int SequenceOrder
    {
        get; set;
    }
    public Dictionary<string, string> Attributes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string ContentText { get; set; } = string.Empty;
    public List<PipelineNode> Children { get; set; } = new();

    public string DisplayName
    {
        get
        {
            if (Attributes.TryGetValue("name", out var name) && !string.IsNullOrWhiteSpace(name))
                return name;
            if (Attributes.TryGetValue("id", out var id) && !string.IsNullOrWhiteSpace(id))
                return id;
            return string.Empty;
        }
    }

    public PipelineNode? GetThenBranch() =>
        Children.FirstOrDefault(c => string.Equals(c.NodeType, "then", StringComparison.OrdinalIgnoreCase));

    public PipelineNode? GetElseBranch() =>
        Children.FirstOrDefault(c => string.Equals(c.NodeType, "else", StringComparison.OrdinalIgnoreCase));

    public PipelineNode Clone(Func<long> nextIdGenerator, long? newParentId = null)
    {
        var newId = nextIdGenerator();
        var copy = new PipelineNode
        {
            Id = newId,
            ParentNodeId = newParentId,
            Section = Section,
            NodeType = NodeType,
            SequenceOrder = SequenceOrder,
            Attributes = new Dictionary<string, string>(Attributes, StringComparer.OrdinalIgnoreCase),
            ContentText = ContentText,
            Children = new List<PipelineNode>()
        };

        foreach (var child in Children)
        {
            copy.Children.Add(child.Clone(nextIdGenerator, newId));
        }

        return copy;
    }
}

/// <summary>
/// Represents a single CLI option entry (<option flag="...">value</option>) inside <flow_cli_options>.
/// </summary>
public sealed class FlowOptionEntry
{
    public long Id
    {
        get; set;
    }
    public string Flag { get; set; } = "--var";
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Represents an active artifact draft (pipeline, config, or options) loaded in the visual designer.
/// </summary>
public sealed class PipelineDraft
{
    public int? ApiArtifactId
    {
        get; set;
    }
    public string ArtifactType { get; set; } = "pipeline"; // "pipeline", "config", or "options"
    public string Name { get; set; } = "default_pipeline";
    public string Description { get; set; } = "Visual pipeline designed in Flow Builder";
    public List<PipelineNode> VariableNodes { get; set; } = new();
    public List<PipelineNode> DatabaseNodes { get; set; } = new();
    public List<PipelineNode> PreflightNodes { get; set; } = new();
    public List<PipelineNode> FlowNodes { get; set; } = new();
    public List<FlowOptionEntry> OptionEntries { get; set; } = new();
    public bool Encrypted
    {
        get; set;
    }
    public bool Signed
    {
        get; set;
    }
    public string SignatureFormat { get; set; } = string.Empty;
    public string? SignedEnvelopeContent
    {
        get; set;
    }
    public string? DetachedSignatureContent
    {
        get; set;
    }
    public CryptoVerifyResponse? LastVerification
    {
        get; set;
    }
    public bool IsDirty
    {
        get; set;
    }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Attribute metadata for a Flow component in the catalog.
/// </summary>
public sealed class AttributeMeta
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "text"; // text, select, boolean, number, textarea

    [JsonPropertyName("required")]
    public bool Required
    {
        get; set;
    }

    [JsonPropertyName("default")]
    public string Default { get; set; } = string.Empty;

    [JsonPropertyName("options")]
    public List<string>? Options
    {
        get; set;
    }

    [JsonPropertyName("placeholder")]
    public string Placeholder { get; set; } = string.Empty;

    [JsonPropertyName("help")]
    public string Help { get; set; } = string.Empty;
}

/// <summary>
/// Component metadata in the Flow Builder component palette.
/// </summary>
public sealed class ComponentMeta
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("tag")]
    public string Tag { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("section")]
    public string Section { get; set; } = "flow";

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("has_content")]
    public bool HasContent
    {
        get; set;
    }

    [JsonPropertyName("content_label")]
    public string ContentLabel { get; set; } = "Content / Script";

    [JsonPropertyName("attributes")]
    public List<AttributeMeta> Attributes { get; set; } = new();
}

/// <summary>
/// Represents an artifact record stored in go-flowAPI (pipeline, options, or config).
/// </summary>
public sealed class ArtifactRecord
{
    [JsonPropertyName("id")]
    public int Id
    {
        get; set;
    }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "pipeline";

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("xml_content")]
    public string XmlContent
    {
        get => Content;
        set
        {
            if (!string.IsNullOrEmpty(value))
                Content = value;
        }
    }

    [JsonPropertyName("encrypted")]
    public bool Encrypted
    {
        get; set;
    }

    [JsonPropertyName("signed")]
    public bool Signed
    {
        get; set;
    }

    [JsonPropertyName("signature_format")]
    public string SignatureFormat { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Saved database connection profile for testing and pipeline reference.
/// </summary>
public sealed class SavedDatabaseProfile
{
    public int Id
    {
        get; set;
    }
    public string Name { get; set; } = string.Empty;
    public string Driver { get; set; } = "sqlserver";
    public string ConnectionString { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int MaxOpenConns { get; set; } = 10;
    public int MaxIdleConns { get; set; } = 5;
    public bool? LastTestSucceeded
    {
        get; set;
    }
    public long? LastTestLatencyMs
    {
        get; set;
    }
    public string? LastTestMessage
    {
        get; set;
    }
    public DateTime? LastTestedAt
    {
        get; set;
    }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
