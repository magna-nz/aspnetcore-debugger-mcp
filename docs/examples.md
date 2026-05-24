# What you can do with it

Eight everyday scenarios. Each shows what you'd ask Claude in plain English and what Claude
might come back with. The wording is illustrative — the actual response depends on your code —
but the **shape** of the conversation is real, and the values shown come from real runs.

- [Is the debugger set up correctly?](#is-the-debugger-set-up-correctly)
- [Why is my endpoint returning the wrong data?](#why-is-my-endpoint-returning-the-wrong-data)
- [Try a fix without editing my code](#try-a-fix-without-editing-my-code)
- [Why did my app throw this exception?](#why-did-my-app-throw-this-exception)
- [Show me the path a request takes through my code](#show-me-the-path-a-request-takes-through-my-code)
- [Where am I right now in the call stack?](#where-am-i-right-now-in-the-call-stack)
- [Why is my app hung?](#why-is-my-app-hung)
- [Show me what the server is logging](#show-me-what-the-server-is-logging)

---

## Is the debugger set up correctly?

**You**

> Before we start — is the debugger ready? I'm not sure if it picked up the right binary on
> my Mac.

**Claude**

> I'll check.
>
> Yes — the bundled netcoredbg is healthy on your machine.
>
> - **Platform:** macOS, arm64 (RID `osx-arm64`)
> - **Binary:** the one that shipped with the NuGet package — no `NETCOREDBG_PATH`
>   override is set, and you're not falling back to a binary on `PATH`
> - **Version:** `NET Core debugger 3.1.3-1 (Release)`
>
> You're good to launch a debug session whenever you want.

<sub>*(under the hood: one call to `debugger_health` — reports RID, resolved netcoredbg path, source (bundled / env var / PATH), and `--version`)*</sub>

---

## Why is my endpoint returning the wrong data?

**You**

> `GET /hello/world` is returning `null` for the greeting. Can you figure out why?

**Claude**

> I'll run your app, stop inside the `/hello/{name}` handler, and read the real values.
>
> Here's what I see at the moment the request hits:
>
> ```
> Paused in Program.cs at line 6
>
>     4: app.MapGet("/hello/{name}", (string name) =>
>     5: {
> →   6:     var greeting = $"Hello, {name}!";
>     7:     var length = greeting.Length;
>     8:     return Results.Ok(new { greeting, length });
>
> Local values:
>   name = "world"
>   greeting = null   (not assigned yet)
> ```
>
> The route parameter `name` came in as `"world"` like you'd expect. The reason the response
> looks wrong is in the lines below — there's a `string? name = null;` further down that
> shadows the route parameter, and that's the `name` that ends up in the JSON response.
>
> Want me to test it by setting the local to a non-null value and continuing? If the response
> comes back correct, we've confirmed the diagnosis.

<sub>*(under the hood: launch · breakpoint at line 6 · wait for request · read locals · evaluate expression)*</sub>

---

## Try a fix without editing my code

**You**

> What if `userId` were `"u-001"` instead of empty? Would the rest of the function work?

**Claude**

> Setting `userId` to `"u-001"` in the paused frame and continuing…
>
> The function returned a real `User` object: `{ Id: "u-001", Name: "Alice", … }`.
>
> So the function itself works correctly when given a valid id. The bug is in the **caller** —
> something upstream is passing an empty string instead of the parsed id. Worth checking the
> code that builds the request.

<sub>*(under the hood: change a value in a live frame · resume · check the result)*</sub>

---

## Why did my app throw this exception?

**You**

> My app is throwing a `NullReferenceException` somewhere on this request. Catch it for me and
> tell me what's going on.

**Claude**

> I'll set a break on unhandled exceptions and re-run the request.
>
> Caught one. Here's the full picture:
>
> - **Type:** `System.NullReferenceException` — *"Object reference not set to an instance of an
>   object."*
> - **Where it threw:** `Program.cs:8`
>   ```
>     6: var greeting = $"Hello, {name}!";
>     7: string? name = null;
>   → 8: Console.WriteLine(name!.Length);
>   ```
> - **What was in scope at the throw:** `name = null`
>
> So you've declared a local `name` on line 7 that's never assigned, and then dereferenced it
> on line 8. The `!` is silencing the nullability warning but not the runtime crash.
>
> Fix: either give it a real value or remove the local entirely (your route already has a
> `name` parameter that's not null).

<sub>*(under the hood: exception breakpoint · wait for the throw · one-call exception autopsy with the chain, frames, locals, and source snippet)*</sub>

---

## Show me the path a request takes through my code

**You**

> Trace what happens when I hit `GET /order/42`. I want to see which methods get called and the
> parameter values at each one.

**Claude**

> I'll instrument the methods in your order pipeline (Controller → Service → Repository →
> SqlClient → EnrichmentService) and hit the endpoint. The instrumentation runs at near-normal
> speed — no manual breakpoints, no visible pauses.
>
> Here's the trace:
>
> ```
> [+  679ms] → OrderController.GetOrder()       id=42, data=null
> [+  711ms] --→ OrderService.LookupOrder()     id=42, raw=null, enriched=null
> [+  735ms] ----→ OrderRepository.FetchById()  id=42
> [+  759ms] ------→ SqlClient.ExecuteQuery()   sql="SELECT * FROM orders WHERE id = 42"
> [+  783ms] ----→ EnrichmentService.Enrich()   raw="rows(sql=SELECT * FROM orders WHERE id …"
> ```
>
> The arrows show the call depth — `SqlClient.ExecuteQuery` is 3 layers deep, called via
> Repository → Service → Controller. `EnrichmentService.Enrich` is at the same level as
> `FetchById` because the Service called Enrich *after* the Repository returned.
>
> Every line is one real method entry with the actual argument values at that moment. The
> request completed normally and returned 200.

<sub>*(under the hood: server-side tracing — methods you name get auto-captured + auto-resumed; your code runs at near-normal speed)*</sub>

---

## Where am I right now in the call stack?

**You**

> I've hit a breakpoint deep inside the handler — show me the full call chain and what's in
> scope at each level.

**Claude**

> ```
> Microsoft.AspNetCore.Server.Kestrel...HttpProtocol.ProcessRequests()
>      │
>      ▼
> Microsoft.AspNetCore.HostFiltering.HostFilteringMiddleware.Invoke()
>      │
>      ▼
> [Native Frames]
>      │
>      ▼
> Program.<>c.<<Main>$>b__0_0()   [Program.cs:6]  ◄ paused here
>   name = "world", greeting = null, length = 0
> ```
>
> You're paused inside the request handler at line 6, called via the Kestrel pipeline. The
> immediately useful values are on the bottom frame — `name = "world"` from the URL,
> `greeting` and `length` aren't assigned yet. The middleware frames above are mostly Kestrel
> internals you can ignore unless something's going wrong in the pipeline itself.

<sub>*(under the hood: one tool call returns the stack, the locals at every frame, and a rendered tree)*</sub>

---

## Why is my app hung?

**You**

> My app is unresponsive. Can you check what every thread is doing and tell me what they're
> waiting on?

**Claude**

> Pausing and looking at every thread…
>
> **3 threads are blocked on synchronization primitives:**
>
> - **Thread 1 (Main)** — blocked on `Task.Wait()`. Waiting for a Task to complete.
> - **Thread 8 (ThreadPool Worker)** — blocked on `Monitor.Wait()` (a `lock` statement).
>   Holding nothing visible, waiting on someone else to release the lock.
> - **Thread 12 (ThreadPool Worker)** — same: `Monitor.Wait()`.
>
> The other 4 threads are idle.
>
> Looks like a classic **sync-over-async** pattern: Thread 1 called `.Wait()` on a Task that's
> presumably waiting for one of the locks the other threads hold. Without the runtime telling
> me who owns which lock, I can't draw the cycle for you — but the stack traces of threads 8
> and 12 should point at the lock object.

<sub>*(under the hood: pause-all + per-thread stack walk + classify the top frame against known blocking primitives)*</sub>

---

## Show me what the server is logging

**You**

> Drain the server's output since I last asked — I want to see what ASP.NET logged.

**Claude**

> ```
> info: Microsoft.AspNetCore.Hosting.Diagnostics[1]
>       Request starting HTTP/1.1 GET http://localhost:5099/hello/world - null null
> info: Microsoft.AspNetCore.Hosting.Diagnostics[2]
>       Request finished HTTP/1.1 GET http://localhost:5099/hello/world - 200 - application/json; charset=utf-8 13.4ms
> info: MyApp.UserService[0]
>       Looking up user u-001
> ```
>
> Two requests, both 200, plus your own `ILogger` lines. No errors.

<sub>*(under the hood: the server buffers everything the debuggee writes to stdout/stderr; this drains the buffer)*</sub>

---

## See also

- **[Install](install.md)** — set this up if you haven't already
- **[Full tool reference](tools.md)** — every parameter on every tool, for when you want to be precise
- **[Known limits](limits.md)** — what this *isn't* for
