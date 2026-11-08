using System.Net;
using System.Text;
using FlowUI.Config;
using FlowUI.Models;
using FlowUI.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowUI.Tests;

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

    public List<HttpRequestMessage> Requests { get; } = new();

    public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        _responder = responder;
    }

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> syncResponder)
    {
        _responder = (req, _) => Task.FromResult(syncResponder(req));
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return _responder(request, cancellationToken);
    }

    public static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
}

public class FlowApiClientTests
{
    private static FlowApiClient CreateClient(FakeHttpMessageHandler handler, out HttpClient httpClient)
    {
        httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:8080")
        };
        return new FlowApiClient(httpClient, NullLogger<FlowApiClient>.Instance);
    }

    [Theory]
    [InlineData("all", true, true, true)]
    [InlineData("admin", true, true, true)]
    [InlineData("*", true, true, true)]
    [InlineData("write", false, true, true)]
    [InlineData("upload", false, true, true)]
    [InlineData("write-only", false, true, true)]
    [InlineData("upload-only", false, true, true)]
    [InlineData("read-write", false, true, true)]
    [InlineData("editor", false, true, true)]
    [InlineData("execution-only", false, false, true)]
    [InlineData("execute", false, false, true)]
    [InlineData("exec", false, false, true)]
    [InlineData("run", false, false, true)]
    [InlineData("read-only", false, false, false)]
    [InlineData(null, false, false, false)]
    public void MapApiPermissionToUser_MapsPermissionsCorrectly(
        string? apiPerm, bool expectedAdmin, bool expectedWrite, bool expectedExec)
    {
        var user = FlowApiClient.MapApiPermissionToUser("alice", apiPerm, "ApiBasic", "Custom Desc", true);
        Assert.Equal("alice", user.Username);
        Assert.True(user.CanRead);
        Assert.Equal(expectedAdmin, user.IsAdmin);
        Assert.Equal(expectedWrite, user.CanCreate);
        Assert.Equal(expectedWrite, user.CanUpdate);
        Assert.Equal(expectedWrite, user.CanDelete);
        Assert.Equal(expectedExec, user.CanExecute);
        Assert.Equal("Custom Desc", user.Description);

        var defaultDescUser = FlowApiClient.MapApiPermissionToUser("alice", apiPerm, "ApiBasic");
        Assert.Contains("go-flowAPI user", defaultDescUser.Description);
    }

    [Fact]
    public void Constructor_And_AuthHeaders_WorkCorrectly()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.JsonResponse("{}"));
        var httpNoBase = new HttpClient(handler);
        var clientAuto = new FlowApiClient(httpNoBase, NullLogger<FlowApiClient>.Instance);
        Assert.Equal(httpNoBase.BaseAddress?.ToString().TrimEnd('/') ?? AppConfig.ApiBaseUrl, clientAuto.BaseUrl);

        var client = CreateClient(handler, out var http);
        Assert.Equal("http://localhost:8080", client.BaseUrl);

        client.SetBasicAuthCredentials("bob", "secret");
        Assert.NotNull(http.DefaultRequestHeaders.Authorization);
        Assert.Equal("Basic", http.DefaultRequestHeaders.Authorization.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("bob:secret")),
            http.DefaultRequestHeaders.Authorization.Parameter);

        client.SetBasicAuthCredentials("   ", "secret");
        Assert.Null(http.DefaultRequestHeaders.Authorization);

        client.SetBasicAuthToken(null);
        Assert.Null(http.DefaultRequestHeaders.Authorization);

        client.SetWindowsIntegratedAuth("CORP\\alice", ["CORP\\ETL_Admins", "ETL_Readers", "  "], "winPass");
        Assert.True(http.DefaultRequestHeaders.Contains("X-Flow-Windows-User"));
        Assert.True(http.DefaultRequestHeaders.Contains("X-Flow-Windows-Groups"));

        client.SetWindowsIntegratedAuth("CORP\\alice", null, null);
        Assert.True(http.DefaultRequestHeaders.Contains("X-Flow-Windows-User"));
        Assert.False(http.DefaultRequestHeaders.Contains("X-Flow-Windows-Groups"));

        client.SetWindowsIntegratedAuth("   ");
        Assert.Null(http.DefaultRequestHeaders.Authorization);

        client.SetSecureKeyHeader("my-key-123");
        Assert.True(http.DefaultRequestHeaders.Contains("X-Flow-Secure-Key"));
        client.SetSecureKeyHeader(null);
        Assert.False(http.DefaultRequestHeaders.Contains("X-Flow-Secure-Key"));

        client.ResetToDefaultAuth();
        if (string.IsNullOrWhiteSpace(AppConfig.ApiBasicAuthToken))
        {
            Assert.Null(http.DefaultRequestHeaders.Authorization);
        }
        else
        {
            Assert.Equal("Basic", http.DefaultRequestHeaders.Authorization?.Scheme);
            Assert.Equal(AppConfig.ApiBasicAuthToken, http.DefaultRequestHeaders.Authorization?.Parameter);
        }

        client.ClearAuth();
        Assert.Null(http.DefaultRequestHeaders.Authorization);
        Assert.False(http.DefaultRequestHeaders.Contains("X-Flow-Windows-User"));
        Assert.False(http.DefaultRequestHeaders.Contains("X-Flow-Windows-Groups"));
    }

    [Fact]
    public async Task CheckHealth_And_LenientJsonConverters_HandleAllTokenTypes()
    {
        int call = 0;
        var handler = new FakeHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.PathAndQuery;
            if (path == "/api/v1/health")
            {
                call++;
                if (call == 1)
                {
                    return FakeHttpMessageHandler.JsonResponse("""
                        {"status":"healthy","dialect":"sqlserver","db_user":"sa","authenticated_as":"admin","permission":"all"}
                        """);
                }
                if (call == 2)
                {
                    return FakeHttpMessageHandler.JsonResponse("""{"error":"unauthorized"}""", HttpStatusCode.Unauthorized);
                }
                throw new HttpRequestException("Network down");
            }
            if (path.StartsWith("/api/v1/artifacts/pipeline"))
            {
                // Exercise LenientStringConverter (null, string, int, double, true, false, object, array)
                // and LenientInt32Converter (null, int, double, valid string, invalid string)
                return FakeHttpMessageHandler.JsonResponse("""
                    [
                      {"id":null,"name":null,"type":123,"description":45.67,"content":true,"created_at":"2026-01-02T03:04:05Z","updated_at":"2026-01-03T04:05:06Z"},
                      {"id":12.8,"name":"pipe_2","content":"<pipeline />"},
                      {"id":"not_an_int","name":"pipe_3","content":"<pipeline />"}
                    ]
                    """);
            }
            if (path.StartsWith("/api/v1/artifacts/config"))
            {
                return FakeHttpMessageHandler.JsonResponse("""{"unexpected_key":123}""");
            }
            return FakeHttpMessageHandler.JsonResponse("[]");
        });

        var client = CreateClient(handler, out _);

        var (online1, summary1) = await client.CheckHealthAsync();
        Assert.True(online1);
        Assert.Contains("healthy (sqlserver as sa)", summary1);

        var (online2, summary2) = await client.CheckHealthAsync();
        Assert.False(online2);
        Assert.Contains("HTTP 401", summary2);

        var (online3, summary3) = await client.CheckHealthAsync();
        Assert.False(online3);
        Assert.Contains("Network down", summary3);

        var list = await client.ListArtifactsAsync("pipeline", includeContent: false);
        Assert.Equal(3, list.Count);
        Assert.Equal(0, list[0].Id);
        Assert.True(string.IsNullOrEmpty(list[0].Name));
        Assert.Equal("123", list[0].Type);
        Assert.Equal("45.67", list[0].Description);
        Assert.Equal("true", list[0].Content);
        Assert.Equal(DateTime.Parse("2026-01-02T03:04:05Z").ToUniversalTime(), list[0].CreatedAt.ToUniversalTime());
        Assert.Equal(DateTime.Parse("2026-01-03T04:05:06Z").ToUniversalTime(), list[0].UpdatedAt.ToUniversalTime());
        Assert.Equal(12, list[1].Id);
        Assert.Equal(0, list[2].Id);

        var emptyWrapper = await client.ListArtifactsAsync("config");
        Assert.Empty(emptyWrapper);
    }

    [Fact]
    public async Task ArtifactCrud_GetById_GetByName_Upsert_Update_Delete_WorkAndHandleErrors()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            var pathAndQuery = req.RequestUri!.PathAndQuery;

            if (req.Method == HttpMethod.Get && pathAndQuery.Contains("/id/1"))
            {
                return FakeHttpMessageHandler.JsonResponse("""{"id":1,"name":"p1","content":"<pipeline />"}""");
            }
            if (req.Method == HttpMethod.Get && pathAndQuery.Contains("/id/5"))
            {
                if (pathAndQuery.Contains("decrypt=true"))
                    return FakeHttpMessageHandler.JsonResponse("""{"error":"missing key"}""", HttpStatusCode.BadRequest);
                return FakeHttpMessageHandler.JsonResponse("""{"id":5,"name":"enc_pipe","content":"FLOWENC:v1:abc","encrypted":true}""");
            }
            if (req.Method == HttpMethod.Get && pathAndQuery.Contains("/id/999"))
            {
                return FakeHttpMessageHandler.JsonResponse("""{"error":"not found"}""", HttpStatusCode.NotFound);
            }
            if (req.Method == HttpMethod.Get && pathAndQuery.Contains("/artifacts/pipeline/by_name"))
            {
                return FakeHttpMessageHandler.JsonResponse("""{"id":2,"name":"by_name","content":"<pipeline />"}""");
            }
            if (req.Method == HttpMethod.Post && pathAndQuery.StartsWith("/api/v1/artifacts/pipeline"))
            {
                if (pathAndQuery.Contains("secure_key=fail"))
                    return FakeHttpMessageHandler.JsonResponse("""{"error_message":"Upsert failed"}""", HttpStatusCode.BadRequest);
                return FakeHttpMessageHandler.JsonResponse("""{"id":10,"name":"saved","content":"<pipeline />"}""");
            }
            if (req.Method == HttpMethod.Put && pathAndQuery.StartsWith("/api/v1/artifacts/pipeline/id/10"))
            {
                return FakeHttpMessageHandler.JsonResponse("null");
            }
            if (req.Method == HttpMethod.Delete && pathAndQuery.Contains("/id/10"))
            {
                return FakeHttpMessageHandler.JsonResponse("""{"deleted":true}""");
            }
            if (req.Method == HttpMethod.Delete && pathAndQuery.Contains("/artifacts/pipeline/del_ok"))
            {
                return FakeHttpMessageHandler.JsonResponse("""{"deleted":true}""");
            }
            if (req.Method == HttpMethod.Delete)
            {
                var longBody = new string('x', 350);
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent(longBody, Encoding.UTF8, "text/plain")
                };
            }
            return FakeHttpMessageHandler.JsonResponse("{}");
        });

        var client = CreateClient(handler, out _);

        var a1 = await client.GetArtifactByIdAsync("pipeline", 1, "secKey", true);
        Assert.NotNull(a1);
        Assert.Equal(1, a1.Id);

        var a5 = await client.GetArtifactByIdAsync("pipeline", 5);
        Assert.NotNull(a5);
        Assert.True(a5.Encrypted);

        var a999 = await client.GetArtifactByIdAsync("pipeline", 999);
        Assert.Null(a999);

        var byName = await client.GetArtifactByNameAsync("pipeline", "by_name", "secKey", false);
        Assert.NotNull(byName);
        Assert.Equal("by_name", byName.Name);

        var upserted = await client.UpsertArtifactAsync("pipeline", "saved", "desc", "<pipeline />", true, "sec");
        Assert.Equal(10, upserted.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.UpsertArtifactAsync("pipeline", "saved", "desc", "<pipeline />", true, "fail"));

        var updatedFallback = await client.UpdateArtifactByIdAsync("pipeline", 10, "new desc", "<pipeline />", true, "sec");
        Assert.Equal(10, updatedFallback.Id);
        Assert.Equal("new desc", updatedFallback.Description);

        Assert.True(await client.DeleteArtifactByIdAsync("pipeline", 10));
        Assert.True(await client.DeleteArtifactByNameAsync("pipeline", "del_ok"));

        var delEx = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.DeleteArtifactByNameAsync("pipeline", "del_fail"));
        Assert.Equal(300, delEx.Message.Length);
    }

    [Fact]
    public async Task ExecutePipelineStreamAsync_HandlesSuccess_ImplicitDone_AndHttpError()
    {
        const string sseStream = """
            event: node
            data: {"node_id":"step_1","kind":"sql","status":"running","sequence":1}

            {"node_id":"step_1","kind":"sql","status":"succeeded","sequence":2,"rows_read":10}

            event: done
            data: {"type":"done","status":"COMPLETED","formatted_output":"All steps done"}
            """;

        int mode = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            mode++;
            if (mode == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(sseStream, Encoding.UTF8, "text/event-stream")
                };
            }
            if (mode == 2)
            {
                // Stream that ends without an explicit done frame + invalid JSON frame
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("data: {bad json}\n\ndata: {\"node_id\":\"s1\",\"status\":\"running\"}\n", Encoding.UTF8, "text/event-stream")
                };
            }
            return FakeHttpMessageHandler.JsonResponse("""{"error":"Stream failed"}""", HttpStatusCode.BadRequest);
        });

        var client = CreateClient(handler, out _);
        var nodeEvents = new List<ExecutionStreamNodeEvent>();
        ExecutionStreamNodeEvent? doneEvt = null;

        await client.ExecutePipelineStreamAsync(
            new ExecutePipelineRequest { PipelineName = "test_pipe" },
            evt => { nodeEvents.Add(evt); return Task.CompletedTask; },
            evt => { doneEvt = evt; return Task.CompletedTask; });

        Assert.Equal(2, nodeEvents.Count);
        Assert.NotNull(doneEvt);
        Assert.Equal("COMPLETED", doneEvt.Status);

        // Mode 2: implicit done when stream ends without done event
        doneEvt = null;
        await client.ExecutePipelineStreamAsync(
            new ExecutePipelineRequest { PipelineName = "test_pipe" },
            evt => { nodeEvents.Add(evt); return Task.CompletedTask; },
            evt => { doneEvt = evt; return Task.CompletedTask; });
        Assert.NotNull(doneEvt);
        Assert.Equal("COMPLETED", doneEvt.Status);

        // Mode 3: HTTP error
        doneEvt = null;
        await client.ExecutePipelineStreamAsync(
            new ExecutePipelineRequest { PipelineName = "test_pipe" },
            _ => Task.CompletedTask,
            evt => { doneEvt = evt; return Task.CompletedTask; });
        Assert.NotNull(doneEvt);
        Assert.Equal("FAILED", doneEvt.Status);
        Assert.Equal("Stream failed", doneEvt.ErrorMessage);
    }

    [Fact]
    public async Task Validate_ExecuteSync_Telemetry_DbTest_And_UserManagement_CoverAllBranches()
    {
        bool failMode = false;
        var handler = new FakeHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (failMode)
            {
                return path switch
                {
                    "/api/v1/validate" => FakeHttpMessageHandler.JsonResponse("", HttpStatusCode.BadRequest),
                    "/api/v1/execute" => new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent("not-json-error", Encoding.UTF8, "text/plain")
                    },
                    "/api/v1/db/test" => FakeHttpMessageHandler.JsonResponse("""{"error":"Login failed"}""", HttpStatusCode.BadRequest),
                    "/api/v1/users" => FakeHttpMessageHandler.JsonResponse("""{"error":"Duplicate user"}""", HttpStatusCode.BadRequest),
                    _ when path.StartsWith("/api/v1/users/") => FakeHttpMessageHandler.JsonResponse("""{"error":"Cannot delete admin"}""", HttpStatusCode.Forbidden),
                    _ => FakeHttpMessageHandler.JsonResponse("{}", HttpStatusCode.BadRequest)
                };
            }

            return path switch
            {
                "/api/v1/validate" => FakeHttpMessageHandler.JsonResponse("""{"status":"","formatted_output":""}"""),
                "/api/v1/execute" => FakeHttpMessageHandler.JsonResponse("""{"run_id":"run-1","status":"SUCCESS","duration_ms":45.5}"""),
                "/api/v1/runs" => FakeHttpMessageHandler.JsonResponse("""{"runs":[{"run_id":"run-1","status":"SUCCESS","duration_ms":null},{"run_id":"run-2","duration_ms":"bad_long"}]}"""),
                "/api/v1/events" => FakeHttpMessageHandler.JsonResponse("""{"events":[{"id":"1","run_id":"run-1","node_id":"s1","rows_read":"100"}]}"""),
                "/api/v1/audit" => FakeHttpMessageHandler.JsonResponse("""{"api_events":[{"event_id":"9","http_method":"POST"}]}"""),
                "/api/v1/db/test" => FakeHttpMessageHandler.JsonResponse("""{"connected":true,"dialect":"sqlserver","spid":55}"""),
                "/api/v1/users" => req.Method == HttpMethod.Get
                    ? FakeHttpMessageHandler.JsonResponse("""{"users":[{"username":"admin","permission":"all","is_active":true}]}""")
                    : FakeHttpMessageHandler.JsonResponse("""{"username":"new_user","permission":"write","is_active":true}"""),
                _ when path.StartsWith("/api/v1/users/") => FakeHttpMessageHandler.JsonResponse("""{"deleted":true}"""),
                _ => FakeHttpMessageHandler.JsonResponse("{}", HttpStatusCode.OK)
            };
        });

        var client = CreateClient(handler, out _);

        var (valid, msg) = await client.ValidatePipelineXmlAsync("<pipeline />");
        Assert.True(valid);
        Assert.Contains("valid", msg);

        var execRes = await client.ExecutePipelineSyncAsync(new ExecutePipelineRequest { PipelineXml = "<pipeline />" });
        Assert.Equal("run-1", execRes.RunId);
        Assert.Equal(45L, execRes.DurationMs);

        var runs = await client.GetPipelineRunsAsync("SUCCESS");
        Assert.Equal(2, runs.Count);

        var events = await client.GetPipelineEventsAsync("run-1");
        Assert.Single(events);
        Assert.Equal(100L, events[0].RowsRead);

        var audits = await client.GetApiAuditLogsAsync(100, "200");
        Assert.Single(audits);

        var ping = await client.TestDatabaseViaApiAsync("Server=localhost;");
        Assert.True(ping.Connected);

        var users = await client.GetApiUsersAsync();
        Assert.Single(users);

        var upserted = await client.UpsertApiUserAsync("new_user", "pw", "write", "desc");
        Assert.NotNull(upserted);

        await client.DeleteApiUserAsync("new_user");

        // Now test failure branches
        failMode = true;
        var (validFail, msgFail) = await client.ValidatePipelineXmlAsync("<pipeline />");
        Assert.False(validFail);
        Assert.Contains("Validation failed", msgFail);

        var execFail = await client.ExecutePipelineSyncAsync(new ExecutePipelineRequest { PipelineXml = "<pipeline />" });
        Assert.Equal("FAILED", execFail.Status);
        Assert.Equal("not-json-error", execFail.ErrorMessage);

        var pingFail = await client.TestDatabaseViaApiAsync("Server=bad;");
        Assert.False(pingFail.Connected);
        Assert.Equal("Login failed", pingFail.Error);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.UpsertApiUserAsync("dup", "pw", "write", "desc"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.DeleteApiUserAsync("admin"));

        // Test exception in TestDatabaseViaApiAsync
        var throwClient = CreateClient(new FakeHttpMessageHandler(_ => throw new HttpRequestException("Socket closed")), out _);
        var pingEx = await throwClient.TestDatabaseViaApiAsync();
        Assert.False(pingEx.Connected);
        Assert.Contains("Socket closed", pingEx.Error);
    }

    [Fact]
    public async Task CryptoApiEndpoints_Keygen_Sign_Verify_Encrypt_Decrypt_CoverAllBranches()
    {
        int verifyMode = 0;
        bool failCrypto = false;

        var handler = new FakeHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (failCrypto)
            {
                return FakeHttpMessageHandler.JsonResponse("""{"error":"Crypto API error"}""", HttpStatusCode.BadRequest);
            }

            return path switch
            {
                "/api/v1/crypto/keygen" => FakeHttpMessageHandler.JsonResponse("""{"algorithm":"ECDSA-P256","public_key_pem":"PUB","private_key_pem":"PRIV"}"""),
                "/api/v1/crypto/sign" => FakeHttpMessageHandler.JsonResponse("""{"signed_envelope":"FLOWSIGNED:v1:env","signature_base64":"SIG"}"""),
                "/api/v1/crypto/verify" => (++verifyMode) switch
                {
                    1 => FakeHttpMessageHandler.JsonResponse("""{"verified":true,"key_fingerprint":"SHA256:abc"}"""),
                    2 => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("", Encoding.UTF8, "text/plain") },
                    _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("", Encoding.UTF8, "text/plain") }
                },
                "/api/v1/crypto/encrypt" => FakeHttpMessageHandler.JsonResponse("""{"encrypted_payload":"FLOWENC:v1:xyz"}"""),
                "/api/v1/crypto/decrypt" => FakeHttpMessageHandler.JsonResponse("""{"decrypted_payload":"<pipeline />"}"""),
                _ => FakeHttpMessageHandler.JsonResponse("{}")
            };
        });

        var client = CreateClient(handler, out _);

        var keygen = await client.CryptoKeygenAsync(new CryptoKeygenRequest());
        Assert.NotNull(keygen);
        Assert.Equal("PUB", keygen.PublicKeyPem);

        var signed = await client.CryptoSignAsync(new CryptoSignRequest { Payload = "<pipeline />" });
        Assert.NotNull(signed);
        Assert.Equal("FLOWSIGNED:v1:env", signed.SignedEnvelope);

        var v1 = await client.CryptoVerifyAsync(new CryptoVerifyRequest { Payload = "FLOWSIGNED:v1:env" });
        Assert.NotNull(v1);
        Assert.True(v1.Verified);

        var v2 = await client.CryptoVerifyAsync(new CryptoVerifyRequest { Payload = "bad" });
        Assert.NotNull(v2);
        Assert.False(v2.Verified);

        var v3 = await client.CryptoVerifyAsync(new CryptoVerifyRequest { Payload = "empty_200" });
        Assert.NotNull(v3);
        Assert.True(v3.Verified);

        var enc = await client.CryptoEncryptViaApiAsync("<pipeline />", "sec");
        Assert.Equal("FLOWENC:v1:xyz", enc);

        var dec = await client.CryptoDecryptViaApiAsync("FLOWENC:v1:xyz", "sec");
        Assert.Equal("<pipeline />", dec);

        failCrypto = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CryptoKeygenAsync(new CryptoKeygenRequest()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CryptoSignAsync(new CryptoSignRequest()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CryptoEncryptViaApiAsync("a"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CryptoDecryptViaApiAsync("b"));
    }

    [Fact]
    public void SetWindowsIntegratedAuth_EncodesAndDeduplicatesGroups()
    {
        var client = CreateClient(new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.JsonResponse("{}")), out var http);

        client.SetWindowsIntegratedAuth("CORP\\alice", [" TeamA ", "teama", "TeamB"], null);

        Assert.True(http.DefaultRequestHeaders.TryGetValues("X-Flow-Windows-Groups", out var groupHeaders));
        string encoded = string.Empty;
        foreach (var value in groupHeaders)
        {
            encoded = value;
            break;
        }

        var decodedJson = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        Assert.Contains("\"TeamA\"", decodedJson);
        Assert.Contains("\"TeamB\"", decodedJson);
        Assert.DoesNotContain("\"teama\"", decodedJson);

        Assert.NotNull(http.DefaultRequestHeaders.Authorization);
        var authRaw = Encoding.UTF8.GetString(Convert.FromBase64String(http.DefaultRequestHeaders.Authorization!.Parameter!));
        Assert.Equal("CORP\\alice:__WINDOWS_INTEGRATED__", authRaw);
    }

    [Fact]
    public void SetBasicAuthToken_ClearsWindowsHeaders()
    {
        var client = CreateClient(new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.JsonResponse("{}")), out var http);

        client.SetWindowsIntegratedAuth("CORP\\alice", ["TeamA"], null);
        Assert.True(http.DefaultRequestHeaders.Contains("X-Flow-Windows-User"));
        Assert.True(http.DefaultRequestHeaders.Contains("X-Flow-Windows-Groups"));

        client.SetBasicAuthToken("abc123");
        Assert.False(http.DefaultRequestHeaders.Contains("X-Flow-Windows-User"));
        Assert.False(http.DefaultRequestHeaders.Contains("X-Flow-Windows-Groups"));
        Assert.Equal("Basic", http.DefaultRequestHeaders.Authorization?.Scheme);
        Assert.Equal("abc123", http.DefaultRequestHeaders.Authorization?.Parameter);
    }

    [Fact]
    public async Task GetAuthenticatedIdentityAsync_InvalidJson_ReturnsOfflineTuple()
    {
        var handler = new FakeHttpMessageHandler(req =>
            req.RequestUri!.AbsolutePath == "/api/v1/health"
                ? FakeHttpMessageHandler.JsonResponse("{not-json")
                : FakeHttpMessageHandler.JsonResponse("{}"));

        var client = CreateClient(handler, out _);
        var (online, authAs, perm, summary) = await client.GetAuthenticatedIdentityAsync();

        Assert.False(online);
        Assert.Equal("", authAs);
        Assert.Equal("read-only", perm);
        Assert.False(string.IsNullOrWhiteSpace(summary));
    }

    [Fact]
    public async Task ListArtifactsAsync_ReadsItemsWrapperPayload()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/v1/artifacts/pipeline")
            {
                return FakeHttpMessageHandler.JsonResponse("""
                    {"items":[{"id":7,"name":"wrapped_pipe","type":"pipeline","content":"<pipeline />"}]}
                    """);
            }
            return FakeHttpMessageHandler.JsonResponse("{}");
        });

        var client = CreateClient(handler, out _);
        var items = await client.ListArtifactsAsync("pipeline", includeContent: true);

        Assert.Single(items);
        Assert.Equal(7, items[0].Id);
        Assert.Equal("wrapped_pipe", items[0].Name);
    }

    [Fact]
    public async Task GetArtifactByNameAsync_DecryptFallback_ReturnsRawArtifact()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            var pathAndQuery = req.RequestUri!.PathAndQuery;
            if (pathAndQuery.StartsWith("/api/v1/artifacts/pipeline/my_pipe?decrypt=true"))
            {
                return FakeHttpMessageHandler.JsonResponse("""{"error":"missing key"}""", HttpStatusCode.BadRequest);
            }
            if (pathAndQuery.StartsWith("/api/v1/artifacts/pipeline/my_pipe?decrypt=false"))
            {
                return FakeHttpMessageHandler.JsonResponse("""{"id":88,"name":"my_pipe","content":"FLOWENC:v1:abc","encrypted":true}""");
            }
            return FakeHttpMessageHandler.JsonResponse("{}");
        });

        var client = CreateClient(handler, out _);
        var artifact = await client.GetArtifactByNameAsync("pipeline", "my_pipe", "secure", true);

        Assert.NotNull(artifact);
        Assert.Equal(88, artifact!.Id);
        Assert.True(artifact.Encrypted);
        Assert.StartsWith("FLOWENC:v1:", artifact.Content);
    }

    [Fact]
    public void SetSecureKeyHeader_TrimsAndReplacesHeaderValue()
    {
        var client = CreateClient(new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.JsonResponse("{}")), out var http);

        client.SetSecureKeyHeader("  key-one  ");
        Assert.True(http.DefaultRequestHeaders.TryGetValues("X-Flow-Secure-Key", out var values1));
        Assert.Single(values1);
        Assert.Equal("key-one", values1.First());

        client.SetSecureKeyHeader("key-two");
        Assert.True(http.DefaultRequestHeaders.TryGetValues("X-Flow-Secure-Key", out var values2));
        Assert.Single(values2);
        Assert.Equal("key-two", values2.First());
    }

    [Fact]
    public void ClearAuth_DoesNotRemoveSecureKeyHeader()
    {
        var client = CreateClient(new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.JsonResponse("{}")), out var http);

        client.SetWindowsIntegratedAuth("CORP\\alice", ["TeamA"], null);
        client.SetSecureKeyHeader("persist-me");

        client.ClearAuth();

        Assert.Null(http.DefaultRequestHeaders.Authorization);
        Assert.False(http.DefaultRequestHeaders.Contains("X-Flow-Windows-User"));
        Assert.False(http.DefaultRequestHeaders.Contains("X-Flow-Windows-Groups"));
        Assert.True(http.DefaultRequestHeaders.TryGetValues("X-Flow-Secure-Key", out var values));
        Assert.Equal("persist-me", values.First());
    }

    [Fact]
    public void SetWindowsIntegratedAuth_WithPassword_UsesPasswordInBasicToken()
    {
        var client = CreateClient(new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.JsonResponse("{}")), out var http);

        client.SetWindowsIntegratedAuth("CORP\\alice", null, "mySecretPw");

        Assert.NotNull(http.DefaultRequestHeaders.Authorization);
        var authRaw = Encoding.UTF8.GetString(Convert.FromBase64String(http.DefaultRequestHeaders.Authorization!.Parameter!));
        Assert.Equal("CORP\\alice:mySecretPw", authRaw);
    }

    [Fact]
    public async Task ListArtifactsAsync_IncludeContentFalse_SendsQueryFlag()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.JsonResponse("[]"));
        var client = CreateClient(handler, out _);

        _ = await client.ListArtifactsAsync("pipeline", includeContent: false);

        Assert.NotEmpty(handler.Requests);
        Assert.Contains("include_content=false", handler.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public async Task GetAuthenticatedIdentityAsync_MissingFields_UsesDefaults()
    {
        var handler = new FakeHttpMessageHandler(req =>
            req.RequestUri!.AbsolutePath == "/api/v1/health"
                ? FakeHttpMessageHandler.JsonResponse("""{}""")
                : FakeHttpMessageHandler.JsonResponse("{}"));

        var client = CreateClient(handler, out _);
        var (online, authAs, perm, summary) = await client.GetAuthenticatedIdentityAsync();

        Assert.True(online);
        Assert.Equal("", authAs);
        Assert.Equal("", perm);
        Assert.Equal("ok (sqlserver)", summary);
    }
}
