# aspnetcore-debugger-mcp

**An MIT-licensed [Model Context Protocol](https://modelcontextprotocol.io/) server that lets an
AI agent (Claude, etc.) debug your .NET / ASP.NET Core app — conversationally.**

Instead of *"I think the bug is around line 42, try this"*, the agent runs your code, pauses it,
reads the actual runtime values, mutates state to test a fix, and answers grounded in what it
actually saw.

26 tools across launching, breakpoints, stepping, inspection, expression evaluation, exception
autopsy, hang analysis, and **server-side request tracing** that captures the full call chain
with variables — without you setting any breakpoint manually.

---

## A taste — real output

You tell Claude: *"Trace what `GET /order/42` does."*

The agent traces 5 methods, hits the endpoint, and gets back:

```
[+  679ms] → OrderController.GetOrder()       id=42, data=null
[+  711ms] --→ OrderService.LookupOrder()     id=42, raw=null, enriched=null
[+  735ms] ----→ OrderRepository.FetchById()  id=42
[+  759ms] ------→ SqlClient.ExecuteQuery()   sql="SELECT * FROM orders WHERE id = 42"
[+  783ms] ----→ EnrichmentService.Enrich()   raw="rows(sql=SELECT * FROM orders WHERE id …"
```

— Controller → Service → Repository → SqlClient (4 levels), with `Enrich` correctly shown as a
sibling of the Repository call at depth 2. Each line is one captured method entry with its
actual locals. The request returned 200; the trace shows the path it took.

No breakpoints were set manually. The agent picked the methods and called `trace_start`.

---

## How it works

```
Claude (MCP client)
   │  MCP  (stdio / JSON-RPC)
   ▼
aspnetcore-debugger-mcp        ← this server
   │  DAP  (Debug Adapter Protocol)
   ▼
netcoredbg                     ← Samsung's MIT-licensed .NET debugger, child process
   │  ICorDebug
   ▼
target .NET process
```

The server is a protocol bridge: MCP server on one side, DAP client on the other. It drives
[`netcoredbg`](https://github.com/Samsung/netcoredbg) and adds higher-level, agent-friendly
composites on top — `exception_autopsy`, `stack_explore`, `hang_analyze`, and the trace tools.

---

## Use it in 4 steps

Once per machine: install prerequisites and the tool. Once per project: register it. Then just chat.

### Step 1 — Prerequisites *(once per machine)*

- **.NET 10 SDK** — [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download)
- **netcoredbg** — Samsung's MIT-licensed .NET debugger that this server drives:

  | Platform | Get it from |
  |---|---|
  | Linux x64 / arm64 | [Samsung release](https://github.com/Samsung/netcoredbg/releases) |
  | Windows x64 | [Samsung release](https://github.com/Samsung/netcoredbg/releases) |
  | macOS Intel (x64) | [Samsung release](https://github.com/Samsung/netcoredbg/releases) |
  | **macOS Apple Silicon (arm64)** | **No prebuilt — build with `scripts/build-netcoredbg-macos-arm64.sh`** |
- **Claude Code** or **Claude Desktop** installed.

### Step 2 — Install the MCP server *(once per machine)*

```bash
dotnet tool install -g AspNetCoreDebuggerMcp --prerelease
```

That puts the `aspnetcore-debugger-mcp` binary on your `PATH`. Verify:

```bash
NETCOREDBG_PATH=/path/to/netcoredbg aspnetcore-debugger-mcp
# starts and idles on stdin; Ctrl-C to exit
```

### Step 3 — Register it with your MCP client *(once per project, or globally)*

**Claude Code** — either run:

```bash
claude mcp add aspnetcore-debugger \
  -e NETCOREDBG_PATH=/path/to/netcoredbg \
  -- aspnetcore-debugger-mcp
```

or edit `.mcp.json` (project) / `~/.claude.json` (global) directly:

```json
{
  "mcpServers": {
    "aspnetcore-debugger": {
      "command": "aspnetcore-debugger-mcp",
      "env": { "NETCOREDBG_PATH": "/absolute/path/to/netcoredbg" }
    }
  }
}
```

**Claude Desktop** — same JSON shape goes in `claude_desktop_config.json`'s `mcpServers` block.

### Step 4 — Just chat with Claude

Open Claude Code (or Claude Desktop) in your .NET project. Run `/mcp` to confirm `aspnetcore-debugger` shows as connected. From here, **don't invoke the tools yourself** — just describe what you want:

> *"Debug my API and figure out why `GET /users/42` returns null."*

Claude picks the right tools (`debug_launch`, `breakpoint_set`, `variables_get`, etc.) and reports back what it actually saw at runtime.

---

## What you can do with it — worked examples

These are the patterns this tool exists to make easy. Each example shows what you'd say to your
AI assistant and what the agent does in response. The tool outputs shown are real — straight
from the project's end-to-end test runs.

### 1. "Why does this endpoint return wrong data?" — break and inspect

> **You:** Why does `GET /hello/world` return `null` when I expect a greeting?

The agent:

1. `debug_launch program=bin/Debug/net10.0/WebApiTest.dll stopAtEntry=true`
2. `breakpoint_wait` → entry stop, ready to configure
3. `breakpoint_set sourcePath=Program.cs line=6` (inside the handler)
4. `debug_continue` + wait for Kestrel to come up
5. **You hit the endpoint** (or it tells curl to): `curl http://localhost:5099/hello/world`
6. `breakpoint_wait` returns:

```
stop: { reason: "breakpoint", threadId: 12345 }
topFrame: Program.<>c.<<Main>$>b__0_0()  [Program.cs:6]
snippet:
    4: app.MapGet("/hello/{name}", (string name) =>
    5: {
→   6:     var greeting = $"Hello, {name}!";
    7:     var length = greeting.Length;
    8:     return Results.Ok(new { greeting, length });
```

7. `variables_get` → sees `name="world"`, `greeting=null` (not yet assigned)
8. `evaluate "name.ToUpper()"` → `"WORLD"` — runs against the live frame
9. Agent answers with the real values, not guesses.

### 2. "Test a fix without changing code" — mutate state mid-run

> **You:** What if `userId` were `"u-001"` instead of empty?

```
[variables_set expression="userId" value="\"u-001\""]
[debug_continue]
[breakpoint_wait]   → function returned with a real User. Confirmed: bug is in the caller.
```

`variables_set` uses DAP `setExpression` so it works on any lvalue, including object members
(`user.Name`, `dict[key]`, etc.).

### 3. "Why did this throw?" — one-call exception autopsy

> **You:** Catch any unhandled exception, then explain it.

```
[breakpoint_set_exception filters=["user-unhandled"]]
[debug_continue]
[breakpoint_wait]   → stopped, reason=exception
[exception_autopsy]
```

Returns in one call:

```
exceptionId: "CLR/System.NullReferenceException"
description: "Object reference not set to an instance of an object."
layers: [
  { typeName: "System.NullReferenceException", message: "..." }
]
stackFrames: [Program.<<Main>$>b__0_0, ...]
topFrameLocals: { name: null, ... }
topFrameSnippet:
    6: var greeting = $"Hello, {name}!";
    7: string? name = null;
→   8: Console.WriteLine(name!.Length);
```

The agent gets type + inner-chain + frames + locals at the throw site + source snippet, all from
a single tool call. No grepping logs for a bare stack trace with no context.

### 4. "Trace the whole call chain through a request" — no manual breakpoints

> **You:** Show me every method the request hits, with its arguments.

```
[trace_start methods=[
   "OrderController.GetOrder",
   "OrderService.LookupOrder",
   "OrderRepository.FetchById",
   "EnrichmentService.Enrich",
   "SqlClient.ExecuteQuery"]]
[debug_continue]              # Kestrel comes up
(curl /order/42)
[trace_get]
```

Output:

```
[+  679ms] → OrderController.GetOrder()       id=42, data=null
[+  711ms] --→ OrderService.LookupOrder()     id=42, raw=null, enriched=null
[+  735ms] ----→ OrderRepository.FetchById()  id=42
[+  759ms] ------→ SqlClient.ExecuteQuery()   sql="SELECT * FROM orders WHERE id = 42"
[+  783ms] ----→ EnrichmentService.Enrich()   raw="rows(sql=SELECT * FROM orders WHERE id …"
```

How it works under the hood: each named method gets a *server-side* trace breakpoint that
captures the call (top frame + locals + stack) and **auto-continues immediately** — your code
runs at near-normal speed, never visibly pausing. Indentation reflects the depth of *traced
ancestors* in each frame's stack, so middleware and Kestrel internals don't inflate the depth.
Sibling calls (FetchById and Enrich, both called by LookupOrder) sit at the same level.

`trace_start` also captures unhandled exceptions during the trace (`includeExceptions=true` by
default), rendered with `⚠` and a small stack.

> Note: while a trace is active, the session can't also have user breakpoints. Call `trace_stop`
> first if you want to switch modes.

### 5. "Walk through a layered request when stopped" — `stack_explore`

When you're paused inside a deeply layered request, this returns the entire call chain plus the
locals at every frame in one call, with a rendered ASCII tree:

```
Microsoft.AspNetCore.Server.Kestrel...HttpProtocol.ProcessRequests()
     │
     ▼
Microsoft.AspNetCore.HostFiltering.HostFilteringMiddleware.Invoke()
     │
     ▼
[Native Frames]
     │
     ▼
Program.<>c.<<Main>$>b__0_0()   [Program.cs:6]  ◄ paused here
  name = "world", greeting = null, length = 0
```

Same shape as `stacktrace_get` + `variables_get` per frame, but consolidated into one tool call
and with a human-readable tree string ready to drop into a reply. Async state-machine frames
are flattened back to their original method names (e.g. `UserService.<GetAsync>d__3.MoveNext`
→ `UserService.GetAsync`).

### 6. "Why is the app hung?" — `hang_analyze`

```
[hang_analyze]
```

Auto-pauses if running, walks every thread, classifies each by what blocking primitive it's on
(Monitor / WaitHandle / Semaphore / Task / Thread.Join / Sleep / async await), returns a summary:

```
blockedCount: 3
notes: "Cycle detection requires lock-ownership data which DAP does not expose..."
threads: [
  { threadId: 1, name: "Main Thread", blockingKind: "blockedOnTask", topFrames: [...] },
  { threadId: 8, name: ".NET ThreadPool Worker", blockingKind: "blockedOnMonitor", topFrames: [...] },
  ...
]
```

### 7. "Show me what's in the response logs" — `process_read_output`

```
[process_read_output]
→ Drains stdout/stderr accumulated since the last call. For an ASP.NET Core app with default
  request logging, that includes the `Request starting`/`Request finished … 200` lines.
```

Useful when you just want the server-side view of a request without setting breakpoints.

---

## All 26 tools

| Category | Tools |
|---|---|
| **Session** | `debug_launch`, `debug_attach`, `debug_disconnect`, `debug_state` |
| **Breakpoints** | `breakpoint_set` (line + conditional + hit-count + logpoint), `breakpoint_set_function`, `breakpoint_set_exception`, `breakpoint_set_data`, `breakpoint_remove`, `breakpoint_list` |
| **Execution** | `debug_continue`, `debug_pause`, `debug_step` (in/over/out), `breakpoint_wait` (blocks until next stop, includes top frame + source snippet) |
| **Inspection** | `threads_list`, `stacktrace_get`, `variables_get` (recursive expansion + truncation), `evaluate`, `variables_set` (via DAP `setExpression`, handles any lvalue), `stack_explore` (stack + locals at every frame + ASCII tree, one call) |
| **Diagnostics** | `exception_autopsy` (chain + frames + locals + snippet), `hang_analyze` (per-thread blocking classification), `trace_start` / `trace_get` / `trace_stop` (server-side call-chain tracing with auto-continue), `process_read_output` (drain buffered stdout/stderr) |

---

## When NOT to use this tool

Be honest about scope:

| Want | Reach for |
|---|---|
| "Why does this return wrong data" | **this tool** — pause, read state, find out |
| "Every method called during a request, with arguments" | **`trace_start`** in this tool |
| "Every method ever called, no instrumentation" | A sampling profiler (`dotnet-trace`, OpenTelemetry) |
| "Just hit my endpoint and see the response" | `curl` |
| "What's slow?" | A profiler — debuggers don't measure perf |
| "Production observability" | Logs / metrics / APM |
| "Read the code" | Your IDE / GitHub |

---

## macOS Apple Silicon — building netcoredbg

Samsung doesn't publish an arm64 macOS prebuilt. Use the included script:

```bash
./scripts/build-netcoredbg-macos-arm64.sh
```

Requires Xcode Command Line Tools, CMake (`brew install cmake`), .NET 10 SDK. Clones
`Samsung/netcoredbg`, configures, builds, installs, and prints the `NETCOREDBG_PATH` to export.

> Samsung notes the arm64 macOS build is "community supported and may not work as expected." In
> our testing on Apple Silicon, every debugger feature used by this project works correctly
> except `dataBreakpointInfo` (returns `E_NOTIMPL`) — see Known limits.

---

## Known limits

- **`breakpoint_set_data` depends on the adapter.** netcoredbg currently returns `E_NOTIMPL` for
  `dataBreakpointInfo`. The tool surfaces this as a clean error; another adapter (or a future
  netcoredbg) would just work.
- **`process_write_input` is not implemented.** DAP launch mode owns the debuggee's stdin;
  plumbing input requires a different launch architecture (we launch the process, netcoredbg
  attaches).
- **Tracing has overhead.** Each trace BP hit ≈ a handful of DAP round-trips. Fine for tracing a
  few methods through a single request; **not** suited to high-throughput loads or tracing every
  method in a namespace — use a real profiler for that.
- **Trace mode owns all breakpoints.** While a trace is active, user breakpoints can't be added
  — call `trace_stop` first.
- **Async continuation chains.** Async state-machine frame *names* are flattened back to their
  original methods, but we don't walk the heap to reconstruct logical "who awaited this" when
  the awaiter isn't on the current thread. That would need ICorDebug-level access beyond what
  DAP exposes.

---

## Build from source

```bash
git clone https://github.com/magna-nz/aspnetcore-debugger-mcp.git
cd aspnetcore-debugger-mcp
dotnet build
dotnet test
NETCOREDBG_PATH=/path/to/netcoredbg dotnet run --project src/AspNetCoreDebuggerMcp
```

79+ unit tests cover the DAP client, breakpoint registry, stop-waiter, state machine, async
frame flattening, stack-tree rendering, thread blocking classifier, source-snippet reader, and
trace renderer. End-to-end smokes exercise the full pipeline against a real ASP.NET Core Web
API on netcoredbg.

---

## License

MIT — see [LICENSE](LICENSE). Depends on `netcoredbg` (also MIT) and the `ModelContextProtocol`
SDK (MIT).
