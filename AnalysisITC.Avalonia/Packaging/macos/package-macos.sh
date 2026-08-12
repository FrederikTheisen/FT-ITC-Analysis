#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
PROJECT="$ROOT/AnalysisITC.Avalonia/AnalysisITC.Avalonia.csproj"
RUNTIME="osx-arm64"
CONFIGURATION="Release"
UNSIGNED=0
NOTARIZE=0
NO_RESTORE=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --runtime) RUNTIME="$2"; shift 2 ;;
    --configuration) CONFIGURATION="$2"; shift 2 ;;
    --unsigned) UNSIGNED=1; shift ;;
    --notarize) NOTARIZE=1; shift ;;
    --no-restore) NO_RESTORE=1; shift ;;
    *) echo "ERROR: Unknown option '$1'." >&2; exit 2 ;;
  esac
done

case "$RUNTIME" in osx-arm64|osx-x64) ;; *) echo "ERROR: Supported runtimes are osx-arm64 and osx-x64." >&2; exit 2 ;; esac
[[ "$(uname -s)" == "Darwin" ]] || { echo "ERROR: macOS packages must be produced on macOS." >&2; exit 1; }
for command in dotnet codesign hdiutil; do command -v "$command" >/dev/null 2>&1 || { echo "ERROR: $command is required." >&2; exit 1; }; done

VERSION="$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$PROJECT" | head -n 1)"
[[ -n "$VERSION" ]] || { echo "ERROR: Could not read Version from $PROJECT." >&2; exit 1; }

BUNDLE_ID="${FTITC_MAC_BUNDLE_ID:-org.ft-itc.Analysis}"
SIGN_IDENTITY="${FTITC_MAC_SIGN_IDENTITY:-}"
NOTARY_PROFILE="${FTITC_MAC_NOTARY_PROFILE:-}"
PUBLISH_DIR="$ROOT/artifacts/publish/$RUNTIME"
PACKAGE_DIR="$ROOT/artifacts/packages"
APP="$ROOT/artifacts/package/macos-$RUNTIME/FT-ITC Analysis.app"
DMG="$PACKAGE_DIR/FT-ITC-Analysis-$VERSION-$RUNTIME.dmg"

if [[ $UNSIGNED -eq 0 && -z "$SIGN_IDENTITY" ]]; then
  echo "ERROR: Set FTITC_MAC_SIGN_IDENTITY to a Developer ID Application identity or explicitly use --unsigned." >&2
  exit 1
fi
if [[ $NOTARIZE -eq 1 && -z "$NOTARY_PROFILE" ]]; then
  echo "ERROR: Set FTITC_MAC_NOTARY_PROFILE to a notarytool keychain profile." >&2
  exit 1
fi

rm -rf "$PUBLISH_DIR" "$(dirname "$APP")"
mkdir -p "$PUBLISH_DIR" "$APP/Contents/MacOS" "$APP/Contents/Resources" "$PACKAGE_DIR"
publish_args=(publish "$PROJECT" -c "$CONFIGURATION" -r "$RUNTIME" --self-contained true -o "$PUBLISH_DIR")
if [[ $NO_RESTORE -eq 1 ]]; then publish_args+=(--no-restore); fi
dotnet "${publish_args[@]}"

cp -R "$PUBLISH_DIR/." "$APP/Contents/MacOS/"
cp "$ROOT/Resources/AppIcon.icns" "$APP/Contents/Resources/AppIcon.icns"
cp "$ROOT/Resources/FTXTCProjectFileIcon.icns" "$APP/Contents/Resources/FTXTCProjectFileIcon.icns"
sed -e "s|@BUNDLE_ID@|$BUNDLE_ID|g" -e "s|@VERSION@|$VERSION|g" "$SCRIPT_DIR/Info.plist.in" > "$APP/Contents/Info.plist"
chmod 0755 "$APP/Contents/MacOS/FT-ITC Analysis"
plutil -lint "$APP/Contents/Info.plist"

if [[ $UNSIGNED -eq 0 ]]; then
  while IFS= read -r -d '' candidate; do
    if file "$candidate" | grep -q 'Mach-O'; then
      codesign --force --timestamp --options runtime --sign "$SIGN_IDENTITY" "$candidate"
    fi
  done < <(find "$APP/Contents/MacOS" -type f -print0)
  codesign --force --timestamp --options runtime --entitlements "$SCRIPT_DIR/Entitlements.plist" --sign "$SIGN_IDENTITY" "$APP"
  codesign --verify --deep --strict --verbose=2 "$APP"
fi

DMG_ROOT="$(dirname "$APP")/dmg-root"
mkdir -p "$DMG_ROOT"
cp -R "$APP" "$DMG_ROOT/"
ln -s /Applications "$DMG_ROOT/Applications"
rm -f "$DMG"
hdiutil create -volname "FT-ITC Analysis" -srcfolder "$DMG_ROOT" -ov -format UDZO "$DMG"

if [[ $UNSIGNED -eq 0 ]]; then
  codesign --force --timestamp --sign "$SIGN_IDENTITY" "$DMG"
  codesign --verify --verbose=2 "$DMG"
fi

if [[ $NOTARIZE -eq 1 ]]; then
  xcrun notarytool submit "$DMG" --keychain-profile "$NOTARY_PROFILE" --wait
  xcrun stapler staple "$DMG"
  xcrun stapler validate "$DMG"
  spctl -a -t open --context context:primary-signature -vv "$DMG"
fi

shasum -a 256 "$DMG" > "$DMG.sha256"
echo "Created $DMG"
