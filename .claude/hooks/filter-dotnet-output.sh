#!/bin/bash
# PreToolUse(Bash): keep dotnet build/test output out of the context window.
#
# A full `dotnet build`/`dotnet test` run prints hundreds of lines of restore and
# per-project noise. Everything after it in the session re-reads those lines on every
# turn (cached input, but paid per turn). This rewrites a plain dotnet invocation so it
# prints only what the outcome depends on: errors, warnings, failed tests, the summary.
#
# Deliberately conservative — it passes the command through untouched when the user
# already shaped the output themselves (pipe, redirect, custom --logger/--verbosity).
set -euo pipefail

input=$(cat)
cmd=$(printf '%s' "$input" | jq -r '.tool_input.command // empty')

passthrough() { echo '{}'; exit 0; }

[ -n "$cmd" ] || passthrough
case "$cmd" in
  dotnet\ test*|dotnet\ build*) ;;
  *) passthrough ;;
esac
case "$cmd" in
  *\|*|*'>'*|*--logger*|*--verbosity*|*-v\ *) passthrough ;;
esac

# Run it, keep the real exit code, show only the lines that decide pass/fail.
keep='error|warning|Fehler|[Ff]ailed|FAILED|Passed!|Failed!|Skipped!|Assert|Expected:|Actual:|^\s+at .*\.cs:line'
wrapped="_o=\$(mktemp); $cmd > \"\$_o\" 2>&1; _rc=\$?; grep -aE '$keep' \"\$_o\" | head -120; \
echo \"--- filtered by .claude/hooks/filter-dotnet-output.sh · \$(wc -l < \"\$_o\") lines total · exit \$_rc ---\"; \
rm -f \"\$_o\"; exit \$_rc"

jq -n --arg c "$wrapped" '{
  hookSpecificOutput: {
    hookEventName: "PreToolUse",
    permissionDecision: "allow",
    updatedInput: { command: $c }
  }
}'
