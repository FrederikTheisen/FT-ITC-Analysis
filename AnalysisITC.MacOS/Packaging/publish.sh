#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "$SCRIPT_DIR/common.sh"

UNSIGNED=0
NO_RESTORE=0
SKIP_TESTS=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --unsigned) UNSIGNED=1; shift ;;
    --no-restore) NO_RESTORE=1; shift ;;
    --skip-tests) SKIP_TESTS=1; shift ;;
    *) echo "ERROR: Unknown option '$1'." >&2; exit 2 ;;
  esac
done

require_macos
require_executable "$MONO_MSBUILD"
require_command plutil
require_command lipo
if [[ $SKIP_TESTS -eq 0 ]]; then require_command dotnet; fi

if [[ $SKIP_TESTS -eq 0 ]]; then
  dotnet test "$ROOT/AnalysisITC.Core.Tests/AnalysisITC.Core.Tests.csproj" \
    --configuration Release \
    --verbosity minimal
fi

if [[ $NO_RESTORE -eq 0 ]]; then
  require_executable "$NUGET"
  "$NUGET" restore "$ROOT/AnalysisITC.MacOS/packages.config" \
    -PackagesDirectory "$ROOT/packages" \
    -NonInteractive
fi

rm -rf "$PUBLISH_DIR"
mkdir -p "$PUBLISH_DIR"

build_args=(
  "$PROJECT"
  /t:Build
  /p:Configuration="$CONFIGURATION"
  /p:Platform=AnyCPU
  /p:OutputPath="$PUBLISH_DIR/"
  /m:1
  /v:minimal
)
if [[ $UNSIGNED -eq 1 ]]; then
  build_args+=(/p:EnableCodeSigning=False)
fi
"$MONO_MSBUILD" "${build_args[@]}"

require_app
plutil -lint "$APP/Contents/Info.plist"

ARCH_INFO="$(lipo -info "$APP/Contents/MacOS/FT-ITC")"
echo "$ARCH_INFO"
for architecture in x86_64 arm64; do
  grep -qw "$architecture" <<< "$ARCH_INFO" || {
    echo "ERROR: Published application does not include $architecture." >&2
    exit 1
  }
done

if [[ $UNSIGNED -eq 0 ]]; then
  require_command codesign
  codesign --verify --deep --strict --verbose=2 "$APP"
fi

echo "Published $APP"
