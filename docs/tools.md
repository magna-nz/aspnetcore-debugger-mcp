# Full tools reference

All 26 MCP tools, grouped by category. Tool names map directly to the `name` field in your MCP
client; agents call them via normal tool-use mechanics — you don't invoke them by hand.

## Session

Lifecycle of one debug session (only one at a time per server instance).

| Tool | Purpose |
|---|---|
| `debug_launch` | Launch a .NET program (`.dll` or apphost) under the debugger. Optional `stopAtEntry`. |
| `debug_attach` | Attach to an already-running .NET process by PID. |
| `debug_disconnect` | Terminate the debuggee and tear down the session. |
| `debug_state` | Return the current session state, process id, and last stop info. |

## Breakpoints

The breakpoint registry is the authoritative intent — every mutation re-sends the full per-source
or per-list set to the DAP adapter.

| Tool | Purpose |
|---|---|
| `breakpoint_set` | Line breakpoint. Supports `condition`, `hitCondition`, and `logMessage` (logpoint / tracepoint). |
| `breakpoint_set_function` | Break when a function is entered, by symbol name. |
| `breakpoint_set_exception` | Set the active exception-breakpoint filters (`all`, `user-unhandled`). |
| `breakpoint_set_data` | Watchpoint on a variable. **netcoredbg returns `E_NOTIMPL`** currently — see [limits](limits.md). |
| `breakpoint_remove` | Remove a breakpoint by id (line, function, or data). |
| `breakpoint_list` | Snapshot of every breakpoint currently set. |

## Execution

| Tool | Purpose |
|---|---|
| `debug_continue` | Resume the debuggee (defaults to the last-stopped thread). |
| `debug_pause` | Pause a thread. |
| `debug_step` | Single-step. `kind: "in"` / `"over"` / `"out"`. |
| `breakpoint_wait` | **Blocking** wait for the next stop. Returns the stop info **plus auto-context**: top stack frame and a source snippet around the stop with an arrow marker. |

## Inspection

| Tool | Purpose |
|---|---|
| `threads_list` | List all threads in the debuggee. |
| `stacktrace_get` | Call stack of a thread. Async state-machine frames are flattened back to original method names by default; pass `raw=true` for the unmodified DAP frames. |
| `variables_get` | Scopes + variables for a frame. Recursively expands compound values up to `depth` levels, truncates at `maxChildren`. |
| `evaluate` | Evaluate a C# expression in the context of a frame. |
| `variables_set` | Set the value of a variable or any lvalue expression (uses DAP `setExpression` — accepts `user.Name`, `dict[key]`, etc., not just bare names). |
| `stack_explore` | **Composite.** In one call: full stack + locals at every frame + a pre-rendered ASCII tree showing caller → callee with arrows. |

## Diagnostics

The agent-value-add tools — composites that bundle multiple DAP requests with structured + pretty-printed output.

| Tool | Purpose |
|---|---|
| `exception_autopsy` | **Composite.** Exception type + inner-exception chain + top stack frames + top-frame locals + source snippet around the throw, all from one call. Use when `state.lastStop.reason == "exception"`. |
| `hang_analyze` | **Composite.** Auto-pauses if running, lists every thread, classifies each by what blocking primitive it's on (`Monitor` / `WaitHandle` / `Semaphore` / `Task` / `Thread.Join` / `Sleep` / `AwaitingAsync` / `Running`). |
| `trace_start` | Begin server-side request tracing. Named methods get internal trace breakpoints that capture the call (top frame + locals + stack) and auto-continue — the request flows at near-normal speed and your foreground debug state is untouched. Optional `includeExceptions=true` also captures unhandled exceptions. |
| `trace_get` | Read the trace events captured since `trace_start`, plus a pre-rendered ASCII timeline with `→` for method entries and `⚠` for exceptions. |
| `trace_stop` | Stop the active trace and remove its breakpoints. |
| `process_read_output` | Drain the debuggee's stdout/stderr accumulated since the last call. With ASP.NET Core's default request logging this gives you `Request starting` / `Request finished … 200` lines per request. |

## Tool design notes

- **Return shape** — every tool returns JSON of the form `{"success": true, ...}` or `{"success": false, "error": "..."}`. Enums are serialised as camelCase strings (e.g. `"blockedOnMonitor"`, not `0`).
- **`null` parameters** are omitted by the DAP client before serialisation — netcoredbg's parser rejects null string values in some places.
- **Defaults** — tools that take optional thread / frame / depth / timeout parameters resolve sensible defaults (last-stopped thread, topmost frame, depth 1, 30 s wait timeout).
- **Composites** (`exception_autopsy`, `stack_explore`, `hang_analyze`, `trace_*`) bundle multiple DAP requests and return **both** structured data and a pre-rendered text view ready to drop into a reply.

See the [worked examples](examples.md) for what these look like in actual use.
