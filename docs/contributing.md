# Contributing / building from source

## Build & test

```bash
git clone https://github.com/magna-nz/aspnetcore-debugger-mcp.git
cd aspnetcore-debugger-mcp
dotnet build
dotnet test

# Fetch bundled netcoredbg binaries (one-time per checkout, ~30s) so the tool
# can run end-to-end against a real target.
bash scripts/fetch-netcoredbg-binaries.sh
dotnet run --project src/AspNetCoreDebuggerMcp
```

Requires .NET 10 SDK. The unit tests don't need netcoredbg (locator resolution is mocked); only
end-to-end runs do.

## Project layout

```
src/AspNetCoreDebuggerMcp/
├── Program.cs              # Entry point, DI, MCP server config
├── Dap/                    # Hand-rolled DAP client (Content-Length framing,
│                           #   request/response correlation, event dispatch)
├── Debugging/              # DebugSession, DebugSessionManager, NetcoredbgProcess,
│                           #   SessionStateMachine, StopWaiter
├── Breakpoints/            # BreakpointRegistry, record types
├── Inspection/             # InspectionService (threads/stack/scopes/variables/
│                           #   evaluate/setExpression/exceptionInfo), AsyncStackFlattener,
│                           #   StackTreeRenderer
├── Diagnostics/            # ThreadAnalyzer, TraceCollector, TraceRenderer, OutputLine
└── Tools/                  # 26 MCP tools across 5 categories

tests/AspNetCoreDebuggerMcp.Tests/    # 80+ xUnit unit tests
.github/workflows/
├── ci.yml                  # Linux + macOS build/test/pack on every push and PR
└── release.yml             # On GitHub Release published: pack + attach + push to NuGet
scripts/
├── fetch-netcoredbg-binaries.sh        # Downloads bundled netcoredbg for all 5 RIDs (CI runs this)
├── package-netcoredbg-osx-arm64.sh     # Packages a local arm64 build into a Samsung-layout tarball
├── build-netcoredbg-macos-arm64.sh     # Builds netcoredbg natively for Apple Silicon (for the bundled binary)
└── smoke-test-bundled-binary.sh        # E2E check: bundled netcoredbg speaks DAP against SampleWebApi
tests/fixtures/SampleWebApi/            # Tiny ASP.NET Core 10 web API used by the smoke test
```

## Architecture in one sentence

**Protocol bridge.** MCP server on one side (Claude talks to us), DAP client on the other (we
talk to netcoredbg over a child-process stdio pipe). The interesting parts are the agent-friendly
composites — `exception_autopsy`, `stack_explore`, `trace_start`, `hang_analyze` — that bundle
multiple DAP requests into a single tool call returning structured + pre-rendered output the
agent can drop into a reply.

## Running the demos

The repository's end-to-end demos live in the developer's local job dir during development; the
unit tests are the canonical "this works" signal. To exercise the full pipeline manually:

```bash
# 1. Stage the bundled netcoredbg binaries (one-time)
bash scripts/fetch-netcoredbg-binaries.sh

# 2. Verify the bundled binary works end-to-end
bash scripts/smoke-test-bundled-binary.sh

# 3. Run the MCP server (no NETCOREDBG_PATH needed — it picks the bundled binary)
dotnet run --project src/AspNetCoreDebuggerMcp

# 4. From an MCP client (or a script), call debug_launch / breakpoint_set / etc.
```

## Pull requests

- Branch naming: `feature/MAG-<number>-<short-name>`
- One PR per logical change (the project uses Linear ticket numbers in branches and commit messages)
- Tests for new behavior — the project keeps to TDD where the surface allows (pure helpers like
  `AsyncStackFlattener`, `StackTreeRenderer`, `ThreadAnalyzer`, `TraceRenderer`, `StopWaiter`,
  `BreakpointRegistry` all have dedicated test classes)
- Build clean (0 warnings, 0 errors)

## Release process

See the [Release workflow](https://github.com/magna-nz/aspnetcore-debugger-mcp/blob/main/.github/workflows/release.yml).
Drafting + publishing a GitHub Release on a `vX.Y.Z` tag triggers the action; the nupkg is
attached to the release and pushed to NuGet.org (if `NUGET_API_KEY` is set as a repo secret).
