# Full tool reference

All 26 tools the agent can use. **You don't call these directly** — Claude picks them based on
what you ask. This page exists so you can see what's possible and check parameters when you
want precision.

## Starting and stopping a debug session

| Tool | What it does |
|---|---|
| `debug_launch` | Start your .NET app under the debugger. Optionally pause at entry. |
| `debug_attach` | Attach to a .NET app that's already running, by process id. |
| `debug_disconnect` | Stop the debug session and shut down the app. |
| `debug_state` | What state are we in? Running, paused, or terminated; current process id; what the last stop was. |

## Setting breakpoints

The tool keeps track of every breakpoint you've set and reapplies them automatically when needed.

| Tool | What it does |
|---|---|
| `breakpoint_set` | Stop at a line of code. Optionally only when a condition is true, only after the Nth hit, or just log a message instead of stopping. |
| `breakpoint_set_function` | Stop when a specific function gets called, by name. |
| `breakpoint_set_exception` | Stop when an exception is thrown (all of them, or just unhandled ones). |
| `breakpoint_set_data` | Stop when a specific variable changes. **Not currently supported by `netcoredbg`** — see [limits](limits.md). |
| `breakpoint_remove` | Remove a breakpoint by id. |
| `breakpoint_list` | Show every breakpoint that's currently set. |

## Running and stepping

| Tool | What it does |
|---|---|
| `debug_continue` | Resume execution. |
| `debug_pause` | Pause a running thread. |
| `debug_step` | Step one instruction. Three flavours: **into** a function call, **over** it (run it without going in), or **out** (run until the current function returns). |
| `breakpoint_wait` | **Blocking.** Wait for the next time execution stops, then return with: the stop info, the top stack frame, and a few lines of source around it. |

## Looking at what's happening

| Tool | What it does |
|---|---|
| `threads_list` | List every thread in the app. |
| `stacktrace_get` | The call chain for a thread. Compiler-generated async method names are cleaned up for you by default (e.g. `UserService.GetAsync` instead of `UserService.<GetAsync>d__3.MoveNext`). |
| `variables_get` | What's in scope at a particular point in the call stack. Compound values like objects and collections get expanded one level deep by default. |
| `evaluate` | Run a C# expression against a paused frame (e.g. `users.Count`, `user.Name`). |
| `variables_set` | Change a variable's value mid-run — handy for testing fixes without editing code. Works on any assignable expression, not just bare names. |
| `stack_explore` | **One-call summary.** Returns the whole call chain *plus* the local variables at each level, with a rendered tree showing depth. Use this when you want the big picture all at once. |

## Diagnosing problems

These are the agent-friendly composites — each one bundles several lower-level calls into a single
result that's ready to drop into a reply.

| Tool | What it does |
|---|---|
| `exception_autopsy` | When you've stopped on an exception, this returns everything at once: the exception type, the inner-exception chain, the top of the call stack, the local variables at the throw site, and a snippet of source around the line that threw. |
| `hang_analyze` | "Why is my app stuck?" Pauses (if needed), looks at every thread, and classifies what each one is blocked on (a lock, a wait handle, a `Task.Wait`, etc.). |
| `trace_start` | Start watching a set of methods. Each one you name gets caught when it runs — argument values, top frame, and stack are captured — and execution continues immediately. Your code runs at near-normal speed. |
| `trace_get` | Read the trace events captured since you started, plus a rendered timeline showing each call with `→` and depth-based indentation. |
| `trace_stop` | Stop the trace and clear its watch points. |
| `process_read_output` | Drain the app's stdout/stderr since the last call. Captures whatever `Console.WriteLine`, `ILogger`, or ASP.NET Core's request logging produces. |
| `debugger_health` | Self-diagnostic. Reports your platform, RID, the resolved netcoredbg path, where it came from (bundled / env var / PATH), and the `--version` banner. Call this first if something looks off, instead of starting a real debug session to find out. |

## Notes on tool behaviour

- **Defaults are sensible.** If you don't pass a thread id, it uses the last-stopped thread.
  If you don't pass a frame id, it uses the topmost frame. If you don't pass a depth or
  timeout, you get reasonable values.
- **Composite tools return both data and a ready-rendered view.** Things like
  `exception_autopsy`, `stack_explore`, and `trace_get` give you structured data *and* a
  formatted text version Claude can drop straight into its reply.
- **Async method names are cleaned up by default.** If you want the raw compiler-generated
  names back, pass `raw: true` to `stacktrace_get`.

For the deeper "this is how each tool talks to the debugger" details, the source under
`src/AspNetCoreDebuggerMcp/Tools/` is the authoritative reference.
