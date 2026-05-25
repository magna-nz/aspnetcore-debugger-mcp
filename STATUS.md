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
- WAVE 4 COMPLETE: differentiators. ThreadAnalyzer (pure classifier) + hang_analyze composite
  that auto-pauses if running, lists threads, fetches top frames, classifies each thread's
  blocking pattern (Monitor / WaitHandle / Semaphore / Task / Thread.Join / Sleep / async).
  DAP `output` events buffered per session; process_read_output drains them. Data breakpoints
  wired (registry + setDataBreakpoints), but **netcoredbg returns E_NOTIMPL (0x80004001) for
  dataBreakpointInfo** — the tool exists and surfaces the adapter limitation cleanly; an
  adapter that supports it would just work. 3 new MCP tools (22 total): hang_analyze,
  breakpoint_set_data, process_read_output. 44 unit tests passing. End-to-end Wave 4 smoke
  validates hang_analyze classifying threads and process_read_output capturing "Sum: 30".
  Enum responses now serialize as camelCase strings (e.g. "blockedOnMonitor"), not ints.
- WAVE 5 COMPLETE: packaging + docs. csproj wired for PackAsTool with ToolCommandName
  "aspnetcore-debugger-mcp", PackageId AspNetCoreDebuggerMcp, version 0.1.0-preview, MIT
  license, README packed in. Full README replaces the placeholder: tools table (all 22),
  install steps for .NET / netcoredbg / the tool, Claude Code + Claude Desktop config
  snippets, macOS arm64 note, known limits (data BP adapter dep, process_write_input gap).
  scripts/build-netcoredbg-macos-arm64.sh automates the arm64 netcoredbg build with the
  cmake-4.x policy workaround. GitHub Actions CI workflow (.github/workflows/ci.yml):
  Linux + macOS matrix, restore/build/test, dotnet pack on Linux + upload nupkg artifact.
  Local `dotnet pack` succeeds — produced AspNetCoreDebuggerMcp.0.1.0-preview.nupkg.

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
- MAG-46 (NuGet metadata + discoverability) DONE. PR #14 merged.
- MAG-47 (Bundle netcoredbg binaries) DONE. PR #16 merged. v0.1.3-preview on NuGet.org.
- MAG-48 (Dockerfile for Glama submission) DONE. PR #17 merged.
- MAG-49 (DX polish — debugger_health tool + README trace example) DONE. PR #18 merged.
- MAG-50 (debug_launch env var support) DONE. PR #19 merged. v0.1.4-preview on NuGet.org.
- MAG-51 (Publish to official MCP registry + automate in release.yml) DONE. PR #20 merged.
  v0.1.5-preview live on NuGet.org AND on registry.modelcontextprotocol.io under
  `io.github.magna-nz/aspnetcore-debugger-mcp`. release.yml now auto-publishes the
  manifest on every NuGet push via GitHub OIDC (no PAT to rotate).
- Submitted to mcp.so (pending review) and punkpeye/awesome-mcp-servers PR #6823 (open).
- Glama follow-up still pending: submit at glama.ai/mcp/servers, wait for introspection,
  add Glama score badge to punkpeye PR #6823.

## What's next
1. Glama submission (saved in auto-memory).
2. No code work queued. Next feature/fix as it comes up.
3. v1.0.0 bump: wait 4–8 weeks until tool surface settles and listings drive real
   usage signal before stabilising.

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
- netcoredbg lacks data breakpoint support (dataBreakpointInfo → E_NOTIMPL). The
  breakpoint_set_data tool exists and works in principle; users get a clear "adapter doesn't
  support this" error message.
- process_write_input is NOT implemented in Wave 4. DAP launch mode owns the debuggee's stdin
  (netcoredbg launched it); we'd need a different launch architecture (we launch the process
  ourselves and have netcoredbg attach) to plumb stdin. Deferred — flag for a future wave.
- csproj `<Version>` (0.1.0-preview) and `.mcp/server.json` version are decorative — release.yml
  overrides via `/p:Version=$tag` for the pack and jq-rewrites server.json before publishing.
  The git tag is the source of truth. Don't bump either before tagging.
- MCP registry namespace `io.github.magna-nz/*` is owned implicitly via GitHub OIDC — no
  account, no token, no manual claim. The workflow's `id-token: write` permission proves it.
