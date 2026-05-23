# aspnetcore-debugger-mcp

An MIT-licensed [Model Context Protocol](https://modelcontextprotocol.io/) server that gives AI
agents interactive debugging for .NET / ASP.NET Core applications.

It lets an AI agent (Claude, etc.) **run and debug a .NET app the way a developer would** — launch
or attach to a process, set breakpoints (line, conditional, hit-count, logpoint, function,
exception, data), step through code, inspect real variable values, evaluate expressions, **mutate
state mid-run** to test fixes, get a one-call exception autopsy, and diagnose hangs.

## How it works

```
Claude (MCP client)
   │  MCP  (stdio / JSON-RPC)
   ▼
aspnetcore-debugger-mcp        ← this server
   │  DAP  (Debug Adapter Protocol)
   ▼
netcoredbg                     ← MIT debugger engine, child process
   │  ICorDebug
   ▼
target .NET process
```

The server is a protocol bridge: an MCP server on one side, a DAP client on the other. It drives
[`netcoredbg`](https://github.com/Samsung/netcoredbg) (Samsung's MIT-licensed .NET debugger) and
adds higher-level, agent-friendly composites — including a one-call `exception_autopsy`, `hang_analyze`
across all threads, and auto-context (top frame + source snippet) on every stop.

## Install

You need **three** things: the .NET 10 SDK, `netcoredbg`, and this tool.

### 1. .NET 10 SDK

Install from [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download).

### 2. netcoredbg

| Platform              | Get it from                                                                                                  |
|-----------------------|--------------------------------------------------------------------------------------------------------------|
| Linux x64 / arm64     | [Samsung release](https://github.com/Samsung/netcoredbg/releases) — extract anywhere                          |
| Windows x64           | [Samsung release](https://github.com/Samsung/netcoredbg/releases) — extract anywhere                          |
| macOS Intel (x64)     | [Samsung release](https://github.com/Samsung/netcoredbg/releases) — extract anywhere                          |
| **macOS Apple Silicon (arm64)** | **No prebuilt** — Samsung doesn't ship one. Build from source with `scripts/build-netcoredbg-macos-arm64.sh` |

Tell the server where the binary lives via one of:

- Set `NETCOREDBG_PATH` to the absolute path of the `netcoredbg` binary (recommended)
- Or place `netcoredbg` on your `PATH`
- Or place it next to this tool's assembly

### 3. The MCP server

```bash
dotnet tool install -g AspNetCoreDebuggerMcp --prerelease
```

Run it directly to verify:

```bash
NETCOREDBG_PATH=/path/to/netcoredbg aspnetcore-debugger-mcp
```

## Configure with an MCP client

### Claude Code

Add to `.mcp.json` in your project (or the user-wide config):

```json
{
  "mcpServers": {
    "aspnetcore-debugger": {
      "command": "aspnetcore-debugger-mcp",
      "env": {
        "NETCOREDBG_PATH": "/absolute/path/to/netcoredbg"
      }
    }
  }
}
```

### Claude Desktop

Add to `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "aspnetcore-debugger": {
      "command": "aspnetcore-debugger-mcp",
      "env": {
        "NETCOREDBG_PATH": "/absolute/path/to/netcoredbg"
      }
    }
  }
}
```

## Tools (22)

| Category | Tools |
|---|---|
| **Session** | `debug_launch`, `debug_attach`, `debug_disconnect`, `debug_state` |
| **Breakpoints** | `breakpoint_set` (line + conditional + hit-count + logpoint), `breakpoint_set_function`, `breakpoint_set_exception`, `breakpoint_set_data`, `breakpoint_remove`, `breakpoint_list` |
| **Execution** | `debug_continue`, `debug_pause`, `debug_step` (in/over/out), `breakpoint_wait` (blocks until next stop, with top frame + source snippet) |
| **Inspection** | `threads_list`, `stacktrace_get`, `variables_get` (recursive expansion + truncation), `evaluate`, `variables_set` |
| **Diagnostics** | `exception_autopsy` (one call: exception chain + frames + top-frame locals + source snippet), `hang_analyze` (auto-pause + classify each thread's blocking primitive), `process_read_output` (drain buffered stdout/stderr) |

## Example session

```
You:    Debug my app and find why GetUser returns null.

Claude: [debug_launch program=bin/Debug/net10.0/MyApp.dll stopAtEntry=true]
        [breakpoint_set sourcePath=…/UserService.cs line=42]
        [debug_continue]
        [breakpoint_wait]   →  stopped at UserService.cs:42 (snippet attached)
        [variables_get]      →  Locals: userId="" (empty string!)
        [evaluate "users.Count"] → 1247
        I found the issue: the caller is passing an empty userId. Want me to test
        a fix by setting a real value and continuing?

You:    Yes, try userId = "u-001".

Claude: [variables_set expression=userId value="\"u-001\""]
        [debug_continue]
        [breakpoint_wait]   →  function returned successfully with a user.
        Confirmed — the bug is in the caller (empty userId). Here's the fix…
```

## macOS Apple Silicon — build netcoredbg

Samsung doesn't publish an arm64 macOS build of netcoredbg. Use the included script:

```bash
./scripts/build-netcoredbg-macos-arm64.sh
```

Requirements: Xcode Command Line Tools, CMake (`brew install cmake`), .NET 10 SDK. The script clones
`Samsung/netcoredbg`, configures, builds, and prints the `NETCOREDBG_PATH` to export.

> Samsung notes the arm64 macOS build is "community supported and may not work as expected." In our
> testing on Apple Silicon, the build succeeds and the full debugger functionality used by this
> project (launch, breakpoints, step, inspect, evaluate, setExpression, exceptionInfo, output
> events) works correctly. `dataBreakpointInfo` returns `E_NOTIMPL` — see "Known limits" below.

## Known limits

- **`breakpoint_set_data` depends on the adapter.** netcoredbg currently returns `E_NOTIMPL` for
  `dataBreakpointInfo`. The tool surfaces this as a clean error; if/when netcoredbg adds support,
  the tool will just work.
- **`process_write_input` is not implemented.** DAP launch mode owns the debuggee's stdin; piping
  input would require a different launch architecture (launch the process ourselves, have
  netcoredbg attach).

## Build from source

```bash
git clone https://github.com/magna-nz/aspnetcore-debugger-mcp.git
cd aspnetcore-debugger-mcp
dotnet build
dotnet test
NETCOREDBG_PATH=/path/to/netcoredbg dotnet run --project src/AspNetCoreDebuggerMcp
```

## License

MIT — see [LICENSE](LICENSE). Depends on `netcoredbg` (also MIT) and the `ModelContextProtocol` SDK.
