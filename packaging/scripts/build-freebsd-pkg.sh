#!/bin/sh
# Builds a FreeBSD (.pkg) package for ETL Madness FlowUI.
# Publishes FlowUI as framework-dependent (.NET 10) under /opt/flow-ui/app with
# launcher /opt/flow-ui/bin/flow-ui (requires FreeBSD dotnet runtime package),
# plus FreeBSD rc.d service script (/usr/local/etc/rc.d/flow_ui), environment sample
# (/usr/local/etc/flow-ui/flow-ui.env.sample), and PATH/MANPATH drop-ins.

set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
# shellcheck source=packaging/scripts/common.sh
. "${SCRIPT_DIR}/common.sh"

if ! command -v pkg >/dev/null 2>&1; then
    log_err "FreeBSD 'pkg' utility is required (run this script on FreeBSD)."
    exit 1
fi

RAW_VERSION=$(resolve_version)
# FreeBSD pkg-version(8) treats '-' as revision separator; normalize pre-releases.
FLOW_VERSION=$(printf '%s' "$RAW_VERSION" | tr '-' '.')
ARCH_TARGET=$(normalize_arch "${ARCH:-${1:-}}")
if [ -z "${FREEBSD_MAJOR:-}" ]; then
    if [ "$(uname -s)" = "FreeBSD" ]; then
        FREEBSD_MAJOR=$(uname -r | cut -d. -f1)
    else
        FREEBSD_MAJOR="14"
    fi
fi

case "$ARCH_TARGET" in
    amd64) FBSD_ARCH="amd64" ;;
    arm64) FBSD_ARCH="aarch64" ;;
    *)
        log_err "Unsupported FreeBSD architecture: $ARCH_TARGET"
        exit 1
        ;;
esac

FBSD_ABI="FreeBSD:${FREEBSD_MAJOR}:${FBSD_ARCH}"
WORK_DIR="${BUILD_ROOT}/freebsd-${FBSD_ARCH}"
STAGE_DIR="${WORK_DIR}/stage"
META_DIR="${WORK_DIR}/meta"
DOC_TMP="${WORK_DIR}/docs"
PLIST_FILE="${WORK_DIR}/pkg-plist"

rm -rf "$WORK_DIR"
mkdir -p \
    "${STAGE_DIR}/opt/flow-ui/app" \
    "${STAGE_DIR}/opt/flow-ui/bin" \
    "${STAGE_DIR}/opt/flow-ui/share/man/man1" \
    "${STAGE_DIR}/opt/flow-ui/share/man/man5" \
    "${STAGE_DIR}/opt/flow-ui/share/info" \
    "${STAGE_DIR}/opt/flow-ui/share/doc" \
    "${STAGE_DIR}/opt/flow-ui/share/services/freebsd" \
    "${STAGE_DIR}/opt/flow-ui/share/licenses" \
    "${STAGE_DIR}/usr/local/etc/rc.d" \
    "${STAGE_DIR}/usr/local/etc/flow-ui" \
    "${STAGE_DIR}/usr/local/etc/profile.d" \
    "${STAGE_DIR}/usr/local/etc/man.d" \
    "$META_DIR" \
    "$DIST_DIR"

publish_flow_ui_framework_dependent "${STAGE_DIR}/opt/flow-ui/app"
write_unix_launcher "${STAGE_DIR}/opt/flow-ui/bin" "framework-dependent"
stage_compressed_docs "$DOC_TMP"

cp "${DOC_TMP}/man1/"*.1.gz "${STAGE_DIR}/opt/flow-ui/share/man/man1/"
cp "${DOC_TMP}/man5/"*.5.gz "${STAGE_DIR}/opt/flow-ui/share/man/man5/"
cp "${DOC_TMP}/info/flow-ui.info.gz" "${STAGE_DIR}/opt/flow-ui/share/info/flow-ui.info.gz"
cp "${DOC_TMP}/info/dir" "${STAGE_DIR}/opt/flow-ui/share/info/dir"

cp "${REPO_ROOT}/README.md" "${STAGE_DIR}/opt/flow-ui/share/doc/README.md"
cp "${REPO_ROOT}/DOCKER.md" "${STAGE_DIR}/opt/flow-ui/share/doc/DOCKER.md"

cp "${PACKAGING_DIR}/services/freebsd/flow_ui" "${STAGE_DIR}/opt/flow-ui/share/services/freebsd/flow_ui"
cp "${PACKAGING_DIR}/services/freebsd/flow_ui" "${STAGE_DIR}/usr/local/etc/rc.d/flow_ui"
sed 's|/etc/flow-ui|/usr/local/etc/flow-ui|g' \
    "${PACKAGING_DIR}/etc/flow-ui/flow-ui.env" > "${STAGE_DIR}/usr/local/etc/flow-ui/flow-ui.env.sample"

cp "${REPO_ROOT}/LICENSE.txt" "${STAGE_DIR}/opt/flow-ui/share/licenses/LICENSE"
cp "${PACKAGING_DIR}/etc/profile.d/flow-ui.sh" "${STAGE_DIR}/usr/local/etc/profile.d/flow-ui.sh"
printf 'MANPATH /opt/flow-ui/share/man\n' > "${STAGE_DIR}/usr/local/etc/man.d/flow-ui.conf"

chmod 0755 \
    "${STAGE_DIR}/opt/flow-ui/share/services/freebsd/flow_ui" \
    "${STAGE_DIR}/usr/local/etc/rc.d/flow_ui"

chmod 0640 "${STAGE_DIR}/usr/local/etc/flow-ui/flow-ui.env.sample"

chmod 0644 \
    "${STAGE_DIR}/opt/flow-ui/share/man/man1/"*.1.gz \
    "${STAGE_DIR}/opt/flow-ui/share/man/man5/"*.5.gz \
    "${STAGE_DIR}/opt/flow-ui/share/info/flow-ui.info.gz" \
    "${STAGE_DIR}/opt/flow-ui/share/info/dir" \
    "${STAGE_DIR}/opt/flow-ui/share/doc/"*.md \
    "${STAGE_DIR}/opt/flow-ui/share/licenses/LICENSE" \
    "${STAGE_DIR}/usr/local/etc/profile.d/flow-ui.sh" \
    "${STAGE_DIR}/usr/local/etc/man.d/flow-ui.conf"

SIZE_KB=$(du -sk "${STAGE_DIR}/opt/flow-ui" "${STAGE_DIR}/usr/local/etc" | awk '{s+=$1} END {print s}')
FLATSIZE=$((SIZE_KB * 1024))

sed \
    -e "s/__VERSION__/${FLOW_VERSION}/g" \
    -e "s/__ABI__/${FBSD_ABI}/g" \
    -e "s/__FLATSIZE__/${FLATSIZE}/g" \
    "${PACKAGING_DIR}/freebsd/+MANIFEST.tmpl" > "${META_DIR}/+MANIFEST"

cp "${PACKAGING_DIR}/freebsd/+POST_INSTALL" "${META_DIR}/+POST_INSTALL"
cp "${PACKAGING_DIR}/freebsd/+PRE_DEINSTALL" "${META_DIR}/+PRE_DEINSTALL"
chmod 0755 "${META_DIR}/+POST_INSTALL" "${META_DIR}/+PRE_DEINSTALL"

# Generate pkg-plist dynamically from staged files (preserving user edits to profile.d & man.d)
(
    cd "$STAGE_DIR"
    find opt usr -type f | LC_ALL=C sort | sed \
        -e 's|^|/|' \
        -e 's|^/usr/local/etc/profile\.d/flow-ui\.sh$|@config /usr/local/etc/profile.d/flow-ui.sh|' \
        -e 's|^/usr/local/etc/man\.d/flow-ui\.conf$|@config /usr/local/etc/man.d/flow-ui.conf|'
) > "$PLIST_FILE"

PKG_TMP_OUT="${WORK_DIR}/pkg-out"
mkdir -p "$PKG_TMP_OUT"

log_info "Building FreeBSD package (${FBSD_ABI})..."
pkg create \
    -m "$META_DIR" \
    -p "$PLIST_FILE" \
    -r "$STAGE_DIR" \
    -o "$PKG_TMP_OUT"

BUILT_PKG=$(find "$PKG_TMP_OUT" -name '*.pkg' -o -name '*.txz' -o -name '*.tzst' | head -n 1)
if [ -z "$BUILT_PKG" ]; then
    log_err "pkg create completed but no package archive was found in ${PKG_TMP_OUT}"
    exit 1
fi

OUT_PKG="${DIST_DIR}/${PKG_NAME}-${FLOW_VERSION}-freebsd-${FBSD_ARCH}.pkg"
cp "$BUILT_PKG" "$OUT_PKG"

log_info "Created FreeBSD package: ${OUT_PKG}"
