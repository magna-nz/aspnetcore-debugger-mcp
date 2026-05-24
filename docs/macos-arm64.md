# macOS Apple Silicon — building netcoredbg (advanced)

**You don't need this for normal use** — the AspNetCoreDebuggerMcp NuGet package bundles a prebuilt
arm64 macOS netcoredbg binary that the tool resolves automatically. This page is only relevant if
you want to build your own (e.g. to track a newer Samsung release than what we ship, or for development).

Samsung doesn't publish an arm64 macOS prebuilt of netcoredbg, so the bundled binary is our own
build. This repo includes the same script we use to produce it.

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

## How this is distributed

We run this build script once per netcoredbg release, package the result into a tarball matching
Samsung's official tarball layout (`scripts/package-netcoredbg-osx-arm64.sh`), and upload it as a
GitHub release asset on a `vendor-netcoredbg-<version>` tag. The release workflow's
`scripts/fetch-netcoredbg-binaries.sh` pulls this tarball along with the four Samsung-prebuilt
RIDs, and `dotnet pack` includes them all under `runtimes/<rid>/native/` in the NuGet package.

End users get the bundled binary automatically — they never have to run this script.
