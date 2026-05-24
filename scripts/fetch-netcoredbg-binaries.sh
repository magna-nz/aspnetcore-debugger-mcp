#!/usr/bin/env bash
# Downloads prebuilt netcoredbg binaries for all supported RIDs and stages them under
# src/AspNetCoreDebuggerMcp/runtimes/<rid>/native/ for inclusion in the NuGet pack.
#
# Pinned version: NETCOREDBG_VERSION below. Bump deliberately when needed.
#
# RIDs:
#   linux-x64, linux-arm64, win-x64, osx-x64 → Samsung official prebuilts
#   osx-arm64                                → our own build (Samsung doesn't ship one),
#                                               hosted as a GitHub release asset on this repo
#
# Usage:
#   scripts/fetch-netcoredbg-binaries.sh
#
# Env vars (override defaults):
#   NETCOREDBG_VERSION       Samsung release tag (default: 3.1.3-1062)
#   OSX_ARM64_BUNDLE_URL     URL to our osx-arm64 tarball (default: GitHub release asset on this repo)

set -euo pipefail

NETCOREDBG_VERSION="${NETCOREDBG_VERSION:-3.1.3-1062}"
SAMSUNG_BASE="https://github.com/Samsung/netcoredbg/releases/download/${NETCOREDBG_VERSION}"
OSX_ARM64_BUNDLE_URL="${OSX_ARM64_BUNDLE_URL:-https://github.com/magna-nz/aspnetcore-debugger-mcp/releases/download/vendor-netcoredbg-${NETCOREDBG_VERSION}/netcoredbg-osx-arm64.tar.gz}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
RUNTIMES_DIR="${REPO_ROOT}/src/AspNetCoreDebuggerMcp/runtimes"
TMP_DIR="$(mktemp -d)"
trap 'rm -rf "${TMP_DIR}"' EXIT

log() { echo "[fetch-netcoredbg] $*"; }

# fetch_and_stage <rid> <asset_url> <archive_kind: tar.gz|zip>
fetch_and_stage() {
    local rid="$1"
    local url="$2"
    local kind="$3"
    local dest="${RUNTIMES_DIR}/${rid}/native"

    log "Fetching ${rid} from ${url}"
    rm -rf "${dest}"
    mkdir -p "${dest}"

    local archive="${TMP_DIR}/${rid}.${kind}"
    if ! curl -fSL "${url}" -o "${archive}"; then
        echo "ERROR: download failed for ${rid}: ${url}" >&2
        return 1
    fi

    local extract_dir="${TMP_DIR}/${rid}-extracted"
    mkdir -p "${extract_dir}"
    case "${kind}" in
        tar.gz) tar -xzf "${archive}" -C "${extract_dir}" ;;
        zip)    unzip -q "${archive}" -d "${extract_dir}" ;;
        *)      echo "ERROR: unknown archive kind: ${kind}" >&2; return 1 ;;
    esac

    # Samsung tarballs and our osx-arm64 tarball wrap files in a netcoredbg/ folder.
    local inner="${extract_dir}/netcoredbg"
    if [[ ! -d "${inner}" ]]; then
        echo "ERROR: expected ${inner}/ inside ${archive}" >&2
        ls -la "${extract_dir}" >&2
        return 1
    fi

    cp -R "${inner}/." "${dest}/"

    # Ensure native binary is executable on Unix-like platforms.
    if [[ "${rid}" != win-* ]] && [[ -f "${dest}/netcoredbg" ]]; then
        chmod +x "${dest}/netcoredbg"
    fi
}

main() {
    mkdir -p "${RUNTIMES_DIR}"

    fetch_and_stage "linux-x64"   "${SAMSUNG_BASE}/netcoredbg-linux-amd64.tar.gz" tar.gz
    fetch_and_stage "linux-arm64" "${SAMSUNG_BASE}/netcoredbg-linux-arm64.tar.gz" tar.gz
    fetch_and_stage "osx-x64"     "${SAMSUNG_BASE}/netcoredbg-osx-amd64.tar.gz"  tar.gz
    fetch_and_stage "win-x64"     "${SAMSUNG_BASE}/netcoredbg-win64.zip"          zip
    fetch_and_stage "osx-arm64"   "${OSX_ARM64_BUNDLE_URL}"                       tar.gz

    log "Verifying all 5 RIDs are populated…"
    local missing=0
    for rid in linux-x64 linux-arm64 osx-x64 osx-arm64; do
        if [[ ! -x "${RUNTIMES_DIR}/${rid}/native/netcoredbg" ]]; then
            echo "MISSING: ${RUNTIMES_DIR}/${rid}/native/netcoredbg" >&2
            missing=1
        fi
    done
    if [[ ! -f "${RUNTIMES_DIR}/win-x64/native/netcoredbg.exe" ]]; then
        echo "MISSING: ${RUNTIMES_DIR}/win-x64/native/netcoredbg.exe" >&2
        missing=1
    fi
    if [[ "${missing}" -ne 0 ]]; then
        exit 1
    fi

    log "Done. netcoredbg ${NETCOREDBG_VERSION} staged under ${RUNTIMES_DIR}"
}

main "$@"
