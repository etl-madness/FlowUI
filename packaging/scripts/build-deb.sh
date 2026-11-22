#!/bin/sh
# Builds a Debian/Ubuntu .deb package for ETL Madness FlowUI.
# Installs self-contained .NET 10 FlowUI app (/opt/flow-ui/app), launcher (/opt/flow-ui/bin/flow-ui),
# man pages, GNU Info manual, reference guides, and systemd/OpenRC/SysVinit service definitions,
# plus /usr/lib/systemd/system/flow-ui.service, /etc/flow-ui/flow-ui.env, and /etc/profile.d/flow-ui.sh.

set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
# shellcheck source=packaging/scripts/common.sh
. "${SCRIPT_DIR}/common.sh"

FLOW_VERSION=$(resolve_version)
ARCH_TARGET=$(normalize_arch "${ARCH:-${1:-}}")

case "$ARCH_TARGET" in
    amd64) DEB_ARCH="amd64" ;;
    arm64) DEB_ARCH="arm64" ;;
    *)
        log_err "Unsupported .deb architecture: $ARCH_TARGET"
        exit 1
        ;;
esac

WORK_DIR="${BUILD_ROOT}/deb-${DEB_ARCH}"
STAGE_DIR="${WORK_DIR}/root"
DOC_TMP="${WORK_DIR}/docs"
OUT_DEB="${DIST_DIR}/${PKG_NAME}_${FLOW_VERSION}-${FLOW_RELEASE}_${DEB_ARCH}.deb"

rm -rf "$WORK_DIR"
mkdir -p \
    "${STAGE_DIR}/DEBIAN" \
    "${STAGE_DIR}/opt/flow-ui/app" \
    "${STAGE_DIR}/opt/flow-ui/bin" \
    "${STAGE_DIR}/opt/flow-ui/share/man/man1" \
    "${STAGE_DIR}/opt/flow-ui/share/man/man5" \
    "${STAGE_DIR}/opt/flow-ui/share/info" \
    "${STAGE_DIR}/opt/flow-ui/share/doc" \
    "${STAGE_DIR}/opt/flow-ui/share/services/systemd" \
    "${STAGE_DIR}/opt/flow-ui/share/services/openrc" \
    "${STAGE_DIR}/opt/flow-ui/share/services/sysvinit" \
    "${STAGE_DIR}/opt/flow-ui/share/licenses" \
    "${STAGE_DIR}/usr/lib/systemd/system" \
    "${STAGE_DIR}/usr/share/doc/${PKG_NAME}" \
    "${STAGE_DIR}/etc/flow-ui" \
    "${STAGE_DIR}/etc/profile.d" \
    "${STAGE_DIR}/var/lib/flow-ui" \
    "${STAGE_DIR}/var/log/flow-ui" \
    "$DIST_DIR"

publish_flow_ui_self_contained "linux" "$ARCH_TARGET" "${STAGE_DIR}/opt/flow-ui/app"
write_unix_launcher "${STAGE_DIR}/opt/flow-ui/bin" "self-contained"
stage_compressed_docs "$DOC_TMP"

cp "${DOC_TMP}/man1/"*.1.gz "${STAGE_DIR}/opt/flow-ui/share/man/man1/"
cp "${DOC_TMP}/man5/"*.5.gz "${STAGE_DIR}/opt/flow-ui/share/man/man5/"
cp "${DOC_TMP}/info/flow-ui.info.gz" "${STAGE_DIR}/opt/flow-ui/share/info/flow-ui.info.gz"
cp "${DOC_TMP}/info/dir" "${STAGE_DIR}/opt/flow-ui/share/info/dir"

cp "${REPO_ROOT}/README.md" "${STAGE_DIR}/opt/flow-ui/share/doc/README.md"
cp "${REPO_ROOT}/DOCKER.md" "${STAGE_DIR}/opt/flow-ui/share/doc/DOCKER.md"

cp "${PACKAGING_DIR}/services/systemd/flow-ui.service" "${STAGE_DIR}/opt/flow-ui/share/services/systemd/flow-ui.service"
cp "${PACKAGING_DIR}/services/openrc/flow-ui" "${STAGE_DIR}/opt/flow-ui/share/services/openrc/flow-ui"
cp "${PACKAGING_DIR}/services/sysvinit/flow-ui" "${STAGE_DIR}/opt/flow-ui/share/services/sysvinit/flow-ui"
cp "${PACKAGING_DIR}/services/systemd/flow-ui.service" "${STAGE_DIR}/usr/lib/systemd/system/flow-ui.service"

cp "${REPO_ROOT}/LICENSE.txt" "${STAGE_DIR}/opt/flow-ui/share/licenses/LICENSE"
cp "${REPO_ROOT}/LICENSE.txt" "${STAGE_DIR}/usr/share/doc/${PKG_NAME}/copyright"
cp "${PACKAGING_DIR}/etc/profile.d/flow-ui.sh" "${STAGE_DIR}/etc/profile.d/flow-ui.sh"
cp "${PACKAGING_DIR}/etc/flow-ui/flow-ui.env" "${STAGE_DIR}/etc/flow-ui/flow-ui.env"

cp "${PACKAGING_DIR}/debian/postinst" "${STAGE_DIR}/DEBIAN/postinst"
cp "${PACKAGING_DIR}/debian/prerm" "${STAGE_DIR}/DEBIAN/prerm"
cp "${PACKAGING_DIR}/debian/postrm" "${STAGE_DIR}/DEBIAN/postrm"

chmod 0755 \
    "${STAGE_DIR}/opt/flow-ui/share/services/openrc/flow-ui" \
    "${STAGE_DIR}/opt/flow-ui/share/services/sysvinit/flow-ui" \
    "${STAGE_DIR}/DEBIAN/postinst" \
    "${STAGE_DIR}/DEBIAN/prerm" \
    "${STAGE_DIR}/DEBIAN/postrm"

chmod 0750 \
    "${STAGE_DIR}/etc/flow-ui" \
    "${STAGE_DIR}/var/lib/flow-ui" \
    "${STAGE_DIR}/var/log/flow-ui"

chmod 0640 "${STAGE_DIR}/etc/flow-ui/flow-ui.env"

chmod 0644 \
    "${STAGE_DIR}/opt/flow-ui/share/man/man1/"*.1.gz \
    "${STAGE_DIR}/opt/flow-ui/share/man/man5/"*.5.gz \
    "${STAGE_DIR}/opt/flow-ui/share/info/flow-ui.info.gz" \
    "${STAGE_DIR}/opt/flow-ui/share/info/dir" \
    "${STAGE_DIR}/opt/flow-ui/share/doc/"*.md \
    "${STAGE_DIR}/opt/flow-ui/share/services/systemd/flow-ui.service" \
    "${STAGE_DIR}/usr/lib/systemd/system/flow-ui.service" \
    "${STAGE_DIR}/opt/flow-ui/share/licenses/LICENSE" \
    "${STAGE_DIR}/usr/share/doc/${PKG_NAME}/copyright" \
    "${STAGE_DIR}/etc/profile.d/flow-ui.sh"

INSTALLED_SIZE=$(du -sk "${STAGE_DIR}/opt" "${STAGE_DIR}/usr" "${STAGE_DIR}/etc" | awk '{s+=$1} END {print s}')

cat > "${STAGE_DIR}/DEBIAN/control" <<EOF
Package: ${PKG_NAME}
Version: ${FLOW_VERSION}-${FLOW_RELEASE}
Section: web
Priority: optional
Architecture: ${DEB_ARCH}
Installed-Size: ${INSTALLED_SIZE}
Maintainer: ${MAINTAINER}
Homepage: ${HOMEPAGE}
Description: ETL Madness FlowUI — Flow Builder Studio (.NET 10 Blazor Server) (/opt/flow-ui)
 Interactive .NET 10 ASP.NET Core Blazor Server web application for visually
 designing, configuring, signing, encrypting, validating, executing, and
 monitoring XML ETL pipelines powered by go-flow and go-flowAPI:
  - /opt/flow-ui/app: Self-contained .NET 10 Blazor Server runtime and assets.
  - /opt/flow-ui/bin/flow-ui: CLI launcher with automatic --contentRoot setup.
  - /usr/lib/systemd/system/flow-ui.service: Native systemd unit configured
    via /etc/flow-ui/flow-ui.env (plus OpenRC and SysVinit scripts).
EOF

cat > "${STAGE_DIR}/DEBIAN/conffiles" <<'EOF'
/etc/flow-ui/flow-ui.env
/etc/profile.d/flow-ui.sh
EOF

chmod 0644 "${STAGE_DIR}/DEBIAN/control" "${STAGE_DIR}/DEBIAN/conffiles"

(
    cd "$STAGE_DIR"
    if command -v md5sum >/dev/null 2>&1; then
        find opt usr etc -type f -exec md5sum {} + > DEBIAN/md5sums
    elif command -v md5 >/dev/null 2>&1; then
        find opt usr etc -type f -exec md5 -r {} + | awk '{sum=$1; $1=""; sub(/^ /, ""); print sum "  " $0}' > DEBIAN/md5sums
    fi
)
[ -f "${STAGE_DIR}/DEBIAN/md5sums" ] && chmod 0644 "${STAGE_DIR}/DEBIAN/md5sums"

if command -v dpkg-deb >/dev/null 2>&1; then
    log_info "Building .deb package with dpkg-deb..."
    if dpkg-deb --help 2>&1 | grep -q -- '--root-owner-group'; then
        dpkg-deb --root-owner-group -Zgzip --build "$STAGE_DIR" "$OUT_DEB"
    elif command -v fakeroot >/dev/null 2>&1; then
        fakeroot dpkg-deb -Zgzip --build "$STAGE_DIR" "$OUT_DEB"
    else
        dpkg-deb -Zgzip --build "$STAGE_DIR" "$OUT_DEB"
    fi
elif command -v nfpm >/dev/null 2>&1; then
    log_info "dpkg-deb not found; falling back to nfpm..."
    REPO_ROOT="$REPO_ROOT" \
    PKG_NAME="$PKG_NAME" \
    STAGE_APP_DIR="${STAGE_DIR}/opt/flow-ui/app" \
    STAGE_BIN_DIR="${STAGE_DIR}/opt/flow-ui/bin" \
    STAGE_DOC_DIR="$DOC_TMP" \
    NFPM_ARCH="$DEB_ARCH" \
    FLOW_VERSION="$FLOW_VERSION" \
    FLOW_RELEASE="$FLOW_RELEASE" \
        nfpm package --config "${PACKAGING_DIR}/nfpm.yaml" --packager deb --target "$OUT_DEB"
else
    log_err "Neither dpkg-deb nor nfpm is installed. Install dpkg-deb (dpkg) or nfpm to build .deb packages."
    exit 1
fi

log_info "Created Debian package: ${OUT_DEB}"
