using System.Net;
using System.Text;
using FlowUI.Models;
using FlowUI.Services;
using FlowUI.Services.Auth;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowUI.Tests;

public class StudioStateServiceTests
{
    private static (StudioStateService Studio, UiAuthenticationService Auth, FlowCryptoService Crypto, FakeUiAuthProvider Provider) CreateStudio(
        FakeHttpMessageHandler? httpHandler = null,
        bool loginAsAdmin = true)
    {
        httpHandler ??= new FakeHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            return path switch
            {
                "/api/v1/health" => FakeHttpMessageHandler.JsonResponse("""{"status":"ok","dialect":"sqlserver"}"""),
                "/api/v1/artifacts/pipeline" => req.Method == HttpMethod.Post
                    ? FakeHttpMessageHandler.JsonResponse("""{"id":101,"name":"saved_pipe","type":"pipeline","content":"<pipeline />"}""")
                    : FakeHttpMessageHandler.JsonResponse("""[{"id":101,"name":"saved_pipe","type":"pipeline","content":"<pipeline name=\"saved_pipe\"><flow /></pipeline>"}]"""),
                "/api/v1/artifacts/config" => FakeHttpMessageHandler.JsonResponse("""[{"id":201,"name":"cfg_1","type":"config","content":"<config name=\"cfg_1\"><variables /></config>"}]"""),
                "/api/v1/artifacts/options" => FakeHttpMessageHandler.JsonResponse("""[{"id":301,"name":"opt_1","type":"options","content":"<options><option flag=\"debug\" value=\"true\"/></options>"}]"""),
                _ => FakeHttpMessageHandler.JsonResponse("{}")
            };
        });

        var http = new HttpClient(httpHandler) { BaseAddress = new Uri("http://localhost:8080") };
        var api = new FlowApiClient(http, NullLogger<FlowApiClient>.Instance);
        var provider = new FakeUiAuthProvider();
        var auth = new UiAuthenticationService([provider], api);
        if (loginAsAdmin)
        {
            auth.LoginAsync("admin", "pw").GetAwaiter().GetResult();
        }

        var catalog = new CatalogService();
        var xmlGen = new XmlGeneratorService();
        var xmlImp = new XmlImporterService();
        var crypto = new FlowCryptoService();
        var studio = new StudioStateService(api, auth, catalog, xmlGen, xmlImp, crypto, NullLogger<StudioStateService>.Instance);

        return (studio, auth, crypto, provider);
    }

    [Fact]
    public void Constructor_Banners_AndModals_WorkCorrectly()
    {
        var (studio, _, _, _) = CreateStudio();

        Assert.Equal("default_pipeline", studio.CurrentDraft.Name);
        Assert.Equal(4, studio.CountAllNodes());
        Assert.NotEmpty(studio.GeneratedXml);
        Assert.Contains("default_db", studio.GetAvailableDatabaseConnectionNames());
        Assert.NotNull(studio.Crypto);

        int changeCount = 0;
        studio.OnChange += () => changeCount++;

        studio.ShowBanner("Test msg", "warning");
        Assert.Equal("Test msg", studio.StatusBannerMessage);
        Assert.Equal("warning", studio.StatusBannerType);
        Assert.NotNull(studio.StatusBannerTime);
        Assert.True(changeCount > 0);

        studio.ClearBanner();
        Assert.Null(studio.StatusBannerMessage);

        studio.OpenSignVerifyModal();
        Assert.True(studio.IsSignVerifyModalOpen);
        studio.CloseSignVerifyModal();
        Assert.False(studio.IsSignVerifyModalOpen);

        studio.OpenUploadModal();
        Assert.True(studio.IsUploadModalOpen);
        Assert.Equal("pipeline", studio.UploadModalDefaultType);
        studio.OpenUploadModal("CONFIG");
        Assert.Equal("config", studio.UploadModalDefaultType);
        studio.CloseUploadModal();
        Assert.False(studio.IsUploadModalOpen);

        studio.OpenComponentPickerModal(123, "then");
        Assert.True(studio.IsComponentPickerModalOpen);
        Assert.Equal(123, studio.PickerParentNodeId);
        Assert.Equal("then", studio.PickerParentLabel);
        studio.CloseComponentPickerModal();
        Assert.False(studio.IsComponentPickerModalOpen);
        Assert.Null(studio.PickerParentNodeId);

        studio.OpenDocModal("sql");
        Assert.True(studio.IsDocModalOpen);
        Assert.Equal("sql", studio.DocModalComponentType);
        studio.CloseDocModal();
        Assert.False(studio.IsDocModalOpen);

        var pEv = new PipelineEventRecord { Id = 1, NodeId = "n1" };
        studio.OpenPipelineEventDetail(pEv);
        Assert.True(studio.IsEventDetailModalOpen);
        Assert.Same(pEv, studio.SelectedPipelineEvent);
        Assert.Null(studio.SelectedUiEvent);

        var uiEv = new UiEventRecord { EventId = 2, EventAction = "ACT" };
        studio.OpenUiEventDetail(uiEv);
        Assert.True(studio.IsEventDetailModalOpen);
        Assert.Same(uiEv, studio.SelectedUiEvent);
        Assert.Null(studio.SelectedPipelineEvent);

        studio.CloseEventDetailModal();
        Assert.False(studio.IsEventDetailModalOpen);
        Assert.Null(studio.SelectedPipelineEvent);
        Assert.Null(studio.SelectedUiEvent);
    }

    [Fact]
    public async Task Initialize_Refresh_Load_Upload_AndDeleteArtifacts_CoverAllPaths()
    {
        var crypto = new FlowCryptoService();
        var keypair = crypto.GenerateKeyPairLocal("ECDSA-P256", "Test");
        var signedXml = FlowCryptoService.WrapSignedPayload(
            "openssl",
            crypto.SignOpenSslLocal(Encoding.UTF8.GetBytes("<pipeline name=\"sig_p\"><flow /></pipeline>"), keypair.PrivateKeyPem),
            Encoding.UTF8.GetBytes("<pipeline name=\"sig_p\"><flow /></pipeline>"));
        var encXml = crypto.EncryptArmored("<pipeline name=\"enc_p\"><flow /></pipeline>", "secret123");

        bool failDelete = false;
        bool returnFalseDelete = false;

        var handler = new FakeHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path == "/api/v1/health")
                return FakeHttpMessageHandler.JsonResponse("""{"status":"ok","dialect":"sqlserver"}""");

            if (path == "/api/v1/artifacts/pipeline" && req.Method == HttpMethod.Get)
            {
                return FakeHttpMessageHandler.JsonResponse($$"""
                    [
                      {"id":101,"name":"saved_pipe","type":"pipeline","content":"<pipeline name=\"saved_pipe\"><flow /></pipeline>"},
                      {"id":102,"name":"enc_pipe","type":"pipeline","content":"{{encXml}}"},
                      {"id":103,"name":"sig_pipe","type":"pipeline","content":"{{signedXml}}"}
                    ]
                    """);
            }
            if (path == "/api/v1/artifacts/config" && req.Method == HttpMethod.Get)
            {
                return FakeHttpMessageHandler.JsonResponse("""[{"id":201,"name":"cfg_1","type":"config","content":"<config name=\"cfg_1\"><variables /></config>"}]""");
            }
            if (path == "/api/v1/artifacts/options" && req.Method == HttpMethod.Get)
            {
                return FakeHttpMessageHandler.JsonResponse("""[{"id":301,"name":"opt_1","type":"options","content":"<options><option flag=\"debug\" value=\"true\"/></options>"}]""");
            }
            if (path == "/api/v1/artifacts/pipeline/id/104")
            {
                return FakeHttpMessageHandler.JsonResponse("""{"id":104,"name":"fetched_by_id","type":"pipeline","content":"<pipeline name=\"fetched_by_id\"><flow /></pipeline>"}""");
            }
            if (path == "/api/v1/artifacts/pipeline/id/102" && req.Method == HttpMethod.Get)
            {
                return FakeHttpMessageHandler.JsonResponse($$"""{"id":102,"name":"enc_pipe","type":"pipeline","content":"{{encXml}}"}""");
            }
            if (path == "/api/v1/artifacts/pipeline/id/999")
            {
                return FakeHttpMessageHandler.JsonResponse("""{"error":"not found"}""", HttpStatusCode.NotFound);
            }
            if (path == "/api/v1/artifacts/pipeline/by_name_pipe")
            {
                return FakeHttpMessageHandler.JsonResponse("""{"id":105,"name":"by_name_pipe","type":"pipeline","content":"<pipeline name=\"by_name_pipe\"><flow /></pipeline>"}""");
            }
            if (req.Method == HttpMethod.Post && path.StartsWith("/api/v1/artifacts/"))
            {
                if (req.RequestUri.Query.Contains("secure_key=throw"))
                    return FakeHttpMessageHandler.JsonResponse("""{"error":"upload error"}""", HttpStatusCode.BadRequest);
                return FakeHttpMessageHandler.JsonResponse("""{"id":110,"name":"uploaded","type":"pipeline","content":"<pipeline name=\"uploaded\"><flow /></pipeline>"}""");
            }
            if (req.Method == HttpMethod.Delete)
            {
                if (failDelete)
                    return FakeHttpMessageHandler.JsonResponse("""{"error":"delete failed"}""", HttpStatusCode.BadRequest);
                return FakeHttpMessageHandler.JsonResponse("""{"deleted":true}""");
            }
            return FakeHttpMessageHandler.JsonResponse("{}");
        });

        var (studio, _, _, _) = CreateStudio(handler);

        await studio.CheckApiHealthOnlyAsync();
        Assert.True(studio.ApiOnline);

        await studio.InitializeStudioAsync();
        Assert.Equal(3, studio.Pipelines.Count);
        Assert.Single(studio.Configs);
        Assert.Single(studio.OptionsList);

        // Load by ID found in cache
        await studio.LoadDatabaseArtifactByIdAsync("pipeline", 101);
        Assert.Equal("saved_pipe", studio.CurrentDraft.Name);

        // Load by ID fetched from API
        await studio.LoadDatabaseArtifactByIdAsync("pipeline", 104);
        Assert.Equal("fetched_by_id", studio.CurrentDraft.Name);

        // Load by ID not found
        await studio.LoadDatabaseArtifactByIdAsync("pipeline", 999);
        Assert.Contains("Could not find", studio.StatusBannerMessage);

        // Load encrypted artifact (locked because no session key)
        await studio.LoadDatabaseArtifactByIdAsync("pipeline", 102);
        Assert.True(studio.RequiresDecryptionUnlock);

        // Load signed artifact
        await studio.LoadDatabaseArtifactByIdAsync("pipeline", 103);
        Assert.True(studio.CurrentDraft.Signed);
        Assert.Equal("openssl", studio.CurrentDraft.SignatureFormat);

        // Load options artifact with legacy <option flag="..."/>
        await studio.LoadDatabaseArtifactByIdAsync("options", 301);
        Assert.Equal("options", studio.CurrentDraft.ArtifactType);
        Assert.Contains("<debug>true</debug>", studio.GeneratedXml);

        // Load artifact with empty content and Name only (fetches by name)
        await studio.LoadPipelineArtifactAsync(new ArtifactRecord { Id = 0, Name = "by_name_pipe", Content = "" });
        Assert.Equal("by_name_pipe", studio.CurrentDraft.Name);

        // Load artifact with invalid XML falls back to raw text in GeneratedXml
        await studio.LoadArtifactRecordAsync(new ArtifactRecord { Id = 55, Name = "bad_xml", Content = "<not_closed" }, "pipeline");
        Assert.Equal("<not_closed", studio.GeneratedXml);

        // Load artifact with completely empty content when API also returns empty content
        await studio.LoadArtifactRecordAsync(new ArtifactRecord { Id = 0, Name = "", Content = "" }, "config");
        Assert.Equal("config", studio.CurrentDraft.ArtifactType);

        // Upload local artifact validation and success
        Assert.Null(await studio.UploadLocalArtifactToDbAsync("pipeline", "", "desc", "<pipeline />"));
        Assert.Null(await studio.UploadLocalArtifactToDbAsync("pipeline", "n", "desc", ""));

        var uploaded = await studio.UploadLocalArtifactToDbAsync("pipeline", "uploaded", "desc", "<pipeline name=\"uploaded\"><flow /></pipeline>", true, true, "myKey");
        Assert.NotNull(uploaded);
        Assert.Equal(110, uploaded.Id);

        // Upload API failure
        var failedUpload = await studio.UploadLocalArtifactToDbAsync("pipeline", "up_fail", "desc", "<pipeline />", true, false, "throw");
        Assert.Null(failedUpload);

        // LoadLocalArtifactAsDraftAsync: valid XML, encrypted locked XML, invalid XML
        studio.SetSessionSecureKey(null);
        await studio.LoadLocalArtifactAsDraftAsync("pipeline", "local_enc", "desc", encXml);
        Assert.True(studio.RequiresDecryptionUnlock);

        await studio.LoadLocalArtifactAsDraftAsync("pipeline", "local_ok", "desc", "<pipeline name=\"local_ok\"><flow /></pipeline>");
        Assert.False(studio.RequiresDecryptionUnlock);
        Assert.Equal("local_ok", studio.CurrentDraft.Name);

        await studio.LoadLocalArtifactAsDraftAsync("pipeline", "local_bad", "desc", "<unclosed");
        Assert.Equal("<unclosed", studio.GeneratedXml);

        // Delete artifact by ID, by Name, with ID=0/Name="", and on error
        studio.CurrentDraft.ApiArtifactId = 101;
        studio.CurrentDraft.ArtifactType = "pipeline";
        await studio.DeletePipelineArtifactAsync(new ArtifactRecord { Id = 101, Name = "saved_pipe" });
        Assert.Null(studio.CurrentDraft.ApiArtifactId);

        await studio.DeleteArtifactAsync("pipeline", new ArtifactRecord { Id = 0, Name = "by_name_pipe" });
        await studio.DeleteArtifactAsync("pipeline", new ArtifactRecord { Id = 0, Name = "" });
        Assert.Contains("Could not delete", studio.StatusBannerMessage);

        failDelete = true;
        await studio.DeleteArtifactAsync("pipeline", new ArtifactRecord { Id = 102, Name = "enc_pipe" });
        Assert.Contains("Delete error", studio.StatusBannerMessage);
        _ = returnFalseDelete;
    }

    [Fact]
    public async Task NodeOperations_Add_Edit_Duplicate_Move_Delete_AndClear_UpdateDraftAndXml()
    {
        var (studio, _, _, _) = CreateStudio();

        // Unknown component
        studio.OpenAddNodeModal("non_existent_component");
        Assert.False(studio.IsNodeEditModalOpen);

        // Disallowed component in config mode
        studio.CurrentDraft.ArtifactType = "config";
        studio.OpenAddNodeModal("sql_bulk"); // sql_bulk is flow-only
        Assert.False(studio.IsNodeEditModalOpen);

        // Allowed component in config mode with section="flow" (if) resolves to preflight
        studio.OpenAddNodeModal("if");
        Assert.True(studio.IsNodeEditModalOpen);
        Assert.Equal("preflight", studio.TargetSection);
        studio.CloseNodeEditModal();

        studio.CurrentDraft.ArtifactType = "pipeline";

        // Add an <if> node to flow
        studio.OpenAddNodeModal("if", null, "flow");
        Assert.True(studio.IsNodeEditModalOpen);
        Assert.NotNull(studio.EditingNode);

        await studio.SaveNodeModalAsync(
            new Dictionary<string, string> { ["id"] = "branch_1", ["var"] = "env", ["equals"] = "prod", ["empty_attr"] = "  " },
            string.Empty);

        Assert.False(studio.IsNodeEditModalOpen);
        Assert.Equal(2, studio.CurrentDraft.FlowNodes.Count);
        var addedIf = studio.CurrentDraft.FlowNodes[1];
        Assert.Equal("if", addedIf.NodeType);
        var thenBranch = addedIf.GetThenBranch()!;
        Assert.NotNull(thenBranch);
        Assert.NotNull(addedIf.GetElseBranch());

        // Add child node inside <then> branch
        studio.OpenAddNodeModal("script", thenBranch.Id);
        await studio.SaveNodeModalAsync(
            new Dictionary<string, string> { ["id"] = "log_in_then", ["level"] = "INFO" },
            "Hello inside then");
        Assert.Single(thenBranch.Children);

        // OpenEditNodeModal on existing node and custom unknown node type
        studio.OpenEditNodeModal(thenBranch.Children[0]);
        Assert.True(studio.IsEditingExistingNode);
        await studio.SaveNodeModalAsync(
            new Dictionary<string, string> { ["id"] = "log_in_then_updated", ["level"] = "WARN" },
            "Updated message");
        Assert.Equal("log_in_then_updated", thenBranch.Children[0].Attributes["id"]);

        var customNode = new PipelineNode { Id = 9999, NodeType = "custom_tag", Section = "flow", ContentText = "body" };
        studio.OpenEditNodeModal(customNode);
        Assert.Equal("Custom", studio.EditingComponentMeta?.Category);
        studio.CloseNodeEditModal();

        // Duplicate first flow node & variable node (which has "name" attribute)
        var firstVarNode = studio.CurrentDraft.VariableNodes[0];
        await studio.DuplicateNodeAsync(firstVarNode);
        Assert.Equal(2, studio.CurrentDraft.VariableNodes.Count);
        Assert.EndsWith("_copy", studio.CurrentDraft.VariableNodes[1].Attributes["name"]);

        var firstFlowNode = studio.CurrentDraft.FlowNodes[0];
        await studio.DuplicateNodeAsync(firstFlowNode);
        Assert.Equal(3, studio.CurrentDraft.FlowNodes.Count);
        Assert.Equal("verify_connection_copy", studio.CurrentDraft.FlowNodes[1].Attributes["id"]);

        // Move duplicated node up (index 1 -> index 0)
        var dupId = studio.CurrentDraft.FlowNodes[1].Id;
        await studio.MoveNodeOrderAsync(dupId, -1);
        Assert.Equal(dupId, studio.CurrentDraft.FlowNodes[0].Id);

        // Delete child node inside <then> and duplicated root node
        await studio.DeleteNodeAsync(thenBranch.Children[0].Id);
        Assert.Empty(thenBranch.Children);

        await studio.DeleteNodeAsync(dupId);
        Assert.Equal(2, studio.CurrentDraft.FlowNodes.Count);

        // Clear specific sections and all
        await studio.ClearCurrentSectionOrAllAsync("variables");
        Assert.Empty(studio.CurrentDraft.VariableNodes);
        await studio.ClearCurrentSectionOrAllAsync("databases");
        Assert.Empty(studio.CurrentDraft.DatabaseNodes);
        await studio.ClearCurrentSectionOrAllAsync("preflight");
        Assert.Empty(studio.CurrentDraft.PreflightNodes);
        await studio.ClearCurrentSectionOrAllAsync();
        Assert.Equal(0, studio.CountAllNodes());
    }

    [Fact]
    public void OptionEntries_Add_Update_AndRemove_RegenerateOptionsXml()
    {
        var (studio, _, _, _) = CreateStudio();
        studio.LoadDefaultOptionsDraft("MY_OPTS", "My Desc");
        Assert.Equal("options", studio.CurrentDraft.ArtifactType);
        Assert.Equal("MY_OPTS", studio.CurrentDraft.Name);
        Assert.Equal(3, studio.CurrentDraft.OptionEntries.Count);

        studio.LoadDefaultConfigDraft("MY_CFG", "Cfg Desc");
        Assert.Equal("config", studio.CurrentDraft.ArtifactType);
        Assert.Equal("MY_CFG", studio.CurrentDraft.Name);

        studio.CurrentDraft.ArtifactType = "options";
        studio.CurrentDraft.OptionEntries.Clear();

        studio.AddOptionEntry("--var", "A=1");
        Assert.Single(studio.CurrentDraft.OptionEntries);
        var entryId = studio.CurrentDraft.OptionEntries[0].Id;

        studio.UpdateOptionEntry(entryId, null, "");
        studio.UpdateOptionEntry(entryId, "format", null);
        Assert.Equal("format", studio.CurrentDraft.OptionEntries[0].Flag);

        studio.UpdateOptionEntry(entryId, null, "markdown");
        Assert.Contains("<format>markdown</format>", studio.GeneratedXml);

        studio.RemoveOptionEntry(entryId);
        Assert.Empty(studio.CurrentDraft.OptionEntries);
    }

    [Fact]
    public async Task CryptoEnvelope_Unwrap_Unlock_AndApiOrLocalSignVerify_WorkEndToEnd()
    {
        var (studio, _, crypto, _) = CreateStudio();
        const string innerXml = "<pipeline name=\"enc_pipe\"><flow><sql id=\"q1\" db=\"default_db\">SELECT 1;</sql></flow></pipeline>";
        var encrypted = crypto.EncryptArmored(innerXml, "my-secret-key");

        Assert.Equal("", studio.UnwrapEnvelopeForDesigner("").UnwrappedXml);
        Assert.False( await studio.TryUnlockCurrentArtifactAsync("   "));

        // Importing encrypted XML without session key locks designer
        await studio.ApplyXmlToCanvasAsync(encrypted);
        Assert.True(studio.RequiresDecryptionUnlock);
        Assert.Equal(encrypted, studio.LockedCiphertextPayload);

        // Wrong key fails unlock
        var wrongUnlock = await studio.TryUnlockCurrentArtifactAsync("wrong-key");
        Assert.False(wrongUnlock);
        Assert.True(studio.RequiresDecryptionUnlock);

        // Correct key unlocks and populates nodes
        var rightUnlock = await studio.TryUnlockCurrentArtifactAsync("my-secret-key");
        Assert.True(rightUnlock);
        Assert.False(studio.RequiresDecryptionUnlock);
        Assert.Single(studio.CurrentDraft.FlowNodes);
        Assert.Equal("q1", studio.CurrentDraft.FlowNodes[0].Attributes["id"]);

        // Apply invalid XML to canvas triggers catch block
        await studio.ApplyXmlToCanvasAsync("<invalid_xml");
        Assert.Contains("XML Import Error", studio.StatusBannerMessage);

        // Local OpenSSL sign + verify when ApiOnline is false
        var keypair = crypto.GenerateKeyPairLocal("ECDSA-P256", "Studio Signer");
        await studio.SignCurrentDraftAsync("openssl", keypair.PrivateKeyPem, keypair.CertificatePem);
        Assert.True(studio.CurrentDraft.Signed);
        Assert.NotEmpty(studio.CurrentDraft.SignedEnvelopeContent!);

        // Non-openssl format when offline fails gracefully
        await studio.SignCurrentDraftAsync("gpg", "priv");
        Assert.Contains("Signing failed", studio.StatusBannerMessage);

        var verifyRes = await studio.VerifyCurrentDraftSignatureAsync(certPem: keypair.CertificatePem);
        Assert.True(verifyRes.Verified);

        // Verify with detached signature override
        var detachedSig = studio.CurrentDraft.DetachedSignatureContent!;
        studio.CurrentDraft.SignedEnvelopeContent = "";
        var verifyDetached = await studio.VerifyCurrentDraftSignatureAsync(signatureOverride: detachedSig, publicKeyPem: keypair.PublicKeyPem);
        Assert.True(verifyDetached.Verified);

        // Verify with raw base64 signature override
        var verifyBadB64 = await studio.VerifyCurrentDraftSignatureAsync(signatureOverride: "!!!not_base64!!!");
        Assert.False(verifyBadB64.Verified);
    }

    [Fact]
    public async Task PermissionChecks_And_CreateNewDrafts_EnforceRoleSecurity()
    {
        var (studio, auth, _, _) = CreateStudio(loginAsAdmin: false);
        // Without login, CurrentPermissions has all false flags
        Assert.False(auth.CurrentPermissions.CanCreate);

        await studio.CreateNewPipelineDraftAsync("p", "d");
        Assert.Contains("Permission denied", studio.StatusBannerMessage);

        await studio.ApplyXmlToCanvasAsync("<pipeline />");
        Assert.Contains("Permission denied", studio.StatusBannerMessage);

        await studio.LoadArtifactRecordAsync(new ArtifactRecord { Id = 1, Name = "p" });
        Assert.Contains("Permission denied", studio.StatusBannerMessage);

        await studio.SaveCurrentPipelineToApiAsync();
        Assert.Contains("Permission denied", studio.StatusBannerMessage);

        Assert.Null(await studio.UploadLocalArtifactToDbAsync("pipeline", "p", "d", "<pipeline />"));
        await studio.LoadLocalArtifactAsDraftAsync("pipeline", "p", "d", "<pipeline />");
        await studio.DeleteArtifactAsync("pipeline", new ArtifactRecord { Id = 1, Name = "p" });
        studio.AddOptionEntry("vars", "A=1");
        await studio.ClearCurrentSectionOrAllAsync();
        studio.OpenAddNodeModal("sql");
        studio.OpenEditNodeModal(new PipelineNode());
        await studio.DeleteNodeAsync(1);
        await studio.MoveNodeOrderAsync(1, 1);
        await studio.DuplicateNodeAsync(new PipelineNode());

        // Log in as admin and create drafts for pipeline, config, options
        await auth.LoginAsync("admin", "pw");
        await studio.CreateNewArtifactDraftAsync("pipeline", "new_p", "desc");
        Assert.Equal("new_p", studio.CurrentDraft.Name);

        await studio.CreateNewArtifactDraftAsync("config", null, null);
        Assert.Equal("config", studio.CurrentDraft.ArtifactType);

        await studio.CreateNewArtifactDraftAsync("options", null, null);
        Assert.Equal("options", studio.CurrentDraft.ArtifactType);
    }

    [Fact]
    public async Task Tabs_Themes_Profiles_AndSaveArtifact_WorkCorrectly()
    {
        var (studio, _, _, _) = CreateStudio();

        // Switch to config tab and back to pipeline tab preserves drafts
        await studio.SetActiveTabAsync("config");
        Assert.Equal("config", studio.ActiveTab);
        Assert.Equal("config", studio.CurrentDraft.ArtifactType);

        await studio.SetActiveTabAsync("pipeline");
        Assert.Equal("pipeline", studio.ActiveTab);
        Assert.Equal("pipeline", studio.CurrentDraft.ArtifactType);

        // Set valid and invalid themes
        await studio.SetThemeAsync("");
        await studio.SetThemeAsync("dracula");
        Assert.Equal("dracula", studio.ActiveTheme);
        await studio.SetThemeAsync("non_existent_theme");
        Assert.Equal("dracula", studio.ActiveTheme);

        // Upsert DB and Key profiles (insert and update existing)
        studio.UpsertSavedDbProfile(new SavedDatabaseProfile { Name = "" });
        var dbProf = new SavedDatabaseProfile
        {
            Name = "analytics_db",
            Driver = "postgres",
            ConnectionString = "Host=localhost;",
            LastTestSucceeded = true
        };
        studio.UpsertSavedDbProfile(dbProf);
        Assert.Contains("analytics_db", studio.GetAvailableDatabaseConnectionNames());

        // Update existing DB profile with changed connection string resets test status
        studio.UpsertSavedDbProfile(new SavedDatabaseProfile
        {
            Id = dbProf.Id,
            Name = "analytics_db",
            Driver = "postgres",
            ConnectionString = "Host=newhost;",
            LastTestSucceeded = null
        });

        studio.UpsertSavedKeyProfile(new SavedCryptoKeyProfile { Name = "" });
        studio.UpsertSavedKeyProfile(new SavedCryptoKeyProfile
        {
            Name = "Main Key",
            PublicKeyPem = "PUB_PEM",
            CertificatePem = "CERT_PEM"
        });
        studio.UpsertSavedKeyProfile(new SavedCryptoKeyProfile
        {
            Name = "Main Key",
            PublicKeyPem = "PUB_PEM_2",
            CertificatePem = "CERT_PEM_2"
        });
        Assert.Single(studio.SavedKeyProfiles);
        Assert.Equal("PUB_PEM_2", studio.SavedKeyProfiles[0].PublicKeyPem);

        // Save artifact via API
        await studio.SaveCurrentArtifactAsync("overrideKey");
        Assert.False(studio.CurrentDraft.IsDirty);
        Assert.Equal(101, studio.CurrentDraft.ApiArtifactId);

        var events = studio.GetUiEvents(categoryFilter: "DESIGNER");
        Assert.NotEmpty(events);
    }
}
