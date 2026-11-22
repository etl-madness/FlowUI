#!/bin/sh
# Builds an RPM (.rpm) package for ETL Madness FlowUI (RHEL, Fedora, SUSE, openSUSE).

set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
# shellcheck source=packaging/scripts/common.sh
. "${SCRIPT_DIR}/common.sh"

RAW_VERSION=$(resolve_version)
# RPM Version field forbids '-'; replace hyphens with '~' for pre-releases.
RPM_VERSION=$(printf '%s' "$RAW_VERSION" | tr '-' '~')
ARCH_TARGET=$(normalize_arch "${ARCH:-${1:-}}")

case "$ARCH_TARGET" in
    amd64) RPM_ARCH="x86_64" ;;
    arm64) RPM_ARCH="aarch64" ;;
    *)
        log_err "Unsupported .rpm architecture: $ARCH_TARGET"
        exit 1
        ;;
esac

WORK_DIR="${BUILD_ROOT}/rpm-${RPM_ARCH}"
STAGE_SRC="${WORK_DIR}/stage"
TOP_DIR="${WORK_DIR}/rpmbuild"

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
    "${TOP_DIR}/BUILD" \
    "${TOP_DIR}/BUILDROOT" \
    "${TOP_DIR}/RPMS" \
    "${TOP_DIR}/SOURCES" \
    "${TOP_DIR}/SPECS" \
    "${TOP_DIR}/SRPMS" \
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

if command -v rpmbuild >/dev/null 2>&1; then
    TARBALL="${TOP_DIR}/SOURCES/flow-ui-${RPM_VERSION}-staged.tar.gz"
    (
        cd "$STAGE_SRC"
        tar -czf "$TARBALL" app bin doc guides services profile.d etc-env LICENSE.txt
    )
    cp "${PACKAGING_DIR}/rpm/flow-ui.spec" "${TOP_DIR}/SPECS/flow-ui.spec"

    log_info "Building .rpm package with rpmbuild (${RPM_ARCH})..."
    rpmbuild -bb \
        --target "$RPM_ARCH" \
        --define "_topdir ${TOP_DIR}" \
        --define "_flow_version ${RPM_VERSION}" \
        --define "_flow_release ${FLOW_RELEASE}" \
        "${TOP_DIR}/SPECS/flow-ui.spec"

    BUILT_RPM=$(find "${TOP_DIR}/RPMS" -name '*.rpm' -type f | head -n 1)
    if [ -z "$BUILT_RPM" ]; then
        log_err "rpmbuild succeeded but no .rpm artifact was found in ${TOP_DIR}/RPMS"
        exit 1
    fi
    OUT_RPM="${DIST_DIR}/$(basename "$BUILT_RPM")"
    cp "$BUILT_RPM" "$OUT_RPM"
elif command -v nfpm >/dev/null 2>&1; then
    log_info "rpmbuild not found; falling back to nfpm..."
    OUT_RPM="${DIST_DIR}/${PKG_NAME}-${RPM_VERSION}-${FLOW_RELEASE}.${RPM_ARCH}.rpm"
    REPO_ROOT="$REPO_ROOT" \
    PKG_NAME="$PKG_NAME" \
    STAGE_APP_DIR="${STAGE_SRC}/app" \
    STAGE_BIN_DIR="${STAGE_SRC}/bin" \
    STAGE_DOC_DIR="${STAGE_SRC}/doc" \
    NFPM_ARCH="$ARCH_TARGET" \
    FLOW_VERSION="$RPM_VERSION" \
    FLOW_RELEASE="$FLOW_RELEASE" \
        nfpm package --config "${PACKAGING_DIR}/nfpm.yaml" --packager rpm --target "$OUT_RPM"
else
    log_err "Neither rpmbuild nor nfpm is installed. Install rpm-build or nfpm to build .rpm packages."
    exit 1
fi

log_info "Created RPM package: ${OUT_RPM}"
