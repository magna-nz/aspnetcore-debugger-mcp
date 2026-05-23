# macOS Apple Silicon — building netcoredbg

Samsung doesn't publish an arm64 macOS prebuilt of netcoredbg. The repo includes a script that
builds it from source.

## Prerequisites

- Xcode Command Line Tools — `xcode-select --install`
- CMake — `brew install cmake`
- .NET 10 SDK — [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download)
- `git`

## Run the script

```bash
./scripts/build-netcoredbg-macos-arm64.sh
```

What it does:

1. Clones `Samsung/netcoredbg` (shallow) to `~/projects/netcoredbg-src` (override with a first arg)
2. Configures via CMake with the modern-CMake policy override (`-DCMAKE_POLICY_VERSION_MINIMUM=3.5`)
3. Builds with all cores (`make -j$(sysctl -n hw.ncpu)`)
4. Installs into `<src>/bin/`
5. Prints the `NETCOREDBG_PATH` to export

When it finishes, you'll see:

```
Built: /Users/you/projects/netcoredbg-src/bin/netcoredbg

Add this to your shell profile (and current shell):
    export NETCOREDBG_PATH=/Users/you/projects/netcoredbg-src/bin/netcoredbg
```

## Is it stable?

Samsung's docs say the arm64 macOS build is "community supported and may not work as expected."
In our testing on Apple Silicon, every debugger feature used by this project works correctly,
**except** `dataBreakpointInfo` (returns `E_NOTIMPL`) — see [Known limits](limits.md).

The validated-on-arm64 functionality includes: launch, attach, line/function/exception
breakpoints (with conditions, hit counts, logpoints), step in/over/out, threads, stack traces,
scopes, variables, evaluate, setExpression, exceptionInfo, output events.

## Why a script instead of a prebuilt binary in this repo?

Distributing a prebuilt unsigned binary in a Git repo brings up signing / notarization /
gatekeeper friction on Apple Silicon. The build script is small, deterministic, and uses your
own toolchain — much cleaner than shipping bytes someone else has to trust.

If we ever need a quicker install path, the right move is to build the netcoredbg arm64 binary
in CI and attach it to GitHub Releases — then the MCP server's auto-discover code can fall back
to a downloaded binary. Not done yet; happy to add it if it's annoying enough.
