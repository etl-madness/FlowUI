using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text;
using FlowUI.Components.Pages;
using FlowUI.Components.Shared;
using FlowUI.Components.Tabs;
using FlowUI.Models;
using FlowUI.Services;
using FlowUI.Services.Auth;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace FlowUI.Tests;

internal sealed class FakeJsRuntime : IJSRuntime
{
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
        InvokeAsync<TValue>(identifier, CancellationToken.None, args);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        if (typeof(TValue) == typeof(string) && identifier == "flowStudio.getSavedTheme")
        {
            return new ValueTask<TValue>((TValue)(object)"dracula");
        }
        return new ValueTask<TValue>(default(TValue)!);
    }
}

internal sealed class FakeWebHostEnvironment : IWebHostEnvironment
{
    public string WebRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string ApplicationName { get; set; } = "FlowUI";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public string EnvironmentName { get; set; } = "Development";
}

internal sealed class TrackingComponentActivator : IComponentActivator
{
    public Dictionary<Type, IComponent> Instances { get; } = new();

    public IComponent CreateInstance(Type componentType)
    {
        var instance = (IComponent)Activator.CreateInstance(componentType)!;
        Instances[componentType] = instance;
        return instance;
    }

    public T Get<T>() where T : IComponent => (T)Instances[typeof(T)];
}

public class RazorComponentsTests
{
    private static readonly BindingFlags NonPublic = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
    private static readonly string[] PipelineStatuses = ["", "SUCCESS", "FAILED", "RUNNING", "WARN"];
    private static readonly string[] BannerTypes = ["error", "success", "warning", "info"];
    private static readonly string[] Tabs = ["config", "options", "templates", "runner", "logs", "settings", "pipeline"];

    private static (HtmlRenderer Renderer, TrackingComponentActivator Activator, StudioStateService Studio, UiAuthenticationService Auth) CreateTestHost(bool loginAsAdmin = true)
    {
        const string sse = "event: node\ndata: {\"node_id\":\"verify_connection\",\"kind\":\"sql\",\"status\":\"running\",\"sequence\":1}\n\nevent: node\ndata: {\"node_id\":\"verify_connection\",\"kind\":\"sql\",\"status\":\"succeeded\",\"duration\":\"12ms\",\"rows_read\":1,\"node_output\":\"row1\"}\n\nevent: done\ndata: {\"type\":\"done\",\"run_id\":\"run-1\",\"status\":\"COMPLETED\",\"formatted_output\":\"\"}\n\n";
        var handler = new FakeHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            return path switch
            {
                "/api/v1/health" => FakeHttpMessageHandler.JsonResponse("""{"status":"ok","dialect":"sqlserver"}"""),
                "/api/v1/artifacts/pipeline" => FakeHttpMessageHandler.JsonResponse("""[{"id":1,"name":"pipe_1","type":"pipeline","groups":["finance_etl"],"content":"<pipeline name=\"pipe_1\"><flow><sql id=\"s1\" db=\"default_db\">SELECT 1;</sql></flow></pipeline>"}]"""),
                "/api/v1/artifacts/config" => FakeHttpMessageHandler.JsonResponse("""[{"id":2,"name":"cfg_1","type":"config","content":"<pipeline name=\"cfg_1\"><variables><variable name=\"a\" value=\"1\"/></variables></pipeline>"}]"""),
                "/api/v1/artifacts/options" => FakeHttpMessageHandler.JsonResponse("""[{"id":3,"name":"opt_1","type":"options","content":"<flow_cli_options><node-output>true</node-output></flow_cli_options>"}]"""),
                "/api/v1/templates" when req.Method == HttpMethod.Get => FakeHttpMessageHandler.JsonResponse("""{"count":2,"templates":[{"id":10,"type":"pipeline","name":"nightly_tmpl","description":"Nightly ETL","created_by":"admin","updated_by":"tadmin","created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-02T00:00:00Z"},{"id":11,"type":"config","name":"cfg_tmpl","description":"DB Config","created_by":"alice","updated_by":"alice","created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-01T00:00:00Z"}]}"""),
                "/api/v1/templates" when req.Method == HttpMethod.Post => FakeHttpMessageHandler.JsonResponse("""{"id":12,"type":"pipeline","name":"pipe_1_template","description":"Saved","content":"<pipeline/>","created_by":"admin","updated_by":"admin"}""", HttpStatusCode.Created),
                "/api/v1/templates/10" when req.Method == HttpMethod.Get => FakeHttpMessageHandler.JsonResponse("""{"id":10,"type":"pipeline","name":"nightly_tmpl","description":"Nightly ETL","content":"<pipeline name=\"nightly_tmpl\"><flow><sql id=\"s1\" db=\"default_db\">SELECT 1;</sql></flow></pipeline>","created_by":"admin","updated_by":"tadmin","created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-02T00:00:00Z"}"""),
                "/api/v1/templates/10" when req.Method == HttpMethod.Put => FakeHttpMessageHandler.JsonResponse("""{"id":10,"type":"pipeline","name":"nightly_tmpl","description":"Nightly ETL","content":"<pipeline name=\"nightly_tmpl\"><flow/></pipeline>","created_by":"admin","updated_by":"admin"}"""),
                "/api/v1/templates/10/instantiate" => FakeHttpMessageHandler.JsonResponse("""{"id":55,"type":"pipeline","name":"nightly_live","description":"Live","content":"<pipeline name=\"nightly_live\"><flow/></pipeline>"}""", HttpStatusCode.Created),
                "/api/v1/templates/10" when req.Method == HttpMethod.Delete => FakeHttpMessageHandler.JsonResponse("""{"deleted":true,"id":10}"""),
                "/api/v1/validate" => FakeHttpMessageHandler.JsonResponse("""{"status":"VALID","formatted_output":"Valid XML"}"""),
                "/api/v1/execute" => FakeHttpMessageHandler.JsonResponse("""{"run_id":"run-1","status":"SUCCESS","formatted_output":"","results":[{"node_id":"verify_connection","kind":"sql","status":"SUCCESS","node_output":"1 row"},{"node_id":"step_new","kind":"script","status":"FAILED","error":"err_msg"}]}"""),
                "/api/v1/execute/stream" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sse, Encoding.UTF8, "text/event-stream") },
                "/api/v1/runs" => FakeHttpMessageHandler.JsonResponse("""{"runs":[{"run_id":"run-1","status":"SUCCESS","hostname":"etl-node-01","db_user_name":"dbo","spid":52,"duration_ms":15,"started_at":"2026-01-01T00:00:00Z"}]}"""),
                "/api/v1/events" => FakeHttpMessageHandler.JsonResponse("""{"events":[{"id":1,"run_id":"run-1","node_id":"verify_connection","node_kind":"sql","status":"SUCCESS","db_user_name":"dbo","spid":"52->54","rows_read":5,"output_text":"out","occurred_at":"2026-01-01T00:00:02Z"},{"id":2,"run_id":"run-1","node_id":"from_evt","node_kind":"log","status":"SUCCESS","rows_written":2,"occurred_at":"2026-01-01T00:00:03Z"}]}"""),
                "/api/v1/audit" => FakeHttpMessageHandler.JsonResponse("""{"api_events":[{"event_id":1,"http_method":"POST","path":"/api/v1/execute","status":"SUCCESS","status_code":200}]}"""),
                "/api/v1/users" => FakeHttpMessageHandler.JsonResponse("""{"users":[{"username":"admin","permission":"all","is_active":true,"groups":["admins"]},{"username":"op","permission":"scoped","is_active":true,"groups":["finance_analysts"]}]}"""),
                "/api/v1/groups" => FakeHttpMessageHandler.JsonResponse("""{"groups":[{"id":1,"name":"finance_analysts","description":"Finance team","members":["op","CORP\\bob"]}]}"""),
                "/api/v1/pipeline-groups" => FakeHttpMessageHandler.JsonResponse("""{"pipeline_groups":[{"id":1,"name":"finance_etl","description":"Finance pipelines","name_pattern":"pipe_*","pipelines":["pipe_1"]}]}"""),
                "/api/v1/acls" => FakeHttpMessageHandler.JsonResponse("""{"acls":[{"id":1,"principal_type":"group","principal_name":"finance_analysts","target_type":"pipeline_group","target_name":"finance_etl","permissions":"read,execute","effect":"allow","description":"Allow finance"},{"id":2,"principal_type":"user","principal_name":"op","target_type":"*","target_name":"*","permissions":"delete","effect":"deny","description":"Block deletes"}]}"""),
                "/api/v1/permissions/effective" => FakeHttpMessageHandler.JsonResponse("""{"username":"op","global_permission":"scoped","strict_acl":true,"groups":["finance_analysts"],"can_create_ungrouped":false,"creatable_groups":["finance_etl"],"can_execute_inline_xml":false,"pipelines":[{"pipeline":"pipe_1","groups":["finance_etl"],"actions":{"create":false,"read":true,"update":false,"delete":false,"execute":true}}]}"""),
                "/api/v1/db/test" => FakeHttpMessageHandler.JsonResponse("""{"connected":true,"dialect":"sqlserver","db_user":"sa","spid":52}"""),
                "/api/v1/crypto/keygen" => FakeHttpMessageHandler.JsonResponse("""{"format":"openssl","algorithm":"ECDSA-P256","private_key_pem":"PRIV","public_key_pem":"PUB","certificate_pem":"CERT"}"""),
                _ => FakeHttpMessageHandler.JsonResponse("{}")
            };
        });

        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        var api = new FlowApiClient(http, NullLogger<FlowApiClient>.Instance);
        var provider = new FakeUiAuthProvider();
        var auth = new UiAuthenticationService([provider], api);
        if (loginAsAdmin)
        {
            auth.LoginAsync("admin", "pw").GetAwaiter().GetResult();
        }

        var catalog = new CatalogService();
        var docs = new DocsService(catalog, new FakeWebHostEnvironment());
        var xmlGen = new XmlGeneratorService();
        var xmlImp = new XmlImporterService();
        var crypto = new FlowCryptoService();
        var studio = new StudioStateService(api, auth, catalog, xmlGen, xmlImp, crypto, NullLogger<StudioStateService>.Instance);
        var activator = new TrackingComponentActivator();

        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddSingleton<IComponentActivator>(activator);
        sc.AddSingleton(api);
        sc.AddSingleton<IAuthenticationService>(auth);
        sc.AddSingleton(catalog);
        sc.AddSingleton(docs);
        sc.AddSingleton(xmlGen);
        sc.AddSingleton(xmlImp);
        sc.AddSingleton(crypto);
        sc.AddSingleton(studio);
        sc.AddSingleton<IJSRuntime>(new FakeJsRuntime());

        var sp = sc.BuildServiceProvider();
        var renderer = new HtmlRenderer(sp, NullLoggerFactory.Instance);
        return (renderer, activator, studio, auth);
    }

    private static void SetField(object target, string name, object? value) =>
        target.GetType().GetField(name, NonPublic)!.SetValue(target, value);

    private static object? Invoke(object target, string methodName, params object?[] args) =>
        target.GetType().GetMethod(methodName, NonPublic)!.Invoke(target, args);

    private static Task InvokeTaskAsync(object target, string methodName, params object?[] args) =>
        (Task)Invoke(target, methodName, args)!;

    [Fact]
    public async Task PipelineDesignerTab_ConfigTab_OptionsTab_And_NodeCard_RenderAndExecuteActions()
    {
        var (renderer, activator, studio, _) = CreateTestHost();
        await studio.InitializeStudioAsync();

        studio.OpenAddNodeModal("if");
        await studio.SaveNodeModalAsync(new Dictionary<string, string> { ["id"] = "if_1", ["var"] = "env" }, "");
        studio.OpenAddNodeModal("group");
        await studio.SaveNodeModalAsync(new Dictionary<string, string> { ["id"] = "grp_1" }, "");

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<PipelineDesignerTab>();
            Assert.Contains("Component Palette", root.ToHtmlString());

            var instance = activator.Get<PipelineDesignerTab>();
            SetField(instance, "_paletteFilter", "sql");
            Invoke(instance, "OnNameChanged", new ChangeEventArgs { Value = "updated_pipe" });
            Invoke(instance, "OnDescriptionChanged", new ChangeEventArgs { Value = "updated_desc" });
            await InvokeTaskAsync(instance, "OnSelectPipelineFromDbAsync", new ChangeEventArgs { Value = "1" });
            await InvokeTaskAsync(instance, "EditSelectedPipelineFromDbAsync");
            await InvokeTaskAsync(instance, "OnSelectConfigFromDbAsync", new ChangeEventArgs { Value = "2" });
            await InvokeTaskAsync(instance, "EditSelectedConfigFromDbAsync");
            await InvokeTaskAsync(instance, "OnSelectOptionsFromDbAsync", new ChangeEventArgs { Value = "3" });
            await InvokeTaskAsync(instance, "EditSelectedOptionsFromDbAsync");
            await InvokeTaskAsync(instance, "ValidateCurrentPipelineAsync");
            await InvokeTaskAsync(instance, "ApplyEditorXmlToCanvasAsync");
            await InvokeTaskAsync(instance, "CopyXmlAsync");
            await InvokeTaskAsync(instance, "DownloadXmlAsync");
            await InvokeTaskAsync(instance, "ClearCurrentArtifactNodesAsync");
            await InvokeTaskAsync(instance, "DeleteSelectedArtifactAsync");

            var cfgRoot = await renderer.RenderComponentAsync<ConfigTab>();
            Assert.Contains("EDITING CONFIG", cfgRoot.ToHtmlString());

            var optRoot = await renderer.RenderComponentAsync<OptionsTab>();
            Assert.Contains("EDITING OPTIONS", optRoot.ToHtmlString());
            await InvokeTaskAsync(activator.Get<PipelineDesignerTab>(), "ClearCurrentArtifactNodesAsync");
        });
    }

    [Fact]
    public async Task RunnerTab_Renders_AndExecutesValidateSyncAndStreamPipelines()
    {
        var (renderer, activator, studio, _) = CreateTestHost();
        await studio.InitializeStudioAsync();
        studio.UpsertSavedKeyProfile(new SavedCryptoKeyProfile
        {
            Name = "cert_prof",
            Format = "pkcs7",
            Algorithm = "RSA-2048",
            CertificatePem = "-----BEGIN CERTIFICATE-----\nMIIB\n-----END CERTIFICATE-----"
        });
        studio.UpsertSavedKeyProfile(new SavedCryptoKeyProfile
        {
            Name = "pub_prof",
            Format = "openssl",
            Algorithm = "ECDSA-P256",
            PublicKeyPem = "-----BEGIN PUBLIC KEY-----\nMFkw\n-----END PUBLIC KEY-----"
        });

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<RunnerTab>();
            var inst = activator.Get<RunnerTab>();

            SetField(inst, "_verifySignature", true);
            SetField(inst, "_varsRaw", "env=prod\nbatch=42");
            SetField(inst, "_selectedOptionsName", "opt_1");
            SetField(inst, "_logNodeOutput", false);
            Invoke(inst, "OnSelectVerifyProfile", new ChangeEventArgs { Value = "cert_prof" });
            Invoke(inst, "OnSelectVerifyProfile", new ChangeEventArgs { Value = "pub_prof" });

            SetField(inst, "_verifyKeyPem", "-----BEGIN PGP PUBLIC KEY BLOCK-----\nABC");
            Invoke(inst, "BuildRequest");

            SetField(inst, "_pipelineSourceMode", "stored");
            SetField(inst, "_selectedPipelineName", "pipe_1");
            Invoke(inst, "BuildRequest");
            SetField(inst, "_pipelineSourceMode", "draft");

            await InvokeTaskAsync(inst, "ValidateOnlyAsync");
            await InvokeTaskAsync(inst, "RunSynchronousAsync");
            await InvokeTaskAsync(inst, " EnrichStepsFromRunEventsAsync".Trim(), null, true);
            await InvokeTaskAsync(inst, "RunStreamingAsync");
            await InvokeTaskAsync(inst, "HandleExecutionExceptionAsync", Stopwatch.StartNew(), "EXECUTE_SYNC", "pipe_1", "Exec", new InvalidOperationException("boom"));

            Invoke(inst, "FormatSingleLinePreview", "");
            Invoke(inst, "FormatSingleLinePreview", new string('x', 120));
            foreach (var st in PipelineStatuses)
            {
                Invoke(inst, "GetBadgeClass", st);
            }

            Invoke(inst, "ToggleAllNodeOutputs", false);
            Invoke(inst, "ToggleAllNodeOutputs", true);

            var html = root.ToHtmlString();
            Assert.Contains("Pipeline Execution Runner", html);
            Assert.Contains("verify_connection", html);

            Invoke(inst, "ClearOutput");
        });
    }

    [Fact]
    public async Task SettingsTab_RendersAllSections_AndExecutesCrudAndKeygenActions()
    {
        var (renderer, activator, studio, _) = CreateTestHost();
        await studio.InitializeStudioAsync();

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<SettingsTab>();
            var inst = activator.Get<SettingsTab>();

            // Section 1: artifacts
            SetField(inst, "_artifactSearchQuery", "pipe");
            SetField(inst, "_artifactTypeFilter", "pipeline");
            Invoke(inst, "StateHasChanged");
            Assert.Contains("Stored API Artifacts", root.ToHtmlString());
            Invoke(inst, "ClearArtifactFilter");
            await InvokeTaskAsync(inst, "OpenArtifactInStudioAsync", studio.Configs[0]);
            await InvokeTaskAsync(inst, "OpenArtifactInStudioAsync", studio.OptionsList[0]);
            await InvokeTaskAsync(inst, "OpenArtifactInStudioAsync", studio.Pipelines[0]);
            await InvokeTaskAsync(inst, "DeleteArtifactFromTableAsync", studio.Pipelines[0]);

            // Section 2: crypto
            SetField(inst, "_section", "crypto");
            await InvokeTaskAsync(inst, "GenerateKeyProfileInSettingsAsync");
            await InvokeTaskAsync(inst, "DownloadKeyFileAsync", "k.pem", "PEM");
            Invoke(inst, "StateHasChanged");
            Assert.Contains("Saved Signing", root.ToHtmlString());

            // Section 3: api_users
            SetField(inst, "_section", "api_users");
            Invoke(inst, "PopulateApiUserForm", new ApiUserRecord { Username = "op", Permission = "write", Description = "d", IsActive = true });
            await InvokeTaskAsync(inst, "SaveApiUserAsync");
            await InvokeTaskAsync(inst, "DeleteApiUserAsync", "op");
            Invoke(inst, "ResetApiUserForm");
            Invoke(inst, "StateHasChanged");
            Assert.Contains("Upsert Backend", root.ToHtmlString());

            // Section 4: auth_provider
            SetField(inst, "_section", "auth_provider");
            await InvokeTaskAsync(inst, "SwitchAuthProviderAsync", "FakeAuth");
            Invoke(inst, "StateHasChanged");
            Assert.Contains("Pluggable Authentication Service", root.ToHtmlString());

            // Section 5: db
            SetField(inst, "_section", "db");
            await InvokeTaskAsync(inst, "LoadAndTestDbProfileAsync", studio.SavedDbProfiles[0]);
            await InvokeTaskAsync(inst, "SaveDbProfileAsync");
            Invoke(inst, "ResetDbProfileForm");
            Invoke(inst, "StateHasChanged");
            Assert.Contains("Saved Session Database Profiles", root.ToHtmlString());
        });
    }

    [Fact]
    public async Task Handbook_And_LogsTab_RenderAndFilterContent()
    {
        var (renderer, activator, studio, _) = CreateTestHost();
        await studio.InitializeStudioAsync();

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var hb = await renderer.RenderComponentAsync<Handbook>();
            var hbInst = activator.Get<Handbook>();
            Assert.Contains("Handbook", hb.ToHtmlString());

            Invoke(hbInst, "ToggleSubheadings");
            Invoke(hbInst, "ToggleAllCategories");
            Invoke(hbInst, "ToggleAllCategories");
            Invoke(hbInst, "ToggleCategory", "Flow Execution");
            Invoke(hbInst, "OnSearchInput", new ChangeEventArgs { Value = "sql query" });
            Invoke(hbInst, "StateHasChanged");
            Invoke(hbInst, "OnSearchInput", new ChangeEventArgs { Value = "zzzz_no_match_zzzz" });
            Invoke(hbInst, "StateHasChanged");
            Invoke(hbInst, "ClearSearch");
            hbInst.OnScrollActiveKeyChanged("", "");
            hbInst.OnScrollActiveKeyChanged("sql", "overview");
            await InvokeTaskAsync(hbInst, "ScrollToTopAsync");
            await InvokeTaskAsync(hbInst, "ScrollToHeadingAsync", "overview", "sql", "Flow Execution");
            await InvokeTaskAsync(hbInst, "OnThemeChanged", new ChangeEventArgs { Value = "nord" });

            var logs = await renderer.RenderComponentAsync<LogsTab>();
            var logsInst = activator.Get<LogsTab>();
            var logsHtml = logs.ToHtmlString();
            Assert.Contains("Pipeline Runs", logsHtml);
            Assert.Contains("Hostname", logsHtml);
            Assert.Contains("etl-node-01", logsHtml);

            await InvokeTaskAsync(logsInst, "OnRunStatusFilterChangedAsync", new ChangeEventArgs { Value = "succeeded" });
            await InvokeTaskAsync(logsInst, "SwitchSubTabAsync", "ui_events");
            Invoke(logsInst, "StateHasChanged");
            Assert.Contains("UI Session Activity", logs.ToHtmlString());

            await InvokeTaskAsync(logsInst, "SwitchSubTabAsync", "api_audit");
            Invoke(logsInst, "StateHasChanged");
            Assert.Contains("Audit Trail", logs.ToHtmlString());
        });
    }

    [Fact]
    public async Task Home_LoginPanel_And_StudioModals_RenderAndExecuteWorkflows()
    {
        var (renderer, activator, studio, auth) = CreateTestHost(loginAsAdmin: false);

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var home = await renderer.RenderComponentAsync<Home>();
            var homeInst = activator.Get<Home>();
            Assert.Contains("Sign In to Studio", home.ToHtmlString());

            var loginInst = activator.Get<LoginPanel>();
            Invoke(loginInst, "FillDefaultAdmin");
            Invoke(loginInst, "OnProviderChanged", new ChangeEventArgs { Value = "FakeAuth" });
            SetField(loginInst, "_username", "admin");
            SetField(loginInst, "_password", "pw");
            await InvokeTaskAsync(loginInst, "HandleKeyDown", new KeyboardEventArgs { Key = "Enter" });
            Assert.True(auth.IsAuthenticated);

            await InvokeTaskAsync(homeInst, "HandleAuthenticatedAsync");
            await InvokeTaskAsync(homeInst, "OnThemeChanged", new ChangeEventArgs { Value = "dracula" });

            foreach (var bannerType in BannerTypes)
            {
                studio.ShowBanner("msg", bannerType);
                Invoke(homeInst, "StateHasChanged");
            }

            foreach (var tab in Tabs)
            {
                await studio.SetActiveTabAsync(tab);
                Invoke(homeInst, "StateHasChanged");
            }

            studio.OpenAddNodeModal("sql_bulk");
            studio.OpenComponentPickerModal(1, "flow");
            studio.OpenDocModal("sql_bulk");
            studio.OpenPipelineEventDetail(new PipelineEventRecord { Id = 1, RunId = "r1", NodeId = "s1", ErrorMessage = "err", NodeOutput = "out" });
            studio.OpenUploadModal("pipeline");
            studio.OpenSignVerifyModal();
            Invoke(homeInst, "StateHasChanged");

            var html = home.ToHtmlString();
            Assert.Contains("Pipeline Designer", html);
            Assert.Contains("Component Guide:", html);
            Assert.Contains("Pipeline Step Event Detail", html);

            studio.OpenUiEventDetail(new UiEventRecord { EventId = 2, Username = "admin", EventAction = "ACT", ErrorMessage = "err", DetailsJson = "{}" });
            Invoke(homeInst, "StateHasChanged");
            Assert.Contains("UI Telemetry Event Detail", home.ToHtmlString());

            var nodeInst = activator.Get<NodeEditModal>();
            SetField(nodeInst, "_customAttrKey", "custom_k");
            SetField(nodeInst, "_customAttrVal", "custom_v");
            Invoke(nodeInst, "AddCustomAttribute");
            await InvokeTaskAsync(nodeInst, "HandleSaveAsync");

            var uploadInst = activator.Get<LocalFileUploadModal>();
            Invoke(uploadInst, "AutoDetectMetadataFromXml", "<pipeline name=\"p1\" description=\"d1\"><flow /></pipeline>", "fb", "p1.xml");
            Invoke(uploadInst, "AutoDetectMetadataFromXml", "<pipeline name=\"c1\"><variables /></pipeline>", "fb", "c1.xml");
            Invoke(uploadInst, "AutoDetectMetadataFromXml", "<flow_cli_options><debug>true</debug></flow_cli_options>", "opt", "opt.xml");
            Invoke(uploadInst, "AutoDetectMetadataFromXml", "<other />", "oth", "oth.xml");
            Invoke(uploadInst, "AutoDetectMetadataFromXml", "<unclosed", "bad", "bad.xml");
            SetField(uploadInst, "_xmlContent", "<pipeline name=\"p1\"><flow /></pipeline>");
            await InvokeTaskAsync(uploadInst, "LoadAsDraftOnlyAsync");
            studio.OpenUploadModal("pipeline");
            SetField(uploadInst, "_name", "p1");
            SetField(uploadInst, "_xmlContent", "<pipeline name=\"p1\"><flow /></pipeline>");
            await InvokeTaskAsync(uploadInst, "UploadAndSaveToDbAsync");

            var cryptoInst = activator.Get<CryptoSignVerifyModal>();
            SetField(cryptoInst, "_mode", "keygen");
            await InvokeTaskAsync(cryptoInst, "GenerateKeypairAsync");
            Invoke(cryptoInst, "OnSelectSavedProfileForSign", new ChangeEventArgs { Value = "default_signer" });
            Invoke(cryptoInst, "OnSelectSavedProfileForVerify", new ChangeEventArgs { Value = "default_signer" });
            await InvokeTaskAsync(cryptoInst, "ExecuteSignAsync");
            SetField(cryptoInst, "_mode", "verify");
            Invoke(cryptoInst, "StateHasChanged");
            await InvokeTaskAsync(cryptoInst, "ExecuteVerifyAsync");
            await InvokeTaskAsync(cryptoInst, "DownloadDetachedSigAsync");
            await InvokeTaskAsync(cryptoInst, "DownloadSignedEnvelopeAsync");
            Invoke(cryptoInst, "ClearSignatureFromDraft");

            await InvokeTaskAsync(homeInst, "HandleLogoutAsync");
        });
    }

    [Fact]
    public async Task SettingsTab_GranularAccessControlSubTabs_RenderAndExecuteGroupPipelineGroupAclAndEffectiveActions()
    {
        var (renderer, activator, studio, _) = CreateTestHost();
        await studio.InitializeStudioAsync();

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<SettingsTab>();
            var inst = activator.Get<SettingsTab>();

            // Artifacts pipeline group filter & JumpToPipelineAcl
            SetField(inst, "_artifactPipelineGroupFilter", "finance_etl");
            Invoke(inst, "StateHasChanged");
            Assert.Contains("pipe_1", root.ToHtmlString());

            SetField(inst, "_artifactPipelineGroupFilter", "__ungrouped__");
            Invoke(inst, "StateHasChanged");
            Invoke(inst, "ClearArtifactFilter");
            Invoke(inst, "JumpToPipelineAcl", "pipe_1");
            Invoke(inst, "StateHasChanged");
            Assert.Contains("Pipeline ACL Rule", root.ToHtmlString());

            // Sub-tab 1: API Users with scoped role + groups
            SetField(inst, "_accessSubTab", "users");
            Invoke(inst, "PopulateApiUserForm", new ApiUserRecord
            {
                Username = "op",
                Permission = "scoped",
                Description = "Scoped finance analyst",
                IsActive = true,
                Groups = ["finance_analysts", "custom_grp"]
            });
            Invoke(inst, "StateHasChanged");
            Assert.Contains("Scoped ACL Mode", root.ToHtmlString());
            await InvokeTaskAsync(inst, "SaveApiUserAsync");

            // Sub-tab 2: API User Groups
            SetField(inst, "_accessSubTab", "groups");
            Invoke(inst, "StateHasChanged");
            Assert.Contains("Upsert API User Group", root.ToHtmlString());
            Invoke(inst, "PopulateApiGroupForm", new ApiGroupRecord
            {
                Id = 1,
                Name = "finance_analysts",
                Description = "Finance team",
                Members = ["op", "CORP\\bob"]
            });
            await InvokeTaskAsync(inst, "SaveApiGroupAsync");
            await InvokeTaskAsync(inst, "RemoveGroupMemberAsync", "finance_analysts", "CORP\\bob");
            await InvokeTaskAsync(inst, "DeleteApiGroupAsync", "finance_analysts");
            Invoke(inst, "ResetApiGroupForm");

            // Sub-tab 3: Pipeline Groups
            SetField(inst, "_accessSubTab", "pipeline_groups");
            Invoke(inst, "StateHasChanged");
            Assert.Contains("Upsert Pipeline Group", root.ToHtmlString());
            Invoke(inst, "PopulatePipelineGroupForm", new PipelineGroupRecord
            {
                Id = 1,
                Name = "finance_etl",
                Description = "Finance ETL",
                NamePattern = "pipe_*",
                Pipelines = ["pipe_1", "custom_pipe"]
            });
            await InvokeTaskAsync(inst, "SavePipelineGroupAsync");
            await InvokeTaskAsync(inst, "RemovePipelineFromGroupAsync", "finance_etl", "pipe_1");
            await InvokeTaskAsync(inst, "DeletePipelineGroupAsync", "finance_etl");
            Invoke(inst, "ResetPipelineGroupForm");

            // Sub-tab 4: Granular Pipeline ACL Rules
            SetField(inst, "_accessSubTab", "acls");
            Invoke(inst, "OpenNewAclForPrincipal", "group", "finance_analysts");
            Invoke(inst, "OnAclTargetTypeChanged", new ChangeEventArgs { Value = "*" });
            Invoke(inst, "ToggleAclAction", "all", true);
            Invoke(inst, "ToggleAclAction", "read", true);
            Invoke(inst, "StateHasChanged");
            Assert.Contains("Evaluation Precedence", root.ToHtmlString());
            await InvokeTaskAsync(inst, "SavePipelineAclAsync");

            Invoke(inst, "PopulateAclForm", new PipelineAclRecord
            {
                Id = 1,
                PrincipalType = "group",
                PrincipalName = "finance_analysts",
                TargetType = "pipeline_group",
                TargetName = "finance_etl",
                Permissions = "read,execute",
                Effect = "allow",
                Description = "Allow finance"
            });
            SetField(inst, "_aclSearchQuery", "finance");
            SetField(inst, "_aclEffectFilter", "allow");
            Invoke(inst, "StateHasChanged");
            await InvokeTaskAsync(inst, "SavePipelineAclAsync");
            await InvokeTaskAsync(inst, "DeletePipelineAclAsync", 1);
            Invoke(inst, "ResetAclForm");

            // Sub-tab 5: Effective Permissions Inspector (including regression check for null collection setters)
            await InvokeTaskAsync(inst, "InspectUserEffectivePermissionsAsync", "op");
            SetField(inst, "_effSimulatedGroupsCsv", "finance_analysts,extra_grp");
            SetField(inst, "_effPipelineFilter", "pipe");
            await InvokeTaskAsync(inst, "EvaluateEffectivePermissionsAsync");
            Invoke(inst, "StateHasChanged");
            Assert.Contains("Effective Pipeline Permissions Inspector", root.ToHtmlString());
            Assert.Contains("Strict ACL (Scoped)", root.ToHtmlString());

            // Verify rendering when EffectivePermissionsResponseDto has null-assigned Groups / CreatableGroups / Pipelines
            var nullDto = new EffectivePermissionsResponseDto
            {
                Username = "ungrouped_non_admin",
                GlobalPermission = "read-only",
                StrictAcl = true,
                Groups = null!,
                CreatableGroups = null!,
                Pipelines = null!
            };
            SetField(inst, "_effectivePerms", nullDto);
            Invoke(inst, "StateHasChanged");
            Assert.Contains("Resolved User Groups (0)", root.ToHtmlString());
            Assert.Contains("No group memberships", root.ToHtmlString());

            await InvokeTaskAsync(inst, "InspectMyEffectivePermissionsAsync");

            // Stored Artifacts section with Templates filter, open & delete
            SetField(inst, "_section", "artifacts");
            SetField(inst, "_artifactTypeFilter", "template");
            SetField(inst, "_artifactSearchQuery", "nightly");
            Invoke(inst, "StateHasChanged");
            var tmplHtml = root.ToHtmlString();
            Assert.Contains("TMPL:PIPELINE", tmplHtml);
            Assert.Contains("nightly_tmpl", tmplHtml);

            var filtered = ((System.Collections.IEnumerable)Invoke(inst, "GetFilteredArtifacts")!).Cast<object>().ToList();
            Assert.NotEmpty(filtered);
            await InvokeTaskAsync(inst, "OpenStoredItemInStudioAsync", filtered[0]);
            await InvokeTaskAsync(inst, "DeleteStoredItemFromTableAsync", filtered[0]);
        });
    }

    [Fact]
    public async Task TemplatesTab_TemplateActionModal_AndDesignerTemplateMode_RenderAndExecute()
    {
        var (renderer, activator, studio, _) = CreateTestHost();
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await studio.InitializeStudioAsync();

            // 1. Render TemplatesTab and exercise filters
            var tabRoot = await renderer.RenderComponentAsync<TemplatesTab>();
            var tabInst = activator.Get<TemplatesTab>();
            Assert.Contains("nightly_tmpl", tabRoot.ToHtmlString());
            Assert.Contains("cfg_tmpl", tabRoot.ToHtmlString());

            SetField(tabInst, "_typeFilter", "config");
            SetField(tabInst, "_search", "alice");
            Invoke(tabInst, "StateHasChanged");
            Assert.Contains("cfg_tmpl", tabRoot.ToHtmlString());
            Assert.DoesNotContain("nightly_tmpl", tabRoot.ToHtmlString());
            tabInst.Dispose();

            // 2. Render TemplateActionModal in save_as_template and instantiate modes
            studio.OpenSaveAsTemplateModal();
            var modalRoot = await renderer.RenderComponentAsync<TemplateActionModal>();
            var modalInst = activator.Get<TemplateActionModal>();
            Assert.Contains("Save PIPELINE as Reusable Template", modalRoot.ToHtmlString());
            await InvokeTaskAsync(modalInst, "SubmitAsync");
            Assert.False(studio.IsTemplateModalOpen);

            studio.OpenInstantiateTemplateModal(studio.Templates[0]);
            Invoke(modalInst, "StateHasChanged");
            Assert.Contains("Save PIPELINE Template as Live PIPELINE", modalRoot.ToHtmlString());
            SetField(modalInst, "_groupsCsv", "finance_etl");
            await InvokeTaskAsync(modalInst, "SubmitAsync");
            Assert.False(studio.IsTemplateModalOpen);
            modalInst.Dispose();

            // 3. Render PipelineDesignerTab while editing a template (Template Mode banner + Run disabled)
            await studio.LoadTemplateIntoDesignerAsync(studio.Templates[0]);
            var designerRoot = await renderer.RenderComponentAsync<PipelineDesignerTab>();
            var designerHtml = designerRoot.ToHtmlString();
            Assert.Contains("Template Mode (PIPELINE)", designerHtml);
            Assert.Contains("Save Template", designerHtml);
            Assert.Contains("Templates cannot be executed", designerHtml);

            // 4. Exercise Handbook search, scroll, theme, and tag copy helpers
            var hbRoot = await renderer.RenderComponentAsync<Handbook>();
            var hbInst = activator.Get<Handbook>();
            await InvokeTaskAsync(hbInst, "OnThemeChanged", new ChangeEventArgs { Value = "dracula" });
            await InvokeTaskAsync(hbInst, "ScrollToTopAsync");
            Invoke(hbInst, "OnSearchInput", new ChangeEventArgs { Value = "sql" });
            Invoke(hbInst, "StateHasChanged");
            Assert.Contains("SQL", hbRoot.ToHtmlString(), StringComparison.OrdinalIgnoreCase);
            Invoke(hbInst, "OnSearchInput", new ChangeEventArgs { Value = "zzz_no_match_zzz" });
            Invoke(hbInst, "StateHasChanged");
            Assert.Contains("No matching component guides found", hbRoot.ToHtmlString());
            Invoke(hbInst, "ClearSearch");
            var filteredGroups = (IReadOnlyList<HandbookCategoryGroup>)Invoke(hbInst, "get_FilteredGroups")!;
            var firstItem = filteredGroups[0].Items[0];
            await InvokeTaskAsync(hbInst, "CopyTagAsync", firstItem);
            hbInst.Dispose();

            // 5. Exercise Home tab switching including templates + unauthenticated LoginPanel branch
            var homeRoot = await renderer.RenderComponentAsync<Home>();
            var homeInst = activator.Get<Home>();
            await InvokeTaskAsync(homeInst, "OnThemeChanged", new ChangeEventArgs { Value = "dark" });
            await InvokeTaskAsync(homeInst, "HandleLogoutAsync");
            homeInst.Dispose();

            // 6. Exercise RunnerTab BuildRequest (cert, PGP, pubkey, signed envelope) & CryptoSignVerifyModal sign/verify
            var cryptoSvc = new FlowCryptoService();
            var kp = cryptoSvc.GenerateKeyPairLocal("ECDSA-P256", "Runner Signer");
            studio.UpsertSavedKeyProfile(new SavedCryptoKeyProfile
            {
                Name = "cert_prof",
                Format = "openssl",
                PrivateKeyPem = kp.PrivateKeyPem,
                PublicKeyPem = kp.PublicKeyPem,
                CertificatePem = kp.CertificatePem,
                CaCertificatePem = kp.CertificatePem
            });
            studio.UpsertSavedKeyProfile(new SavedCryptoKeyProfile { Name = "pub_prof", PublicKeyPem = kp.PublicKeyPem });

            var runnerRoot = await renderer.RenderComponentAsync<RunnerTab>();
            var runnerInst = activator.Get<RunnerTab>();
            SetField(runnerInst, "_verifySignature", true);
            SetField(runnerInst, "_pipelineSourceMode", "draft");
            Invoke(runnerInst, "OnSelectVerifyProfile", new ChangeEventArgs { Value = "cert_prof" });
            var reqCert = (ExecutePipelineRequest)Invoke(runnerInst, "BuildRequest")!;
            Assert.NotNull(reqCert.VerifyCert);
            Invoke(runnerInst, "OnSelectVerifyProfile", new ChangeEventArgs { Value = "pub_prof" });
            var reqPub = (ExecutePipelineRequest)Invoke(runnerInst, "BuildRequest")!;
            Assert.NotNull(reqPub.VerifyPubKey);
            SetField(runnerInst, "_verifyKeyPem", "-----BEGIN PGP PUBLIC KEY BLOCK-----\nABC\n-----END PGP PUBLIC KEY BLOCK-----");
            var reqPgp = (ExecutePipelineRequest)Invoke(runnerInst, "BuildRequest")!;
            Assert.NotNull(reqPgp.VerifyKeyring);

            studio.OpenSignVerifyModal();
            var cModalRoot = await renderer.RenderComponentAsync<CryptoSignVerifyModal>();
            var cModalInst = activator.Get<CryptoSignVerifyModal>();
            Invoke(cModalInst, "OnSelectSavedProfileForSign", new ChangeEventArgs { Value = "cert_prof" });
            var pemFile = new FakeBrowserFile("key.pem", kp.PrivateKeyPem);
            await InvokeTaskAsync(cModalInst, "OnLoadPrivateKeyFileAsync", new InputFileChangeEventArgs([pemFile]));
            await InvokeTaskAsync(cModalInst, "OnLoadCertFileAsync", new InputFileChangeEventArgs([new FakeBrowserFile("cert.pem", kp.CertificatePem)]));
            await InvokeTaskAsync(cModalInst, "OnLoadDetachedSigFileAsync", new InputFileChangeEventArgs([new FakeBrowserFile("sig.txt", "SIG")]));
            await InvokeTaskAsync(cModalInst, "ExecuteSignAsync");
            Invoke(cModalInst, "OnSelectSavedProfileForVerify", new ChangeEventArgs { Value = "cert_prof" });
            await InvokeTaskAsync(cModalInst, "ExecuteVerifyAsync");
            cModalInst.Dispose();

            // 7. Exercise LocalFileUploadModal file selection & metadata detection
            studio.OpenUploadModal("pipeline");
            var upRoot = await renderer.RenderComponentAsync<LocalFileUploadModal>();
            var upInst = activator.Get<LocalFileUploadModal>();
            await InvokeTaskAsync(upInst, "OnFileSelectedAsync", new InputFileChangeEventArgs([new FakeBrowserFile("sample.xml", "<pipeline name=\"uploaded_pipe\" description=\"From file\"><flow/></pipeline>")]));
            upInst.Dispose();
        });
    }

    private sealed class FakeBrowserFile(string name, string text) : IBrowserFile
    {
        private readonly byte[] _bytes = Encoding.UTF8.GetBytes(text);
        public string Name => name;
        public DateTimeOffset LastModified => DateTimeOffset.UtcNow;
        public long Size => _bytes.Length;
        public string ContentType => "application/xml";
        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) =>
            new MemoryStream(_bytes, writable: false);
    }
}
