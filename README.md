# AspNetcore Debugger MCP

[![CI](https://github.com/magna-nz/aspnetcore-debugger-mcp/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/magna-nz/aspnetcore-debugger-mcp/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/vpre/AspNetCoreDebuggerMcp.svg?label=NuGet)](https://www.nuget.org/packages/AspNetCoreDebuggerMcp)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/)
[![MCP](https://img.shields.io/badge/MCP-compatible-005FBA)](https://modelcontextprotocol.io/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**An MIT-licensed [Model Context Protocol](https://modelcontextprotocol.io/) server that lets an
AI agent (Claude, etc.) debug your .NET / ASP.NET Core app — conversationally.**

Instead of *"I think the bug is around line 42, try this"*, the agent runs your code, pauses it,
reads the actual runtime values, mutates state to test a fix, and answers grounded in what it
actually saw.

26 tools across launching, breakpoints, stepping, inspection, expression evaluation, exception
autopsy, hang analysis, and **server-side request tracing** that captures the full call chain
with variables — without you setting any breakpoint manually.

## A taste — real output

You tell Claude *"trace what `GET /order/42` does"* and the agent traces 5 methods, hits the
endpoint, and returns:

```
[+  679ms] → OrderController.GetOrder()       id=42, data=null
[+  711ms] --→ OrderService.LookupOrder()     id=42, raw=null, enriched=null
[+  735ms] ----→ OrderRepository.FetchById()  id=42
[+  759ms] ------→ SqlClient.ExecuteQuery()   sql="SELECT * FROM orders WHERE id = 42"
[+  783ms] ----→ EnrichmentService.Enrich()   raw="rows(sql=SELECT * FROM orders WHERE id …"
```

— Controller → Service → Repository → SqlClient (4 levels), with `Enrich` correctly shown as a
sibling of the Repository call at depth 2. Each line is one captured method entry with its
actual locals. No breakpoints set manually.

[See 6 more worked examples →](docs/examples.md)

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

## Use it in 4 steps

1. **Install prerequisites** — .NET 10 SDK + `netcoredbg` ([prebuilt for Linux/Win/Intel macOS](https://github.com/Samsung/netcoredbg/releases) · [build for macOS arm64](docs/macos-arm64.md))
2. **Install the tool** — `dotnet tool install -g AspNetCoreDebuggerMcp --prerelease`
3. **Register with Claude:**
   ```bash
   claude mcp add aspnetcore-debugger \
     -e NETCOREDBG_PATH=/path/to/netcoredbg \
     -- aspnetcore-debugger-mcp
   ```
   (Or edit `.mcp.json` / `claude_desktop_config.json` directly.)
4. **Just chat with Claude.** `/mcp` confirms it's connected. From there, describe what you want — *"why does this endpoint return null"* — and the agent picks the right tools.

[Full install + troubleshooting →](docs/install.md)

## Tools at a glance

| Category | Tools |
|---|---|
| **Session** | launch / attach / disconnect / state |
| **Breakpoints** | line (conditional/hit-count/logpoint), function, exception, data, remove, list |
| **Execution** | continue / pause / step / blocking wait-for-stop |
| **Inspection** | threads / stack / variables / evaluate / set-variable / **`stack_explore` composite** |
| **Diagnostics** | **`exception_autopsy`** · **`hang_analyze`** · **`trace_start/get/stop`** · stdout drain |

26 tools total. [Full reference with parameters →](docs/tools.md)

## Docs

- **[Install & configure](docs/install.md)** — 4 steps, both Claude Code & Desktop, troubleshooting
- **[Worked examples](docs/examples.md)** — 7 real use cases with actual tool output
- **[Full tool reference](docs/tools.md)** — every parameter on every tool
- **[Known limits](docs/limits.md)** — when *not* to use this tool, adapter & tracing limits
- **[macOS Apple Silicon — building netcoredbg](docs/macos-arm64.md)**
- **[Contributing](docs/contributing.md)** — repo layout, tests, dev loop

## License

MIT — see [LICENSE](LICENSE). Built on [netcoredbg](https://github.com/Samsung/netcoredbg) (MIT)
and the [ModelContextProtocol SDK](https://github.com/modelcontextprotocol/csharp-sdk) (MIT).
