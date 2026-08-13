#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

[[ -f "$ROOT/AnalysisITC.sln" ]] || {
  echo "ERROR: Could not identify the FT-ITC Analysis repository root." >&2
  exit 1
}

generated_paths=(
  "$ROOT/AnalysisITC.Avalonia/bin"
  "$ROOT/AnalysisITC.Avalonia/obj"
  "$ROOT/AnalysisITC.Avalonia.Tests/bin"
  "$ROOT/AnalysisITC.Avalonia.Tests/obj"
  "$ROOT/AnalysisITC.Core/bin"
  "$ROOT/AnalysisITC.Core/obj"
  "$ROOT/AnalysisITC.Core.Tests/bin"
  "$ROOT/AnalysisITC.Core.Tests/obj"
  "$ROOT/AnalysisITC.Core.Tests/TestResults"
  "$ROOT/AnalysisITC.MacOS/bin"
  "$ROOT/AnalysisITC.MacOS/obj"
  "$ROOT/AnalysisITC.Web/bin"
  "$ROOT/AnalysisITC.Web/obj"
  "$ROOT/AnalysisITC.Web.Tests/bin"
  "$ROOT/AnalysisITC.Web.Tests/obj"
  "$ROOT/artifacts"
  "$ROOT/bin"
  "$ROOT/dist"
  "$ROOT/obj"
  "$ROOT/publish"
)

for generated_path in "${generated_paths[@]}"; do
  if [[ -e "$generated_path" ]]; then
    rm -rf -- "$generated_path"
    echo "Removed ${generated_path#"$ROOT"/}"
  fi
done
