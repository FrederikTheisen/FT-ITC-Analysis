#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "$SCRIPT_DIR/common.sh"

require_macos
require_executable "$MONO_MSBUILD"

echo "Cleaning the $CONFIGURATION macOS release..."
"$MONO_MSBUILD" "$PROJECT" \
  /t:Clean \
  /p:Configuration="$CONFIGURATION" \
  /p:Platform=AnyCPU \
  /p:OutputPath="$PUBLISH_DIR/" \
  /m:1 \
  /v:minimal

rm -rf "$WORK_DIR" "$OUTPUT_DIR"
echo "Cleaned $WORK_DIR and $OUTPUT_DIR"
