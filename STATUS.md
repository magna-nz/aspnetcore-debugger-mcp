# STATUS

## What was built
- Repo scaffolding: LICENSE (MIT), README.md, SPEC.md, .gitignore (committed + pushed, commit 6f76708).
- netcoredbg built natively for arm64 macOS from source — binary works, runs as native arm64.
- WAVE 0 COMPLETE: DAP launch-break test passed — netcoredbg launched a .NET 10 app, bound a
  breakpoint, and hit it (`stopped` reason=breakpoint). No macOS permission/entitlement blocker.
- WAVE 1 COMPLETE: C# .NET 10 MCP server building cleanly. DAP layer (framing + client +
  request/response correlation + event dispatch). netcoredbg process lifecycle. Session state
  machine + handshake. Four MCP tools (debug_launch / _attach / _disconnect / _state). 14 unit
  tests passing. End-to-end smoke test passes against the real netcoredbg + a real test .NET app.
- WAVE 2 COMPLETE: breakpoints + execution control. BreakpointRegistry (intent-tracking, used to
  re-send the full per-source set on every mutation). StopWaiter (TCS waiter with Reset/Terminate
  semantics for breakpoint_wait). 9 new MCP tools: breakpoint_set / _set_function / _remove /
  _list / _set_exception, debug_continue / _pause / _step, breakpoint_wait. 27 unit tests passing.
  End-to-end Wave 2 smoke passes: set BP, continue, hit BP, step over, remove, continue to exit.
- WAVE 3 COMPLETE — MVP DONE: inspection + agent value-add. InspectionService translates DAP
  threads/stackTrace/scopes/variables/evaluate/setExpression/exceptionInfo into typed records,
  with recursive variable expansion (depth + max-children truncation) and an exception_autopsy
  composite that returns exception chain + top frames + top-frame locals + source snippet in
  one call. breakpoint_wait now also returns the topmost frame and a source snippet
  (auto-context-on-stop). 6 new MCP tools: threads_list, stacktrace_get, variables_get,
  evaluate, variables_set, exception_autopsy. 30 unit tests passing. End-to-end Wave 3 smoke
  validates the whole MVP loop including variables_set actually mutating runtime state
  (set x=100, evaluate confirms 100) and exception_autopsy capturing a real NRE.

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
- Wave 3 done on branch `feature/MAG-39-debugger-mcp`. Linear ticket: MAG-39.
- 19 MCP tools live, MVP functionally complete. Awaiting user "go" for Wave 4.

## What's next
1. Wave 4 (needs user "go"): hang/deadlock analyzer, data/watch breakpoints, process stdin/stdout I/O.
2. Wave 5: package as a .NET tool, netcoredbg bundling, README, macOS setup, CI.
3. Run NETCOREDBG_PATH=~/projects/netcoredbg-src/bin/netcoredbg when launching the server until
   bundling is sorted (Wave 5).

## Gotchas
- cmake 4.x: configured with `-DCMAKE_POLICY_VERSION_MINIMUM=3.5` for old `cmake_minimum_required`.
- An x64 debugger can only debug x64 targets; a native arm64 netcoredbg is required to debug
  native arm64 .NET processes (and to support attach on Apple Silicon).
- MIT LICENSE copyright holder is "magna-nz" (confirmed correct by user).
- netcoredbg source + build tree live at `~/projects/netcoredbg-src` (separate from this repo);
  how to vendor/bundle the binary is a Wave 5 decision.
- DAP behavior to handle in Wave 1: `setBreakpoints` may respond `verified:false` initially, then
  send an async `breakpoint` event upgrading to `verified:true` once the module loads. The DAP
  client must track `breakpoint` events, not just the `setBreakpoints` response.
- DAP launch flow confirmed: initialize → (response) → launch → `initialized` event →
  setBreakpoints → configurationDone → process runs → `stopped` event.
- DAP client serialises requests with `WhenWritingNull` — netcoredbg's parser rejects null
  string fields (breakpoint condition / hitCondition / logMessage). Required for setBreakpoints.
- StopWaiter is Reset() synchronously inside ContinueAsync/StepAsync (before the DAP request is
  sent) so a wait registered immediately after sees a fresh TCS. Resetting on the `continued`
  event instead would race the agent.
