#!/bin/sh
# Builds an OpenBSD (.tgz) package for ETL Madness FlowUI.
# Publishes FlowUI as framework-dependent (.NET 10) under /opt/flow-ui/app with
# launcher /opt/flow-ui/bin/flow-ui, man pages, GNU Info manual, OpenBSD rc.d
# service script (/etc/rc.d/flow_ui), environment sample
# (/opt/flow-ui/share/examples/flow-ui/flow-ui.env.sample -> /etc/flow-ui/flow-ui.env),
# and @sample /etc/profile.d/flow-ui.sh.

set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
# shellcheck source=packaging/scripts/common.sh
. "${SCRIPT_DIR}/common.sh"

RAW_VERSION=$(resolve_version)
# OpenBSD packages-specs(7) forbids hyphens inside the version component
# (e.g. 1.3.1-rc1 -> 1.3.1rc1) because hyphens separate package flavors.
OBSD_BASE_VER=$(printf '%s' "$RAW_VERSION" | tr -d '-')
if [ "${FLOW_RELEASE}" -gt 1 ] 2>/dev/null; then
    OBSD_REV=$((FLOW_RELEASE - 1))
    OBSD_PKGVER="${OBSD_BASE_VER}p${OBSD_REV}"
else
    OBSD_PKGVER="${OBSD_BASE_VER}"
fi

ARCH_TARGET=$(normalize_arch "${ARCH:-${1:-}}")

case "$ARCH_TARGET" in
    amd64) OBSD_ARCH="amd64" ;;
    arm64) OBSD_ARCH="aarch64" ;;
    *)
        log_err "Unsupported OpenBSD architecture: $ARCH_TARGET (expected amd64 or arm64)"
        exit 1
        ;;
esac

FULLPKGNAME="${PKG_NAME}-${OBSD_PKGVER}"
WORK_DIR="${BUILD_ROOT}/openbsd-${OBSD_ARCH}"
STAGE_DIR="${WORK_DIR}/stage"
META_DIR="${WORK_DIR}/meta"
DOC_TMP="${WORK_DIR}/docs"
PLIST_FILE="${WORK_DIR}/PLIST"

rm -rf "$WORK_DIR"
mkdir -p \
    "${STAGE_DIR}/opt/flow-ui/app" \
    "${STAGE_DIR}/opt/flow-ui/bin" \
    "${STAGE_DIR}/opt/flow-ui/share/man/man1" \
    "${STAGE_DIR}/opt/flow-ui/share/man/man5" \
    "${STAGE_DIR}/opt/flow-ui/share/info" \
    "${STAGE_DIR}/opt/flow-ui/share/doc" \
    "${STAGE_DIR}/opt/flow-ui/share/services/openbsd" \
    "${STAGE_DIR}/opt/flow-ui/share/examples/flow-ui" \
    "${STAGE_DIR}/opt/flow-ui/share/licenses" \
    "${STAGE_DIR}/etc/rc.d" \
    "${STAGE_DIR}/etc/profile.d" \
    "$META_DIR" \
    "$DIST_DIR"

publish_flow_ui_framework_dependent "${STAGE_DIR}/opt/flow-ui/app"
write_unix_launcher "${STAGE_DIR}/opt/flow-ui/bin" "framework-dependent"
stage_compressed_docs "$DOC_TMP"

# OpenBSD @man expects uncompressed roff man pages and manages mandoc.db automatically.
cp "${PACKAGING_DIR}/man/man1/flow-ui.1" "${STAGE_DIR}/opt/flow-ui/share/man/man1/flow-ui.1"
cp "${PACKAGING_DIR}/man/man5/flow-ui.env.5" "${STAGE_DIR}/opt/flow-ui/share/man/man5/flow-ui.env.5"
cp "${DOC_TMP}/info/flow-ui.info.gz" "${STAGE_DIR}/opt/flow-ui/share/info/flow-ui.info.gz"
cp "${DOC_TMP}/info/dir" "${STAGE_DIR}/opt/flow-ui/share/info/dir"

cp "${REPO_ROOT}/README.md" "${STAGE_DIR}/opt/flow-ui/share/doc/README.md"
cp "${REPO_ROOT}/DOCKER.md" "${STAGE_DIR}/opt/flow-ui/share/doc/DOCKER.md"

cp "${PACKAGING_DIR}/services/openbsd/flow_ui" "${STAGE_DIR}/opt/flow-ui/share/services/openbsd/flow_ui"
cp "${PACKAGING_DIR}/openbsd/+INSTALL" "${STAGE_DIR}/opt/flow-ui/share/services/openbsd/+INSTALL"
cp "${PACKAGING_DIR}/openbsd/+DEINSTALL" "${STAGE_DIR}/opt/flow-ui/share/services/openbsd/+DEINSTALL"
cp "${PACKAGING_DIR}/services/openbsd/flow_ui" "${STAGE_DIR}/etc/rc.d/flow_ui"

cp "${PACKAGING_DIR}/etc/flow-ui/flow-ui.env" "${STAGE_DIR}/opt/flow-ui/share/examples/flow-ui/flow-ui.env.sample"
cp "${PACKAGING_DIR}/etc/profile.d/flow-ui.sh" "${STAGE_DIR}/opt/flow-ui/share/examples/flow-ui/flow-ui.sh"
cp "${PACKAGING_DIR}/etc/profile.d/flow-ui.sh" "${STAGE_DIR}/etc/profile.d/flow-ui.sh"
cp "${REPO_ROOT}/LICENSE.txt" "${STAGE_DIR}/opt/flow-ui/share/licenses/LICENSE"

chmod 0755 \
    "${STAGE_DIR}/opt/flow-ui/share/services/openbsd/flow_ui" \
    "${STAGE_DIR}/opt/flow-ui/share/services/openbsd/+INSTALL" \
    "${STAGE_DIR}/opt/flow-ui/share/services/openbsd/+DEINSTALL"

chmod 0555 "${STAGE_DIR}/etc/rc.d/flow_ui"
chmod 0640 "${STAGE_DIR}/opt/flow-ui/share/examples/flow-ui/flow-ui.env.sample"

chmod 0644 \
    "${STAGE_DIR}/opt/flow-ui/share/man/man1/flow-ui.1" \
    "${STAGE_DIR}/opt/flow-ui/share/man/man5/flow-ui.env.5" \
    "${STAGE_DIR}/opt/flow-ui/share/info/flow-ui.info.gz" \
    "${STAGE_DIR}/opt/flow-ui/share/info/dir" \
    "${STAGE_DIR}/opt/flow-ui/share/doc/"*.md \
    "${STAGE_DIR}/opt/flow-ui/share/licenses/LICENSE" \
    "${STAGE_DIR}/opt/flow-ui/share/examples/flow-ui/flow-ui.sh" \
    "${STAGE_DIR}/etc/profile.d/flow-ui.sh"

cp "${PACKAGING_DIR}/openbsd/+DESC" "${META_DIR}/+DESC"

# Generate dynamic OpenBSD PLIST covering published /opt/flow-ui/app/** files
{
    printf '@comment $OpenBSD$\n'
    (
        cd "${STAGE_DIR}/opt/flow-ui"
        find app -type d | LC_ALL=C sort | sed 's|$|/|'
        find app -type f | LC_ALL=C sort
    )
    cat <<EOF
bin/
bin/flow-ui
share/
share/doc/
share/doc/DOCKER.md
share/doc/README.md
share/examples/
share/examples/flow-ui/
share/examples/flow-ui/flow-ui.env.sample
@sample /etc/profile.d/
share/examples/flow-ui/flow-ui.sh
@sample /etc/profile.d/flow-ui.sh
share/info/
share/info/dir
share/info/flow-ui.info.gz
share/licenses/
share/licenses/LICENSE
@mandir share/man
share/man/man1/
@man share/man/man1/flow-ui.1
share/man/man5/
@man share/man/man5/flow-ui.env.5
share/services/
share/services/openbsd/
@unexec-delete /bin/sh /opt/flow-ui/share/services/openbsd/+DEINSTALL ${FULLPKGNAME} DEINSTALL
share/services/openbsd/+DEINSTALL
share/services/openbsd/+INSTALL
share/services/openbsd/flow_ui
@exec /bin/sh /opt/flow-ui/share/services/openbsd/+INSTALL ${FULLPKGNAME} POST-INSTALL
@rcscript /etc/rc.d/flow_ui
EOF
} > "$PLIST_FILE"

PKG_CREATE_BIN=$(command -v pkg_create 2>/dev/null || true)
if [ -z "$PKG_CREATE_BIN" ] && [ -x /usr/sbin/pkg_create ]; then
    PKG_CREATE_BIN="/usr/sbin/pkg_create"
fi

if [ "$(uname -s)" = "OpenBSD" ] && [ -n "$PKG_CREATE_BIN" ]; then
    OUT_DIR="${DIST_DIR}/openbsd-${OBSD_ARCH}"
    mkdir -p "$OUT_DIR"
    OUT_PKG="${OUT_DIR}/${FULLPKGNAME}.tgz"

    log_info "Building native OpenBSD package (${OBSD_ARCH}) via pkg_create..."
    "$PKG_CREATE_BIN" \
        -A "$OBSD_ARCH" \
        -B "$STAGE_DIR" \
        -d "${META_DIR}/+DESC" \
        -D COMMENT="ETL Madness FlowUI Blazor Server Studio (/opt/flow-ui)" \
        -D FULLPKGPATH="www/${PKG_NAME}" \
        -D MAINTAINER="${MAINTAINER}" \
        -D HOMEPAGE="${HOMEPAGE}" \
        -D FTP="yes" \
        -f "$PLIST_FILE" \
        -p "/opt/flow-ui" \
        "$OUT_PKG"

    if [ -n "${OPENBSD_SIGNIFY_KEY:-}" ]; then
        PKG_SIGN_BIN=$(command -v pkg_sign 2>/dev/null || true)
        [ -z "$PKG_SIGN_BIN" ] && [ -x /usr/sbin/pkg_sign ] && PKG_SIGN_BIN="/usr/sbin/pkg_sign"
        if [ -z "$PKG_SIGN_BIN" ]; then
            log_err "OPENBSD_SIGNIFY_KEY was set, but pkg_sign was not found."
            exit 1
        fi
        SIGNED_DIR="${WORK_DIR}/signed"
        mkdir -p "$SIGNED_DIR"
        log_info "Signing OpenBSD package with signify2 key ${OPENBSD_SIGNIFY_KEY}..."
        "$PKG_SIGN_BIN" -s signify2 -s "$OPENBSD_SIGNIFY_KEY" -o "$SIGNED_DIR" "$OUT_PKG"
        cp "${SIGNED_DIR}/${FULLPKGNAME}.tgz" "$OUT_PKG"
    fi
else
    OUT_PKG="${DIST_DIR}/${PKG_NAME}-${FLOW_VERSION}-openbsd-${OBSD_ARCH}-bundle.tar.gz"
    log_info "OpenBSD pkg_create not present on host; creating cross-published root bundle: ${OUT_PKG}"
    tar -czf "$OUT_PKG" -C "$STAGE_DIR" opt etc
fi

log_info "Created OpenBSD package: ${OUT_PKG}"
