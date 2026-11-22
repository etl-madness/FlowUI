#!/bin/sh
# Builds a macOS Installer (.pkg) package for ETL Madness FlowUI.
# Installs self-contained .NET 10 FlowUI app (/opt/flow-ui/app), launcher (/opt/flow-ui/bin/flow-ui),
# man pages, GNU Info manual, reference guides, LaunchDaemon plist, and flow-ui.env.example,
# plus path_helper drop-ins in /private/etc/paths.d/flow-ui and /private/etc/manpaths.d/flow-ui.
# Supports ARCH=arm64 (osx-arm64) or ARCH=amd64 (osx-x64).

set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
# shellcheck source=packaging/scripts/common.sh
. "${SCRIPT_DIR}/common.sh"

if ! command -v pkgbuild >/dev/null 2>&1 || ! command -v productbuild >/dev/null 2>&1; then
    log_err "pkgbuild and productbuild are required (run this script on macOS with Xcode Command Line Tools)."
    exit 1
fi

FLOW_VERSION=$(resolve_version)
ARCH_TARGET=$(normalize_arch "${ARCH:-${1:-}}")

case "$ARCH_TARGET" in
    arm64)
        PKG_ARCH="arm64"
        HOST_ARCHS="arm64"
        ;;
    amd64)
        PKG_ARCH="amd64"
        HOST_ARCHS="x86_64"
        ;;
    *)
        log_err "Unsupported macOS architecture: $ARCH_TARGET (expected arm64 or amd64)"
        exit 1
        ;;
esac

WORK_DIR="${BUILD_ROOT}/macos-${PKG_ARCH}"
PAYLOAD_DIR="${WORK_DIR}/payload"
PKG_SCRIPTS_DIR="${WORK_DIR}/scripts"
DOC_TMP="${WORK_DIR}/docs"
COMPONENT_PKG="${WORK_DIR}/flow-ui-component.pkg"
DIST_XML="${WORK_DIR}/Distribution.xml"
OUT_PKG="${DIST_DIR}/${PKG_NAME}-${FLOW_VERSION}-darwin-${PKG_ARCH}.pkg"

rm -rf "$WORK_DIR"
mkdir -p \
    "${PAYLOAD_DIR}/opt/flow-ui/app" \
    "${PAYLOAD_DIR}/opt/flow-ui/bin" \
    "${PAYLOAD_DIR}/opt/flow-ui/etc" \
    "${PAYLOAD_DIR}/opt/flow-ui/share/man/man1" \
    "${PAYLOAD_DIR}/opt/flow-ui/share/man/man5" \
    "${PAYLOAD_DIR}/opt/flow-ui/share/info" \
    "${PAYLOAD_DIR}/opt/flow-ui/share/doc" \
    "${PAYLOAD_DIR}/opt/flow-ui/share/services/launchd" \
    "${PAYLOAD_DIR}/opt/flow-ui/share/licenses" \
    "${PAYLOAD_DIR}/private/etc/paths.d" \
    "${PAYLOAD_DIR}/private/etc/manpaths.d" \
    "$PKG_SCRIPTS_DIR" \
    "$DIST_DIR"

publish_flow_ui_self_contained "darwin" "$ARCH_TARGET" "${PAYLOAD_DIR}/opt/flow-ui/app"
write_unix_launcher "${PAYLOAD_DIR}/opt/flow-ui/bin" "self-contained"
stage_compressed_docs "$DOC_TMP"

cp "${DOC_TMP}/man1/"*.1.gz "${PAYLOAD_DIR}/opt/flow-ui/share/man/man1/"
cp "${DOC_TMP}/man5/"*.5.gz "${PAYLOAD_DIR}/opt/flow-ui/share/man/man5/"
cp "${DOC_TMP}/info/flow-ui.info.gz" "${PAYLOAD_DIR}/opt/flow-ui/share/info/flow-ui.info.gz"
cp "${DOC_TMP}/info/dir" "${PAYLOAD_DIR}/opt/flow-ui/share/info/dir"

cp "${REPO_ROOT}/README.md" "${PAYLOAD_DIR}/opt/flow-ui/share/doc/README.md"
cp "${REPO_ROOT}/DOCKER.md" "${PAYLOAD_DIR}/opt/flow-ui/share/doc/DOCKER.md"

cp "${PACKAGING_DIR}/services/launchd/com.etlmadness.flow-ui.plist" \
    "${PAYLOAD_DIR}/opt/flow-ui/share/services/launchd/com.etlmadness.flow-ui.plist"
sed 's|/etc/flow-ui|/opt/flow-ui/etc|g' \
    "${PACKAGING_DIR}/etc/flow-ui/flow-ui.env" > "${PAYLOAD_DIR}/opt/flow-ui/etc/flow-ui.env.example"

cp "${REPO_ROOT}/LICENSE.txt" "${PAYLOAD_DIR}/opt/flow-ui/share/licenses/LICENSE"

printf '/opt/flow-ui/bin\n' > "${PAYLOAD_DIR}/private/etc/paths.d/flow-ui"
printf '/opt/flow-ui/share/man\n' > "${PAYLOAD_DIR}/private/etc/manpaths.d/flow-ui"

cat > "${PKG_SCRIPTS_DIR}/postinstall" <<'EOF'
#!/bin/sh
mkdir -p /var/db/flow-ui /var/log/flow-ui
chmod 0750 /var/db/flow-ui /var/log/flow-ui
if [ ! -f /opt/flow-ui/etc/flow-ui.env ] && [ -f /opt/flow-ui/etc/flow-ui.env.example ]; then
    cp /opt/flow-ui/etc/flow-ui.env.example /opt/flow-ui/etc/flow-ui.env
    chmod 0600 /opt/flow-ui/etc/flow-ui.env
fi
exit 0
EOF
chmod 0755 "${PKG_SCRIPTS_DIR}/postinstall"

chmod 0644 \
    "${PAYLOAD_DIR}/opt/flow-ui/share/man/man1/"*.1.gz \
    "${PAYLOAD_DIR}/opt/flow-ui/share/man/man5/"*.5.gz \
    "${PAYLOAD_DIR}/opt/flow-ui/share/info/flow-ui.info.gz" \
    "${PAYLOAD_DIR}/opt/flow-ui/share/info/dir" \
    "${PAYLOAD_DIR}/opt/flow-ui/share/doc/"*.md \
    "${PAYLOAD_DIR}/opt/flow-ui/share/services/launchd/com.etlmadness.flow-ui.plist" \
    "${PAYLOAD_DIR}/opt/flow-ui/etc/flow-ui.env.example" \
    "${PAYLOAD_DIR}/opt/flow-ui/share/licenses/LICENSE" \
    "${PAYLOAD_DIR}/private/etc/paths.d/flow-ui" \
    "${PAYLOAD_DIR}/private/etc/manpaths.d/flow-ui"

sed \
    -e "s/__VERSION__/${FLOW_VERSION}/g" \
    -e "s/__HOST_ARCHS__/${HOST_ARCHS}/g" \
    "${PACKAGING_DIR}/macos/Distribution.xml.tmpl" > "$DIST_XML"

log_info "Building macOS component package..."
pkgbuild \
    --root "$PAYLOAD_DIR" \
    --scripts "$PKG_SCRIPTS_DIR" \
    --identifier "com.etlmadness.flow-ui" \
    --version "$FLOW_VERSION" \
    --install-location "/" \
    "$COMPONENT_PKG"

log_info "Building macOS distribution package..."
if [ -n "${MACOS_SIGN_IDENTITY:-}" ]; then
    productbuild \
        --distribution "$DIST_XML" \
        --package-path "$WORK_DIR" \
        --sign "$MACOS_SIGN_IDENTITY" \
        "$OUT_PKG"
else
    productbuild \
        --distribution "$DIST_XML" \
        --package-path "$WORK_DIR" \
        "$OUT_PKG"
fi

log_info "Created macOS package: ${OUT_PKG}"
