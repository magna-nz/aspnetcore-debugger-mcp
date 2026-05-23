# Install & configure

Once per machine: install prerequisites and the tool. Once per project: register it. Then just chat.

## Step 1 — Prerequisites *(once per machine)*

- **.NET 10 SDK** — [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download)
- **netcoredbg** — Samsung's MIT-licensed .NET debugger that this server drives:

  | Platform | Get it from |
  |---|---|
  | Linux x64 / arm64 | [Samsung release](https://github.com/Samsung/netcoredbg/releases) — extract anywhere |
  | Windows x64 | [Samsung release](https://github.com/Samsung/netcoredbg/releases) — extract anywhere |
  | macOS Intel (x64) | [Samsung release](https://github.com/Samsung/netcoredbg/releases) — extract anywhere |
  | **macOS Apple Silicon (arm64)** | No prebuilt — see [Building netcoredbg on macOS arm64](macos-arm64.md) |

  Tell the server where the binary lives via one of:
  - Set `NETCOREDBG_PATH` to its absolute path *(recommended)*
  - Put `netcoredbg` on your `PATH`
  - Place it next to this tool's assembly
- **Claude Code** or **Claude Desktop** installed.

## Step 2 — Install the MCP server *(once per machine)*

```bash
dotnet tool install -g AspNetCoreDebuggerMcp --prerelease
```

`--prerelease` is needed while the version is `0.1.0-preview` (or any `-preview`/`-rc` suffix). Drop it once a stable `1.0.0` ships.

That puts the `aspnetcore-debugger-mcp` binary on your `PATH`. Verify:

```bash
NETCOREDBG_PATH=/path/to/netcoredbg aspnetcore-debugger-mcp
# starts and idles on stdin; Ctrl-C to exit
```

If it errors with *"netcoredbg not found"*, the env var or PATH isn't pointing at the binary — re-check Step 1.

## Step 3 — Register with your MCP client *(once per project, or globally)*

### Claude Code

Either CLI:

```bash
claude mcp add aspnetcore-debugger \
  -e NETCOREDBG_PATH=/path/to/netcoredbg \
  -- aspnetcore-debugger-mcp
```

Or edit `.mcp.json` (project) / `~/.claude.json` (global):

```json
{
  "mcpServers": {
    "aspnetcore-debugger": {
      "command": "aspnetcore-debugger-mcp",
      "env": { "NETCOREDBG_PATH": "/absolute/path/to/netcoredbg" }
    }
  }
}
```

### Claude Desktop

Edit `claude_desktop_config.json` — same JSON shape goes in the `mcpServers` block:

```json
{
  "mcpServers": {
    "aspnetcore-debugger": {
      "command": "aspnetcore-debugger-mcp",
      "env": { "NETCOREDBG_PATH": "/absolute/path/to/netcoredbg" }
    }
  }
}
```

Restart Claude Desktop after editing.

## Step 4 — Just chat with Claude

Open Claude Code or Claude Desktop in your .NET project. Run `/mcp` — `aspnetcore-debugger` should show as **connected**.

From here, **don't invoke the tools yourself**. Just describe what you want:

> *"Debug my API and figure out why `GET /users/42` returns null."*

Claude picks the right tools (`debug_launch`, `breakpoint_set`, `variables_get`, etc.) and reports back what it actually saw at runtime. See the [worked examples](examples.md) for the patterns this enables.

## Updating

```bash
dotnet tool update -g AspNetCoreDebuggerMcp --prerelease
```

## Uninstalling

```bash
dotnet tool uninstall -g AspNetCoreDebuggerMcp
```

Then remove the `aspnetcore-debugger` entry from your MCP client config.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| `/mcp` shows the server as failed / disconnected | The `aspnetcore-debugger-mcp` binary isn't on PATH, or `NETCOREDBG_PATH` is wrong. Run the verify command from Step 2 in a regular shell. |
| Tool calls error with *"netcoredbg not found"* | `NETCOREDBG_PATH` env var not propagating from MCP client config — make sure it's set in the `env` block of the server entry. |
| First debug session hangs on launch | netcoredbg arm64 macOS build hasn't been built — see [macOS arm64](macos-arm64.md). |
| Trace tool errors with "user breakpoints exist" | Trace mode requires no user BPs — call `breakpoint_remove` on each first, or `trace_stop` if a previous trace is still active. |
