#!/usr/bin/env bash
# POSIX entry point for agent.ps1. The logic lives in one place (agent.ps1 + Agent.Lib.ps1, which
# Pester tests) so the two can never disagree about slots, ports or generated files.
#
#   scripts/agent/agent.sh new alpha --up
#   scripts/agent/agent.sh up alpha --no-hot-reload
#   scripts/agent/agent.sh remove alpha --purge
#
# --kebab-case flags are translated to PowerShell's -PascalCase (--no-hot-reload -> -NoHotReload).
set -euo pipefail

if ! command -v pwsh >/dev/null 2>&1; then
  echo "agent.sh needs PowerShell 7 (pwsh): https://learn.microsoft.com/powershell/scripting/install/installing-powershell" >&2
  echo "The dev container image already has it." >&2
  exit 127
fi

args=()
for arg in "$@"; do
  case "$arg" in
    --*)
      pascal=$(printf '%s' "${arg#--}" | awk -F- '{ for (i = 1; i <= NF; i++) printf "%s%s", toupper(substr($i, 1, 1)), substr($i, 2) }')
      args+=("-$pascal")
      ;;
    *) args+=("$arg") ;;
  esac
done

exec pwsh -NoProfile -File "$(dirname "$0")/agent.ps1" "${args[@]}"
