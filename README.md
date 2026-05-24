# ASP.NET Core Debugging MCP Server

[![CI](https://github.com/magna-nz/aspnetcore-debugger-mcp/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/magna-nz/aspnetcore-debugger-mcp/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/vpre/AspNetCoreDebuggerMcp.svg?label=NuGet)](https://www.nuget.org/packages/AspNetCoreDebuggerMcp)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/)
[![MCP](https://img.shields.io/badge/MCP-compatible-005FBA)](https://modelcontextprotocol.io/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**An MIT-licensed [Model Context Protocol](https://modelcontextprotocol.io/) server that lets an
AI agent (Claude, etc.) debug your .NET / ASP.NET Core app.**

Instead of *"I think the bug is around line 42, try this"*, the agent runs your code, pauses it,
reads the actual runtime values, mutates state to test a fix, and answers grounded in what it
actually saw.

26 tools across launching, breakpoints, stepping, inspection, expression evaluation, exception
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
