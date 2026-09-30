#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
repository_root="$(cd -- "${script_dir}/.." && pwd -P)"
verification_script="${repository_root}/eng/verify.ps1"

case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*) ;;
  *)
    echo "ERROR: ThirdLife's governed local verification runs on the active Windows Codex machine." >&2
    exit 1
    ;;
esac

if command -v pwsh >/dev/null 2>&1; then
  exec pwsh -NoLogo -NoProfile -File "${verification_script}" "$@"
fi

if command -v powershell.exe >/dev/null 2>&1; then
  if ! command -v cygpath >/dev/null 2>&1; then
    echo "ERROR: Git Bash cygpath is required to invoke Windows PowerShell." >&2
    exit 1
  fi
  windows_script="$(cygpath -w "${verification_script}")"
  exec powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "${windows_script}" "$@"
fi

echo "ERROR: PowerShell is required to run ThirdLife verification." >&2
exit 1
