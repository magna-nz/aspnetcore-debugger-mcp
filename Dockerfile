# Multi-stage build for the aspnetcore-debugger-mcp MCP server.
#
# This image is what Glama.ai uses to build, start, and introspect the server.
# At runtime the MCP server speaks JSON-RPC over stdio; Glama can attach to it
# and call `tools/list` to enumerate the tool surface.
#
# Build:  docker build -t aspnetcore-debugger-mcp .
# Run:    docker run -i --rm aspnetcore-debugger-mcp     # -i for stdin (MCP is stdio)

# ---- Build stage --------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# `unzip` is needed for the Samsung win-x64 archive; the SDK image doesn't include it.
RUN apt-get update && apt-get install -y --no-install-recommends unzip \
    && rm -rf /var/lib/apt/lists/*

# Fetch bundled netcoredbg binaries (linux-x64 + linux-arm64 needed at runtime
# in-container; others ship for completeness). Cached as a separate layer from
# the source copy so source edits don't re-trigger the ~17 MB download.
COPY scripts/fetch-netcoredbg-binaries.sh ./scripts/
RUN bash scripts/fetch-netcoredbg-binaries.sh

COPY . .
RUN dotnet publish src/AspNetCoreDebuggerMcp -c Release -o /app

# ---- Runtime stage ------------------------------------------------------------
# ASP.NET runtime image because netcoredbg's bundled DLLs include some
# Microsoft.CodeAnalysis bits that resolve against the ASP.NET shared
# framework; using `aspnet` is the safer choice over plain `runtime`.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# MCP servers are stdio-based — no port to expose.
ENTRYPOINT ["dotnet", "AspNetCoreDebuggerMcp.dll"]
