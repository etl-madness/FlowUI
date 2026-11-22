#!/bin/sh
# Shared helper functions for ETL Madness FlowUI Unix package build scripts.
# Portable POSIX sh (compatible with Linux dash/bash, macOS sh, and FreeBSD sh).

set -eu

PACKAGING_SCRIPTS_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
PACKAGING_DIR=$(CDPATH= cd -- "${PACKAGING_SCRIPTS_DIR}/.." && pwd)
REPO_ROOT=$(CDPATH= cd -- "${PACKAGING_DIR}/.." && pwd)

BUILD_ROOT="${BUILD_ROOT:-${PACKAGING_DIR}/build}"
DIST_DIR="${DIST_DIR:-${PACKAGING_DIR}/dist}"
PKG_NAME="${PKG_NAME:-flow-ui}"
INSTALL_PREFIX="/opt/flow-ui"
FLOW_RELEASE="${FLOW_RELEASE:-1}"
MAINTAINER="${MAINTAINER:-ETL Madness <maintainers@etl-madness.invalid>}"
HOMEPAGE="${HOMEPAGE:-https://github.com/etl-madness/flowUI}"
CSPROJ_PATH="${REPO_ROOT}/FlowUI/FlowUI.csproj"

log_info() {
    printf '[INFO] %s\n' "$*"
}

log_warn() {
    printf '[WARN] %s\n' "$*" >&2
}

log_err() {
    printf '[ERROR] %s\n' "$*" >&2
}

resolve_version() {
    if [ -n "${VERSION:-}" ]; then
        raw_ver="$VERSION"
    elif command -v git >/dev/null 2>&1 && git -C "$REPO_ROOT" describe --tags --abbrev=0 >/dev/null 2>&1; then
        raw_ver=$(git -C "$REPO_ROOT" describe --tags --abbrev=0)
    elif [ -f "$CSPROJ_PATH" ]; then
        raw_ver=$(sed -n 's|.*<Version>\([^<]*\)</Version>.*|\1|p' "$CSPROJ_PATH" | head -n 1)
        raw_ver="${raw_ver:-1.3.1}"
    else
        raw_ver="1.3.1"
    fi
    # Strip leading 'v' or 'V'
    printf '%s' "$raw_ver" | sed 's/^[vV]//'
}

normalize_arch() {
    arch_in="${1:-}"
    if [ -z "$arch_in" ]; then
        arch_in=$(uname -m)
    fi
    case "$arch_in" in
        x86_64|amd64|x64) printf 'amd64' ;;
        aarch64|arm64) printf 'arm64' ;;
        *)
            log_err "Unsupported architecture: $arch_in (expected amd64 or arm64)"
            exit 1
            ;;
    esac
}

map_dotnet_rid() {
    target_os="$1"
    target_arch="$2"
    case "${target_os}/${target_arch}" in
        linux/amd64)   printf 'linux-x64' ;;
        linux/arm64)   printf 'linux-arm64' ;;
        darwin/amd64)  printf 'osx-x64' ;;
        darwin/arm64)  printf 'osx-arm64' ;;
        windows/amd64) printf 'win-x64' ;;
        windows/arm64) printf 'win-arm64' ;;
        *)
            log_err "Unsupported .NET self-contained RID mapping: ${target_os}/${target_arch}"
            exit 1
            ;;
    esac
}

publish_flow_ui_self_contained() {
    target_os="$1"
    target_arch="$2"
    out_app_dir="$3"
    rid=$(map_dotnet_rid "$target_os" "$target_arch")

    if ! command -v dotnet >/dev/null 2>&1; then
        log_err "dotnet SDK (10.0+) is required to publish FlowUI."
        exit 1
    fi

    rm -rf "$out_app_dir"
    mkdir -p "$out_app_dir"
    log_info "Publishing self-contained FlowUI for ${rid}..."

    (
        cd "$REPO_ROOT"
        dotnet publish "$CSPROJ_PATH" \
            -c Release \
            -r "$rid" \
            --self-contained true \
            -p:PublishSingleFile=false \
            -p:DebugType=none \
            -p:DebugSymbols=false \
            -o "$out_app_dir"
    )

    if [ "$target_os" != "windows" ] && [ -f "${out_app_dir}/FlowUI" ]; then
        chmod 0755 "${out_app_dir}/FlowUI"
    fi
}

publish_flow_ui_framework_dependent() {
    out_app_dir="$1"

    if ! command -v dotnet >/dev/null 2>&1; then
        log_err "dotnet SDK (10.0+) is required to publish FlowUI."
        exit 1
    fi

    rm -rf "$out_app_dir"
    mkdir -p "$out_app_dir"
    log_info "Publishing framework-dependent FlowUI (net10.0)..."

    (
        cd "$REPO_ROOT"
        dotnet publish "$CSPROJ_PATH" \
            -c Release \
            --no-self-contained \
            -p:UseAppHost=false \
            -p:DebugType=none \
            -p:DebugSymbols=false \
            -o "$out_app_dir"
    )
}

write_unix_launcher() {
    out_bin_dir="$1"
    mode="${2:-self-contained}"

    mkdir -p "$out_bin_dir"
    if [ "$mode" = "framework-dependent" ]; then
        cat > "${out_bin_dir}/flow-ui" <<'EOF'
#!/bin/sh
set -eu
DOTNET_BIN="${DOTNET_BIN:-$(command -v dotnet 2>/dev/null || true)}"
if [ -z "$DOTNET_BIN" ] && [ -x /usr/local/bin/dotnet ]; then
    DOTNET_BIN="/usr/local/bin/dotnet"
fi
if [ -z "$DOTNET_BIN" ]; then
    printf '[ERROR] dotnet (.NET 10 ASP.NET Core runtime) was not found in PATH or /usr/local/bin/dotnet.\n' >&2
    exit 1
fi
exec "$DOTNET_BIN" /opt/flow-ui/app/FlowUI.dll --contentRoot /opt/flow-ui/app "$@"
EOF
    else
        cat > "${out_bin_dir}/flow-ui" <<'EOF'
#!/bin/sh
set -eu
exec /opt/flow-ui/app/FlowUI --contentRoot /opt/flow-ui/app "$@"
EOF
    fi
    chmod 0755 "${out_bin_dir}/flow-ui"
}

stage_compressed_docs() {
    out_doc_dir="$1"

    sh "${PACKAGING_SCRIPTS_DIR}/generate-docs.sh"

    mkdir -p "${out_doc_dir}/man1" "${out_doc_dir}/man5" "${out_doc_dir}/info"

    gzip -9nc "${PACKAGING_DIR}/man/man1/flow-ui.1" > "${out_doc_dir}/man1/flow-ui.1.gz"
    gzip -9nc "${PACKAGING_DIR}/man/man5/flow-ui.env.5" > "${out_doc_dir}/man5/flow-ui.env.5.gz"
    gzip -9nc "${PACKAGING_DIR}/info/flow-ui.info" > "${out_doc_dir}/info/flow-ui.info.gz"
    cp "${PACKAGING_DIR}/info/dir" "${out_doc_dir}/info/dir"

    chmod 0644 \
        "${out_doc_dir}/man1/flow-ui.1.gz" \
        "${out_doc_dir}/man5/flow-ui.env.5.gz" \
        "${out_doc_dir}/info/flow-ui.info.gz" \
        "${out_doc_dir}/info/dir"
}
