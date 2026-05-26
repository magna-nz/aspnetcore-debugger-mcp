# ASP.NET Core Debugging MCP Server

### The cross-platform .NET debugging MCP — runs on **Linux**, **macOS**, and **Windows**.

<!-- mcp-name: io.github.magna-nz/aspnetcore-debugger-mcp -->

[![CI](https://github.com/magna-nz/aspnetcore-debugger-mcp/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/magna-nz/aspnetcore-debugger-mcp/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/vpre/AspNetCoreDebuggerMcp.svg?label=NuGet)](https://www.nuget.org/packages/AspNetCoreDebuggerMcp)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/)
[![MCP](https://img.shields.io/badge/MCP-compatible-005FBA)](https://modelcontextprotocol.io/)
[![Platforms](https://img.shields.io/badge/runs%20on-Linux%20%7C%20macOS%20%7C%20Windows-success)](#platforms)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

<a href="https://glama.ai/mcp/servers/magna-nz/aspnetcore-debugger-mcp">
  <img width="380" height="200" src="https://glama.ai/mcp/servers/magna-nz/aspnetcore-debugger-mcp/badge" alt="aspnetcore-debugger-mcp MCP server" />
</a>

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

### Why is my endpoint returning null?

**You**

> `GET /users/42` is returning null. Can you figure out why?

**Claude**

> I'll run your app and stop inside the handler when the request hits.
>
> I followed the call from the controller into the user service and then into the repository.
> Here's the query the repo is issuing against the DB:
>
> ```
> SELECT * FROM users WHERE id = '42'
> ```
>
> That's the bug — the `id` is being dropped into the SQL as text instead of as a value, but
> the `id` column is an `int`. Postgres returns zero rows for that comparison. Fix is to use a
> parameter for the id and pass it as an int.

### Why did my app throw?

**You**

> My `/checkout` endpoint throws a `NullReferenceException` somewhere. Find it.

**Claude**

> I'll catch any unhandled exception and run through your checkout flow. Caught it.
>
> ```
> Thrown at OrderService.cs:67
>
>   65: if (cart == null) throw new ArgumentNullException(nameof(cart));
>   66: var total = 0m;
> → 67: foreach (var item in cart.Items) total += item.Price;
>
> At the throw:
>   cart.Items = null
> ```
>
> `LoadCart(userId)` on line 62 returned a `Cart` whose `Items` is `null` for new users — and
> the null check on line 65 only checks `cart` itself, not `cart.Items`. Two fix options:
> initialise `Items = []` in the `Cart` constructor, or use `cart.Items ?? []` on line 67.

### Trace a request through the call chain

**You**

> Hit `GET /order/42` and show me which methods get called and the arguments at each one — I
> don't want to set breakpoints by hand.

**Claude**

> I'll instrument your order pipeline (Controller → Service → Repository → SqlClient) and fire
> the request. The instrumentation auto-resumes after each capture, so your code runs at
> near-normal speed.
>
> ```
> [+  679ms] → OrderController.GetOrder()       id=42, data=null
> [+  711ms] --→ OrderService.LookupOrder()     id=42, raw=null, enriched=null
> [+  735ms] ----→ OrderRepository.FetchById()  id=42
> [+  759ms] ------→ SqlClient.ExecuteQuery()   sql="SELECT * FROM orders WHERE id = 42"
> [+  783ms] ----→ EnrichmentService.Enrich()   raw="rows(sql=…)"
> ```
>
> Every line is one real method entry with the actual arguments at that moment. The arrows
> show call depth. The request finished with 200 — no manual breakpoints, no pauses.

[See 5 more →](docs/examples.md)

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
