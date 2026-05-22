# STATUS

## What was built
- Repo scaffolding: LICENSE (MIT), README.md, SPEC.md, .gitignore.
- Wave 0 in progress.

## Decisions made
- **Approach:** MCP server that wraps `netcoredbg` (MIT) over the Debug Adapter Protocol — chosen
  over forking the AGPL-licensed `debug-mcp`, so the result is corporate-safe.
- **Language:** C# / .NET 10.
- **Platform:** macOS Apple Silicon (arm64) first.
- **netcoredbg on arm64:** no prebuilt arm64 macOS binary exists, so we build it from source
  (Path 2). Samsung flags arm64 macOS as community-supported.
- Wave 4 differentiators in scope: hang/deadlock analyzer, data/watch breakpoints, process I/O.
  Roslyn code navigation is out of scope.

## Where we left off
- Wave 0: netcoredbg arm64 source build running (cmake + make in `~/projects/netcoredbg-src/build`).
- Feasibility of the native arm64 build is the gate for proceeding to Wave 1.

## What's next
- Confirm the netcoredbg arm64 build succeeds and the binary runs / speaks DAP.
- Create the Linear ticket before any Wave 1 code.
- Wave 1: scaffold the C# MCP server + hand-rolled DAP client.

## Gotchas
- cmake 4.x: configured with `-DCMAKE_POLICY_VERSION_MINIMUM=3.5` for old `cmake_minimum_required`.
- An x64 debugger can only debug x64 targets; a native arm64 netcoredbg is required to debug
  native arm64 .NET processes (and to support attach on Apple Silicon).
- MIT LICENSE copyright holder is currently "magna-nz" — change to the correct person/company.
