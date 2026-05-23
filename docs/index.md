# aspnetcore-debugger-mcp — documentation

An MIT-licensed [Model Context Protocol](https://modelcontextprotocol.io/) server that lets an AI
agent (Claude, etc.) debug your .NET / ASP.NET Core app — conversationally.

## Pages

- **[Install & configure](install.md)** — the 4-step setup (prerequisites → tool install → MCP register → chat).
- **[Worked examples](examples.md)** — 7 real use cases with the actual tool output the agent sees.
- **[Full tool reference](tools.md)** — every one of the 26 tools, grouped by category.
- **[macOS Apple Silicon — building netcoredbg](macos-arm64.md)** — what to do when no Samsung prebuilt exists.
- **[Known limits](limits.md)** — what this tool *isn't* for, and where the adapter falls short.
- **[Contributing / building from source](contributing.md)** — repo layout, tests, dev loop.

## What it does, in one paragraph

Instead of *"I think the bug is around line 42, try this"*, the agent runs your code, pauses it,
reads the actual runtime values, mutates state to test a fix, and answers grounded in what it
actually saw. 26 tools across launching, breakpoints, stepping, inspection, expression evaluation,
exception autopsy, hang analysis, and **server-side request tracing** that captures the full call
chain with variables — without you setting any breakpoint manually.

## Quick teaser — real output

You tell Claude *"trace what `GET /order/42` does"* and you get back:

```
[+  679ms] → OrderController.GetOrder()       id=42, data=null
[+  711ms] --→ OrderService.LookupOrder()     id=42, raw=null, enriched=null
[+  735ms] ----→ OrderRepository.FetchById()  id=42
[+  759ms] ------→ SqlClient.ExecuteQuery()   sql="SELECT * FROM orders WHERE id = 42"
[+  783ms] ----→ EnrichmentService.Enrich()   raw="rows(sql=SELECT * FROM orders WHERE id …"
```

4 layers, with `Enrich` correctly shown as a sibling of the Repository call at depth 2. No
manual breakpoints. See [the full examples](examples.md) for the other patterns.

## License

MIT. Built on [netcoredbg](https://github.com/Samsung/netcoredbg) (MIT) and the
[ModelContextProtocol SDK](https://github.com/modelcontextprotocol/csharp-sdk) (MIT).
