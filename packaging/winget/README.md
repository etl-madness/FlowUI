# WinGet Manifest Guide (`ETLMadness.FlowUI`)

`Build-Windows.ps1` and `build-windows.sh` generate validated WinGet 1.6.0 multi-file manifests in:

```text
packaging/dist/winget/e/ETLMadness/FlowUI/<version>/
├── ETLMadness.FlowUI.yaml
├── ETLMadness.FlowUI.locale.en-US.yaml
└── ETLMadness.FlowUI.installer.yaml
```

## 1. Build Windows Packages & Manifests

```powershell
# Build both portable .zip and WiX .msi for x64 (requires WiX Toolset for .msi: dotnet tool install --global wix)
.\packaging\scripts\Build-Windows.ps1 -Format all -Arch amd64

# Build both x64 and arm64
.\packaging\scripts\Build-Windows.ps1 -Format all -Arch both
```

## 2. Validate Manifests Locally

```powershell
winget validate --manifest .\packaging\dist\winget\e\ETLMadness\FlowUI\1.3.1\
```

## 3. Test Local Installation via WinGet

```powershell
# One-time Administrator setting to allow local manifest directories:
winget settings --enable LocalManifestFiles

# Install portable .zip (user scope)
winget install --manifest .\packaging\dist\winget\e\ETLMadness\FlowUI\1.3.1\

# Install machine-wide .msi into C:\Program Files\ETL Madness\Flow UI\
winget install --manifest .\packaging\dist\winget\e\ETLMadness\FlowUI\1.3.1\ --scope machine --installer-type wix
```

## 4. Authenticode Code Signing (Optional)

Pass `-SignCertThumbprint <SHA1>` (and optional `-TimestampServer`) to `Build-Windows.ps1` to sign `FlowUI.exe` and `.msi` installers with `signtool.exe`.
