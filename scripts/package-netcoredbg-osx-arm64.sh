#!/usr/bin/env bash
# Packages a locally-built netcoredbg (macOS arm64) into a tarball matching
# Samsung's official tarball layout so it can be uploaded as a GitHub release asset
# and consumed by scripts/fetch-netcoredbg-binaries.sh.
#
# Prerequisites:
#   - Apple Silicon (arm64) macOS
#   - netcoredbg already built — defaults to ~/projects/netcoredbg-src/build,
#     override with NETCOREDBG_BUILD_DIR.
#
# Produces: ./netcoredbg-osx-arm64.tar.gz containing:
#   netcoredbg/netcoredbg
#   netcoredbg/ManagedPart.dll
#   netcoredbg/libdbgshim.dylib
#   netcoredbg/Microsoft.CodeAnalysis.*.dll
#
# Upload manually:
#   gh release create vendor-netcoredbg-3.1.3-1062 netcoredbg-osx-arm64.tar.gz \
#     --title "Bundled netcoredbg vendor binaries (3.1.3-1062)" \
#     --notes "macOS arm64 build of Samsung/netcoredbg ${NETCOREDBG_VERSION}, since Samsung does not ship a prebuilt for this RID."

set -euo pipefail

NETCOREDBG_BUILD_DIR="${NETCOREDBG_BUILD_DIR:-$HOME/projects/netcoredbg-src/build}"
OUT_DIR="${OUT_DIR:-$(pwd)}"

if [[ "$(uname -s)" != "Darwin" ]] || [[ "$(uname -m)" != "arm64" ]]; then
    echo "This script must run on Apple Silicon macOS (arm64)." >&2
    exit 1
fi

SRC_DIR="${NETCOREDBG_BUILD_DIR}/src"
BIN="${SRC_DIR}/netcoredbg"

if [[ ! -x "${BIN}" ]]; then
    echo "netcoredbg binary not found at ${BIN}" >&2
    echo "Build it first: scripts/build-netcoredbg-macos-arm64.sh" >&2
    exit 1
fi

STAGE_DIR="$(mktemp -d)"
trap 'rm -rf "${STAGE_DIR}"' EXIT

INNER="${STAGE_DIR}/netcoredbg"
mkdir -p "${INNER}"

cp "${BIN}" "${INNER}/netcoredbg"

# Required runtime dependencies — mirror what Samsung's official tarball contains.
for f in \
    ManagedPart.dll \
    Microsoft.CodeAnalysis.dll \
    Microsoft.CodeAnalysis.CSharp.dll \
    Microsoft.CodeAnalysis.Scripting.dll \
    Microsoft.CodeAnalysis.CSharp.Scripting.dll; do
    if [[ -f "${SRC_DIR}/${f}" ]]; then
        cp "${SRC_DIR}/${f}" "${INNER}/${f}"
    else
        echo "WARNING: ${SRC_DIR}/${f} not found — skipping" >&2
    fi
done

# dbgshim ships as libdbgshim.dylib on macOS; find wherever it landed.
DBGSHIM=$(find "${NETCOREDBG_BUILD_DIR}" -name "libdbgshim.dylib" -print -quit 2>/dev/null || true)
if [[ -n "${DBGSHIM}" ]] && [[ -f "${DBGSHIM}" ]]; then
    cp "${DBGSHIM}" "${INNER}/libdbgshim.dylib"
else
    echo "WARNING: libdbgshim.dylib not found in build tree" >&2
fi

chmod +x "${INNER}/netcoredbg"

OUT="${OUT_DIR}/netcoredbg-osx-arm64.tar.gz"
tar -czf "${OUT}" -C "${STAGE_DIR}" netcoredbg

echo "Created: ${OUT}"
echo "Contents:"
tar -tzf "${OUT}" | sed 's/^/  /'
