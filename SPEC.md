# SPEC — aspnetcore-debugger-mcp

## Overview

An MIT-licensed MCP server that gives AI agents interactive debugging for .NET / ASP.NET Core
applications. The server bridges the Model Context Protocol (toward the AI client) to the Debug
Adapter Protocol (toward a debugger engine), driving `netcoredbg` as a child process. On top of the
raw DAP surface it adds higher-level, agent-ergonomic composite tools.

## Why

A licensing-clean alternative to existing AGPL-licensed .NET debugging MCP servers. AGPL is banned
or restricted at many companies; this project is MIT and depends only on MIT-licensed components
(`netcoredbg`, `ClrDebug`, `DbgShim`), so it is safe for internal corporate use.

## Goals

- Let an AI agent launch or attach to a .NET process and debug it conversationally.
- Provide breakpoints, stepping, inspection, evaluation, and exception analysis.
- Add composite tools that suit an AI agent (fewer, richer calls) rather than a GUI.
- Stay MIT-licensed end to end.

## Non-Goals (out of scope)

- GC root analysis, raw memory reads, object memory layout — DAP cannot express these.
- Edit-and-Continue / hot reload.
- Roslyn static code navigation (go-to-definition, find-usages) — explicitly deferred.

## Architecture

```
Claude (MCP client)
   │  MCP  (stdio / JSON-RPC)
   ▼
aspnetcore-debugger-mcp   — MCP server + DAP client + session manager
   │  DAP  (Content-Length-framed JSON over stdio)
   ▼
netcoredbg --interpreter=vscode   — child process
   │  ICorDebug
   ▼
target .NET process
```

- The server is a **protocol bridge**: MCP server on one side, DAP *client* on the other.
- DAP is event-driven; the server tracks session state and converts async DAP events
  (`stopped`, `output`, `terminated`) into things an agent can query and wait on.
- We hand-roll a minimal DAP client (our own DTOs for the DAP message subset we use) to keep the
  project cleanly MIT and avoid restrictively-licensed DAP type packages.

## Tech Stack

- **Language/Runtime:** C# / .NET 10
- **MCP SDK:** `ModelContextProtocol` (NuGet)
- **Debugger engine:** `netcoredbg` 3.1.3+ (Samsung, MIT), spawned as a child process
- **Tests:** xUnit (TDD)

## Platform

- **Primary target:** macOS Apple Silicon (arm64).
- `netcoredbg` ships no prebuilt arm64 macOS binary, so the project builds it from source for
  arm64 (Path 2). Samsung flags arm64 macOS as community-supported; build reliability is validated
  in Wave 0.
- Linux and Windows are expected to work afterward with little extra effort (netcoredbg ships
  prebuilt binaries for both).

## Tools

### Session
`debug_launch`, `debug_attach`, `debug_disconnect`, `debug_state`

### Breakpoints & execution
`breakpoint_set` (line, conditional, hit-count, logpoint), `breakpoint_set_function` (by symbol),
`breakpoint_remove`, `breakpoint_list`, `breakpoint_set_exception`,
`debug_continue`, `debug_pause`, `debug_step` (in/over/out),
`breakpoint_wait` (blocking run-until-stopped)

### Inspection & agent value-add
`threads_list`, `stacktrace_get`, `variables_get` (recursive expansion + collection summary),
`evaluate`, `variables_set`, `exception_autopsy` (one-call exception analysis),
auto-context-on-stop (every stop returns location + source + frames + key locals)

### Differentiators
`hang_analyze` (deadlock/hang analysis over all-thread stacks), data/watch breakpoints
(pending netcoredbg DAP capability), `process_write_input` / `process_read_output`

## Wave Plan

- **Wave 0 — Setup & feasibility (GATING):** repo, SPEC, license; build netcoredbg natively for
  arm64 macOS and verify it speaks DAP and can launch+break a test app. If unreliable, reassess.
- **Wave 1 — DAP bridge + session:** MCP server scaffold; hand-rolled DAP client; netcoredbg
  process lifecycle; `initialize → launch/attach → configurationDone` handshake; session state
  machine. Tools: `debug_launch`, `debug_attach`, `debug_disconnect`, `debug_state`.
- **Wave 2 — Breakpoints & execution:** all breakpoint tools + `debug_continue/pause/step` +
  `breakpoint_wait`.
- **Wave 3 — Inspection & value-add (MVP completes):** `threads_list`, `stacktrace_get`,
  `variables_get`, `evaluate`, `variables_set`, `exception_autopsy`, auto-context-on-stop.
- **Wave 4 — Differentiators:** hang/deadlock analyzer, data/watch breakpoints, process I/O.
- **Wave 5 — Packaging & docs:** package as a .NET tool, netcoredbg bundling, README, macOS setup,
  CI.

## Process

- TDD; tests and a compile check after every wave.
- Gemini architectural review after every wave.
- Pause for approval between waves.
- All waves share one branch and one PR.
