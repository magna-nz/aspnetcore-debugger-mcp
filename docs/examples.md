# Worked examples

Seven patterns this tool exists to make easy. Each example shows what you'd say to your AI
assistant and what the agent does in response. The tool outputs shown are real — straight from
the project's end-to-end test runs against a real ASP.NET Core Web API on netcoredbg.

- [1. Why does this endpoint return wrong data? — break and inspect](#1-why-does-this-endpoint-return-wrong-data--break-and-inspect)
- [2. Test a fix without changing code — mutate state mid-run](#2-test-a-fix-without-changing-code--mutate-state-mid-run)
- [3. Why did this throw? — one-call exception autopsy](#3-why-did-this-throw--one-call-exception-autopsy)
- [4. Trace the whole call chain through a request — no manual breakpoints](#4-trace-the-whole-call-chain-through-a-request--no-manual-breakpoints)
- [5. Walk through a layered request when stopped — `stack_explore`](#5-walk-through-a-layered-request-when-stopped--stack_explore)
- [6. Why is the app hung? — `hang_analyze`](#6-why-is-the-app-hung--hang_analyze)
- [7. Show me what's in the response logs — `process_read_output`](#7-show-me-whats-in-the-response-logs--process_read_output)

---

## 1. Why does this endpoint return wrong data? — break and inspect

> **You:** Why does `GET /hello/world` return `null` when I expect a greeting?

The agent:

1. `debug_launch program=bin/Debug/net10.0/WebApiTest.dll stopAtEntry=true`
2. `breakpoint_wait` → entry stop, ready to configure
3. `breakpoint_set sourcePath=Program.cs line=6` (inside the handler)
4. `debug_continue` + waits for Kestrel to come up
5. **You hit the endpoint** (or it tells `curl` to): `curl http://localhost:5099/hello/world`
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

---

## 2. Test a fix without changing code — mutate state mid-run

> **You:** What if `userId` were `"u-001"` instead of empty?

```
[variables_set expression="userId" value="\"u-001\""]
[debug_continue]
[breakpoint_wait]   → function returned with a real User. Confirmed: bug is in the caller.
```

`variables_set` uses DAP `setExpression` so it works on any lvalue, including object members
(`user.Name`, `dict[key]`, etc.) — not just bare variable names.

---

## 3. Why did this throw? — one-call exception autopsy

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

The agent gets type + inner-chain + frames + locals at the throw site + source snippet, **all
from a single tool call**. No grepping logs for a bare stack trace with no context.

---

## 4. Trace the whole call chain through a request — no manual breakpoints

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

How it works: each named method gets a **server-side** trace breakpoint that captures the call
(top frame + locals + stack) and **auto-continues immediately** — your code runs at near-normal
speed, never visibly pausing. Indentation reflects the depth of *traced ancestors* in each
frame's stack, so middleware and Kestrel internals don't inflate the depth. Sibling calls
(`FetchById` and `Enrich`, both called by `LookupOrder`) sit at the same level.

`trace_start` also captures unhandled exceptions during the trace (`includeExceptions=true` by
default), rendered with `⚠` and a small stack.

> Note: while a trace is active, the session can't also have user breakpoints. Call `trace_stop`
> first if you want to switch modes.

---

## 5. Walk through a layered request when stopped — `stack_explore`

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

Same data as `stacktrace_get` + `variables_get` per frame, but consolidated into one call and
with a human-readable tree string ready to drop into a reply. Async state-machine frames are
flattened back to their original method names (e.g. `UserService.<GetAsync>d__3.MoveNext` →
`UserService.GetAsync`).

---

## 6. Why is the app hung? — `hang_analyze`

```
[hang_analyze]
```

Auto-pauses if running, walks every thread, classifies each by what blocking primitive it's
stuck on (`Monitor` / `WaitHandle` / `Semaphore` / `Task` / `Thread.Join` / `Sleep` /
`AwaitingAsync`), returns a summary:

```
blockedCount: 3
notes: "Cycle detection requires lock-ownership data which DAP does not expose..."
threads: [
  { threadId: 1, name: "Main Thread",             blockingKind: "blockedOnTask",    topFrames: [...] },
  { threadId: 8, name: ".NET ThreadPool Worker",  blockingKind: "blockedOnMonitor", topFrames: [...] },
  ...
]
```

---

## 7. Show me what's in the response logs — `process_read_output`

```
[process_read_output]
```

Drains stdout/stderr accumulated since the last call. For an ASP.NET Core app with default
request logging, that includes the `Request starting` / `Request finished … 200` lines.

Useful when you just want the server-side view of a request without setting breakpoints.

---

## See also

- [Full tool reference](tools.md) — every parameter on every tool
- [Known limits](limits.md) — what this tool *isn't* for
- [Install](install.md) — to set up the prerequisites if you haven't already
