#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

usage() {
  cat <<'EOF'
Usage: AnalysisITC.Avalonia/Packaging/package.sh <windows|linux|macos> [platform options]

Examples:
  AnalysisITC.Avalonia/Packaging/package.sh windows
  AnalysisITC.Avalonia/Packaging/package.sh linux --runtime linux-x64
  AnalysisITC.Avalonia/Packaging/package.sh macos --runtime osx-arm64 --notarize

Run the Windows target from PowerShell on Windows. Linux DEB packages should be
built on Linux, and signed/notarized macOS packages must be built on macOS.
See AnalysisITC.Avalonia/Packaging/README.md for signing environment variables.
EOF
}

if [[ $# -lt 1 ]]; then
  usage
  exit 2
fi

target="$1"
shift

case "$target" in
  windows)
    if ! command -v pwsh >/dev/null 2>&1; then
      echo "ERROR: PowerShell 7 (pwsh) is required for Windows packaging." >&2
      exit 1
    fi
    exec pwsh -NoProfile -File "$SCRIPT_DIR/windows/package-windows.ps1" "$@"
    ;;
  linux)
    exec "$SCRIPT_DIR/linux/package-linux.sh" "$@"
    ;;
  macos)
    exec "$SCRIPT_DIR/macos/package-macos.sh" "$@"
    ;;
  -h|--help|help)
    usage
    ;;
  *)
    echo "ERROR: Unknown target '$target'." >&2
    usage
    exit 2
    ;;
esac
