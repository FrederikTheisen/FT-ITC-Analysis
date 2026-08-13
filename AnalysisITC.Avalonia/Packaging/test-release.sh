#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

dotnet test "$ROOT/AnalysisITC.Core.Tests/AnalysisITC.Core.Tests.csproj" \
  --configuration Release \
  --verbosity minimal

dotnet test "$ROOT/AnalysisITC.Avalonia.Tests/AnalysisITC.Avalonia.Tests.csproj" \
  --configuration Release \
  --verbosity minimal
