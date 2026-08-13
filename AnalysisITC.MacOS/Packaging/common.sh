#!/usr/bin/env bash

PACKAGING_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$PACKAGING_DIR/../.." && pwd)"
PROJECT="$ROOT/AnalysisITC.MacOS/AnalysisITC.MacOS.csproj"
CONFIGURATION="${FTITC_MAC_CONFIGURATION:-Release}"

WORK_DIR="$PACKAGING_DIR/work"
PUBLISH_DIR="$WORK_DIR/publish"
STAGE_DIR="$WORK_DIR/dmg-root"
OUTPUT_DIR="$PACKAGING_DIR/output"

MONO_MSBUILD="${FTITC_MONO_MSBUILD:-/Library/Frameworks/Mono.framework/Versions/Current/Commands/msbuild}"
NUGET="${FTITC_NUGET:-/Library/Frameworks/Mono.framework/Versions/Current/Commands/nuget}"
PROJECT_SIGN_IDENTITY="$(sed -n 's:.*<CodeSigningKey>\([^<]*\)</CodeSigningKey>.*:\1:p' "$PROJECT" | tail -n 1)"
SIGN_IDENTITY="${FTITC_MAC_SIGN_IDENTITY:-$PROJECT_SIGN_IDENTITY}"

APP="$PUBLISH_DIR/FT-ITC.app"

require_macos() {
  [[ "$(uname -s)" == "Darwin" ]] || {
    echo "ERROR: The Xamarin.Mac release must be produced on macOS." >&2
    exit 1
  }
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || {
    echo "ERROR: $1 is required." >&2
    exit 1
  }
}

require_executable() {
  [[ -x "$1" ]] || {
    echo "ERROR: Required executable not found: $1" >&2
    exit 1
  }
}

require_app() {
  [[ -d "$APP" ]] || {
    echo "ERROR: Published application not found: $APP" >&2
    echo "Run $PACKAGING_DIR/publish.sh first." >&2
    exit 1
  }
}

app_version() {
  plutil -extract CFBundleShortVersionString raw "$APP/Contents/Info.plist"
}

dmg_path() {
  local version
  version="$(app_version)"
  printf '%s/ft-itc-analysis_%s_macos-universal.dmg\n' "$OUTPUT_DIR" "$version"
}

write_checksum() {
  local artifact="$1"
  (
    cd "$(dirname "$artifact")"
    shasum -a 256 "$(basename "$artifact")" > "$(basename "$artifact").sha256"
  )
}
