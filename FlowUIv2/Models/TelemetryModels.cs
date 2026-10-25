using System.Text.Json.Serialization;

namespace FlowUIv2.Models;

public sealed class PipelineRunRecord
{
    [JsonPropertyName("run_id")]
    public string RunId { get; set; } = string.Empty;

    [JsonPropertyName("file_path")]
    public string FilePath { get; set; } = string.Empty;

    [JsonPropertyName("config_path")]
    public string ConfigPath { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("started_at")]
    public DateTime StartedAt { get; set; }

    [JsonPropertyName("finished_at")]
    public DateTime FinishedAt { get; set; }

    [JsonPropertyName("duration_ms")]
    public long DurationMs { get; set; }

    [JsonPropertyName("task_count")]
    public int TaskCount { get; set; }

    [JsonPropertyName("user_name")]
    public string UserName { get; set; } = string.Empty;

    [JsonPropertyName("os_user_name")]
    public string OsUserName { get; set; } = string.Empty;

    [JsonPropertyName("db_user_name")]
    public string DbUserName { get; set; } = string.Empty;

    [JsonPropertyName("spid")]
    public int Spid { get; set; }

    [JsonPropertyName("application_name")]
    public string ApplicationName { get; set; } = string.Empty;

    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = string.Empty;

    [JsonPropertyName("options_path")]
    public string OptionsPath { get; set; } = string.Empty;

    [JsonPropertyName("error_class")]
    public string ErrorClass { get; set; } = string.Empty;

    [JsonPropertyName("error_message")]
    public string ErrorMessage { get; set; } = string.Empty;
}

public sealed class PipelineEventRecord
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("run_id")]
    public string RunId { get; set; } = string.Empty;

    [JsonPropertyName("execution_id")]
    public string ExecutionId { get; set; } = string.Empty;

    [JsonPropertyName("sequence_num")]
    public long SequenceNum { get; set; }

    [JsonPropertyName("occurred_at")]
    public DateTime OccurredAt { get; set; }

    [JsonPropertyName("event_type")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("node_kind")]
    public string NodeKind { get; set; } = string.Empty;

    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("user_name")]
    public string UserName { get; set; } = string.Empty;

    [JsonPropertyName("os_user_name")]
    public string OsUserName { get; set; } = string.Empty;

    [JsonPropertyName("db_user_name")]
    public string DbUserName { get; set; } = string.Empty;

    [JsonPropertyName("spid")]
    public int Spid { get; set; }

    [JsonPropertyName("application_name")]
    public string ApplicationName { get; set; } = string.Empty;

    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = string.Empty;

    [JsonPropertyName("options_path")]
    public string OptionsPath { get; set; } = string.Empty;

    [JsonPropertyName("error_message")]
    public string ErrorMessage { get; set; } = string.Empty;

    [JsonPropertyName("rows_read")]
    public long RowsRead { get; set; }

    [JsonPropertyName("rows_written")]
    public long RowsWritten { get; set; }

    [JsonPropertyName("rows_affected")]
    public long RowsAffected { get; set; }

    [JsonPropertyName("node_output")]
    public string NodeOutput { get; set; } = string.Empty;
}

public sealed class ApiAuditRecord
{
    [JsonPropertyName("event_id")]
    public long EventId { get; set; }

    [JsonPropertyName("occurred_at")]
    public DateTime OccurredAt { get; set; }

    [JsonPropertyName("event_type")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("http_method")]
    public string HttpMethod { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("artifact_type")]
    public string ArtifactType { get; set; } = string.Empty;

    [JsonPropertyName("artifact_name")]
    public string ArtifactName { get; set; } = string.Empty;

    [JsonPropertyName("run_id")]
    public string RunId { get; set; } = string.Empty;

    [JsonPropertyName("auth_user")]
    public string AuthUser { get; set; } = string.Empty;

    [JsonPropertyName("remote_addr")]
    public string RemoteAddr { get; set; } = string.Empty;

    [JsonPropertyName("status_code")]
    public int StatusCode { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("duration_ms")]
    public long DurationMs { get; set; }

    [JsonPropertyName("error_message")]
    public string ErrorMessage { get; set; } = string.Empty;

    [JsonPropertyName("details_json")]
    public string DetailsJson { get; set; } = string.Empty;
}

/// <summary>
/// UI event telemetry record logged to dbo.flow_ui_events in DB.
/// </summary>
public sealed class UiEventRecord
{
    public long EventId { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public string Username { get; set; } = "anonymous";
    public string EventCategory { get; set; } = string.Empty;
    public string EventAction { get; set; } = string.Empty;
    public string? ArtifactType { get; set; }
    public string? ArtifactName { get; set; }
    public string Status { get; set; } = "succeeded";
    public long? DurationMs { get; set; }
    public string? ClientIp { get; set; }
    public string? ErrorMessage { get; set; }
    public string? DetailsJson { get; set; }
}

public sealed class ExecutionStreamNodeEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("event_type")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("node_kind")]
    public string NodeKind
    {
        get => Kind;
        set { if (!string.IsNullOrEmpty(value)) Kind = value; }
    }

    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("sequence")]
    public long SequenceNum { get; set; }

    [JsonPropertyName("sequence_num")]
    public long SequenceNumAlt
    {
        get => SequenceNum;
        set { if (value > 0) SequenceNum = value; }
    }

    [JsonPropertyName("run_id")]
    public string RunId { get; set; } = string.Empty;

    [JsonPropertyName("execution_id")]
    public string ExecutionId { get; set; } = string.Empty;

    [JsonPropertyName("rows_read")]
    public long RowsRead { get; set; }

    [JsonPropertyName("rows_written")]
    public long RowsWritten { get; set; }

    [JsonPropertyName("rows_affected")]
    public long RowsAffected { get; set; }

    [JsonPropertyName("error")]
    public string ErrorMessage { get; set; } = string.Empty;

    [JsonPropertyName("error_message")]
    public string ErrorMessageAlt
    {
        get => ErrorMessage;
        set { if (!string.IsNullOrEmpty(value)) ErrorMessage = value; }
    }

    [JsonPropertyName("duration")]
    public string Duration { get; set; } = string.Empty;

    [JsonPropertyName("node_output")]
    public string NodeOutput { get; set; } = string.Empty;

    [JsonPropertyName("formatted_output")]
    public string FormattedOutput { get; set; } = string.Empty;

    [JsonPropertyName("results")]
    public List<ScriptStepResultDto>? Results { get; set; }
}

public sealed class ScriptStepResultDto
{
    [JsonPropertyName("script_id")]
    public string ScriptId { get; set; } = string.Empty;

    [JsonPropertyName("node_id")]
    public string NodeIdAlt
    {
        get => ScriptId;
        set { if (!string.IsNullOrEmpty(value)) ScriptId = value; }
    }

    [JsonIgnore]
    public string NodeId
    {
        get => ScriptId;
        set => ScriptId = value;
    }

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("return_code")]
    public string ReturnCodeRaw { get; set; } = string.Empty;

    [JsonPropertyName("duration")]
    public string DurationRaw { get; set; } = string.Empty;

    [JsonPropertyName("results_string")]
    public string ResultsString { get; set; } = string.Empty;

    [JsonPropertyName("output")]
    public string OutputRaw { get; set; } = string.Empty;

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? ExtraFields { get; set; }

    [JsonIgnore]
    public string EffectiveNodeOutput =>
        !string.IsNullOrEmpty(ResultsString)
            ? ResultsString
            : OutputRaw;

    [JsonIgnore]
    public int EffectiveReturnCode
    {
        get
        {
            if (int.TryParse(ReturnCodeRaw, out var rc)) return rc;
            return 0;
        }
    }

    [JsonIgnore]
    public string EffectiveStatus
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Status)) return Status;
            if (!string.IsNullOrWhiteSpace(Error)) return "failed";
            if (!string.IsNullOrWhiteSpace(ReturnCodeRaw) && ReturnCodeRaw != "0") return "failed";
            return EffectiveReturnCode == 0 ? "succeeded" : "failed";
        }
    }

    [JsonIgnore]
    public string EffectiveDurationDisplay
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(DurationRaw))
            {
                if (long.TryParse(DurationRaw, out var ns) && ns > 0)
                    return $"{ns / 1_000_000.0:F1} ms";
                return DurationRaw;
            }
            return "-";
        }
    }
}

public sealed class ExecutionStepResult
{
    public string ExecutionId { get; set; } = string.Empty;
    public string NodeId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = "running";
    public int ReturnCode { get; set; }
    public string DurationDisplay { get; set; } = "-";
    public string OutputSummary { get; set; } = string.Empty;
    public string NodeOutput { get; set; } = string.Empty;
    public bool IsExpanded { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
}

public sealed class ExecutePipelineRequest
{
    [JsonPropertyName("artifact_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ArtifactType { get; set; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Id { get; set; }

    [JsonPropertyName("pipeline_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PipelineId { get; set; }

    [JsonPropertyName("file")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PipelineName { get; set; }

    [JsonPropertyName("file_xml")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PipelineXml { get; set; }

    [JsonPropertyName("options_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? OptionsId { get; set; }

    [JsonPropertyName("options")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OptionsName { get; set; }

    [JsonPropertyName("options_xml")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OptionsXml { get; set; }

    [JsonPropertyName("config_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ConfigId { get; set; }

    [JsonPropertyName("config")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ConfigName { get; set; }

    [JsonPropertyName("config_xml")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ConfigXml { get; set; }

    [JsonPropertyName("vars")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Variables { get; set; }

    [JsonPropertyName("vars_raw")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VarsRaw { get; set; }

    [JsonPropertyName("preflight")]
    public bool PreflightOnly { get; set; }

    [JsonPropertyName("validate")]
    public bool ValidateOnly { get; set; }

    [JsonPropertyName("format")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Format { get; set; }

    [JsonPropertyName("node_output")]
    public bool LogNodeOutput { get; set; }

    [JsonPropertyName("debug")]
    public bool Debug { get; set; }

    [JsonPropertyName("encrypted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Encrypted { get; set; }

    [JsonPropertyName("secure_key")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SecureKey { get; set; }

    [JsonPropertyName("verify_signature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool VerifySignature { get; set; }

    [JsonPropertyName("public_key")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PublicKey { get; set; }

    [JsonPropertyName("verify_pub_key")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VerifyPubKey
    {
        get => PublicKey;
        set => PublicKey = value;
    }

    [JsonPropertyName("cert")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cert { get; set; }

    [JsonPropertyName("verify_cert")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VerifyCert
    {
        get => Cert;
        set => Cert = value;
    }

    [JsonPropertyName("ca_cert")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CaCert { get; set; }

    [JsonPropertyName("verify_ca_cert")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VerifyCaCert
    {
        get => CaCert;
        set => CaCert = value;
    }

    [JsonPropertyName("keyring")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Keyring { get; set; }

    [JsonPropertyName("verify_keyring")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VerifyKeyring
    {
        get => Keyring;
        set => Keyring = value;
    }

    [JsonPropertyName("signature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Signature { get; set; }

    [JsonPropertyName("pipeline_sig")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PipelineSig
    {
        get => Signature;
        set => Signature = value;
    }
}

public sealed class ExecutePipelineResponse
{
    [JsonPropertyName("run_id")]
    public string RunId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("started_at")]
    public DateTime StartedAt { get; set; }

    [JsonPropertyName("finished_at")]
    public DateTime FinishedAt { get; set; }

    [JsonPropertyName("duration_ms")]
    public long DurationMs { get; set; }

    [JsonPropertyName("format")]
    public string Format { get; set; } = string.Empty;

    [JsonPropertyName("formatted_output")]
    public string FormattedOutput { get; set; } = string.Empty;

    [JsonPropertyName("results")]
    public List<ScriptStepResultDto>? Results { get; set; }

    [JsonPropertyName("error_message")]
    public string ErrorMessage { get; set; } = string.Empty;

    [JsonPropertyName("error")]
    public string Error
    {
        get => ErrorMessage;
        set { if (!string.IsNullOrEmpty(value)) ErrorMessage = value; }
    }
}

public sealed class DbPingTestResult
{
    [JsonPropertyName("connected")]
    public bool Connected { get; set; }

    [JsonPropertyName("dialect")]
    public string Dialect { get; set; } = string.Empty;

    [JsonPropertyName("spid")]
    public int Spid { get; set; }

    [JsonPropertyName("db_user")]
    public string DbUser { get; set; } = string.Empty;

    [JsonPropertyName("application_name")]
    public string ApplicationName { get; set; } = string.Empty;

    [JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;

    public long LatencyMs { get; set; }
}

public sealed class ApiUserRecord
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("permission")]
    public string Permission { get; set; } = "read-only";

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
