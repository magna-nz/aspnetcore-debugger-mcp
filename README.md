# aspnetcore-debugger-mcp

An MIT-licensed [Model Context Protocol](https://modelcontextprotocol.io/) server that gives AI agents
interactive debugging capabilities for .NET / ASP.NET Core applications.

It lets an AI agent (Claude, etc.) **run and debug a .NET app the way a developer would** — launch or
attach to a process, set breakpoints, step through code, inspect real variable values, catch exceptions
with full context, and test fixes live.

## How it works

```
Claude (MCP client)
   │  MCP  (stdio / JSON-RPC)
   ▼
aspnetcore-debugger-mcp        ← this server
   │  DAP  (Debug Adapter Protocol)
   ▼
netcoredbg                     ← MIT debugger engine, runs as a child process
   │  ICorDebug
   ▼
target .NET process
```

The server is a protocol bridge: an MCP server on one side, a DAP client on the other. It drives
`netcoredbg` (Samsung's MIT-licensed .NET debugger) and adds higher-level, agent-friendly tools on top.

## Status

Early development. See [SPEC.md](SPEC.md) for scope and [STATUS.md](STATUS.md) for current state.

## License

MIT — see [LICENSE](LICENSE). Bundles `netcoredbg` (also MIT).
