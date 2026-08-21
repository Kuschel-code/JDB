#!/bin/bash
# SessionStart: make the .NET toolchain usable in Claude Code on the web.
#
# Cloud containers ship without a .NET SDK — `dotnet` is not on PATH at all — so a web
# session cannot build or test and has to push and wait for CI to learn whether the code
# compiles. This installs the SDK the projects target (net9.0) and warms the NuGet cache,
# so `dotnet build` / `dotnet test` work from the first turn.
#
# Local sessions are left alone: they already have whatever SDK the developer installed.
set -euo pipefail

# Web/cloud only.
[ "${CLAUDE_CODE_REMOTE:-}" = "true" ] || exit 0

DOTNET_CHANNEL="9.0"

# Prefer an SDK the environment's setup script already provisioned (that one lives in the
# cached filesystem snapshot; anything this hook installs does not, because the snapshot is
# taken before SessionStart hooks run). Fall back to a per-user install under $HOME.
if [ -z "${DOTNET_ROOT:-}" ] && command -v dotnet >/dev/null 2>&1; then
    _dotnet_bin="$(readlink -f "$(command -v dotnet)")"
    DOTNET_ROOT="$(dirname "$_dotnet_bin")"
fi
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

log() { echo "[session-start] $*" >&2; }

has_net9() {
    command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q '^9\.'
}

# Verified 2026-08-20: this environment's egress policy answers 403 to
# builds.dotnet.microsoft.com (where dot.net redirects), while api.nuget.org is allowed.
# The install therefore cannot succeed until the environment's network policy permits the
# Microsoft download hosts. Say so plainly instead of failing silently — the session still
# works, it just has to verify through CI.
blocked_note() {
    log "ERROR: could not fetch the .NET SDK."
    log "  The environment's network policy blocks the Microsoft download hosts"
    log "  (builds.dotnet.microsoft.com / dotnetcli.azureedge.net -> 403 at the egress proxy)."
    log "  Check with: curl -sS \"\$HTTPS_PROXY/__agentproxy/status\""
    log "  Fix: allow those hosts in this environment's network policy"
    log "  (https://code.claude.com/docs/en/claude-code-on-the-web), then start a new session."
    log "  Until then: no local build/test — push the branch and read the CI build-test job."
}

# 1. SDK — idempotent, so a cached container skips straight past it.
if has_net9; then
    log "SDK $(dotnet --version) already present, skipping install"
else
    log "installing .NET $DOTNET_CHANNEL SDK into $DOTNET_ROOT (~200 MB, once per container)"
    installer="$(mktemp)"
    # Microsoft's official installer script (learn.microsoft.com/dotnet/core/tools/dotnet-install-script).
    if ! curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$installer"; then
        blocked_note
        rm -f "$installer"; exit 0        # never block the session on a network failure
    fi
    if ! bash "$installer" --channel "$DOTNET_CHANNEL" --install-dir "$DOTNET_ROOT" --no-path; then
        blocked_note
        rm -f "$installer"; exit 0
    fi
    rm -f "$installer"
    log "installed $(dotnet --version)"
fi

# 2. Persist the toolchain for every command the session runs.
if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
    {
        echo "export DOTNET_ROOT=\"$DOTNET_ROOT\""
        echo "export PATH=\"$DOTNET_ROOT:$DOTNET_ROOT/tools:\$PATH\""
        echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1'
        echo 'export DOTNET_NOLOGO=1'
    } >> "$CLAUDE_ENV_FILE"
    log "wrote DOTNET_ROOT/PATH to \$CLAUDE_ENV_FILE"
fi

# 3. Warm the caches. The container image is snapshotted after this hook, so a later
#    session gets restored packages for free. Failures here are not fatal — a missing
#    package should surface at build time with a real error, not as a dead session.
cd "${CLAUDE_PROJECT_DIR:-$(dirname "$0")/../..}"
[ -f .config/dotnet-tools.json ] && { dotnet tool restore || log "WARN: tool restore failed"; }
[ -f MetaHub.sln ] && { dotnet restore MetaHub.sln || log "WARN: restore failed"; }

log "ready — dotnet build / dotnet test now work in this session"
