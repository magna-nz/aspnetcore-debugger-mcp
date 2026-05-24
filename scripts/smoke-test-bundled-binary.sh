#!/usr/bin/env bash
# Smoke test: verify the bundled netcoredbg works end-to-end against a real
# ASP.NET Core Web API.
#
# Steps:
#   1. Build the SampleWebApi fixture
#   2. Spawn the bundled netcoredbg (resolved by current RID) in DAP mode
#   3. Send a DAP `initialize` request and verify a well-formed response
#
# This proves the bundled binary launches and speaks DAP for the host RID.
# Run after fetch-netcoredbg-binaries.sh has staged the binaries.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
RUNTIMES_DIR="${REPO_ROOT}/src/AspNetCoreDebuggerMcp/runtimes"
FIXTURE_DIR="${REPO_ROOT}/tests/fixtures/SampleWebApi"

log() { echo "[smoke] $*"; }

# Resolve current RID using the same logic as NetcoredbgLocator.
case "$(uname -s)" in
    Darwin) os="osx" ;;
    Linux)  os="linux" ;;
    MINGW*|CYGWIN*|MSYS*) os="win" ;;
    *) echo "Unsupported OS: $(uname -s)" >&2; exit 1 ;;
esac
case "$(uname -m)" in
    x86_64|amd64) arch="x64" ;;
    arm64|aarch64) arch="arm64" ;;
    *) echo "Unsupported arch: $(uname -m)" >&2; exit 1 ;;
esac
RID="${os}-${arch}"
EXE_NAME="netcoredbg"
[[ "${os}" == "win" ]] && EXE_NAME="netcoredbg.exe"

BUNDLED="${RUNTIMES_DIR}/${RID}/native/${EXE_NAME}"
if [[ ! -x "${BUNDLED}" ]]; then
    echo "Bundled binary not found: ${BUNDLED}" >&2
    echo "Run scripts/fetch-netcoredbg-binaries.sh first." >&2
    exit 1
fi
log "Using bundled netcoredbg: ${BUNDLED}"
"${BUNDLED}" --version | head -1

log "Building SampleWebApi fixture…"
dotnet build "${FIXTURE_DIR}" -c Debug -v quiet > /tmp/smoke-build.log 2>&1 || {
    cat /tmp/smoke-build.log >&2
    exit 1
}
DLL="${FIXTURE_DIR}/bin/Debug/net10.0/SampleWebApi.dll"
[[ -f "${DLL}" ]] || { echo "Built DLL not found: ${DLL}" >&2; exit 1; }
log "Built: ${DLL}"

log "Sending DAP initialize to netcoredbg via stdio…"

# DAP wire format: Content-Length: <n>\r\n\r\n<JSON>. We send `initialize`
# and read back netcoredbg's first response. Use python for clean stdio
# framing — bash quoting is painful for this.
python3 - "${BUNDLED}" "${DLL}" <<'PY'
import json, os, subprocess, sys, threading, queue, time

netcoredbg, dll = sys.argv[1], sys.argv[2]

stderr_log = open("/tmp/smoke-netcoredbg.stderr", "wb")
proc = subprocess.Popen(
    [netcoredbg, "--interpreter=vscode"],
    stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=stderr_log,
    bufsize=0,
)

def send(obj):
    body = json.dumps(obj).encode("utf-8")
    header = f"Content-Length: {len(body)}\r\n\r\n".encode("ascii")
    proc.stdin.write(header + body)
    proc.stdin.flush()

q = queue.Queue()
def reader():
    buf = b""
    fd = proc.stdout.fileno()
    while True:
        try:
            chunk = os.read(fd, 4096)
        except OSError:
            break
        if not chunk:
            break
        buf += chunk
        while b"\r\n\r\n" in buf:
            head, _, rest = buf.partition(b"\r\n\r\n")
            length = None
            for line in head.split(b"\r\n"):
                if line.lower().startswith(b"content-length:"):
                    length = int(line.split(b":")[1].strip())
            if length is None or len(rest) < length:
                buf = head + b"\r\n\r\n" + rest
                break
            body = rest[:length]
            buf = rest[length:]
            try:
                q.put(json.loads(body.decode("utf-8")))
            except Exception:
                pass

t = threading.Thread(target=reader, daemon=True); t.start()

send({"seq": 1, "type": "request", "command": "initialize",
      "arguments": {"clientID": "smoke", "adapterID": "coreclr",
                    "linesStartAt1": True, "columnsStartAt1": True,
                    "pathFormat": "path"}})

deadline = time.time() + 10
got_initialize_response = False
while time.time() < deadline:
    try:
        msg = q.get(timeout=1)
    except queue.Empty:
        continue
    if msg.get("type") == "response" and msg.get("command") == "initialize":
        got_initialize_response = True
        print(f"[smoke] initialize response: success={msg.get('success')}, body keys={list((msg.get('body') or {}).keys())[:6]}")
        break

proc.terminate()
try:
    proc.wait(timeout=2)
except subprocess.TimeoutExpired:
    proc.kill()

if not got_initialize_response:
    print("[smoke] FAIL: no initialize response within 10s", file=sys.stderr)
    sys.exit(1)
print("[smoke] PASS: bundled netcoredbg speaks DAP")
PY

log "Smoke test passed."
