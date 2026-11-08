using FlowUI.Models;

namespace FlowUI.Tests;

public class ModelsTests
{
    [Fact]
    public void CrudPermissions_FullAdmin_And_ReadOnly_HaveExpectedFlags()
    {
        var admin = CrudPermissions.FullAdmin();
        Assert.True(admin.CanCreate);
        Assert.True(admin.CanRead);
        Assert.True(admin.CanUpdate);
        Assert.True(admin.CanDelete);
        Assert.True(admin.CanExecute);
        Assert.True(admin.IsAdmin);
        Assert.Equal("ADMIN (FULL CRUD+EXEC)", admin.ToSummaryBadge());

        var ro = CrudPermissions.ReadOnly();
        Assert.False(ro.CanCreate);
        Assert.True(ro.CanRead);
        Assert.False(ro.CanUpdate);
        Assert.False(ro.CanDelete);
        Assert.False(ro.CanExecute);
        Assert.False(ro.IsAdmin);
        Assert.Equal("R", ro.ToSummaryBadge());
    }

    [Fact]
    public void CrudPermissions_ToSummaryBadge_FormatsAllCombinations()
    {
        var none = new CrudPermissions { CanRead = false };
        Assert.Equal("NONE", none.ToSummaryBadge());

        var custom = new CrudPermissions
        {
            CanCreate = true,
            CanRead = true,
            CanUpdate = true,
            CanDelete = true,
            CanExecute = true,
            IsAdmin = false
        };
        Assert.Equal("C·R·U·D·EXEC", custom.ToSummaryBadge());
    }

    [Fact]
    public void AppUser_GetPermissions_AdminOverridesIndividualFlags()
    {
        var adminUser = new AppUser
        {
            Username = "admin",
            IsAdmin = true,
            CanCreate = false,
            CanRead = false,
            CanUpdate = false,
            CanDelete = false,
            CanExecute = false
        };

        var perms = adminUser.GetPermissions();
        Assert.True(perms.IsAdmin);
        Assert.True(perms.CanCreate);
        Assert.True(perms.CanRead);
        Assert.True(perms.CanUpdate);
        Assert.True(perms.CanDelete);
        Assert.True(perms.CanExecute);

        var regularUser = new AppUser
        {
            Username = "exec_user",
            IsAdmin = false,
            CanCreate = false,
            CanRead = true,
            CanExecute = true
        };
        var regPerms = regularUser.GetPermissions();
        Assert.False(regPerms.IsAdmin);
        Assert.False(regPerms.CanCreate);
        Assert.True(regPerms.CanRead);
        Assert.True(regPerms.CanExecute);
    }

    [Fact]
    public void AuthResult_Success_And_Fail_SetProperties()
    {
        var user = new AppUser { Username = "alice" };
        var ok = AuthResult.Success(user, "ApiBasic");
        Assert.True(ok.Succeeded);
        Assert.Same(user, ok.User);
        Assert.Equal("ApiBasic", ok.ProviderName);
        Assert.Null(ok.ErrorMessage);

        var fail = AuthResult.Fail("Invalid credentials", "ApiBasic");
        Assert.False(fail.Succeeded);
        Assert.Null(fail.User);
        Assert.Equal("Invalid credentials", fail.ErrorMessage);
        Assert.Equal("ApiBasic", fail.ProviderName);
    }

    [Fact]
    public void PipelineNode_DisplayName_Branches_And_Clone_WorkCorrectly()
    {
        var node = new PipelineNode
        {
            Id = 1,
            Section = "flow",
            NodeType = "if",
            SequenceOrder = 1
        };
        Assert.Equal(string.Empty, node.DisplayName);

        node.Attributes["id"] = "step_1";
        Assert.Equal("step_1", node.DisplayName);

        node.Attributes["name"] = "My Step";
        Assert.Equal("My Step", node.DisplayName);

        var thenNode = new PipelineNode { Id = 2, ParentNodeId = 1, NodeType = "then" };
        var elseNode = new PipelineNode { Id = 3, ParentNodeId = 1, NodeType = "else" };
        thenNode.Children.Add(new PipelineNode { Id = 4, ParentNodeId = 2, NodeType = "sql", ContentText = "SELECT 1;" });
        node.Children.Add(thenNode);
        node.Children.Add(elseNode);

        Assert.Same(thenNode, node.GetThenBranch());
        Assert.Same(elseNode, node.GetElseBranch());

        long nextId = 100;
        var cloned = node.Clone(() => ++nextId, newParentId: 50);
        Assert.Equal(101, cloned.Id);
        Assert.Equal(50, cloned.ParentNodeId);
        Assert.Equal("My Step", cloned.DisplayName);
        Assert.Equal(2, cloned.Children.Count);
        Assert.Equal(102, cloned.Children[0].Id);
        Assert.Equal(101, cloned.Children[0].ParentNodeId);
        Assert.Single(cloned.Children[0].Children);
        Assert.Equal(103, cloned.Children[0].Children[0].Id);
        Assert.Equal(102, cloned.Children[0].Children[0].ParentNodeId);
        Assert.Equal("SELECT 1;", cloned.Children[0].Children[0].ContentText);
    }

    [Fact]
    public void ArtifactRecord_And_CryptoModels_Aliases_MapCorrectly()
    {
        var artifact = new ArtifactRecord();
        artifact.XmlContent = "<pipeline />";
        Assert.Equal("<pipeline />", artifact.Content);
        Assert.Equal("<pipeline />", artifact.XmlContent);
        artifact.XmlContent = ""; // empty should not overwrite Content
        Assert.Equal("<pipeline />", artifact.Content);

        var keygenReq = new CryptoKeygenRequest { ValidDays = 90 };
        Assert.Equal(90, keygenReq.ValidityDays);
        Assert.Equal(90, keygenReq.ValidDays);

        var signReq = new CryptoSignRequest { Payload = "data", Format = "pkcs7" };
        Assert.Equal("data", signReq.Content);
        Assert.Equal("pkcs7", signReq.Scheme);

        var signResp = new CryptoSignResponse { SignedEnvelope = "FLOWSIGNED:v1:..." };
        Assert.Equal("FLOWSIGNED:v1:...", signResp.SignedPayload);
        Assert.Equal("FLOWSIGNED:v1:...", signResp.SignedEnvelope);

        var verifyReq = new CryptoVerifyRequest { Payload = "body", KeyringAsc = "pgp-key" };
        Assert.Equal("body", verifyReq.Content);
        Assert.Equal("pgp-key", verifyReq.KeyringPem);

        var verifyResp = new CryptoVerifyResponse
        {
            Verified = true,
            UnwrappedPayload = "<pipeline />",
            Error = "none"
        };
        Assert.True(verifyResp.Valid);
        Assert.Equal("<pipeline />", verifyResp.UnwrappedContent);
        Assert.Equal("none", verifyResp.ErrorMessage);
    }

    [Fact]
    public void TelemetryModels_ComputedProperties_And_Aliases_WorkCorrectly()
    {
        var streamEvt = new ExecutionStreamNodeEvent
        {
            NodeKind = "sql",
            SequenceNumAlt = 42,
            ErrorMessageAlt = "failed query"
        };
        Assert.Equal("sql", streamEvt.Kind);
        Assert.Equal(42, streamEvt.SequenceNum);
        Assert.Equal("failed query", streamEvt.ErrorMessage);

        var step = new ScriptStepResultDto
        {
            NodeIdAlt = "step_sql",
            ReturnCodeRaw = "0",
            DurationRaw = "2500000",
            OutputRaw = "raw_out",
            ResultsString = "formatted_out"
        };
        Assert.Equal("step_sql", step.ScriptId);
        Assert.Equal("step_sql", step.NodeId);
        Assert.Equal("formatted_out", step.EffectiveNodeOutput);
        Assert.Equal(0, step.EffectiveReturnCode);
        Assert.Equal("succeeded", step.EffectiveStatus);
        Assert.Equal("2.5 ms", step.EffectiveDurationDisplay);

        step.ResultsString = string.Empty;
        step.ReturnCodeRaw = "2";
        step.DurationRaw = "15ms";
        Assert.Equal("raw_out", step.EffectiveNodeOutput);
        Assert.Equal(2, step.EffectiveReturnCode);
        Assert.Equal("failed", step.EffectiveStatus);
        Assert.Equal("15ms", step.EffectiveDurationDisplay);

        var execReq = new ExecutePipelineRequest
        {
            VerifyPubKey = "pub",
            VerifyCert = "cert",
            VerifyCaCert = "ca",
            VerifyKeyring = "kr",
            PipelineSig = "sig"
        };
        Assert.Equal("pub", execReq.PublicKey);
        Assert.Equal("cert", execReq.Cert);
        Assert.Equal("ca", execReq.CaCert);
        Assert.Equal("kr", execReq.Keyring);
        Assert.Equal("sig", execReq.Signature);

        var execResp = new ExecutePipelineResponse { Error = "boom" };
        Assert.Equal("boom", execResp.ErrorMessage);
    }
}
