# Known limits

Honest scope notes. These aren't bugs — they're trade-offs we made, or things the underlying
debugger adapter doesn't support.

## When NOT to use this tool

A debugger fundamentally needs to **pause** to see anything. If you don't want to pause, this is
the wrong tool. Use the right tool for the job:

| Want | Use |
|---|---|
| "Why does this return wrong data" | **this tool** — pause, read state, find out |
| "Every method called during a request, with arguments" | **`trace_start`** in this tool |
| "Every method ever called, no instrumentation, no manual list" | A sampling profiler (`dotnet-trace`, OpenTelemetry) |
| "Just hit my endpoint and see the response" | `curl` |
| "What's slow?" | A profiler — debuggers don't measure perf |
| "Production observability" | Logs / metrics / APM |
| "Read the code" | Your IDE / GitHub |

## Adapter limits

- **`breakpoint_set_data` depends on the adapter.** netcoredbg currently returns `E_NOTIMPL` for
  `dataBreakpointInfo`. The tool surfaces this as a clean error; another adapter (or a future
  netcoredbg) would just work — no code change needed on our side.
- **`process_write_input` is not implemented.** DAP launch mode owns the debuggee's stdin;
  piping input would require a different launch architecture (we launch the process, netcoredbg
  attaches). Doable, just not done.

## Tracing limits

- **Tracing has overhead.** Each trace BP hit ≈ a handful of DAP round-trips. Fine for tracing a
  few methods through a single request (a few hundred ms total overhead); **not** suited to
  high-throughput loads or tracing every method in a namespace — use a real profiler for that.
- **Trace mode owns all breakpoints.** While a trace is active, user breakpoints can't be added.
  This sidesteps netcoredbg not populating `hitBreakpointIds` in stopped events, so we can't
  otherwise tell a trace BP hit from a user BP hit. Call `trace_stop` first if you need to set
  user breakpoints.

## Async-stack limits

- **State-machine frame names are flattened** (`UserService.<GetAsync>d__3.MoveNext` →
  `UserService.GetAsync`) and BCL async infrastructure is hidden — good enough for normal use.
- We **don't walk the heap to reconstruct logical "who awaited this"** when the awaiter isn't on
  the current thread. That needs ICorDebug-level access beyond what DAP exposes. If this becomes
  a pain point, it's a future enhancement.

## What you can fix with config / your code

| Symptom | Fix |
|---|---|
| `breakpoint_set_function` doesn't hit | Use the exact symbol name. For generic methods, include the type parameter (`Foo``1.Bar`). |
| Trace shows depth 0 for everything | Increase `maxFramesPerEvent` so the captured stack reaches your traced ancestors. |
| Variable values show as `null` when you expect data | The breakpoint may have hit *before* assignment — check the source snippet. |
