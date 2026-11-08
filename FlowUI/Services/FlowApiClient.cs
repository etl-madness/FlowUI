using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlowUI.Config;
using FlowUI.Models;

namespace FlowUI.Services;

/// <summary>
/// HTTP client for communicating with go-flowAPI using Basic Auth configured via FLOW_API_BASIC_AUTH.
/// </summary>
public sealed class FlowApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<FlowApiClient> _logger;
    private static readonly JsonSerializerOptions JsonOpts = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
        opts.Converters.Add(new LenientInt32Converter());
        opts.Converters.Add(new LenientInt64Converter());
        opts.Converters.Add(new LenientStringConverter());
        return opts;
    }

    private sealed class LenientStringConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return string.Empty;
                case JsonTokenType.String:
                    return reader.GetString() ?? string.Empty;
                case JsonTokenType.Number:
                    if (reader.TryGetInt64(out var l)) return l.ToString();
                    if (reader.TryGetDouble(out var d)) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    return string.Empty;
                case JsonTokenType.True:
                    return "true";
                case JsonTokenType.False:
                    return "false";
                case JsonTokenType.StartObject:
                case JsonTokenType.StartArray:
                    using (var doc = JsonDocument.ParseValue(ref reader))
                    {
                        return doc.RootElement.GetRawText();
                    }
                default:
                    return string.Empty;
            }
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value);
    }

    private sealed class LenientInt32Converter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return 0;
            if (reader.TokenType == JsonTokenType.Number)
            {
                if (reader.TryGetInt32(out var i)) return i;
                if (reader.TryGetDouble(out var d)) return (int)d;
            }
            if (reader.TokenType == JsonTokenType.String)
            {
                var s = reader.GetString();
                if (int.TryParse(s, out var i)) return i;
                return 0;
            }
            return 0;
        }

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value);
    }

    private sealed class LenientInt64Converter : JsonConverter<long>
    {
        public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return 0L;
            if (reader.TokenType == JsonTokenType.Number)
            {
                if (reader.TryGetInt64(out var l)) return l;
                if (reader.TryGetDouble(out var d)) return (long)d;
            }
            if (reader.TokenType == JsonTokenType.String)
            {
                var s = reader.GetString();
                if (long.TryParse(s, out var l)) return l;
                return 0L;
            }
            return 0L;
        }

        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value);
    }

    public FlowApiClient(HttpClient http, ILogger<FlowApiClient> logger)
    {
        _http = http;
        _logger = logger;

        if (_http.BaseAddress is null && !string.IsNullOrWhiteSpace(AppConfig.ApiBaseUrl))
        {
            _http.BaseAddress = new Uri(AppConfig.ApiBaseUrl);
        }

        // Only apply a pre-configured service token automatically when using Windows Integrated auth.
        // In ApiBasic mode, Authorization must come exclusively from the user's interactive login.
        if (string.Equals(AppConfig.AuthMode, "Windows", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(AppConfig.ApiBasicAuthToken))
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", AppConfig.ApiBasicAuthToken);
        }
    }

    public string BaseUrl => _http.BaseAddress?.ToString().TrimEnd('/') ?? AppConfig.ApiBaseUrl;

    /// <summary>
    /// Updates the HTTP Basic Auth credentials used for API calls in the current session.
    /// </summary>
    public void SetBasicAuthCredentials(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            ClearAuth();
            return;
        }
        var raw = $"{username.Trim()}:{password}";
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        SetBasicAuthToken(token);
    }

    private const string WindowsIntegratedAuthMarker = "__WINDOWS_INTEGRATED__";
    private const string HeaderWindowsUser = "X-Flow-Windows-User";
    private const string HeaderWindowsGroups = "X-Flow-Windows-Groups";
    private const string HeaderSecureKey = "X-Flow-Secure-Key";

    /// <summary>
    /// Sets or clears the default X-Flow-Secure-Key header on this scoped HttpClient instance.
    /// </summary>
    public void SetSecureKeyHeader(string? secureKey)
    {
        _http.DefaultRequestHeaders.Remove(HeaderSecureKey);
        if (!string.IsNullOrWhiteSpace(secureKey))
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation(HeaderSecureKey, secureKey.Trim());
        }
    }

    /// <summary>
    /// Updates the Base64 Basic Auth token on this scoped HttpClient instance from the logged-in user's credentials.
    /// </summary>
    public void SetBasicAuthToken(string? base64Token)
    {
        _http.DefaultRequestHeaders.Remove(HeaderWindowsUser);
        _http.DefaultRequestHeaders.Remove(HeaderWindowsGroups);

        if (!string.IsNullOrWhiteSpace(base64Token))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64Token.Trim());
        }
        else
        {
            _http.DefaultRequestHeaders.Authorization = null;
        }
    }

    /// <summary>
    /// Configures the scoped HttpClient to authenticate against go-flowAPI using the active Windows user
    /// and their AD / Windows group memberships (matched against dbo.flow_api_users).
    /// </summary>
    public void SetWindowsIntegratedAuth(string windowsUser, IEnumerable<string>? windowsGroups = null, string? windowsPassword = null)
    {
        var trimmedUser = windowsUser?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedUser))
        {
            ClearAuth();
            return;
        }

        _http.DefaultRequestHeaders.Remove(HeaderWindowsUser);
        _http.DefaultRequestHeaders.Remove(HeaderWindowsGroups);

        var passOrMarker = string.IsNullOrEmpty(windowsPassword) ? WindowsIntegratedAuthMarker : windowsPassword;
        var rawBasic = $"{trimmedUser}:{passOrMarker}";
        var basicToken = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawBasic));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicToken);
        _http.DefaultRequestHeaders.TryAddWithoutValidation(HeaderWindowsUser, trimmedUser);

        var groupList = windowsGroups?
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();

        if (groupList.Length > 0)
        {
            var groupsJson = JsonSerializer.Serialize(groupList);
            var groupsB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(groupsJson));
            _http.DefaultRequestHeaders.TryAddWithoutValidation(HeaderWindowsGroups, groupsB64);
        }
    }

    /// <summary>
    /// Clears any active HTTP Basic Auth or Windows Integrated headers on logout.
    /// </summary>
    public void ClearAuth()
    {
        _http.DefaultRequestHeaders.Authorization = null;
        _http.DefaultRequestHeaders.Remove(HeaderWindowsUser);
        _http.DefaultRequestHeaders.Remove(HeaderWindowsGroups);
    }

    /// <summary>
    /// Applies the optional service token (FLOW_API_BASIC_AUTH) when running in Windows Integrated mode,
    /// or clears auth when none is configured.
    /// </summary>
    public void ResetToDefaultAuth() => SetBasicAuthToken(AppConfig.ApiBasicAuthToken);

    /// <summary>
    /// Maps a go-flowAPI permission string ("read-only", "execution-only", "upload", "write", "all")
    /// to an AppUser with granular UI CRUD + Execute permissions.
    /// </summary>
    public static AppUser MapApiPermissionToUser(
        string username,
        string? apiPermission,
        string providerName,
        string? description = null,
        bool isActive = true)
    {
        var perm = (apiPermission ?? "read-only").Trim().ToLowerInvariant();
        bool isAdmin = perm is "all" or "admin" or "*";
        bool canWrite = isAdmin || perm is "write" or "upload" or "write-only" or "upload-only" or "read-write" or "editor";
        bool canExec = isAdmin || canWrite || perm is "execution-only" or "execute" or "exec" or "run";

        return new AppUser
        {
            Username = username.Trim(),
            CanCreate = canWrite,
            CanRead = true,
            CanUpdate = canWrite,
            CanDelete = canWrite,
            CanExecute = canExec,
            IsAdmin = isAdmin,
            IsActive = isActive,
            Description = string.IsNullOrWhiteSpace(description)
                ? $"go-flowAPI user ({perm})"
                : description,
            AuthProvider = providerName
        };
    }

    public async Task<(bool IsOnline, string AuthenticatedAs, string Permission, string StatusSummary)> GetAuthenticatedIdentityAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync("/api/v1/health", ct);
            if (!resp.IsSuccessStatusCode)
            {
                return (false, "", "read-only", $"HTTP {(int)resp.StatusCode} ({resp.ReasonPhrase})");
            }

            var raw = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "ok" : "ok";
            var dialect = root.TryGetProperty("dialect", out var d) ? d.GetString() ?? "sqlserver" : "sqlserver";
            var dbUser = root.TryGetProperty("db_user", out var u) ? u.GetString() ?? "" : "";
            var authAs = root.TryGetProperty("authenticated_as", out var a) ? a.GetString() ?? "" : "";
            var perm = root.TryGetProperty("permission", out var p) ? p.GetString() ?? "" : "";
            return (true, authAs, perm, $"{status} ({dialect}{(string.IsNullOrWhiteSpace(dbUser) ? "" : $" as {dbUser}")})");
        }
        catch (Exception ex)
        {
            return (false, "", "read-only", ex.Message);
        }
    }

    public async Task<(bool IsOnline, string StatusSummary)> CheckHealthAsync(CancellationToken ct = default)
    {
        var (online, _, _, summary) = await GetAuthenticatedIdentityAsync(ct);
        return (online, summary);
    }

    // =========================================================================
    // Artifact CRUD (pipeline, config, options)
    // =========================================================================

    private async Task<List<T>> GetListFromJsonAsync<T>(string url, string wrapperProperty, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<T>>(json, JsonOpts) ?? new();
        }

        if (doc.RootElement.TryGetProperty(wrapperProperty, out var itemsElem) && itemsElem.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<T>>(itemsElem.GetRawText(), JsonOpts) ?? new();
        }

        return new List<T>();
    }

    public Task<List<ArtifactRecord>> ListArtifactsAsync(
        string artifactType = "pipeline",
        bool includeContent = true,
        CancellationToken ct = default)
    {
        var url = $"/api/v1/artifacts/{Uri.EscapeDataString(artifactType)}?include_content={(includeContent ? "true" : "false")}";
        return GetListFromJsonAsync<ArtifactRecord>(url, "items", ct);
    }

    private async Task<ArtifactRecord?> GetArtifactCoreAsync(
        string basePath,
        string? secureKey,
        bool decrypt,
        CancellationToken ct)
    {
        var url = $"{basePath}?decrypt={(decrypt ? "true" : "false")}";
        if (!string.IsNullOrWhiteSpace(secureKey))
        {
            url += $"&secure_key={Uri.EscapeDataString(secureKey)}";
        }
        using var resp = await _http.GetAsync(url, ct);
        if (resp.IsSuccessStatusCode)
        {
            var json = await resp.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<ArtifactRecord>(json, JsonOpts);
        }

        var rawUrl = $"{basePath}?decrypt=false";
        using var rawResp = await _http.GetAsync(rawUrl, ct);
        if (!rawResp.IsSuccessStatusCode) return null;
        var rawJson = await rawResp.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<ArtifactRecord>(rawJson, JsonOpts);
    }

    public Task<ArtifactRecord?> GetArtifactByIdAsync(
        string artifactType,
        int id,
        string? secureKey = null,
        bool decrypt = true,
        CancellationToken ct = default) =>
        GetArtifactCoreAsync($"/api/v1/artifacts/{Uri.EscapeDataString(artifactType)}/id/{id}", secureKey, decrypt, ct);

    public Task<ArtifactRecord?> GetArtifactByNameAsync(
        string artifactType,
        string name,
        string? secureKey = null,
        bool decrypt = true,
        CancellationToken ct = default) =>
        GetArtifactCoreAsync($"/api/v1/artifacts/{Uri.EscapeDataString(artifactType)}/{Uri.EscapeDataString(name)}", secureKey, decrypt, ct);

    private async Task<ArtifactRecord> SendArtifactMutationAsync(
        HttpMethod method,
        string url,
        object payload,
        string errorPrefix,
        ArtifactRecord fallback,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        using var resp = await _http.SendAsync(req, ct);
        var respBody = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ExtractErrorMessage(respBody, $"{errorPrefix} (HTTP {(int)resp.StatusCode})"));
        }

        return JsonSerializer.Deserialize<ArtifactRecord>(respBody, JsonOpts) ?? fallback;
    }

    public Task<ArtifactRecord> UpsertArtifactAsync(
        string artifactType, string name, string description, string xmlContent,
        bool encrypted = false, string? secureKey = null, CancellationToken ct = default)
    {
        var url = $"/api/v1/artifacts/{Uri.EscapeDataString(artifactType)}?upsert=true"
                + (string.IsNullOrWhiteSpace(secureKey) ? "" : $"&secure_key={Uri.EscapeDataString(secureKey)}");
        var payload = new { name = name.Trim(), description = description ?? "", content = xmlContent, xml_content = xmlContent, encrypted, secure_key = secureKey ?? "", upsert = true };
        var fallback = new ArtifactRecord { Name = name, Description = description ?? "", Content = xmlContent, Type = artifactType, Encrypted = encrypted };
        return SendArtifactMutationAsync(HttpMethod.Post, url, payload, $"Failed saving {artifactType} '{name}'", fallback, ct);
    }

    public Task<ArtifactRecord> UpdateArtifactByIdAsync(
        string artifactType, int id, string description, string xmlContent,
        bool encrypted = false, string? secureKey = null, CancellationToken ct = default)
    {
        var url = $"/api/v1/artifacts/{Uri.EscapeDataString(artifactType)}/id/{id}"
                + (string.IsNullOrWhiteSpace(secureKey) ? "" : $"?secure_key={Uri.EscapeDataString(secureKey)}");
        var payload = new { description = description ?? "", content = xmlContent, xml_content = xmlContent, encrypted, secure_key = secureKey ?? "" };
        var fallback = new ArtifactRecord { Id = id, Description = description ?? "", Content = xmlContent, Type = artifactType, Encrypted = encrypted };
        return SendArtifactMutationAsync(HttpMethod.Put, url, payload, $"Failed updating {artifactType} #{id}", fallback, ct);
    }

    private async Task<bool> DeleteCoreAsync(string url, string errorPrefix, CancellationToken ct)
    {
        using var resp = await _http.DeleteAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(ExtractErrorMessage(body, errorPrefix));
        }
        return true;
    }

    public Task<bool> DeleteArtifactByIdAsync(string artifactType, int id, CancellationToken ct = default) =>
        DeleteCoreAsync($"/api/v1/artifacts/{Uri.EscapeDataString(artifactType)}/id/{id}", $"Failed deleting {artifactType} #{id}", ct);

    public Task<bool> DeleteArtifactByNameAsync(string artifactType, string name, CancellationToken ct = default) =>
        DeleteCoreAsync($"/api/v1/artifacts/{Uri.EscapeDataString(artifactType)}/{Uri.EscapeDataString(name)}", $"Failed deleting {artifactType} '{name}'", ct);

    // =========================================================================
    // Pipeline Validation & Execution (Sync + SSE Stream)
    // =========================================================================

    public async Task<(bool Valid, string Message)> ValidatePipelineXmlAsync(
        string xmlContent,
        CancellationToken ct = default)
    {
        var res = await ValidatePipelineAsync(new ExecutePipelineRequest
        {
            PipelineXml = xmlContent,
            ValidateOnly = true
        }, ct);

        var isValid = string.IsNullOrEmpty(res.ErrorMessage) &&
                      !string.Equals(res.Status, "INVALID", StringComparison.OrdinalIgnoreCase) &&
                      !string.Equals(res.Status, "FAILED", StringComparison.OrdinalIgnoreCase);

        var msg = isValid
            ? (string.IsNullOrEmpty(res.FormattedOutput) ? "Pipeline XML syntax and structure are valid." : res.FormattedOutput)
            : res.ErrorMessage;

        return (isValid, msg);
    }

    public async Task<ExecutePipelineResponse> ValidatePipelineAsync(
        ExecutePipelineRequest request,
        CancellationToken ct = default)
    {
        request.ValidateOnly = true;
        using var reqContent = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync("/api/v1/validate", reqContent, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            return new ExecutePipelineResponse
            {
                Status = "INVALID",
                ErrorMessage = ExtractErrorMessage(body, $"Validation failed (HTTP {(int)resp.StatusCode})")
            };
        }

        var parsed = JsonSerializer.Deserialize<ExecutePipelineResponse>(body, JsonOpts) ?? new ExecutePipelineResponse();
        if (string.IsNullOrEmpty(parsed.Status))
            parsed.Status = "VALID";
        return parsed;
    }

    public async Task<ExecutePipelineResponse> ExecutePipelineSyncAsync(
        ExecutePipelineRequest request,
        CancellationToken ct = default)
    {
        using var reqContent = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync("/api/v1/execute", reqContent, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        var parsed = TryDeserialize<ExecutePipelineResponse>(body);
        if (parsed is not null)
        {
            if (!resp.IsSuccessStatusCode && string.IsNullOrEmpty(parsed.ErrorMessage))
            {
                parsed.ErrorMessage = ExtractErrorMessage(body, $"Execution failed (HTTP {(int)resp.StatusCode})");
            }
            return parsed;
        }

        return new ExecutePipelineResponse
        {
            Status = resp.IsSuccessStatusCode ? "SUCCESS" : "FAILED",
            ErrorMessage = resp.IsSuccessStatusCode ? string.Empty : ExtractErrorMessage(body, $"HTTP {(int)resp.StatusCode}")
        };
    }

    /// <summary>
    /// Streams real-time SSE node execution events from POST /api/v1/execute/stream.
    /// </summary>
    public async Task ExecutePipelineStreamAsync(
        ExecutePipelineRequest request,
        Func<ExecutionStreamNodeEvent, Task> onNodeEvent,
        Func<ExecutionStreamNodeEvent, Task> onDoneEvent,
        CancellationToken ct = default)
    {
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/execute/stream")
        {
            Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
        };
        httpReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var resp = await _http.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var errBody = await resp.Content.ReadAsStringAsync(ct);
            var errMsg = ExtractErrorMessage(errBody, $"Stream execution failed (HTTP {(int)resp.StatusCode})");
            await onDoneEvent(new ExecutionStreamNodeEvent
            {
                Type = "done",
                Status = "FAILED",
                ErrorMessage = errMsg
            });
            return;
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string currentEvent = "message";
        var dataBuilder = new StringBuilder();
        bool doneDispatched = false;

        async Task DispatchBufferedFrameAsync()
        {
            if (dataBuilder.Length == 0)
                return;

            var payloadJson = dataBuilder.ToString().Trim();
            dataBuilder.Clear();

            var evt = TryDeserialize<ExecutionStreamNodeEvent>(payloadJson);
            if (evt is not null)
            {
                bool isDone =
                    string.Equals(currentEvent, "done", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(currentEvent, "complete", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(currentEvent, "completed", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(evt.Type, "done", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(evt.EventType, "done", StringComparison.OrdinalIgnoreCase) ||
                    (string.IsNullOrEmpty(evt.NodeId) && (evt.Results is not null || !string.IsNullOrEmpty(evt.FormattedOutput)));

                if (isDone)
                {
                    doneDispatched = true;
                    await onDoneEvent(evt);
                }
                else
                {
                    await onNodeEvent(evt);
                }
            }
            currentEvent = "message";
        }

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
            {
                await DispatchBufferedFrameAsync();
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                await DispatchBufferedFrameAsync();
                continue;
            }

            if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
            {
                currentEvent = line["event:".Length..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                dataBuilder.AppendLine(line["data:".Length..].Trim());
            }
            else if (line.TrimStart().StartsWith('{'))
            {
                dataBuilder.AppendLine(line.Trim());
            }
        }

        if (!doneDispatched)
        {
            await onDoneEvent(new ExecutionStreamNodeEvent
            {
                Type = "done",
                Status = "COMPLETED"
            });
        }
    }

    // =========================================================================
    // Telemetry & Audit Logs (/api/v1/runs, /api/v1/events, /api/v1/audit)
    // =========================================================================

    public Task<List<PipelineRunRecord>> GetPipelineRunsAsync(
        string? statusFilter = null,
        int limit = 150,
        CancellationToken ct = default)
    {
        var url = $"/api/v1/runs?limit={limit}";
        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            url += $"&status={Uri.EscapeDataString(statusFilter)}";
        }
        return GetListFromJsonAsync<PipelineRunRecord>(url, "runs", ct);
    }

    public Task<List<PipelineEventRecord>> GetPipelineEventsAsync(
        string runId,
        int limit = 500,
        CancellationToken ct = default)
    {
        var url = $"/api/v1/events?run_id={Uri.EscapeDataString(runId)}&limit={limit}";
        return GetListFromJsonAsync<PipelineEventRecord>(url, "events", ct);
    }

    public Task<List<ApiAuditRecord>> GetApiAuditLogsAsync(
        int limit = 200,
        string? statusFilter = null,
        CancellationToken ct = default)
    {
        var url = $"/api/v1/audit?limit={limit}";
        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            url += $"&status={Uri.EscapeDataString(statusFilter)}";
        }
        return GetListFromJsonAsync<ApiAuditRecord>(url, "api_events", ct);
    }

    // =========================================================================
    // Database Ping Test & API User Management
    // =========================================================================

    public async Task<DbPingTestResult> TestDatabaseViaApiAsync(
        string? connectionString = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var payload = new
            {
                dsn = connectionString ?? string.Empty,
                connection_string = connectionString ?? string.Empty
            };
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync("/api/v1/db/test", content, ct);
            sw.Stop();
            var body = await resp.Content.ReadAsStringAsync(ct);

            var parsed = TryDeserialize<DbPingTestResult>(body) ?? new DbPingTestResult();
            parsed.LatencyMs = sw.ElapsedMilliseconds;
            if (!resp.IsSuccessStatusCode)
            {
                parsed.Connected = false;
                parsed.Error = ExtractErrorMessage(body, $"HTTP {(int)resp.StatusCode}");
            }
            return parsed;
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new DbPingTestResult
            {
                Connected = false,
                LatencyMs = sw.ElapsedMilliseconds,
                Error = ex.Message
            };
        }
    }

    public Task<List<ApiUserRecord>> GetApiUsersAsync(CancellationToken ct = default) =>
        GetListFromJsonAsync<ApiUserRecord>("/api/v1/users", "users", ct);

    public async Task<ApiUserRecord?> UpsertApiUserAsync(
        string username,
        string password,
        string permission,
        string description,
        bool isActive = true,
        CancellationToken ct = default)
    {
        var payload = new
        {
            username,
            password,
            permission,
            description,
            is_active = isActive
        };
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync("/api/v1/users", content, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ExtractErrorMessage(body, $"Failed upserting API user '{username}'"));
        }
        return TryDeserialize<ApiUserRecord>(body);
    }

    public async Task DeleteApiUserAsync(string username, CancellationToken ct = default)
    {
        using var resp = await _http.DeleteAsync($"/api/v1/users/{Uri.EscapeDataString(username)}", ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(ExtractErrorMessage(body, $"Failed deleting API user '{username}'"));
        }
    }

    // =========================================================================
    // Cryptography & PKI Operations (/api/v1/crypto/*)
    // =========================================================================

    private async Task<T?> PostJsonForObjectAsync<T>(string url, object request, string errorPrefix, CancellationToken ct) where T : class
    {
        using var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync(url, content, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ExtractErrorMessage(body, errorPrefix));
        }
        return TryDeserialize<T>(body);
    }

    public Task<CryptoKeygenResponse?> CryptoKeygenAsync(CryptoKeygenRequest request, CancellationToken ct = default) =>
        PostJsonForObjectAsync<CryptoKeygenResponse>("/api/v1/crypto/keygen", request, "Crypto key generation failed via API.", ct);

    public Task<CryptoSignResponse?> CryptoSignAsync(CryptoSignRequest request, CancellationToken ct = default) =>
        PostJsonForObjectAsync<CryptoSignResponse>("/api/v1/crypto/sign", request, "Digital signature failed via API.", ct);

    public async Task<CryptoVerifyResponse?> CryptoVerifyAsync(
        CryptoVerifyRequest request,
        CancellationToken ct = default)
    {
        using var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync("/api/v1/crypto/verify", content, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        var parsed = TryDeserialize<CryptoVerifyResponse>(body);
        if (parsed is not null)
        {
            return parsed;
        }
        if (!resp.IsSuccessStatusCode)
        {
            return new CryptoVerifyResponse
            {
                Verified = false,
                Error = ExtractErrorMessage(body, $"Verification failed (HTTP {(int)resp.StatusCode})")
            };
        }
        return new CryptoVerifyResponse { Verified = true };
    }

    private async Task<string> PostCryptoTransformAsync(string url, string payloadText, string? secureKey, string propName, string errorPrefix, CancellationToken ct)
    {
        var req = new { payload = payloadText, secure_key = secureKey ?? string.Empty };
        using var content = new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync(url, content, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ExtractErrorMessage(body, errorPrefix));
        }
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty(propName, out var prop) ? (prop.GetString() ?? string.Empty) : string.Empty;
    }

    public Task<string> CryptoEncryptViaApiAsync(string payloadText, string? secureKey = null, CancellationToken ct = default) =>
        PostCryptoTransformAsync("/api/v1/crypto/encrypt", payloadText, secureKey, "encrypted_payload", "Encryption failed via API.", ct);

    public Task<string> CryptoDecryptViaApiAsync(string encryptedText, string? secureKey = null, CancellationToken ct = default) =>
        PostCryptoTransformAsync("/api/v1/crypto/decrypt", encryptedText, secureKey, "decrypted_payload", "Decryption failed via API.", ct);

    private T? TryDeserialize<T>(string json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize {Type} from JSON payload.", typeof(T).Name);
            return null;
        }
    }

    private static string ExtractErrorMessage(string body, string fallback)
    {
        if (string.IsNullOrWhiteSpace(body)) return fallback;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var errProp) && errProp.ValueKind == JsonValueKind.String)
            {
                return errProp.GetString() ?? fallback;
            }
            if (doc.RootElement.TryGetProperty("error_message", out var errMsgProp) && errMsgProp.ValueKind == JsonValueKind.String)
            {
                return errMsgProp.GetString() ?? fallback;
            }
        }
        catch
        {
            // Ignore JSON parse failure
        }
        return body.Length > 300 ? body[..300] : body;
    }
}
