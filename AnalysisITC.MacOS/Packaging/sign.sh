#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "$SCRIPT_DIR/common.sh"

NOTARIZE=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --notarize) NOTARIZE=1; shift ;;
    *) echo "ERROR: Unknown option '$1'." >&2; exit 2 ;;
  esac
done

require_macos
require_command codesign
require_command plutil
require_command shasum
require_app
[[ -n "$SIGN_IDENTITY" ]] || {
  echo "ERROR: Set FTITC_MAC_SIGN_IDENTITY or configure CodeSigningKey in the project." >&2
  exit 1
}

DMG="$(dmg_path)"
[[ -f "$DMG" ]] || {
  echo "ERROR: Packaged disk image not found: $DMG" >&2
  echo "Run $PACKAGING_DIR/package.sh first." >&2
  exit 1
}

# package.sh checksums the unsigned DMG. Remove that checksum before changing
# the file, and only publish a replacement after every requested signing step
# has completed successfully.
rm -f "$DMG.sha256"
codesign --verify --deep --strict --verbose=2 "$APP"
codesign --force --timestamp --sign "$SIGN_IDENTITY" "$DMG"
codesign --verify --strict --verbose=2 "$DMG"

NOTARY_PROFILE="${FTITC_MAC_NOTARY_PROFILE:-FTITC-notary}"

if [[ $NOTARIZE -eq 1 ]]; then
  require_command xcrun
  xcrun notarytool submit "$DMG" \
  	--keychain-profile "$NOTARY_PROFILE" \
  	--wait
  xcrun stapler staple "$DMG"
  xcrun stapler validate "$DMG"
  spctl -a -t open --context context:primary-signature -vv "$DMG"
fi

write_checksum "$DMG"
echo "Signed $DMG"
