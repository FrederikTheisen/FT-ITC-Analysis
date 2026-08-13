#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
PROJECT="$ROOT/AnalysisITC.MacOS/AnalysisITC.MacOS.csproj"
CONFIGURATION="Release"
UNSIGNED=0
NOTARIZE=0
NO_RESTORE=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --unsigned) UNSIGNED=1; shift ;;
    --notarize) NOTARIZE=1; shift ;;
    --no-restore) NO_RESTORE=1; shift ;;
    *) echo "ERROR: Unknown option '$1'." >&2; exit 2 ;;
  esac
done

[[ "$(uname -s)" == "Darwin" ]] || { echo "ERROR: The Xamarin.Mac app must be packaged on macOS." >&2; exit 1; }

MONO_MSBUILD="${FTITC_MONO_MSBUILD:-/Library/Frameworks/Mono.framework/Versions/Current/Commands/msbuild}"
NOTARY_PROFILE="${FTITC_MAC_NOTARY_PROFILE:-}"
PROJECT_SIGN_IDENTITY="$(sed -n 's:.*<CodeSigningKey>\([^<]*\)</CodeSigningKey>.*:\1:p' "$PROJECT" | tail -n 1)"
SIGN_IDENTITY="${FTITC_MAC_SIGN_IDENTITY:-$PROJECT_SIGN_IDENTITY}"

[[ -x "$MONO_MSBUILD" ]] || { echo "ERROR: Xamarin/Mono msbuild was not found at $MONO_MSBUILD." >&2; exit 1; }
for command in dotnet hdiutil plutil codesign; do
  command -v "$command" >/dev/null 2>&1 || { echo "ERROR: $command is required." >&2; exit 1; }
done
if [[ $UNSIGNED -eq 0 && -z "$SIGN_IDENTITY" ]]; then
  echo "ERROR: Set FTITC_MAC_SIGN_IDENTITY or configure CodeSigningKey in the project." >&2
  exit 1
fi
if [[ $NOTARIZE -eq 1 && -z "$NOTARY_PROFILE" ]]; then
  echo "ERROR: Set FTITC_MAC_NOTARY_PROFILE to a notarytool keychain profile." >&2
  exit 1
fi

# The shared scientific core is the release gate for the legacy macOS UI.
dotnet test "$ROOT/AnalysisITC.Core.Tests/AnalysisITC.Core.Tests.csproj" \
  --configuration Release \
  --verbosity minimal

if [[ $NO_RESTORE -eq 0 ]]; then
  NUGET="${FTITC_NUGET:-/Library/Frameworks/Mono.framework/Versions/Current/Commands/nuget}"
  [[ -x "$NUGET" ]] || { echo "ERROR: NuGet was not found at $NUGET." >&2; exit 1; }
  "$NUGET" restore "$ROOT/AnalysisITC.MacOS/packages.config" \
    -PackagesDirectory "$ROOT/packages" \
    -NonInteractive
fi

build_args=(
  "$PROJECT"
  /t:Build
  /p:Configuration="$CONFIGURATION"
  /p:Platform=AnyCPU
  /m:1
  /v:minimal
)
if [[ $UNSIGNED -eq 1 ]]; then
  build_args+=(/p:EnableCodeSigning=False)
fi
"$MONO_MSBUILD" "${build_args[@]}"

APP="$ROOT/AnalysisITC.MacOS/bin/$CONFIGURATION/FT-ITC.app"
[[ -d "$APP" ]] || { echo "ERROR: Build did not produce $APP." >&2; exit 1; }
plutil -lint "$APP/Contents/Info.plist"

VERSION="$(plutil -extract CFBundleShortVersionString raw "$APP/Contents/Info.plist")"
PACKAGE_DIR="$ROOT/artifacts/packages"
STAGE_DIR="$ROOT/artifacts/package/xamarin-macos-universal"
DMG="$PACKAGE_DIR/FT-ITC-Analysis-$VERSION-macos-universal.dmg"

rm -rf "$STAGE_DIR"
mkdir -p "$STAGE_DIR" "$PACKAGE_DIR"
cp -R "$APP" "$STAGE_DIR/FT-ITC.app"
ln -s /Applications "$STAGE_DIR/Applications"

if [[ $UNSIGNED -eq 0 ]]; then
  codesign --verify --deep --strict --verbose=2 "$STAGE_DIR/FT-ITC.app"
fi

rm -f "$DMG" "$DMG.sha256"
hdiutil create -volname "FT-ITC Analysis" -srcfolder "$STAGE_DIR" -ov -format UDZO "$DMG"

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
