# Contributing / building from source

## Build & test

```bash
git clone https://github.com/magna-nz/aspnetcore-debugger-mcp.git
cd aspnetcore-debugger-mcp
dotnet build
dotnet test
NETCOREDBG_PATH=/path/to/netcoredbg dotnet run --project src/AspNetCoreDebuggerMcp
```

Requires .NET 10 SDK + `netcoredbg` on your machine (see [Install](install.md) and
[macOS arm64 build](macos-arm64.md)).

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
└── build-netcoredbg-macos-arm64.sh    # Builds netcoredbg natively for Apple Silicon
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
# 1. Build a tiny ASP.NET Core test app
dotnet new web -o /tmp/Demo
# (write a small Program.cs handler)

# 2. Run the MCP server pointing at netcoredbg
NETCOREDBG_PATH=~/projects/netcoredbg-src/bin/netcoredbg \
  dotnet run --project src/AspNetCoreDebuggerMcp

# 3. From an MCP client (or a script), call debug_launch / breakpoint_set / etc.
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
