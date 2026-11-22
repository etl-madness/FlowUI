#!/bin/sh
# Cross-builds Windows portable .zip, optional WiX .msi, and WinGet 1.6.0 YAML
# manifests for ETL Madness FlowUI from Linux/macOS/FreeBSD.

set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
# shellcheck source=packaging/scripts/common.sh
. "${SCRIPT_DIR}/common.sh"

FLOW_VERSION=$(resolve_version)
ARCH_TARGET=$(normalize_arch "${ARCH:-${1:-amd64}}")
WIN_FORMAT="${WIN_FORMAT:-all}"
RELEASE_BASE_URL="${RELEASE_BASE_URL:-https://github.com/etl-madness/flowUI/releases/download/v${FLOW_VERSION}}"
RELEASE_BASE_URL=$(printf '%s' "$RELEASE_BASE_URL" | sed 's|/$||')
UPGRADE_CODE="{8F3A9C12-6D4E-4B8A-9E21-5C7B2A1F04D9}"

case "$ARCH_TARGET" in
    amd64) WINGET_ARCH="x64"; WIX_ARCH="x64" ;;
    arm64) WINGET_ARCH="arm64"; WIX_ARCH="arm64" ;;
    *)
        log_err "Unsupported Windows architecture: $ARCH_TARGET"
        exit 1
        ;;
esac

sha256_upper() {
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$1" | awk '{print toupper($1)}'
    elif command -v shasum >/dev/null 2>&1; then
        shasum -a 256 "$1" | awk '{print toupper($1)}'
    elif command -v sha256 >/dev/null 2>&1; then
        sha256 -q "$1" | tr '[:lower:]' '[:upper:]'
    else
        openssl dgst -sha256 "$1" | awk '{print toupper($NF)}'
    fi
}

deterministic_product_code() {
    seed_hex=$(printf 'ETLMadness.FlowUI.MSI:%s:%s' "$FLOW_VERSION" "$ARCH_TARGET" | {
        if command -v sha256sum >/dev/null 2>&1; then
            sha256sum | awk '{print toupper($1)}'
        elif command -v shasum >/dev/null 2>&1; then
            shasum -a 256 | awk '{print toupper($1)}'
        elif command -v sha256 >/dev/null 2>&1; then
            sha256 -q | tr '[:lower:]' '[:upper:]'
        else
            openssl dgst -sha256 | awk '{print toupper($NF)}'
        fi
    })
    p1=$(printf '%s' "$seed_hex" | cut -c1-8)
    p2=$(printf '%s' "$seed_hex" | cut -c9-12)
    p3="5$(printf '%s' "$seed_hex" | cut -c14-16)"
    p4="8$(printf '%s' "$seed_hex" | cut -c18-20)"
    p5=$(printf '%s' "$seed_hex" | cut -c21-32)
    printf '{%s-%s-%s-%s-%s}' "$p1" "$p2" "$p3" "$p4" "$p5"
}

msi_numeric_version() {
    clean=$(printf '%s' "$1" | cut -d'-' -f1 | cut -d'+' -f1)
    printf '%s' "$clean" | awk -F. '{printf "%d.%d.%d", ($1+0), ($2+0), ($3+0)}'
}

WORK_DIR="${BUILD_ROOT}/windows-${ARCH_TARGET}"
STAGE_DIR="${WORK_DIR}/payload"
ZIP_NAME="${PKG_NAME}-${FLOW_VERSION}-windows-${ARCH_TARGET}.zip"
ZIP_PATH="${DIST_DIR}/${ZIP_NAME}"
MSI_NAME="${PKG_NAME}-${FLOW_VERSION}-windows-${ARCH_TARGET}.msi"
MSI_PATH="${DIST_DIR}/${MSI_NAME}"
MSI_VER=$(msi_numeric_version "$FLOW_VERSION")

rm -rf "$WORK_DIR"
mkdir -p \
    "${STAGE_DIR}/app" \
    "${STAGE_DIR}/docs" \
    "${STAGE_DIR}/share/services/windows" \
    "${STAGE_DIR}/share/man/man1" \
    "${STAGE_DIR}/share/man/man5" \
    "$DIST_DIR"

publish_flow_ui_self_contained "windows" "$ARCH_TARGET" "${STAGE_DIR}/app"

cp "${REPO_ROOT}/README.md" "${STAGE_DIR}/docs/README.md"
cp "${REPO_ROOT}/DOCKER.md" "${STAGE_DIR}/docs/DOCKER.md"
cp "${PACKAGING_DIR}/services/windows/Install-FlowUIService.ps1" "${STAGE_DIR}/share/services/windows/Install-FlowUIService.ps1"
cp "${PACKAGING_DIR}/services/windows/Uninstall-FlowUIService.ps1" "${STAGE_DIR}/share/services/windows/Uninstall-FlowUIService.ps1"
sed 's|/etc/flow-ui|C:/ProgramData/ETL Madness/Flow UI|g' \
    "${PACKAGING_DIR}/etc/flow-ui/flow-ui.env" > "${STAGE_DIR}/share/services/windows/flow-ui.env.example"
cp "${PACKAGING_DIR}/man/man1/flow-ui.1" "${STAGE_DIR}/share/man/man1/flow-ui.1"
cp "${PACKAGING_DIR}/man/man5/flow-ui.env.5" "${STAGE_DIR}/share/man/man5/flow-ui.env.5"
cp "${REPO_ROOT}/LICENSE.txt" "${STAGE_DIR}/LICENSE.txt"

rm -f "$ZIP_PATH"
log_info "Creating Windows portable archive: ${ZIP_PATH}"
if command -v zip >/dev/null 2>&1; then
    (cd "$STAGE_DIR" && zip -q -r "$ZIP_PATH" .)
elif command -v python3 >/dev/null 2>&1; then
    python3 -c '
import os, sys, zipfile
stage, out = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as zf:
    for root, _, files in os.walk(stage):
        for fn in sorted(files):
            full = os.path.join(root, fn)
            rel = os.path.relpath(full, stage).replace(os.sep, "/")
            zf.write(full, rel)
' "$STAGE_DIR" "$ZIP_PATH"
else
    log_err "Either 'zip' or 'python3' is required to create ${ZIP_PATH}."
    exit 1
fi

ZIP_SHA256=$(sha256_upper "$ZIP_PATH")
MSI_BLOCK=""

if [ "$WIN_FORMAT" = "all" ] || [ "$WIN_FORMAT" = "msi" ]; then
    if command -v wix >/dev/null 2>&1; then
        PRODUCT_CODE=$(deterministic_product_code)
        log_info "Building MSI with WiX Toolset (${WIX_ARCH})..."
        wix build "${PACKAGING_DIR}/windows/flow-ui.wxs" \
            -arch "$WIX_ARCH" \
            -d "StageDir=${STAGE_DIR}" \
            -d "FlowMsiVersion=${MSI_VER}" \
            -d "FlowProductCode=${PRODUCT_CODE}" \
            -o "$MSI_PATH"
        MSI_SHA256=$(sha256_upper "$MSI_PATH")
        MSI_BLOCK="  - Architecture: ${WINGET_ARCH}
    Scope: machine
    InstallerType: wix
    InstallerUrl: ${RELEASE_BASE_URL}/${MSI_NAME}
    InstallerSha256: ${MSI_SHA256}
    ProductCode: '${PRODUCT_CODE}'
    AppsAndFeaturesEntries:
      - DisplayName: ETL Madness FlowUI
        Publisher: ETL Madness
        DisplayVersion: ${MSI_VER}
        ProductCode: '${PRODUCT_CODE}'
        UpgradeCode: '${UPGRADE_CODE}'"
    elif [ "$WIN_FORMAT" = "msi" ]; then
        log_err "WiX CLI ('wix') is required to build .msi packages."
        exit 1
    else
        log_warn "WiX CLI ('wix') not found; skipping .msi build (portable .zip and WinGet manifests generated)."
    fi
fi

WINGET_DIR="${DIST_DIR}/winget/e/ETLMadness/FlowUI/${FLOW_VERSION}"
mkdir -p "$WINGET_DIR"

cat > "${WINGET_DIR}/ETLMadness.FlowUI.yaml" <<EOF
# yaml-language-server: \$schema=https://aka.ms/winget-manifest.version.1.6.0.schema.json

PackageIdentifier: ETLMadness.FlowUI
PackageVersion: ${FLOW_VERSION}
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.6.0
EOF

cat > "${WINGET_DIR}/ETLMadness.FlowUI.locale.en-US.yaml" <<EOF
# yaml-language-server: \$schema=https://aka.ms/winget-manifest.defaultLocale.1.6.0.schema.json

PackageIdentifier: ETLMadness.FlowUI
PackageVersion: ${FLOW_VERSION}
PackageLocale: en-US
Publisher: ETL Madness
PublisherUrl: https://github.com/etl-madness
PublisherSupportUrl: https://github.com/etl-madness/flowUI/issues
PackageName: ETL Madness FlowUI
PackageUrl: https://github.com/etl-madness/flowUI
License: MIT
LicenseUrl: https://github.com/etl-madness/flowUI/blob/main/LICENSE.txt
ShortDescription: Interactive .NET 10 Blazor Server visual studio (Flow Builder Studio) for ETL Madness go-flow and go-flowAPI.
Description: >-
  ETL Madness FlowUI (Flow Builder Studio) is an interactive .NET 10 ASP.NET Core
  Blazor Server web application and native Windows Service for visually designing,
  configuring, signing, encrypting, validating, executing, and monitoring XML ETL
  pipelines powered by go-flow and go-flowAPI.
Moniker: flow-ui
Tags:
  - blazor
  - dotnet
  - etl
  - pipeline
  - studio
  - ui
  - workflow
Documentations:
  - DocumentLabel: Online Documentation
    DocumentUrl: https://etl-madness.github.io/flow-docs/
ManifestType: defaultLocale
ManifestVersion: 1.6.0
EOF

{
    cat <<EOF
# yaml-language-server: \$schema=https://aka.ms/winget-manifest.installer.1.6.0.schema.json

PackageIdentifier: ETLMadness.FlowUI
PackageVersion: ${FLOW_VERSION}
Commands:
  - flow-ui
Installers:
  - Architecture: ${WINGET_ARCH}
    InstallerType: zip
    NestedInstallerType: portable
    NestedInstallerFiles:
      - RelativeFilePath: app\FlowUI.exe
        PortableCommandAlias: flow-ui
    InstallerUrl: ${RELEASE_BASE_URL}/${ZIP_NAME}
    InstallerSha256: ${ZIP_SHA256}
EOF
    if [ -n "$MSI_BLOCK" ]; then
        printf '%s\n' "$MSI_BLOCK"
    fi
    cat <<EOF
ManifestType: installer
ManifestVersion: 1.6.0
EOF
} > "${WINGET_DIR}/ETLMadness.FlowUI.installer.yaml"

log_info "Generated Windows artifacts and WinGet manifests in: ${WINGET_DIR}"
