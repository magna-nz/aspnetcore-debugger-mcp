#!/usr/bin/env bash
# Builds netcoredbg from source for native macOS arm64 (Apple Silicon).
#
# Why: Samsung doesn't publish an arm64 macOS prebuilt. They flag the arm64
# macOS build as "community supported"; it builds cleanly and works for the
# functionality this MCP server exercises.
#
# Prerequisites:
#   - Xcode Command Line Tools (`xcode-select --install`)
#   - CMake (`brew install cmake`)
#   - .NET 10 SDK
#
# Output: prints the absolute path of the built binary and the
# NETCOREDBG_PATH export to copy into your shell.

set -euo pipefail

DEST="${1:-$HOME/projects/netcoredbg-src}"

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "This script is for macOS only." >&2
  exit 1
fi
if [[ "$(uname -m)" != "arm64" ]]; then
  echo "This script is for Apple Silicon (arm64). On Intel macOS use Samsung's prebuilt." >&2
  exit 1
fi

for cmd in git cmake clang make dotnet; do
  if ! command -v "$cmd" >/dev/null 2>&1; then
    echo "Required command not found: $cmd" >&2
    exit 1
  fi
done

if [[ ! -d "$DEST" ]]; then
  echo "Cloning Samsung/netcoredbg into $DEST..."
  git clone --depth 1 https://github.com/Samsung/netcoredbg.git "$DEST"
else
  echo "Using existing checkout at $DEST"
fi

cd "$DEST"
rm -rf build
mkdir build
cd build

# -DCMAKE_POLICY_VERSION_MINIMUM=3.5 lets newer CMake accept older
# cmake_minimum_required statements in netcoredbg's tree.
echo "Configuring..."
CC=clang CXX=clang++ cmake .. \
  -DCMAKE_INSTALL_PREFIX="$PWD/../bin" \
  -DCMAKE_POLICY_VERSION_MINIMUM=3.5

echo "Building..."
make -j"$(sysctl -n hw.ncpu)"

echo "Installing..."
make install

BIN="$DEST/bin/netcoredbg"
if [[ ! -x "$BIN" ]]; then
  echo "Build appeared to succeed but no binary at $BIN" >&2
  exit 1
fi

echo
echo "Built: $BIN"
echo
echo "Add this to your shell profile (and current shell):"
echo "    export NETCOREDBG_PATH=$BIN"
