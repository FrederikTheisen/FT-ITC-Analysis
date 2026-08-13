#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "$SCRIPT_DIR/common.sh"

require_macos
require_command create-dmg
require_command plutil
require_command shasum
require_app

DMG="$(dmg_path)"
rm -rf "$STAGE_DIR"
mkdir -p "$STAGE_DIR" "$OUTPUT_DIR"
cp -R "$APP" "$STAGE_DIR/FT-ITC.app"

rm -f "$DMG" "$DMG.sha256"
create-dmg \
  --volname "FT-ITC Analysis" \
  --volicon "$ROOT/Resources/AppIcon.icns" \
  --background "$PACKAGING_DIR/installer-background.png" \
  --window-pos 200 120 \
  --window-size 800 500 \
  --icon-size 140 \
  --icon "FT-ITC.app" 200 230 \
  --app-drop-link 600 230 \
  --no-internet-enable \
  "$DMG" \
  "$STAGE_DIR"

write_checksum "$DMG"
echo "Packaged $DMG"
