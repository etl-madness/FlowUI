using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FlowUIv2.Models;
using FlowUIv2.Services.Auth;

namespace FlowUIv2.Services;

/// <summary>
/// Scoped UI state coordinator for Flow Builder Studio.
/// Manages the active pipeline draft, node tree operations, API artifact cache,
/// active modals, theme selection, and in-memory session telemetry.
/// All artifact retrieval, validation, execution, and persistence go strictly through go-flowAPI.
/// </summary>
public sealed class StudioStateService
{
    private readonly FlowApiClient _api;
    private readonly IAuthenticationService _auth;
    private readonly CatalogService _catalog;
    private readonly XmlGeneratorService _xmlGen;
    private readonly XmlImporterService _xmlImporter;
    private readonly FlowCryptoService _crypto;
    private readonly ILogger<StudioStateService> _logger;

    private long _nodeIdCounter = 1000;
    private long _uiEventIdCounter = 0;

    public StudioStateService(
        FlowApiClient api,
        IAuthenticationService auth,
        CatalogService catalog,
        XmlGeneratorService xmlGen,
        XmlImporterService xmlImporter,
        FlowCryptoService crypto,
        ILogger<StudioStateService> logger)
    {
        _api = api;
        _auth = auth;
        _catalog = catalog;
        _xmlGen = xmlGen;
        _xmlImporter = xmlImporter;
        _crypto = crypto;
        _logger = logger;

        SavedDbProfiles.Add(new SavedDatabaseProfile
        {
            Id = 1,
            Name = "default_db",
            Driver = "sqlserver",
            ConnectionString = "Server=localhost;Database=DB;Trusted_Connection=True;TrustServerCertificate=True;",
            Description = "Default pipeline target database profile",
            UpdatedAt = DateTime.UtcNow
        });

        SeedDefaultDraft();
    }

    public event Action? OnChange;
    public void NotifyStateChanged() => OnChange?.Invoke();

    // =========================================================================
    // Active Tab & Theme State
    // =========================================================================
    private static string _lastSavedTheme = "midnight";
    public string ActiveTab { get; private set; } = "pipeline"; // pipeline, config, options, runner, logs, settings
    public string ActiveTheme { get; private set; } = _lastSavedTheme;

    public static readonly IReadOnlyList<(string Id, string Label)> Themes = new List<(string, string)>
    {
        ("midnight", "Midnight Slate (Dark)"),
        ("obsidian", "Obsidian Carbon (Dark)"),
        ("dracula", "Dracula (Dark)"),
        ("nord", "Nordic Frost (Dark)"),
        ("monokai", "Monokai Pro (Dark)"),
        ("gruvbox-dark", "Gruvbox (Dark)"),
        ("solarized-dark", "Solarized (Dark)"),
        ("matrix", "Matrix Terminal (Dark)"),
        ("synthwave", "Synthwave Neon (Dark)"),
        ("daylight", "Daylight Clean (Light)"),
        ("github-light", "GitHub Light (Light)"),
        ("solarized-light", "Solarized (Light)"),
        ("nord-light", "Nord Snow (Light)"),
        ("latte", "Catppuccin Latte (Light)"),
        ("paper", "Warm Paper (Light)")
    };

    // =========================================================================
    // Connectivity & Toast Banner
    // =========================================================================
    public bool ApiOnline { get; private set; }
    public string ApiStatusText { get; private set; } = "Checking...";
    public string? StatusBannerMessage { get; private set; }
    public string StatusBannerType { get; private set; } = "info"; // info, success, warning, error
    public DateTime? StatusBannerTime { get; private set; }

    public void ShowBanner(string message, string type = "info")
    {
        StatusBannerMessage = message;
        StatusBannerType = type;
        StatusBannerTime = DateTime.Now;
        NotifyStateChanged();
    }

    public void ClearBanner()
    {
        StatusBannerMessage = null;
        NotifyStateChanged();
    }

    // =========================================================================
    // Artifacts, Session Events & Active Pipeline Draft
    // =========================================================================
    public List<ArtifactRecord> Pipelines { get; private set; } = new();
    public List<ArtifactRecord> Configs { get; private set; } = new();
    public List<ArtifactRecord> OptionsList { get; private set; } = new();
    public List<SavedDatabaseProfile> SavedDbProfiles { get; private set; } = new();
    public List<SavedCryptoKeyProfile> SavedKeyProfiles { get; private set; } = new();
    public List<UiEventRecord> UiEvents { get; private set; } = new();

    public PipelineDraft CurrentDraft { get; private set; } = new();
    public string GeneratedXml { get; set; } = string.Empty;
    public bool XmlAutoSync { get; set; } = true;

    // =========================================================================
    // Session Crypto & PKI State
    // =========================================================================
    public FlowCryptoService Crypto => _crypto;
    public string SessionSecureKey { get; private set; } = string.Empty;
    public string ActiveSigningKeyPem { get; set; } = string.Empty;
    public string ActiveSigningCertPem { get; set; } = string.Empty;
    public string ActiveVerifyPublicKeyPem { get; set; } = string.Empty;
    public string ActiveVerifyCertPem { get; set; } = string.Empty;
    public string ActiveVerifyCaCertPem { get; set; } = string.Empty;
    public string ActiveVerifyKeyringAsc { get; set; } = string.Empty;

    public bool RequiresDecryptionUnlock { get; private set; }
    public string LockedCiphertextPayload { get; private set; } = string.Empty;
    public bool IsSignVerifyModalOpen { get; private set; }

    public void SetSessionSecureKey(string? secureKey)
    {
        SessionSecureKey = secureKey?.Trim() ?? string.Empty;
        _api.SetSecureKeyHeader(SessionSecureKey);
        NotifyStateChanged();
    }

    public void OpenSignVerifyModal()
    {
        IsSignVerifyModalOpen = true;
        ModalVersion++;
        NotifyStateChanged();
    }

    public void CloseSignVerifyModal()
    {
        IsSignVerifyModalOpen = false;
        NotifyStateChanged();
    }

    public void UpsertSavedKeyProfile(SavedCryptoKeyProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
            return;

        var existing = SavedKeyProfiles.FirstOrDefault(p =>
            string.Equals(p.Name, profile.Name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Algorithm = profile.Algorithm;
            existing.Format = profile.Format;
            existing.PrivateKeyPem = profile.PrivateKeyPem;
            existing.PublicKeyPem = profile.PublicKeyPem;
            existing.CertificatePem = profile.CertificatePem;
            existing.CaCertificatePem = profile.CaCertificatePem;
            existing.SecureKey = profile.SecureKey;
            existing.CreatedAt = DateTime.UtcNow;
        }
        else
        {
            SavedKeyProfiles.Add(profile);
        }

        if (!string.IsNullOrWhiteSpace(profile.PublicKeyPem) && string.IsNullOrWhiteSpace(ActiveVerifyPublicKeyPem))
            ActiveVerifyPublicKeyPem = profile.PublicKeyPem;
        if (!string.IsNullOrWhiteSpace(profile.CertificatePem) && string.IsNullOrWhiteSpace(ActiveVerifyCertPem))
            ActiveVerifyCertPem = profile.CertificatePem;

        NotifyStateChanged();
    }

    // =========================================================================
    // Modal State
    // =========================================================================
    public long ModalVersion { get; private set; }
    public bool IsNodeEditModalOpen { get; private set; }
    public bool IsEditingExistingNode { get; private set; }
    public PipelineNode? EditingNode { get; private set; }
    public ComponentMeta? EditingComponentMeta { get; private set; }
    public long? TargetParentNodeId { get; private set; }
    public string TargetSection { get; private set; } = "flow";

    public bool IsComponentPickerModalOpen { get; private set; }
    public long? PickerParentNodeId { get; private set; }
    public string PickerParentLabel { get; private set; } = string.Empty;

    public bool IsDocModalOpen { get; private set; }
    public string DocModalComponentType { get; private set; } = string.Empty;

    public bool IsEventDetailModalOpen { get; private set; }
    public PipelineEventRecord? SelectedPipelineEvent { get; private set; }
    public UiEventRecord? SelectedUiEvent { get; private set; }

    public bool IsUploadModalOpen { get; private set; }
    public string UploadModalDefaultType { get; private set; } = "pipeline";

    public void OpenUploadModal(string? defaultType = null)
    {
        UploadModalDefaultType = string.IsNullOrWhiteSpace(defaultType)
            ? (CurrentDraft.ArtifactType ?? "pipeline")
            : defaultType.Trim().ToLowerInvariant();
        IsUploadModalOpen = true;
        ModalVersion++;
        NotifyStateChanged();
    }

    public void CloseUploadModal()
    {
        IsUploadModalOpen = false;
        NotifyStateChanged();
    }

    // =========================================================================
    // Initialization & Switching
    // =========================================================================

    public long NextNodeId() => Interlocked.Increment(ref _nodeIdCounter);

    private void SeedDefaultDraft()
    {
        CurrentDraft = new PipelineDraft
        {
            ArtifactType = "pipeline",
            Name = "default_pipeline",
            Description = "Visual pipeline designed in Flow Builder"
        };

        CurrentDraft.VariableNodes.Add(new PipelineNode
        {
            Id = NextNodeId(),
            Section = "variables",
            NodeType = "variable",
            SequenceOrder = 1,
            Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = "env",
                ["value"] = "production"
            }
        });

        CurrentDraft.DatabaseNodes.Add(new PipelineNode
        {
            Id = NextNodeId(),
            Section = "databases",
            NodeType = "database",
            SequenceOrder = 1,
            Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = "default_db",
                ["driver"] = "sqlserver",
                ["connection_string"] = "Server=localhost;Database=DB;Trusted_Connection=True;TrustServerCertificate=True;",
                ["workload"] = "OLTP"
            }
        });

        CurrentDraft.PreflightNodes.Add(new PipelineNode
        {
            Id = NextNodeId(),
            Section = "preflight",
            NodeType = "assert",
            SequenceOrder = 1,
            Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = "check_db_db",
                ["db"] = "default_db",
                ["equals"] = "1"
            },
            ContentText = "SELECT 1;"
        });

        CurrentDraft.FlowNodes.Add(new PipelineNode
        {
            Id = NextNodeId(),
            Section = "flow",
            NodeType = "sql",
            SequenceOrder = 1,
            Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = "verify_connection",
                ["db"] = "default_db",
                ["output_var"] = "server_name"
            },
            ContentText = "SELECT @@SERVERNAME AS server_name;"
        });

        RegenerateXmlFromDraft();
    }

    public async Task CheckApiHealthOnlyAsync()
    {
        var (online, statusText) = await _api.CheckHealthAsync();
        ApiOnline = online;
        ApiStatusText = statusText;
        NotifyStateChanged();
    }

    public async Task InitializeStudioAsync()
    {
        await RefreshHealthAndArtifactsAsync();

        // Automatically load the first API pipeline on initial startup if still on the default unsaved draft
        if (CurrentDraft.ApiArtifactId is null && !CurrentDraft.IsDirty && Pipelines.Count > 0)
        {
            await LoadArtifactRecordAsync(Pipelines[0], "pipeline");
        }
    }

    public async Task RefreshHealthAndArtifactsAsync()
    {
        var (online, statusText) = await _api.CheckHealthAsync();
        ApiOnline = online;
        ApiStatusText = statusText;

        if (ApiOnline)
        {
            try
            {
                var pTask = _api.ListArtifactsAsync("pipeline", includeContent: true);
                var cTask = _api.ListArtifactsAsync("config", includeContent: true);
                var oTask = _api.ListArtifactsAsync("options", includeContent: true);
                await Task.WhenAll(pTask, cTask, oTask);

                Pipelines = NormalizeArtifactList(await pTask, "pipeline");
                Configs = NormalizeArtifactList(await cTask, "config");
                OptionsList = NormalizeArtifactList(await oTask, "options");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed fetching artifacts from go-flowAPI.");
            }
        }

        NotifyStateChanged();
    }

    private static List<ArtifactRecord> NormalizeArtifactList(List<ArtifactRecord> apiList, string artifactType)
    {
        var map = new Dictionary<string, ArtifactRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in apiList)
        {
            item.Type = artifactType;
            if (!string.IsNullOrWhiteSpace(item.Content))
            {
                var trimmed = item.Content.Trim();
                if (trimmed.StartsWith(FlowCryptoService.EncryptedPrefix, StringComparison.Ordinal))
                    item.Encrypted = true;
                if (trimmed.StartsWith(FlowCryptoService.SignedPrefix, StringComparison.Ordinal))
                {
                    item.Signed = true;
                    if (FlowCryptoService.TryUnwrapSignedPayload(trimmed, out var fmt, out _, out _))
                        item.SignatureFormat = fmt;
                }
            }
            if (!string.IsNullOrWhiteSpace(item.Name))
            {
                map[item.Name] = item;
            }
        }
        return map.Values.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void UpsertSavedDbProfile(SavedDatabaseProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
            return;

        var existing = SavedDbProfiles.FirstOrDefault(p =>
            string.Equals(p.Name, profile.Name.Trim(), StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            existing.Driver = profile.Driver;
            existing.ConnectionString = profile.ConnectionString;
            existing.Description = profile.Description;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            SavedDbProfiles.Add(new SavedDatabaseProfile
            {
                Id = SavedDbProfiles.Count + 1,
                Name = profile.Name.Trim(),
                Driver = profile.Driver,
                ConnectionString = profile.ConnectionString,
                Description = profile.Description,
                UpdatedAt = DateTime.UtcNow
            });
        }
        NotifyStateChanged();
    }

    private PipelineDraft? _savedPipelineDraft;
    private string? _savedPipelineXml;
    private PipelineDraft? _savedConfigDraft;
    private string? _savedConfigXml;

    public async Task SetActiveTabAsync(string tab)
    {
        if (string.Equals(ActiveTab, tab, StringComparison.OrdinalIgnoreCase))
            return;

        // Stash active Pipeline or Config draft so switching tabs preserves unsaved work
        if (string.Equals(CurrentDraft.ArtifactType, "pipeline", StringComparison.OrdinalIgnoreCase))
        {
            _savedPipelineDraft = CurrentDraft;
            _savedPipelineXml = GeneratedXml;
        }
        else if (string.Equals(CurrentDraft.ArtifactType, "config", StringComparison.OrdinalIgnoreCase))
        {
            _savedConfigDraft = CurrentDraft;
            _savedConfigXml = GeneratedXml;
        }

        ActiveTab = tab;

        if (string.Equals(tab, "config", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(CurrentDraft.ArtifactType, "config", StringComparison.OrdinalIgnoreCase))
        {
            if (_savedConfigDraft is not null)
            {
                CurrentDraft = _savedConfigDraft;
                GeneratedXml = !string.IsNullOrWhiteSpace(_savedConfigXml)
                    ? _savedConfigXml
                    : _xmlGen.GeneratePipelineXml(CurrentDraft);
            }
            else if (Configs.Count > 0)
            {
                await LoadArtifactRecordAsync(Configs[0], "config");
            }
            else
            {
                SeedDefaultConfigDraft();
            }
        }
        else if (string.Equals(tab, "pipeline", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(CurrentDraft.ArtifactType, "pipeline", StringComparison.OrdinalIgnoreCase))
        {
            if (_savedPipelineDraft is not null)
            {
                CurrentDraft = _savedPipelineDraft;
                GeneratedXml = !string.IsNullOrWhiteSpace(_savedPipelineXml)
                    ? _savedPipelineXml
                    : _xmlGen.GeneratePipelineXml(CurrentDraft);
            }
            else if (Pipelines.Count > 0)
            {
                await LoadArtifactRecordAsync(Pipelines[0], "pipeline");
            }
            else
            {
                SeedDefaultDraft();
            }
        }

        NotifyStateChanged();

        await LogEventAsync("NAVIGATION", "SWITCH_TAB", "tab", tab, "SUCCESS", 0, null);
    }

    public void SeedDefaultConfigDraft(string? name = null, string? description = null)
    {
        CurrentDraft = new PipelineDraft
        {
            ApiArtifactId = null,
            ArtifactType = "config",
            Name = string.IsNullOrWhiteSpace(name) ? "CONFIG_DEV_DEFAULT" : name.Trim(),
            Description = string.IsNullOrWhiteSpace(description)
                ? "Environment variables, database connections & preflight configuration"
                : description.Trim()
        };

        CurrentDraft.VariableNodes.Add(new PipelineNode
        {
            Id = NextNodeId(),
            Section = "variables",
            NodeType = "variable",
            SequenceOrder = 1,
            Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = "ENV",
                ["value"] = "DEV"
            }
        });

        CurrentDraft.DatabaseNodes.Add(new PipelineNode
        {
            Id = NextNodeId(),
            Section = "databases",
            NodeType = "database",
            SequenceOrder = 1,
            Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = "default_db",
                ["driver"] = "sqlserver",
                ["connection_string"] = "Server=localhost;Database=DB;Trusted_Connection=True;TrustServerCertificate=True;",
                ["workload"] = "oltp"
            }
        });

        CurrentDraft.PreflightNodes.Add(new PipelineNode
        {
            Id = NextNodeId(),
            Section = "preflight",
            NodeType = "assert",
            SequenceOrder = 1,
            Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = "check_db_ready",
                ["db"] = "default_db",
                ["equals"] = "1"
            },
            ContentText = "SELECT 1;"
        });

        RegenerateXmlFromDraft();
    }

    public async Task SetThemeAsync(string themeId, bool logEvent = true)
    {
        if (string.IsNullOrWhiteSpace(themeId))
            return;

        if (!Themes.Any(t => string.Equals(t.Id, themeId, StringComparison.OrdinalIgnoreCase)))
            return;

        ActiveTheme = themeId;
        _lastSavedTheme = themeId;
        NotifyStateChanged();
        if (logEvent)
        {
            await LogEventAsync("UI", "CHANGE_THEME", "theme", themeId, "SUCCESS", 0, null);
        }
    }

    // =========================================================================
    // XML Sync & Unified Artifact Operations (Pipeline, Config, Options)
    // =========================================================================

    public void RegenerateXmlFromDraft()
    {
        GeneratedXml = _xmlGen.GeneratePipelineXml(CurrentDraft);
    }

    /// <summary>
    /// Inspects and unwraps FLOWENC:v1: / FLOWSIGNED:v1: envelopes from raw content,
    /// returning the inner XML payload along with encryption and signature metadata.
    /// </summary>
    public (string UnwrappedXml, bool IsEncrypted, bool IsLockedCiphertext, bool IsSigned, string SignatureFormat, string SignedEnvelope)
        UnwrapEnvelopeForDesigner(string rawContent, string? overridePassphrase = null)
    {
        if (string.IsNullOrWhiteSpace(rawContent))
            return (string.Empty, false, false, false, string.Empty, string.Empty);

        var current = rawContent.Trim();
        var key = !string.IsNullOrWhiteSpace(overridePassphrase) ? overridePassphrase.Trim() : SessionSecureKey;
        bool isEncrypted = false;
        bool isLocked = false;
        bool isSigned = false;
        string sigFormat = string.Empty;
        string signedEnv = string.Empty;

        for (int depth = 0; depth < 3; depth++)
        {
            if (current.StartsWith(FlowCryptoService.EncryptedPrefix, StringComparison.Ordinal))
            {
                isEncrypted = true;
                if (!string.IsNullOrWhiteSpace(key))
                {
                    try
                    {
                        current = _crypto.DecryptPayload(current, key).Trim();
                        continue;
                    }
                    catch
                    {
                        isLocked = true;
                        break;
                    }
                }
                else
                {
                    isLocked = true;
                    break;
                }
            }

            if (current.StartsWith(FlowCryptoService.SignedPrefix, StringComparison.Ordinal) &&
                FlowCryptoService.TryUnwrapSignedPayload(current, out var fmt, out var innerText, out _))
            {
                isSigned = true;
                sigFormat = fmt;
                signedEnv = current;
                current = innerText.Trim();
                continue;
            }

            break;
        }

        return (current, isEncrypted, isLocked, isSigned, sigFormat, signedEnv);
    }

    public async Task<bool> TryUnlockCurrentArtifactAsync(string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase))
        {
            ShowBanner("Please enter a valid decryption key / passphrase.", "warning");
            return false;
        }

        var sourcePayload = !string.IsNullOrWhiteSpace(LockedCiphertextPayload)
            ? LockedCiphertextPayload
            : GeneratedXml;

        var (unwrapped, isEnc, isLocked, isSigned, sigFmt, signedEnv) =
            UnwrapEnvelopeForDesigner(sourcePayload, passphrase);

        if (isLocked)
        {
            ShowBanner("Decryption failed: Invalid Secure Key or corrupted FLOWENC:v1: payload.", "error");
            return false;
        }

        SetSessionSecureKey(passphrase);
        RequiresDecryptionUnlock = false;
        LockedCiphertextPayload = string.Empty;
        CurrentDraft.Encrypted = true;
        CurrentDraft.Signed = isSigned;
        CurrentDraft.SignatureFormat = sigFmt;
        CurrentDraft.SignedEnvelopeContent = signedEnv;

        await ApplyXmlToCanvasAsync(unwrapped, CurrentDraft.Name);
        CurrentDraft.IsDirty = false;
        ShowBanner($"Unlocked and decrypted '{CurrentDraft.Name}' into visual designer.", "success");
        return true;
    }

    public async Task SignCurrentDraftAsync(
        string format,
        string privateKeyPem,
        string? certPem = null,
        string? passphrase = null,
        bool attachUnifiedEnvelope = true)
    {
        if (XmlAutoSync && (CurrentDraft.IsDirty || string.IsNullOrWhiteSpace(GeneratedXml)))
        {
            RegenerateXmlFromDraft();
        }

        var xmlPayload = GeneratedXml;
        var sw = Stopwatch.StartNew();
        try
        {
            CryptoSignResponse? resp = null;
            if (ApiOnline)
            {
                try
                {
                    resp = await _api.CryptoSignAsync(new CryptoSignRequest
                    {
                        Format = format,
                        Payload = xmlPayload,
                        PrivateKeyPem = privateKeyPem,
                        CertPem = certPem ?? string.Empty,
                        Passphrase = passphrase ?? string.Empty
                    });
                }
                catch (Exception apiEx)
                {
                    _logger.LogWarning(apiEx, "API signing failed, attempting local fallback for OpenSSL.");
                }
            }

            if (resp is null)
            {
                if (!string.Equals(format, "openssl", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Format '{format}' requires go-flowAPI online connectivity.");
                }
                var sigBytes = _crypto.SignOpenSslLocal(Encoding.UTF8.GetBytes(xmlPayload), privateKeyPem, passphrase);
                resp = new CryptoSignResponse
                {
                    Format = "openssl",
                    RawSignatureBase64 = Convert.ToBase64String(sigBytes),
                    DetachedSignature = FlowCryptoService.WrapSignatureOnly("openssl", sigBytes),
                    SignedEnvelope = FlowCryptoService.WrapSignedPayload("openssl", sigBytes, Encoding.UTF8.GetBytes(xmlPayload))
                };
            }

            CurrentDraft.Signed = true;
            CurrentDraft.SignatureFormat = resp.Format;
            CurrentDraft.DetachedSignatureContent = resp.DetachedSignature;
            CurrentDraft.SignedEnvelopeContent = resp.SignedEnvelope;
            CurrentDraft.IsDirty = true;

            sw.Stop();
            ShowBanner($"Digitally signed '{CurrentDraft.Name}' using {resp.Format.ToUpperInvariant()} ({ (attachUnifiedEnvelope ? "FLOWSIGNED:v1: envelope attached" : "FLOWSIG:v1: detached signature ready") }).", "success");
            NotifyStateChanged();
            await LogEventAsync("CRYPTO", "SIGN_ARTIFACT", CurrentDraft.ArtifactType, CurrentDraft.Name, "SUCCESS", sw.ElapsedMilliseconds, null,
                new { format = resp.Format, attachUnifiedEnvelope });
        }
        catch (Exception ex)
        {
            sw.Stop();
            ShowBanner($"Signing failed: {ex.Message}", "error");
            await LogEventAsync("CRYPTO", "SIGN_ARTIFACT", CurrentDraft.ArtifactType, CurrentDraft.Name, "ERROR", sw.ElapsedMilliseconds, ex.Message);
        }
    }

    public async Task<CryptoVerifyResponse> VerifyCurrentDraftSignatureAsync(
        string? signatureOverride = null,
        string? publicKeyPem = null,
        string? certPem = null,
        string? caCertPem = null,
        string? keyringAsc = null)
    {
        var payloadToVerify = !string.IsNullOrWhiteSpace(CurrentDraft.SignedEnvelopeContent) && string.IsNullOrWhiteSpace(signatureOverride)
            ? CurrentDraft.SignedEnvelopeContent
            : GeneratedXml;
        var sigToUse = !string.IsNullOrWhiteSpace(signatureOverride)
            ? signatureOverride
            : CurrentDraft.DetachedSignatureContent;

        var pub = !string.IsNullOrWhiteSpace(publicKeyPem) ? publicKeyPem : ActiveVerifyPublicKeyPem;
        var cert = !string.IsNullOrWhiteSpace(certPem) ? certPem : ActiveVerifyCertPem;
        var ca = !string.IsNullOrWhiteSpace(caCertPem) ? caCertPem : ActiveVerifyCaCertPem;
        var kr = !string.IsNullOrWhiteSpace(keyringAsc) ? keyringAsc : ActiveVerifyKeyringAsc;

        try
        {
            if (ApiOnline)
            {
                var apiRes = await _api.CryptoVerifyAsync(new CryptoVerifyRequest
                {
                    Payload = payloadToVerify,
                    Signature = sigToUse ?? string.Empty,
                    PublicKeyPem = pub,
                    CertPem = cert,
                    CaCertPem = ca,
                    KeyringAsc = kr
                });
                if (apiRes is not null)
                {
                    CurrentDraft.LastVerification = apiRes;
                    NotifyStateChanged();
                    return apiRes;
                }
            }

            // Local fallback for OpenSSL verification
            byte[] dataBytes;
            byte[] sigBytes;
            string fmt = "openssl";
            if (FlowCryptoService.TryUnwrapSignedPayload(payloadToVerify, out var envFmt, out var innerText, out var envSig))
            {
                fmt = envFmt;
                dataBytes = Encoding.UTF8.GetBytes(innerText);
                sigBytes = envSig;
            }
            else if (FlowCryptoService.TryUnwrapSignatureOnly(sigToUse, out var detFmt, out var detSig))
            {
                fmt = detFmt;
                dataBytes = Encoding.UTF8.GetBytes(payloadToVerify);
                sigBytes = detSig;
            }
            else
            {
                dataBytes = Encoding.UTF8.GetBytes(payloadToVerify);
                sigBytes = Convert.FromBase64String((sigToUse ?? string.Empty).Trim());
            }

            var keyMat = !string.IsNullOrWhiteSpace(pub) ? pub : cert;
            var ok = _crypto.VerifyOpenSslLocal(dataBytes, sigBytes, keyMat);
            var localRes = new CryptoVerifyResponse
            {
                Verified = ok,
                Format = fmt,
                UnwrappedPayload = Encoding.UTF8.GetString(dataBytes),
                Error = ok ? null : "Cryptographic signature verification failed."
            };
            CurrentDraft.LastVerification = localRes;
            NotifyStateChanged();
            return localRes;
        }
        catch (Exception ex)
        {
            var errRes = new CryptoVerifyResponse
            {
                Verified = false,
                Error = ex.Message
            };
            CurrentDraft.LastVerification = errRes;
            NotifyStateChanged();
            return errRes;
        }
    }

    public async Task ApplyXmlToCanvasAsync(string xmlText, string? overrideName = null)
    {
        if (!_auth.CurrentPermissions.CanUpdate && !_auth.CurrentPermissions.CanCreate)
        {
            ShowBanner("Permission denied: Update or Create permission required to import XML.", "error");
            await LogEventAsync("DESIGNER", "IMPORT_XML", CurrentDraft.ArtifactType, CurrentDraft.Name, "DENIED", 0, "Insufficient permissions");
            return;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var preserveName = CurrentDraft.ApiArtifactId.HasValue && string.IsNullOrWhiteSpace(overrideName)
                ? CurrentDraft.Name
                : null;
            var preserveDesc = CurrentDraft.ApiArtifactId.HasValue
                ? CurrentDraft.Description
                : null;

            var (unwrappedXml, isEnc, isLocked, isSigned, sigFmt, signedEnv) = UnwrapEnvelopeForDesigner(xmlText);
            if (isLocked)
            {
                RequiresDecryptionUnlock = true;
                LockedCiphertextPayload = xmlText;
                CurrentDraft.Encrypted = true;
                GeneratedXml = xmlText;
                ShowBanner("Encrypted FLOWENC:v1: payload detected. Enter your Secure Key above to unlock and decrypt into the visual designer.", "warning");
                NotifyStateChanged();
                return;
            }

            var imported = _xmlImporter.ImportFromXml(
                unwrappedXml,
                NextNodeId,
                overrideName ?? CurrentDraft.Name,
                CurrentDraft.Description,
                CurrentDraft.ArtifactType);

            imported.ApiArtifactId = CurrentDraft.ApiArtifactId;
            imported.ArtifactType = CurrentDraft.ArtifactType;
            imported.Encrypted = isEnc || CurrentDraft.Encrypted;
            imported.Signed = isSigned || CurrentDraft.Signed;
            imported.SignatureFormat = !string.IsNullOrWhiteSpace(sigFmt) ? sigFmt : CurrentDraft.SignatureFormat;
            imported.SignedEnvelopeContent = !string.IsNullOrWhiteSpace(signedEnv) ? signedEnv : CurrentDraft.SignedEnvelopeContent;
            if (!string.IsNullOrWhiteSpace(preserveName))
                imported.Name = preserveName;
            if (!string.IsNullOrWhiteSpace(preserveDesc))
                imported.Description = preserveDesc;

            if (string.Equals(imported.ArtifactType, "config", StringComparison.OrdinalIgnoreCase))
            {
                imported.FlowNodes.Clear();
            }

            RequiresDecryptionUnlock = false;
            LockedCiphertextPayload = string.Empty;
            imported.IsDirty = true;
            CurrentDraft = imported;
            GeneratedXml = _xmlGen.GeneratePipelineXml(CurrentDraft);
            sw.Stop();

            var summary = string.Equals(CurrentDraft.ArtifactType, "options", StringComparison.OrdinalIgnoreCase)
                ? $"{CurrentDraft.OptionEntries.Count} CLI options"
                : $"{CountAllNodes()} nodes";

            ShowBanner($"Parsed XML into visual designer ({summary}). Click Save to persist changes via API.", "success");
            NotifyStateChanged();
            await LogEventAsync("DESIGNER", "IMPORT_XML", CurrentDraft.ArtifactType, CurrentDraft.Name, "SUCCESS", sw.ElapsedMilliseconds, null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            ShowBanner($"XML Import Error: {ex.Message}", "error");
            await LogEventAsync("DESIGNER", "IMPORT_XML", CurrentDraft.ArtifactType, CurrentDraft.Name, "ERROR", sw.ElapsedMilliseconds, ex.Message);
        }
    }

    public Task CreateNewPipelineDraftAsync(string name, string description) =>
        CreateNewArtifactDraftAsync("pipeline", name, description);

    public async Task CreateNewArtifactDraftAsync(string artifactType, string? name = null, string? description = null)
    {
        if (!_auth.CurrentPermissions.CanCreate)
        {
            ShowBanner("Permission denied: You do not have Create permissions.", "error");
            await LogEventAsync("DESIGNER", "NEW_DRAFT", artifactType, name ?? "new", "DENIED", 0, "Missing CanCreate");
            return;
        }

        var normType = (artifactType ?? "pipeline").Trim().ToLowerInvariant();
        var defaultName = normType switch
        {
            "config" => $"CONFIG_{DateTime.Now:HHmmss}",
            "options" => $"OPTIONS_{DateTime.Now:HHmmss}",
            _ => $"pipeline_{DateTime.Now:HHmmss}"
        };
        var defaultDesc = normType switch
        {
            "config" => "Environment & Preflight Config designed in Flow Builder",
            "options" => "Flow CLI Options designed in Flow Builder",
            _ => "Visual pipeline designed in Flow Builder"
        };

        CurrentDraft = new PipelineDraft
        {
            ApiArtifactId = null,
            ArtifactType = normType,
            Name = string.IsNullOrWhiteSpace(name) ? defaultName : name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? defaultDesc : description.Trim()
        };

        if (normType == "config")
        {
            SeedDefaultConfigDraft(
                string.IsNullOrWhiteSpace(name) ? defaultName : name.Trim(),
                string.IsNullOrWhiteSpace(description) ? defaultDesc : description.Trim());
        }
        else if (normType == "options")
        {
            CurrentDraft.OptionEntries.Add(new FlowOptionEntry
            {
                Id = NextNodeId(),
                Flag = "vars",
                Value = "ENV=DEV"
            });
            CurrentDraft.OptionEntries.Add(new FlowOptionEntry
            {
                Id = NextNodeId(),
                Flag = "node-output",
                Value = "true"
            });
            CurrentDraft.OptionEntries.Add(new FlowOptionEntry
            {
                Id = NextNodeId(),
                Flag = "debug",
                Value = "true"
            });
        }

        RegenerateXmlFromDraft();
        ShowBanner($"Created new {normType.ToUpperInvariant()} draft '{CurrentDraft.Name}'.", "success");
        NotifyStateChanged();

        await LogEventAsync("DESIGNER", "NEW_DRAFT", normType, CurrentDraft.Name, "SUCCESS", 0, null);
    }

    public Task LoadPipelineArtifactAsync(ArtifactRecord artifact) =>
        LoadArtifactRecordAsync(artifact, "pipeline");

    public async Task LoadDatabaseArtifactByIdAsync(string artifactType, int id)
    {
        var normType = (artifactType ?? "pipeline").Trim().ToLowerInvariant();
        var list = normType switch
        {
            "config" => Configs,
            "options" => OptionsList,
            _ => Pipelines
        };

        var found = list.FirstOrDefault(a => a.Id == id)
                 ?? await _api.GetArtifactByIdAsync(normType, id);

        if (found is not null)
        {
            await LoadArtifactRecordAsync(found, normType);
        }
        else
        {
            ShowBanner($"Could not find {normType} record #{id} via go-flowAPI.", "warning");
        }
    }

    public async Task LoadArtifactRecordAsync(ArtifactRecord artifact, string? overrideType = null)
    {
        if (!_auth.CurrentPermissions.CanRead)
        {
            ShowBanner("Permission denied: Read permission required.", "error");
            return;
        }

        var normType = (overrideType ?? artifact.Type ?? "pipeline").Trim().ToLowerInvariant();
        var sw = Stopwatch.StartNew();
        try
        {
            var cachedList = normType switch
            {
                "config" => Configs,
                "options" => OptionsList,
                _ => Pipelines
            };
            var cachedMatch = cachedList.FirstOrDefault(a =>
                (artifact.Id > 0 && a.Id == artifact.Id) ||
                (!string.IsNullOrWhiteSpace(artifact.Name) && string.Equals(a.Name, artifact.Name, StringComparison.OrdinalIgnoreCase)));

            bool wasOriginallyEncrypted = artifact.Encrypted ||
                (cachedMatch?.Encrypted == true) ||
                (!string.IsNullOrWhiteSpace(artifact.Content) &&
                 artifact.Content.TrimStart().StartsWith(FlowCryptoService.EncryptedPrefix, StringComparison.Ordinal)) ||
                (!string.IsNullOrWhiteSpace(cachedMatch?.Content) &&
                 cachedMatch.Content.TrimStart().StartsWith(FlowCryptoService.EncryptedPrefix, StringComparison.Ordinal));

            bool wasOriginallySigned = artifact.Signed ||
                (cachedMatch?.Signed == true) ||
                (!string.IsNullOrWhiteSpace(artifact.Content) &&
                 artifact.Content.TrimStart().StartsWith(FlowCryptoService.SignedPrefix, StringComparison.Ordinal));

            var full = artifact;
            if (string.IsNullOrWhiteSpace(full.Content) ||
                full.Content.TrimStart().StartsWith(FlowCryptoService.EncryptedPrefix, StringComparison.Ordinal))
            {
                if (full.Id > 0)
                {
                    full = await _api.GetArtifactByIdAsync(normType, full.Id, SessionSecureKey) ?? artifact;
                }
                else if (!string.IsNullOrWhiteSpace(full.Name))
                {
                    full = await _api.GetArtifactByNameAsync(normType, full.Name, SessionSecureKey) ?? artifact;
                }
            }
            full.Encrypted |= wasOriginallyEncrypted;
            full.Signed |= wasOriginallySigned;

            RequiresDecryptionUnlock = false;
            LockedCiphertextPayload = string.Empty;

            if (!string.IsNullOrWhiteSpace(full.Content))
            {
                var (unwrappedXml, isEnc, isLocked, isSigned, sigFmt, signedEnv) = UnwrapEnvelopeForDesigner(full.Content);
                var effectiveEncrypted = wasOriginallyEncrypted || full.Encrypted || isEnc;
                var effectiveSigned = wasOriginallySigned || full.Signed || isSigned;

                if (isLocked)
                {
                    RequiresDecryptionUnlock = true;
                    LockedCiphertextPayload = full.Content;
                    CurrentDraft = new PipelineDraft
                    {
                        ApiArtifactId = full.Id,
                        ArtifactType = normType,
                        Encrypted = true,
                        Signed = effectiveSigned,
                        SignatureFormat = full.SignatureFormat,
                        Name = full.Name,
                        Description = full.Description
                    };
                    GeneratedXml = full.Content;
                    sw.Stop();
                    ShowBanner($"Loaded encrypted {normType.ToUpperInvariant()} '{full.Name}' (#{full.Id}). Enter your Secure Key to unlock and decrypt.", "warning");
                    NotifyStateChanged();
                    return;
                }

                try
                {
                    var imported = _xmlImporter.ImportFromXml(unwrappedXml, NextNodeId, full.Name, full.Description, normType);
                    imported.ApiArtifactId = full.Id;
                    imported.ArtifactType = normType;
                    imported.Encrypted = effectiveEncrypted;
                    imported.Signed = effectiveSigned;
                    imported.SignatureFormat = !string.IsNullOrWhiteSpace(sigFmt) ? sigFmt : full.SignatureFormat;
                    imported.SignedEnvelopeContent = signedEnv;
                    if (!string.IsNullOrWhiteSpace(full.Name))
                        imported.Name = full.Name;
                    if (!string.IsNullOrWhiteSpace(full.Description))
                        imported.Description = full.Description;
                    if (string.Equals(normType, "config", StringComparison.OrdinalIgnoreCase))
                        imported.FlowNodes.Clear();

                    CurrentDraft = imported;
                    GeneratedXml = string.Equals(normType, "options", StringComparison.OrdinalIgnoreCase) &&
                                   unwrappedXml.Contains("<option ", StringComparison.OrdinalIgnoreCase)
                        ? _xmlGen.GeneratePipelineXml(imported)
                        : unwrappedXml;
                }
                catch
                {
                    CurrentDraft = new PipelineDraft
                    {
                        ApiArtifactId = full.Id,
                        ArtifactType = normType,
                        Encrypted = effectiveEncrypted,
                        Signed = effectiveSigned,
                        SignatureFormat = !string.IsNullOrWhiteSpace(sigFmt) ? sigFmt : full.SignatureFormat,
                        SignedEnvelopeContent = signedEnv,
                        Name = full.Name,
                        Description = full.Description
                    };
                    GeneratedXml = unwrappedXml;
                }
            }
            else
            {
                CurrentDraft = new PipelineDraft
                {
                    ApiArtifactId = full.Id,
                    ArtifactType = normType,
                    Encrypted = wasOriginallyEncrypted || full.Encrypted,
                    Signed = wasOriginallySigned || full.Signed,
                    SignatureFormat = full.SignatureFormat,
                    Name = full.Name,
                    Description = full.Description
                };
                RegenerateXmlFromDraft();
            }

            sw.Stop();
            ShowBanner($"Loaded {normType.ToUpperInvariant()} '{full.Name}' (#{full.Id}) from go-flowAPI for editing.", "info");
            NotifyStateChanged();

            await LogEventAsync("DESIGNER", "LOAD_ARTIFACT", normType, full.Name, "SUCCESS", sw.ElapsedMilliseconds, null,
                new { id = full.Id, artifactType = normType });
        }
        catch (Exception ex)
        {
            sw.Stop();
            ShowBanner($"Failed to load {normType} '{artifact.Name}': {ex.Message}", "error");
            await LogEventAsync("DESIGNER", "LOAD_ARTIFACT", normType, artifact.Name, "ERROR", sw.ElapsedMilliseconds, ex.Message);
        }
    }

    public Task SaveCurrentPipelineToApiAsync() => SaveCurrentArtifactAsync();

    public async Task SaveCurrentArtifactAsync(string? overrideSecureKey = null)
    {
        var normType = (CurrentDraft.ArtifactType ?? "pipeline").Trim().ToLowerInvariant();
        if (!_auth.CurrentPermissions.CanCreate && !_auth.CurrentPermissions.CanUpdate)
        {
            ShowBanner($"Permission denied: Create or Update permission required to save {normType}.", "error");
            await LogEventAsync("DESIGNER", "SAVE_ARTIFACT", normType, CurrentDraft.Name, "DENIED", 0, "Missing CanCreate/CanUpdate");
            return;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            if (XmlAutoSync && (CurrentDraft.IsDirty || string.IsNullOrWhiteSpace(GeneratedXml)))
            {
                RegenerateXmlFromDraft();
            }

            var keyToUse = !string.IsNullOrWhiteSpace(overrideSecureKey) ? overrideSecureKey.Trim() : SessionSecureKey;
            if (!string.IsNullOrWhiteSpace(overrideSecureKey))
            {
                SetSessionSecureKey(keyToUse);
            }

            var contentToSave = CurrentDraft.Signed && !string.IsNullOrWhiteSpace(CurrentDraft.SignedEnvelopeContent)
                ? CurrentDraft.SignedEnvelopeContent
                : GeneratedXml;

            // If Encrypted is requested and we have a session secure key, we can either let go-flowAPI encrypt it
            // with secure_key or pass secure_key in UpsertArtifactAsync.
            var saved = await _api.UpsertArtifactAsync(
                normType,
                CurrentDraft.Name,
                CurrentDraft.Description,
                contentToSave,
                encrypted: CurrentDraft.Encrypted,
                secureKey: keyToUse);

            CurrentDraft.ApiArtifactId = saved.Id > 0 ? saved.Id : CurrentDraft.ApiArtifactId;
            CurrentDraft.IsDirty = false;
            CurrentDraft.UpdatedAt = DateTime.UtcNow;

            await RefreshHealthAndArtifactsAsync();

            var list = normType switch
            {
                "config" => Configs,
                "options" => OptionsList,
                _ => Pipelines
            };
            var refreshed = list.FirstOrDefault(a =>
                string.Equals(a.Name, CurrentDraft.Name, StringComparison.OrdinalIgnoreCase));
            if (refreshed is not null && refreshed.Id > 0)
            {
                CurrentDraft.ApiArtifactId = refreshed.Id;
            }

            sw.Stop();

            ShowBanner($"Saved {normType.ToUpperInvariant()} '{CurrentDraft.Name}' (ID #{CurrentDraft.ApiArtifactId ?? saved.Id}) via go-flowAPI.", "success");
            NotifyStateChanged();

            await LogEventAsync("DESIGNER", "SAVE_ARTIFACT", normType, CurrentDraft.Name, "SUCCESS", sw.ElapsedMilliseconds, null,
                new { artifactId = CurrentDraft.ApiArtifactId ?? saved.Id, artifactType = normType, encrypted = CurrentDraft.Encrypted, signed = CurrentDraft.Signed });
        }
        catch (Exception ex)
        {
            sw.Stop();
            ShowBanner($"Failed to save {normType} via API: {ex.Message}", "error");
            await LogEventAsync("DESIGNER", "SAVE_ARTIFACT", normType, CurrentDraft.Name, "ERROR", sw.ElapsedMilliseconds, ex.Message);
        }
    }

    public async Task<ArtifactRecord?> UploadLocalArtifactToDbAsync(
        string artifactType,
        string name,
        string description,
        string xmlContent,
        bool encrypted = false,
        bool loadIntoDesigner = true,
        string? secureKey = null)
    {
        var normType = (artifactType ?? "pipeline").Trim().ToLowerInvariant();
        if (!_auth.CurrentPermissions.CanCreate && !_auth.CurrentPermissions.CanUpdate)
        {
            ShowBanner($"Permission denied: Create or Update permission required to upload {normType}.", "error");
            await LogEventAsync("DESIGNER", "UPLOAD_ARTIFACT", normType, name, "DENIED", 0, "Missing CanCreate/CanUpdate");
            return null;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            ShowBanner("Artifact name is required when uploading.", "error");
            return null;
        }

        if (string.IsNullOrWhiteSpace(xmlContent))
        {
            ShowBanner("XML content is empty.", "error");
            return null;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var keyToUse = !string.IsNullOrWhiteSpace(secureKey) ? secureKey.Trim() : SessionSecureKey;
            if (!string.IsNullOrWhiteSpace(secureKey))
                SetSessionSecureKey(keyToUse);

            var saved = await _api.UpsertArtifactAsync(
                normType,
                name.Trim(),
                description?.Trim() ?? string.Empty,
                xmlContent,
                encrypted: encrypted,
                secureKey: keyToUse);

            await RefreshHealthAndArtifactsAsync();

            if (loadIntoDesigner)
            {
                await LoadArtifactRecordAsync(saved, normType);
            }

            sw.Stop();
            ShowBanner($"Uploaded local {normType.ToUpperInvariant()} '{saved.Name}' (ID #{saved.Id}) via go-flowAPI.", "success");
            NotifyStateChanged();

            await LogEventAsync("DESIGNER", "UPLOAD_ARTIFACT", normType, saved.Name, "SUCCESS", sw.ElapsedMilliseconds, null,
                new { artifactId = saved.Id, artifactType = normType });

            return saved;
        }
        catch (Exception ex)
        {
            sw.Stop();
            ShowBanner($"Failed to upload {normType} '{name}' via API: {ex.Message}", "error");
            await LogEventAsync("DESIGNER", "UPLOAD_ARTIFACT", normType, name, "ERROR", sw.ElapsedMilliseconds, ex.Message);
            return null;
        }
    }

    public async Task LoadLocalArtifactAsDraftAsync(
        string artifactType,
        string name,
        string description,
        string xmlContent)
    {
        var normType = (artifactType ?? "pipeline").Trim().ToLowerInvariant();
        if (!_auth.CurrentPermissions.CanCreate && !_auth.CurrentPermissions.CanUpdate)
        {
            ShowBanner("Permission denied: Create or Update permission required to import local XML.", "error");
            return;
        }

        var (unwrappedXml, isEnc, isLocked, isSigned, sigFmt, signedEnv) = UnwrapEnvelopeForDesigner(xmlContent);
        if (isLocked)
        {
            RequiresDecryptionUnlock = true;
            LockedCiphertextPayload = xmlContent;
            CurrentDraft = new PipelineDraft
            {
                ApiArtifactId = null,
                ArtifactType = normType,
                Encrypted = true,
                Name = name.Trim(),
                Description = description?.Trim() ?? string.Empty,
                IsDirty = true
            };
            GeneratedXml = xmlContent;
            ShowBanner($"Loaded encrypted local {normType.ToUpperInvariant()} '{CurrentDraft.Name}'. Enter Secure Key to unlock.", "warning");
            NotifyStateChanged();
            return;
        }

        try
        {
            var imported = _xmlImporter.ImportFromXml(
                unwrappedXml,
                NextNodeId,
                name.Trim(),
                description?.Trim() ?? string.Empty,
                normType);

            imported.ApiArtifactId = null;
            imported.ArtifactType = normType;
            imported.Encrypted = isEnc;
            imported.Signed = isSigned;
            imported.SignatureFormat = sigFmt;
            imported.SignedEnvelopeContent = signedEnv;
            if (!string.IsNullOrWhiteSpace(name))
                imported.Name = name.Trim();
            if (!string.IsNullOrWhiteSpace(description))
                imported.Description = description.Trim();
            imported.IsDirty = true;

            RequiresDecryptionUnlock = false;
            LockedCiphertextPayload = string.Empty;
            CurrentDraft = imported;
            GeneratedXml = unwrappedXml;

            ShowBanner($"Loaded local {normType.ToUpperInvariant()} '{CurrentDraft.Name}' into designer draft. Click Save to persist via API.", "info");
            NotifyStateChanged();
            await LogEventAsync("DESIGNER", "IMPORT_LOCAL_FILE", normType, CurrentDraft.Name, "SUCCESS", 0, null);
        }
        catch
        {
            CurrentDraft = new PipelineDraft
            {
                ApiArtifactId = null,
                ArtifactType = normType,
                Encrypted = isEnc,
                Signed = isSigned,
                SignatureFormat = sigFmt,
                SignedEnvelopeContent = signedEnv,
                Name = name.Trim(),
                Description = description?.Trim() ?? string.Empty,
                IsDirty = true
            };
            GeneratedXml = unwrappedXml;
            ShowBanner($"Loaded raw XML for '{CurrentDraft.Name}' into editor.", "info");
            NotifyStateChanged();
        }
    }

    public Task DeletePipelineArtifactAsync(ArtifactRecord artifact) =>
        DeleteArtifactAsync("pipeline", artifact);

    public async Task DeleteArtifactAsync(string artifactType, ArtifactRecord artifact)
    {
        if (!_auth.CurrentPermissions.CanDelete)
        {
            ShowBanner("Permission denied: Delete permission required.", "error");
            await LogEventAsync("DESIGNER", "DELETE_ARTIFACT", artifactType, artifact.Name, "DENIED", 0, "Missing CanDelete");
            return;
        }

        var normType = (artifactType ?? "pipeline").Trim().ToLowerInvariant();
        var sw = Stopwatch.StartNew();
        try
        {
            bool deleted = false;
            if (artifact.Id > 0)
            {
                deleted = await _api.DeleteArtifactByIdAsync(normType, artifact.Id);
            }
            else if (!string.IsNullOrWhiteSpace(artifact.Name))
            {
                deleted = await _api.DeleteArtifactByNameAsync(normType, artifact.Name);
            }

            sw.Stop();
            if (deleted)
            {
                if (CurrentDraft.ApiArtifactId == artifact.Id &&
                    string.Equals(CurrentDraft.ArtifactType, normType, StringComparison.OrdinalIgnoreCase))
                {
                    CurrentDraft.ApiArtifactId = null;
                }
                await RefreshHealthAndArtifactsAsync();
                ShowBanner($"Deleted {normType.ToUpperInvariant()} '{artifact.Name}' via go-flowAPI.", "info");
                NotifyStateChanged();
                await LogEventAsync("DESIGNER", "DELETE_ARTIFACT", normType, artifact.Name, "SUCCESS", sw.ElapsedMilliseconds, null);
            }
            else
            {
                ShowBanner($"Could not delete {normType} '{artifact.Name}'.", "warning");
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            ShowBanner($"Delete error: {ex.Message}", "error");
            await LogEventAsync("DESIGNER", "DELETE_ARTIFACT", normType, artifact.Name, "ERROR", sw.ElapsedMilliseconds, ex.Message);
        }
    }

    public void AddOptionEntry(string flag = "vars", string value = "")
    {
        if (!_auth.CurrentPermissions.CanCreate && !_auth.CurrentPermissions.CanUpdate)
        {
            ShowBanner("Permission denied: Create/Update permission required.", "error");
            return;
        }
        var normTag = XmlGeneratorService.NormalizeOptionTagName(flag);
        CurrentDraft.OptionEntries.Add(new FlowOptionEntry
        {
            Id = NextNodeId(),
            Flag = string.IsNullOrEmpty(normTag) ? "vars" : normTag,
            Value = value
        });
        CurrentDraft.IsDirty = true;
        RegenerateXmlFromDraft();
        NotifyStateChanged();
    }

    public void UpdateOptionEntry(long id, string? flag, string? value)
    {
        var entry = CurrentDraft.OptionEntries.FirstOrDefault(o => o.Id == id);
        if (entry is null) return;
        if (flag is not null)
        {
            var normTag = XmlGeneratorService.NormalizeOptionTagName(flag);
            entry.Flag = normTag;
            var meta = XmlGeneratorService.OptionsXsdCatalog.FirstOrDefault(x =>
                string.Equals(x.Tag, normTag, StringComparison.OrdinalIgnoreCase));
            if (meta.Tag is not null && string.IsNullOrWhiteSpace(entry.Value))
            {
                entry.Value = meta.DefaultValue;
            }
        }
        if (value is not null) entry.Value = value;
        CurrentDraft.IsDirty = true;
        RegenerateXmlFromDraft();
        NotifyStateChanged();
    }

    public void RemoveOptionEntry(long id)
    {
        var idx = CurrentDraft.OptionEntries.FindIndex(o => o.Id == id);
        if (idx >= 0)
        {
            CurrentDraft.OptionEntries.RemoveAt(idx);
            CurrentDraft.IsDirty = true;
            RegenerateXmlFromDraft();
            NotifyStateChanged();
        }
    }

    public async Task ClearCurrentSectionOrAllAsync(string? section = null)
    {
        if (!_auth.CurrentPermissions.CanDelete && !_auth.CurrentPermissions.CanUpdate)
        {
            ShowBanner("Permission denied: Update or Delete permission required to clear nodes.", "error");
            return;
        }

        if (string.IsNullOrEmpty(section))
        {
            CurrentDraft.VariableNodes.Clear();
            CurrentDraft.DatabaseNodes.Clear();
            CurrentDraft.PreflightNodes.Clear();
            CurrentDraft.FlowNodes.Clear();
        }
        else
        {
            GetRootListForSection(section).Clear();
        }

        CurrentDraft.IsDirty = true;
        RegenerateXmlFromDraft();
        NotifyStateChanged();

        await LogEventAsync("DESIGNER", "CLEAR_NODES", "pipeline", CurrentDraft.Name, "SUCCESS", 0, null, new { section = section ?? "all" });
    }

    // =========================================================================
    // Node Tree Operations & Modals
    // =========================================================================

    public List<PipelineNode> GetRootListForSection(string section) =>
        section.ToLowerInvariant() switch
        {
            "variables" => CurrentDraft.VariableNodes,
            "databases" => CurrentDraft.DatabaseNodes,
            "preflight" => CurrentDraft.PreflightNodes,
            _ => CurrentDraft.FlowNodes
        };

    public int CountAllNodes()
    {
        int CountList(IEnumerable<PipelineNode> list)
        {
            var total = 0;
            foreach (var n in list)
            {
                if (n.NodeType is not ("then" or "else"))
                    total++;
                total += CountList(n.Children);
            }
            return total;
        }

        return CountList(CurrentDraft.VariableNodes)
             + CountList(CurrentDraft.DatabaseNodes)
             + CountList(CurrentDraft.PreflightNodes)
             + CountList(CurrentDraft.FlowNodes);
    }

    public List<string> GetAvailableDatabaseConnectionNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dbNode in CurrentDraft.DatabaseNodes)
        {
            if (dbNode.Attributes.TryGetValue("name", out var n) && !string.IsNullOrWhiteSpace(n))
                names.Add(n.Trim());
        }

        foreach (var profile in SavedDbProfiles)
        {
            if (!string.IsNullOrWhiteSpace(profile.Name))
                names.Add(profile.Name.Trim());
        }

        if (names.Count == 0)
            names.Add("default_db");

        return names.OrderBy(x => x).ToList();
    }

    public void OpenAddNodeModal(string componentType, long? parentNodeId = null, string? targetSectionOverride = null)
    {
        if (!_auth.CurrentPermissions.CanCreate && !_auth.CurrentPermissions.CanUpdate)
        {
            ShowBanner("Permission denied: Create/Update permission required to add components.", "error");
            return;
        }

        var meta = _catalog.GetComponent(componentType);
        if (meta is null)
        {
            ShowBanner($"Unknown component type: {componentType}", "error");
            return;
        }

        var isConfig = string.Equals(CurrentDraft.ArtifactType, "config", StringComparison.OrdinalIgnoreCase);
        if (isConfig && !_catalog.IsAllowedInConfig(meta))
        {
            ShowBanner($"Component <{meta.Tag}> is only supported in Pipeline <flow>, not in a Config.", "warning");
            return;
        }

        var defaultAttrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attr in meta.Attributes)
        {
            if (!string.IsNullOrEmpty(attr.Default))
                defaultAttrs[attr.Name] = attr.Default;
        }

        string resolvedSection;
        if (!string.IsNullOrWhiteSpace(targetSectionOverride))
        {
            resolvedSection = targetSectionOverride.Trim().ToLowerInvariant();
        }
        else if (parentNodeId.HasValue)
        {
            var parentNode = FindNodeById(parentNodeId.Value);
            resolvedSection = parentNode?.Section ?? (isConfig ? "preflight" : "flow");
        }
        else if (isConfig && string.Equals(meta.Section, "flow", StringComparison.OrdinalIgnoreCase))
        {
            resolvedSection = "preflight";
        }
        else
        {
            resolvedSection = meta.Section;
        }

        IsEditingExistingNode = false;
        EditingComponentMeta = meta;
        TargetParentNodeId = parentNodeId;
        TargetSection = resolvedSection;

        EditingNode = new PipelineNode
        {
            Id = NextNodeId(),
            ParentNodeId = parentNodeId,
            Section = TargetSection,
            NodeType = meta.Type,
            Attributes = defaultAttrs,
            ContentText = string.Empty
        };

        ModalVersion++;
        IsComponentPickerModalOpen = false;
        IsNodeEditModalOpen = true;
        NotifyStateChanged();
    }

    public void OpenEditNodeModal(PipelineNode node)
    {
        if (!_auth.CurrentPermissions.CanUpdate && !_auth.CurrentPermissions.CanRead)
        {
            ShowBanner("Permission denied.", "error");
            return;
        }

        var meta = _catalog.GetComponent(node.NodeType) ?? new ComponentMeta
        {
            Type = node.NodeType,
            Tag = node.NodeType,
            Name = node.NodeType,
            Category = "Custom",
            Section = node.Section,
            HasContent = !string.IsNullOrEmpty(node.ContentText)
        };

        IsEditingExistingNode = true;
        EditingComponentMeta = meta;
        TargetParentNodeId = node.ParentNodeId;
        TargetSection = node.Section;
        EditingNode = node;
        ModalVersion++;
        IsNodeEditModalOpen = true;
        NotifyStateChanged();
    }

    public void CloseNodeEditModal()
    {
        ModalVersion++;
        IsNodeEditModalOpen = false;
        EditingNode = null;
        EditingComponentMeta = null;
        NotifyStateChanged();
    }

    public async Task SaveNodeModalAsync(Dictionary<string, string> updatedAttributes, string updatedContent)
    {
        if (EditingNode is null || EditingComponentMeta is null)
            return;

        if (!_auth.CurrentPermissions.CanUpdate && !_auth.CurrentPermissions.CanCreate)
        {
            ShowBanner("Permission denied: You cannot modify pipeline nodes.", "error");
            return;
        }

        var cleanAttrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in updatedAttributes)
        {
            if (!string.IsNullOrWhiteSpace(kv.Value))
            {
                cleanAttrs[kv.Key] = kv.Value.Trim();
            }
        }

        EditingNode.Attributes = cleanAttrs;
        EditingNode.ContentText = updatedContent;

        if (!IsEditingExistingNode)
        {
            if (string.Equals(EditingNode.NodeType, "if", StringComparison.OrdinalIgnoreCase))
            {
                EditingNode.Children.Add(new PipelineNode
                {
                    Id = NextNodeId(),
                    ParentNodeId = EditingNode.Id,
                    Section = EditingNode.Section,
                    NodeType = "then",
                    SequenceOrder = 1
                });
                EditingNode.Children.Add(new PipelineNode
                {
                    Id = NextNodeId(),
                    ParentNodeId = EditingNode.Id,
                    Section = EditingNode.Section,
                    NodeType = "else",
                    SequenceOrder = 2
                });
            }

            if (TargetParentNodeId.HasValue)
            {
                var parent = FindNodeById(TargetParentNodeId.Value);
                if (parent is not null)
                {
                    EditingNode.SequenceOrder = parent.Children.Count + 1;
                    parent.Children.Add(EditingNode);
                }
            }
            else
            {
                var list = GetRootListForSection(TargetSection);
                EditingNode.SequenceOrder = list.Count + 1;
                list.Add(EditingNode);
            }
        }

        var action = IsEditingExistingNode ? "UPDATE_NODE" : "ADD_NODE";
        var nodeType = EditingNode.NodeType;
        var nodeDisplay = EditingNode.DisplayName;

        CurrentDraft.IsDirty = true;
        RegenerateXmlFromDraft();
        CloseNodeEditModal();
        ShowBanner($"{(action == "UPDATE_NODE" ? "Updated" : "Added")} <{nodeType}> node '{nodeDisplay}'. Click Save to persist via API.", "info");

        await LogEventAsync("DESIGNER", action, nodeType,
            string.IsNullOrEmpty(nodeDisplay) ? nodeType : nodeDisplay,
            "SUCCESS", 0, null,
            new { pipeline = CurrentDraft.Name, section = TargetSection });
    }

    public async Task DeleteNodeAsync(long nodeId)
    {
        if (!_auth.CurrentPermissions.CanDelete && !_auth.CurrentPermissions.CanUpdate)
        {
            ShowBanner("Permission denied: Delete permission required to remove nodes.", "error");
            return;
        }

        var removed = RemoveNodeFromList(CurrentDraft.VariableNodes, nodeId)
                   || RemoveNodeFromList(CurrentDraft.DatabaseNodes, nodeId)
                   || RemoveNodeFromList(CurrentDraft.PreflightNodes, nodeId)
                   || RemoveNodeFromList(CurrentDraft.FlowNodes, nodeId);

        if (removed)
        {
            CurrentDraft.IsDirty = true;
            RegenerateXmlFromDraft();
            NotifyStateChanged();
            await LogEventAsync("DESIGNER", "DELETE_NODE", "node", nodeId.ToString(), "SUCCESS", 0, null);
        }
    }

    public async Task MoveNodeOrderAsync(long nodeId, int direction)
    {
        if (!_auth.CurrentPermissions.CanUpdate)
        {
            ShowBanner("Permission denied: Update permission required to reorder nodes.", "error");
            return;
        }

        var list = FindContainingList(nodeId);
        if (list is null) return;

        var index = list.FindIndex(n => n.Id == nodeId);
        if (index < 0) return;

        var targetIndex = index + direction;
        if (targetIndex < 0 || targetIndex >= list.Count) return;

        (list[index], list[targetIndex]) = (list[targetIndex], list[index]);
        for (int i = 0; i < list.Count; i++)
        {
            list[i].SequenceOrder = i + 1;
        }

        CurrentDraft.IsDirty = true;
        RegenerateXmlFromDraft();
        NotifyStateChanged();
        await LogEventAsync("DESIGNER", "REORDER_NODE", "node", nodeId.ToString(), "SUCCESS", 0, null, new { direction });
    }

    public async Task DuplicateNodeAsync(PipelineNode node)
    {
        if (!_auth.CurrentPermissions.CanCreate && !_auth.CurrentPermissions.CanUpdate)
        {
            ShowBanner("Permission denied: Create/Update permission required to duplicate nodes.", "error");
            return;
        }

        var list = FindContainingList(node.Id);
        if (list is null) return;

        var index = list.FindIndex(n => n.Id == node.Id);
        if (index < 0) return;

        var clone = node.Clone(NextNodeId, node.ParentNodeId);
        if (clone.Attributes.TryGetValue("id", out var existingId) && !string.IsNullOrWhiteSpace(existingId))
        {
            clone.Attributes["id"] = $"{existingId}_copy";
        }
        if (clone.Attributes.TryGetValue("name", out var existingName) && !string.IsNullOrWhiteSpace(existingName))
        {
            clone.Attributes["name"] = $"{existingName}_copy";
        }

        list.Insert(index + 1, clone);
        for (int i = 0; i < list.Count; i++)
        {
            list[i].SequenceOrder = i + 1;
        }

        CurrentDraft.IsDirty = true;
        RegenerateXmlFromDraft();
        NotifyStateChanged();
        await LogEventAsync("DESIGNER", "DUPLICATE_NODE", node.NodeType, clone.DisplayName, "SUCCESS", 0, null);
    }

    public void OpenComponentPickerModal(long parentNodeId, string parentLabel)
    {
        PickerParentNodeId = parentNodeId;
        PickerParentLabel = parentLabel;
        IsComponentPickerModalOpen = true;
        NotifyStateChanged();
    }

    public void CloseComponentPickerModal()
    {
        IsComponentPickerModalOpen = false;
        PickerParentNodeId = null;
        NotifyStateChanged();
    }

    public void OpenDocModal(string componentType)
    {
        DocModalComponentType = componentType;
        IsDocModalOpen = true;
        NotifyStateChanged();
    }

    public void CloseDocModal()
    {
        IsDocModalOpen = false;
        NotifyStateChanged();
    }

    public void OpenPipelineEventDetail(PipelineEventRecord ev)
    {
        SelectedPipelineEvent = ev;
        SelectedUiEvent = null;
        IsEventDetailModalOpen = true;
        NotifyStateChanged();
    }

    public void OpenUiEventDetail(UiEventRecord ev)
    {
        SelectedUiEvent = ev;
        SelectedPipelineEvent = null;
        IsEventDetailModalOpen = true;
        NotifyStateChanged();
    }

    public void CloseEventDetailModal()
    {
        IsEventDetailModalOpen = false;
        SelectedPipelineEvent = null;
        SelectedUiEvent = null;
        NotifyStateChanged();
    }

    // =========================================================================
    // Internal Tree Helpers & In-Memory Session Telemetry Logging
    // =========================================================================

    public PipelineNode? FindNodeById(long id)
    {
        return FindInList(CurrentDraft.VariableNodes, id)
            ?? FindInList(CurrentDraft.DatabaseNodes, id)
            ?? FindInList(CurrentDraft.PreflightNodes, id)
            ?? FindInList(CurrentDraft.FlowNodes, id);
    }

    private static PipelineNode? FindInList(List<PipelineNode> list, long id)
    {
        foreach (var n in list)
        {
            if (n.Id == id) return n;
            var found = FindInList(n.Children, id);
            if (found is not null) return found;
        }
        return null;
    }

    private List<PipelineNode>? FindContainingList(long id)
    {
        return FindListRecursive(CurrentDraft.VariableNodes, id)
            ?? FindListRecursive(CurrentDraft.DatabaseNodes, id)
            ?? FindListRecursive(CurrentDraft.PreflightNodes, id)
            ?? FindListRecursive(CurrentDraft.FlowNodes, id);
    }

    private static List<PipelineNode>? FindListRecursive(List<PipelineNode> list, long id)
    {
        if (list.Any(n => n.Id == id)) return list;
        foreach (var n in list)
        {
            var sub = FindListRecursive(n.Children, id);
            if (sub is not null) return sub;
        }
        return null;
    }

    private static bool RemoveNodeFromList(List<PipelineNode> list, long id)
    {
        var idx = list.FindIndex(n => n.Id == id);
        if (idx >= 0)
        {
            list.RemoveAt(idx);
            for (int i = 0; i < list.Count; i++)
                list[i].SequenceOrder = i + 1;
            return true;
        }

        foreach (var n in list)
        {
            if (RemoveNodeFromList(n.Children, id))
                return true;
        }

        return false;
    }

    public Task LogEventAsync(
        string category,
        string action,
        string? artifactType,
        string? artifactName,
        string status = "SUCCESS",
        long? durationMs = null,
        string? errorMessage = null,
        object? details = null)
    {
        var username = _auth.CurrentUser?.Username ?? "anonymous";
        var record = new UiEventRecord
        {
            EventId = Interlocked.Increment(ref _uiEventIdCounter),
            OccurredAt = DateTime.UtcNow,
            Username = username,
            EventCategory = category,
            EventAction = action,
            ArtifactType = artifactType,
            ArtifactName = artifactName,
            Status = status,
            DurationMs = durationMs,
            ErrorMessage = errorMessage,
            DetailsJson = details is not null ? JsonSerializer.Serialize(details) : null
        };

        UiEvents.Insert(0, record);
        if (UiEvents.Count > 500)
        {
            UiEvents.RemoveRange(500, UiEvents.Count - 500);
        }

        return Task.CompletedTask;
    }

    public List<UiEventRecord> GetUiEvents(int limit = 250, string? categoryFilter = null)
    {
        IEnumerable<UiEventRecord> query = UiEvents;
        if (!string.IsNullOrWhiteSpace(categoryFilter))
        {
            query = query.Where(e => e.EventCategory.Contains(categoryFilter.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        return query.Take(limit).ToList();
    }
}
