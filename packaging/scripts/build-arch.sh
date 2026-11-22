#!/bin/sh
# Builds an Arch Linux (.pkg.tar.zst) package for ETL Madness FlowUI.

set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
# shellcheck source=packaging/scripts/common.sh
. "${SCRIPT_DIR}/common.sh"

RAW_VERSION=$(resolve_version)
# Arch pkgver forbids hyphens '-'; replace hyphens with underscores '_'.
ARCH_VERSION=$(printf '%s' "$RAW_VERSION" | tr '-' '_')
ARCH_TARGET=$(normalize_arch "${ARCH:-${1:-}}")

case "$ARCH_TARGET" in
    amd64) ARCH_CARCH="x86_64" ;;
    arm64) ARCH_CARCH="aarch64" ;;
    *)
        log_err "Unsupported Arch Linux architecture: $ARCH_TARGET"
        exit 1
        ;;
esac

WORK_DIR="${BUILD_ROOT}/arch-${ARCH_CARCH}"
STAGE_SRC="${WORK_DIR}/stage"
MAKEPKG_DIR="${WORK_DIR}/makepkg"
OUT_PKG="${DIST_DIR}/${PKG_NAME}-${ARCH_VERSION}-${FLOW_RELEASE}-${ARCH_CARCH}.pkg.tar.zst"

rm -rf "$WORK_DIR"
mkdir -p \
    "${STAGE_SRC}/app" \
    "${STAGE_SRC}/bin" \
    "${STAGE_SRC}/doc" \
    "${STAGE_SRC}/guides" \
    "${STAGE_SRC}/services/systemd" \
    "${STAGE_SRC}/services/openrc" \
    "${STAGE_SRC}/services/sysvinit" \
    "${STAGE_SRC}/profile.d" \
    "${STAGE_SRC}/etc-env" \
    "$MAKEPKG_DIR" \
    "$DIST_DIR"

publish_flow_ui_self_contained "linux" "$ARCH_TARGET" "${STAGE_SRC}/app"
write_unix_launcher "${STAGE_SRC}/bin" "self-contained"
stage_compressed_docs "${STAGE_SRC}/doc"

cp "${REPO_ROOT}/README.md" "${STAGE_SRC}/guides/README.md"
cp "${REPO_ROOT}/DOCKER.md" "${STAGE_SRC}/guides/DOCKER.md"

cp "${PACKAGING_DIR}/services/systemd/flow-ui.service" "${STAGE_SRC}/services/systemd/flow-ui.service"
cp "${PACKAGING_DIR}/services/openrc/flow-ui" "${STAGE_SRC}/services/openrc/flow-ui"
cp "${PACKAGING_DIR}/services/sysvinit/flow-ui" "${STAGE_SRC}/services/sysvinit/flow-ui"

cp "${REPO_ROOT}/LICENSE.txt" "${STAGE_SRC}/LICENSE.txt"
cp "${PACKAGING_DIR}/etc/profile.d/flow-ui.sh" "${STAGE_SRC}/profile.d/flow-ui.sh"
cp "${PACKAGING_DIR}/etc/flow-ui/flow-ui.env" "${STAGE_SRC}/etc-env/flow-ui.env"

build_with_nfpm() {
    REPO_ROOT="$REPO_ROOT" \
    PKG_NAME="$PKG_NAME" \
    STAGE_APP_DIR="${STAGE_SRC}/app" \
    STAGE_BIN_DIR="${STAGE_SRC}/bin" \
    STAGE_DOC_DIR="${STAGE_SRC}/doc" \
    NFPM_ARCH="$ARCH_TARGET" \
    FLOW_VERSION="$ARCH_VERSION" \
    FLOW_RELEASE="$FLOW_RELEASE" \
        nfpm package --config "${PACKAGING_DIR}/nfpm.yaml" --packager archlinux --target "$OUT_PKG"
}

if command -v makepkg >/dev/null 2>&1 && [ "$(id -u)" -ne 0 ]; then
    TARBALL="${MAKEPKG_DIR}/flow-ui-${ARCH_VERSION}-staged.tar.gz"
    (
        cd "$STAGE_SRC"
        tar -czf "$TARBALL" app bin doc guides services profile.d etc-env LICENSE.txt
    )
    sed \
        -e "s/__PKGVER__/${ARCH_VERSION}/g" \
        -e "s/__PKGREL__/${FLOW_RELEASE}/g" \
        -e "s/__CARCH__/${ARCH_CARCH}/g" \
        "${PACKAGING_DIR}/arch/PKGBUILD.tmpl" > "${MAKEPKG_DIR}/PKGBUILD"
    cp "${PACKAGING_DIR}/arch/flow-ui.install" "${MAKEPKG_DIR}/flow-ui.install"

    log_info "Building Arch Linux package with makepkg (${ARCH_CARCH})..."
    (
        cd "$MAKEPKG_DIR"
        CARCH="$ARCH_CARCH" PKGEXT='.pkg.tar.zst' makepkg -f --nodeps
    )

    BUILT_PKG=$(find "$MAKEPKG_DIR" -name '*.pkg.tar.*' -type f | head -n 1)
    if [ -z "$BUILT_PKG" ]; then
        log_err "makepkg succeeded but no .pkg.tar.* file was found in ${MAKEPKG_DIR}"
        exit 1
    fi
    cp "$BUILT_PKG" "$OUT_PKG"
elif command -v nfpm >/dev/null 2>&1; then
    if command -v makepkg >/dev/null 2>&1 && [ "$(id -u)" -eq 0 ]; then
        log_warn "Running as root (makepkg disallows root); building Arch package via nfpm..."
    else
        log_info "makepkg not found; falling back to nfpm..."
    fi
    build_with_nfpm
else
    if command -v makepkg >/dev/null 2>&1 && [ "$(id -u)" -eq 0 ]; then
        log_err "makepkg cannot be run as root. Run build-arch.sh as a non-root user, or install nfpm."
    else
        log_err "Neither makepkg nor nfpm is installed. Install pacman/makepkg (on Arch) or nfpm to build .pkg.tar.zst packages."
    fi
    exit 1
fi

log_info "Created Arch Linux package: ${OUT_PKG}"
