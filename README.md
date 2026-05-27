# ASP.NET Core Debugging MCP Server

### The cross-platform .NET debugging MCP — runs on **Linux**, **macOS**, and **Windows**.

<!-- mcp-name: io.github.magna-nz/aspnetcore-debugger-mcp -->

[![CI](https://github.com/magna-nz/aspnetcore-debugger-mcp/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/magna-nz/aspnetcore-debugger-mcp/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/vpre/AspNetCoreDebuggerMcp.svg?label=NuGet)](https://www.nuget.org/packages/AspNetCoreDebuggerMcp)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/)
[![MCP](https://img.shields.io/badge/MCP-compatible-005FBA)](https://modelcontextprotocol.io/)
[![Platforms](https://img.shields.io/badge/runs%20on-Linux%20%7C%20macOS%20%7C%20Windows-success)](#platforms)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Glama Quality](https://glama.ai/mcp/servers/magna-nz/aspnetcore-debugger-mcp/badges/score.svg?v=2)](https://glama.ai/mcp/servers/magna-nz/aspnetcore-debugger-mcp)

> **MIT-licensed [MCP](https://modelcontextprotocol.io/) server that lets an AI agent (Claude, etc.) debug your .NET / ASP.NET Core app interactively.**
>
> ✅ **Linux** (x64, arm64) &nbsp;·&nbsp; ✅ **macOS** (Intel + Apple Silicon) &nbsp;·&nbsp; ✅ **Windows** (x64)
>
> netcoredbg is bundled for every platform — nothing extra to install.

Instead of *"I think the bug is around line 42, try this"*, the agent runs your code, pauses it,
reads the actual runtime values, mutates state to test a fix, and answers grounded in what it
actually saw.

27 tools across launching, breakpoints, stepping, inspection, expression evaluation, exception
autopsy, hang analysis, and **server-side request tracing** that captures the full call chain
with variables — without you setting any breakpoint manually.

## See it in action

### Why is my EF Core query returning empty?

**You**

> `GET /users/active` returns `[]`, but I know there are active users in the DB. What's the
> query actually doing?

**Claude**

> I'll pause inside `UserRepository.GetActiveAsync` and read what EF Core actually sent.
>
> ```sql
> SELECT u.*
> FROM Users AS u
> WHERE u.IsActive = 1 AND u.TenantId = @__tenantId_0
>
> @__tenantId_0 = 0
> ```
>
> The SQL is correct; the parameter is wrong. `TenantId = 0`, but your tenants start at 1.
> `ICurrentUser.TenantId` is being read in the repository constructor — before the request
> scope resolves it. Fix: read the tenant inside the query (or apply it via `HasQueryFilter`
> with `IHttpContextAccessor`) instead of capturing it at construction.

### Why is my JWT auth returning 401?

**You**

> `/admin/users` is 401-ing for Anna, but her token looks valid to me. What does the auth
> handler actually see?

**Claude**

> I'll break inside `JwtBearerHandler.HandleAuthenticateAsync` after her request lands.
>
> ```
> Token signature: ✓ valid
> Claims built:
>   name : anna
>   role : User
>   iss  : https://auth.staging.example
>   aud  : app-api
>   exp  : 2026-05-26T14:12:00Z      (not expired)
> ```
>
> The token validates fine — but the claim is `role=User`. Your endpoint is
> `[Authorize(Roles="Admin")]`. So the 401 is correct: Anna isn't actually an admin yet
> upstream. Either her IdP record needs updating, or this policy is wrong.

### Test a fix without restarting

**You**

> I think capping `discount` at 100 fixes the bug. Test that hypothesis without me touching
> the code.

**Claude**

> I'll pause inside `ApplyDiscount` on the next checkout and mutate `discount` in place.
>
> ```
> Paused at PricingService.cs:47
>   discount = 250        ← bug: two promos stacked
>
> Set discount = 100, resumed.
>
> Result:
>   subtotal = $400
>   final    = $300
>   refund-service complaints: 0
> ```
>
> Capping at 100 unblocks the flow downstream. Hypothesis confirmed — time to make the edit
> for real and decide where the cap belongs (`ApplyDiscount` itself, or upstream in
> `PromoEngine`).

### My app is hung — what's actually blocking?

**You**

> `/reports` hangs forever in staging. Attach and tell me why.

**Claude**

> Attached. Ran `hang_analyze`.
>
> ```
> Thread 12 (HTTP request)
>   → blockedOnTask    at ReportService.cs:38   Task.Wait() on T#14
>
> Thread 14 (worker)
>   → blockedOnMonitor at SqlConnectionPool.cs:91
>                       waiting for SqlConnection
>                       (pool max=5, in-use=5)
> ```
>
> Classic sync-over-async deadlock. Thread 12 calls `.Wait()` on a task that itself needs to
> open another `SqlConnection` from a fully-held pool. The connection it's holding can't be
> released until the task completes; the task can't proceed until a connection frees up. Fix:
> `await` all the way down in `ReportService`, or don't hold a connection across the awaited
> call.

### Where is a slow request actually spending its time?

**You**

> `GET /order/42` takes 800ms. I don't want to set breakpoints all over — just show me where
> the time is going.

**Claude**

> I'll auto-instrument your order pipeline and fire the request. Each entry logs the
> timestamp + arguments, then resumes — the code runs near-normal speed.
>
> ```
> [+   4ms] → OrderController.GetOrder()       id=42
> [+   6ms] --→ OrderService.LookupOrder()     id=42
> [+   9ms] ----→ OrderRepository.FetchById()  id=42
> [+  14ms] ------→ SqlClient.ExecuteQuery()   sql="SELECT * FROM orders WHERE id=42"
> [+ 287ms] ----→ EnrichmentService.Enrich()   raw="rows(...)"
> [+ 781ms] ------→ HttpClient.PostAsync()     url="https://crm.internal/enrich"
> ```
>
> Time is in the CRM enrichment HTTP call — 500ms inside `HttpClient.PostAsync`. DB itself
> was 5ms. Worth caching `EnrichmentService.Enrich` or moving it off the request path.

[More examples →](docs/examples.md)

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

A protocol bridge with agent-friendly composites on top — `exception_autopsy`, `stack_explore`,
`hang_analyze`, and the trace tools — that bundle multiple DAP requests into a single tool call.

## Use it in 3 steps

1. **Install the tool** — needs the [.NET 10 SDK](https://dotnet.microsoft.com/download).
   ```bash
   dotnet tool install -g AspNetCoreDebuggerMcp --prerelease
   ```
   The package bundles prebuilt `netcoredbg` for `linux-x64`, `linux-arm64`, `win-x64`, `osx-x64`, and `osx-arm64` — no separate install needed.
2. **Register with Claude** — either the quick CLI command:
   ```bash
   claude mcp add aspnetcore-debugger -- aspnetcore-debugger-mcp
   ```
   …or edit `.mcp.json` (project-scoped) / `~/.claude.json` (global) / `claude_desktop_config.json` (Claude Desktop) directly:
   ```json
   {
     "mcpServers": {
       "aspnetcore-debugger": {
         "command": "aspnetcore-debugger-mcp"
       }
     }
   }
   ```
3. **Just chat with Claude.** `/mcp` confirms it's connected. From there, describe what you want — *"why does this endpoint return null"* — and the agent picks the right tools.

[Full install + troubleshooting →](docs/install.md)

## Platforms

Bundled `netcoredbg` binary is selected at runtime — no per-platform install dance.

| OS | Architectures | Status |
|---|---|---|
| **Linux** | x64, arm64 | ✅ Supported (Samsung prebuilt) |
| **macOS** | Intel (x64), Apple Silicon (arm64) | ✅ Supported (arm64 built by us, since Samsung doesn't ship one) |
| **Windows** | x64 | ✅ Supported (Samsung prebuilt) |

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) on the host. The MCP server itself
is a cross-platform .NET global tool — same install command everywhere.

## Tools (27)

| Category | Tools | What it's for |
|---|---|---|
| **Session** | `debug_launch`, `debug_attach`, `debug_disconnect`, `debug_state` | Start, attach to, or stop a debug session |
| **Execution** | `debug_continue`, `debug_pause`, `debug_step`, `breakpoint_wait` | Drive the debuggee and wait for it to stop |
| **Breakpoints** | `breakpoint_set`, `breakpoint_set_function`, `breakpoint_set_exception`, `breakpoint_set_data`, `breakpoint_remove`, `breakpoint_list` | Line, function, exception, and data breakpoints |
| **Inspection** | `threads_list`, `stacktrace_get`, `variables_get`, `variables_set`, `evaluate`, `stack_explore` | Examine and mutate program state |
| **Exception Autopsy** | `exception_autopsy` | One call: exception chain + top frames + locals + source snippet |
| **Hang / Deadlock** | `hang_analyze` | Auto-pause, classify each thread's blocking pattern (Monitor / Task / Semaphore / async / …) |
| **Request Tracing** | `trace_start`, `trace_get`, `trace_stop` | Server-side request tracing — auto-instrument a call chain and capture arguments at every entry |
| **Process I/O** | `process_read_output` | Drain the debuggee's stdout/stderr |
| **Health** | `debugger_health` | Quick check that netcoredbg loaded and the bundled binary is reachable |

[Full tool reference with parameters →](docs/tools.md)

## How this compares

| Project | License | Platforms | Approach | .NET |
|---|---|---|---|---|
| **aspnetcore-debugger-mcp** *(this)* | **MIT** | **Linux + macOS + Windows** | netcoredbg via DAP, ASP.NET-focused composites (request tracing, hang analysis) | Native, .NET 10 |
| [debug-mcp](https://github.com/jkolo/debug-mcp) | AGPL-3.0 | Linux only (Win/macOS planned) | ICorDebug direct, Roslyn code nav | Native, .NET 10 |
| [mcp-debugger](https://github.com/debugmcp/mcp-debugger) | — | Cross-platform | DAP | Via external debugger |
| [dap-mcp](https://github.com/KashunCheng/dap_mcp) | — | Cross-platform | DAP | Via external debugger |
| [LLDB MCP](https://lldb.llvm.org/use/mcp.html) | NCSA | Cross-platform | Native LLDB | No |

Different sweet spots: this project is the **MIT, cross-platform** option, with ASP.NET-flavoured
composites on top of a DAP. debug-mcp goes deeper into runtime internals via ICorDebug but is
Linux-only and AGPL today.

## Docs

- **[Install & configure](docs/install.md)** — 3 steps, both Claude Code & Desktop, troubleshooting
- **[What you can do with it](docs/examples.md)** — 7 things you can ask Claude to do for you
- **[Full tool reference](docs/tools.md)** — every parameter on every tool
- **[Known limits](docs/limits.md)** — when *not* to use this tool, adapter & tracing limits
- **[macOS Apple Silicon — building netcoredbg](docs/macos-arm64.md)**
- **[Contributing](docs/contributing.md)** — repo layout, tests, dev loop

## License

MIT — see [LICENSE](LICENSE). Built on [netcoredbg](https://github.com/Samsung/netcoredbg) (MIT)
and the [ModelContextProtocol SDK](https://github.com/modelcontextprotocol/csharp-sdk) (MIT).
