# What this tool isn't for

Honest scope notes — these aren't bugs, they're trade-offs. Use the right tool for the job:

| You want | Use |
|---|---|
| "Why does this return the wrong data?" | **This tool** — pause your code, read the real values, find out |
| "Every method called during a request, with arguments" | **This tool's tracing** (see the [examples](examples.md#show-me-the-path-a-request-takes-through-my-code)) |
| "Every method ever called, no instrumentation, no list of names" | A sampling profiler like [`dotnet-trace`](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace) |
| "Just hit my endpoint and see the response" | `curl` |
| "What's slow?" | A profiler — debuggers don't measure performance |
| "What happened in production?" | Logs, metrics, or an APM (Application Performance Monitoring) tool |
| "I want to read the code" | Your IDE / GitHub |

## A few things this tool can't do (yet)

### Data breakpoints

The watchpoint tool (`breakpoint_set_data`) is wired up, but the underlying debugger
(`netcoredbg`) doesn't support data breakpoints yet — so for now the tool returns a clear "not
supported" error. If `netcoredbg` adds support later, the tool will start working with no
changes on our side.

### Writing to the running app's standard input

You can read the app's stdout/stderr, but you can't pipe text into its `stdin` while it's
running. This is a structural limit of how the debugger launches your app; it'd need a
different launch model to fix.

### Tracing has overhead

Each traced method call costs a handful of round-trips between the server and the debugger.
**Fine for tracing a few methods through a single request** (a few hundred ms total overhead).
**Not** suited to high-throughput loads or tracing every method in a namespace — that's
profiler territory.

### Tracing can't share a session with manual breakpoints

While a trace is running, you can't also have your own breakpoints set. Call **stop trace**
first if you need to switch back to manual debugging.

### Async stack traces

Method names in the stack are unmangled (you see `UserService.GetUserAsync`, not the cryptic
compiler version). But we don't walk the heap to reconstruct the logical "who awaited this
method" when the original caller isn't on the current thread — you only see what's actually
on the stack right now.

## Tips when something doesn't work

| What you see | Likely fix |
|---|---|
| Setting a breakpoint on a function name doesn't catch it | Use the exact fully-qualified name. For generic methods, include the type parameter (`Foo``1.Bar`). |
| The trace shows everything at the same depth | Increase the *frames per event* setting so each captured stack reaches your other traced methods. |
| Variable values are `null` when you expect data | Your breakpoint may have stopped *before* the variable was assigned — check the source snippet that comes back with the stop. |
| The server shows as disconnected in `/mcp` | The `NETCOREDBG_PATH` env var isn't pointing at the binary. Test it: `NETCOREDBG_PATH=… aspnetcore-debugger-mcp` should start in a regular shell. |
