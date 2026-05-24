# Install & configure

Once per machine: install the tool. Once per project: register it. Then just chat.

## Step 1 — Install the MCP server *(once per machine)*

Needs the **.NET 10 SDK** ([download](https://dotnet.microsoft.com/download)).

```bash
dotnet tool install -g AspNetCoreDebuggerMcp --prerelease
```

`--prerelease` is needed while the version is `0.x` (or any `-preview`/`-rc` suffix). Drop it once a stable `1.0.0` ships.

The package ships prebuilt `netcoredbg` (Samsung's MIT-licensed .NET debugger) for **linux-x64, linux-arm64, win-x64, osx-x64, osx-arm64**. The tool auto-detects your platform at startup — no separate install needed.

That puts the `aspnetcore-debugger-mcp` binary on your `PATH`. Verify:

```bash
aspnetcore-debugger-mcp
# starts and idles on stdin; Ctrl-C to exit
```

If it errors with *"netcoredbg not found"*, see [Troubleshooting](#troubleshooting) below.

## Step 2 — Register with your MCP client *(once per project, or globally)*

### Claude Code

Either CLI:

```bash
claude mcp add aspnetcore-debugger -- aspnetcore-debugger-mcp
```

Or edit `.mcp.json` (project) / `~/.claude.json` (global):

```json
{
  "mcpServers": {
    "aspnetcore-debugger": {
      "command": "aspnetcore-debugger-mcp"
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
      "command": "aspnetcore-debugger-mcp"
    }
  }
}
```

Restart Claude Desktop after editing.

## Step 3 — Just chat with Claude

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

## Using a custom netcoredbg build

The bundled binary is pinned to a specific Samsung release. If you need to use a custom build (e.g. a newer Samsung release, a patched binary, or an unbundled architecture), set `NETCOREDBG_PATH`:

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

The env var wins over the bundled binary. See [macOS arm64 — building netcoredbg](macos-arm64.md) if you want to build your own.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| `/mcp` shows the server as failed / disconnected | The `aspnetcore-debugger-mcp` binary isn't on PATH. Run the verify command from Step 1 in a regular shell. |
| Tool calls error with *"netcoredbg not found"* | The bundled binary for your RID couldn't be found — your platform may not be one of the five we bundle. Set `NETCOREDBG_PATH` to a binary you've built or downloaded. |
| Trace tool errors with "user breakpoints exist" | Trace mode requires no user BPs — call `breakpoint_remove` on each first, or `trace_stop` if a previous trace is still active. |
