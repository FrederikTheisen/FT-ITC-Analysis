#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
ROOT="$(cd "$SCRIPT_DIR/../.." && pwd -P)"

dotnet test "$ROOT/AnalysisITC.Core.Tests/AnalysisITC.Core.Tests.csproj" \
  --configuration Release \
  --verbosity minimal

dotnet build "$ROOT/AnalysisITC.Avalonia.Tests/AnalysisITC.Avalonia.Tests.csproj" \
  --configuration Release \
  --verbosity minimal

dotnet test "$ROOT/AnalysisITC.Avalonia.Tests/AnalysisITC.Avalonia.Tests.csproj" \
  --configuration Release \
  --no-build \
  --verbosity minimal
