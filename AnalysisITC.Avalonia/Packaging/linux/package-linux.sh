#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
PROJECT="$ROOT/AnalysisITC.Avalonia/AnalysisITC.Avalonia.csproj"
RUNTIME="linux-x64"
CONFIGURATION="Release"
UNSIGNED=0
NO_RESTORE=0
MAINTAINER_NAME="${FTITC_MAINTAINER_NAME:-Frederik Theisen}"
MAINTAINER_EMAIL="${FTITC_MAINTAINER_EMAIL:-application@ft-itc.org}"
FTITC_GPG_KEY_ID="${FTITC_GPG_KEY_ID:-75F067A00024D408005E0C8FA9D0A980D599BEE6}"

if [[ -z "$MAINTAINER_EMAIL" ]]; then
  echo "ERROR: Set FTITC_MAINTAINER_EMAIL to the package support address." >&2
  exit 1
fi

while [[ $# -gt 0 ]]; do
  case "$1" in
    --runtime) RUNTIME="$2"; shift 2 ;;
    --configuration) CONFIGURATION="$2"; shift 2 ;;
    --unsigned) UNSIGNED=1; shift ;;
    --no-restore) NO_RESTORE=1; shift ;;
    *) echo "ERROR: Unknown option '$1'." >&2; exit 2 ;;
  esac
done

case "$RUNTIME" in
  linux-x64) DEB_ARCH="amd64" ;;
  linux-arm64) DEB_ARCH="arm64" ;;
  *) echo "ERROR: Supported runtimes are linux-x64 and linux-arm64." >&2; exit 2 ;;
esac

for command in dotnet dpkg-deb; do
  command -v "$command" >/dev/null 2>&1 || { echo "ERROR: $command is required." >&2; exit 1; }
done

VERSION="$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$PROJECT" | head -n 1)"
[[ -n "$VERSION" ]] || { echo "ERROR: Could not read Version from $PROJECT." >&2; exit 1; }
TARGET_FRAMEWORK="$(sed -n 's:.*<TargetFramework>\([^<]*\)</TargetFramework>.*:\1:p' "$PROJECT" | head -n 1)"
[[ -n "$TARGET_FRAMEWORK" ]] || { echo "ERROR: Could not read TargetFramework from $PROJECT." >&2; exit 1; }

PUBLISH_DIR="$ROOT/artifacts/publish/$RUNTIME"
STAGE_DIR="$ROOT/artifacts/package/linux-$RUNTIME"
PACKAGE_DIR="$ROOT/artifacts/packages"
DEB="$PACKAGE_DIR/ft-itc-analysis_${VERSION}_${DEB_ARCH}.deb"

rm -rf "$PUBLISH_DIR" "$STAGE_DIR"
mkdir -p "$PUBLISH_DIR" "$PACKAGE_DIR"

if [[ $NO_RESTORE -eq 0 ]]; then
  dotnet restore "$PROJECT" --runtime "$RUNTIME"
else
  ASSETS_FILE="$ROOT/AnalysisITC.Avalonia/obj/project.assets.json"
  RID_TARGET="$TARGET_FRAMEWORK/$RUNTIME"
  if [[ ! -f "$ASSETS_FILE" ]] || ! grep -Fq "\"$RID_TARGET\":" "$ASSETS_FILE"; then
    echo "ERROR: --no-restore requires an existing NuGet assets target for $RID_TARGET." >&2
    echo "Run: dotnet restore \"$PROJECT\" --runtime $RUNTIME" >&2
    exit 1
  fi
fi

dotnet publish "$PROJECT" \
  --configuration "$CONFIGURATION" \
  --runtime "$RUNTIME" \
  --self-contained true \
  --no-restore \
  --output "$PUBLISH_DIR"

mkdir -p \
  "$STAGE_DIR/DEBIAN" \
  "$STAGE_DIR/usr/bin" \
  "$STAGE_DIR/usr/lib/ft-itc-analysis" \
  "$STAGE_DIR/usr/share/applications" \
  "$STAGE_DIR/usr/share/icons/hicolor/512x512/apps" \
  "$STAGE_DIR/usr/share/icons/hicolor/512x512/mimetypes" \
  "$STAGE_DIR/usr/share/metainfo" \
  "$STAGE_DIR/usr/share/mime/packages" \
  "$STAGE_DIR/usr/share/doc/ft-itc-analysis"

cp -R "$PUBLISH_DIR/." "$STAGE_DIR/usr/lib/ft-itc-analysis/"
cp "$SCRIPT_DIR/org.ft_itc.analysis.desktop" "$STAGE_DIR/usr/share/applications/"
cp "$SCRIPT_DIR/org.ft_itc.analysis.metainfo.xml" "$STAGE_DIR/usr/share/metainfo/"
cp "$SCRIPT_DIR/ft-itc-analysis.xml" "$STAGE_DIR/usr/share/mime/packages/"
cp "$SCRIPT_DIR/org.ft_itc.analysis.png" "$STAGE_DIR/usr/share/icons/hicolor/512x512/apps/"
cp "$SCRIPT_DIR/mimetypes/application-vnd.ftitc.project+zip.png" "$STAGE_DIR/usr/share/icons/hicolor/512x512/mimetypes/"
cp "$SCRIPT_DIR/mimetypes/application-x-ftitc-project.png" "$STAGE_DIR/usr/share/icons/hicolor/512x512/mimetypes/"
cp "$ROOT/LICENSE.md" "$STAGE_DIR/usr/share/doc/ft-itc-analysis/copyright"
chmod 0755 "$STAGE_DIR/usr/lib/ft-itc-analysis/AnalysisITC.Avalonia"

ln -s ../lib/ft-itc-analysis/AnalysisITC.Avalonia \
  "$STAGE_DIR/usr/bin/ft-itc-analysis"

cat > "$STAGE_DIR/DEBIAN/control" <<EOF
Package: ft-itc-analysis
Version: $VERSION
Section: science
Priority: optional
Architecture: $DEB_ARCH
Maintainer: $MAINTAINER_NAME <$MAINTAINER_EMAIL>
Installed-Size: $(du -sk "$STAGE_DIR/usr" | cut -f1)
Depends: ca-certificates, libc6, libgcc-s1, libgssapi-krb5-2, libstdc++6, libicu78 | libicu76 | libicu74 | libicu72 | libicu70, libssl3t64 | libssl3, tzdata, zlib1g, libfontconfig1, libfreetype6, libx11-6, libice6, libsm6, libxext6
Homepage: https://ft-itc.org
Description: Isothermal titration calorimetry analysis
 Process, analyze, fit, and present ITC data and FT-ITC project files.
EOF

cat > "$STAGE_DIR/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
command -v update-mime-database >/dev/null 2>&1 && update-mime-database /usr/share/mime || true
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database /usr/share/applications || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -f -t /usr/share/icons/hicolor || true
exit 0
EOF

cat > "$STAGE_DIR/DEBIAN/postrm" <<'EOF'
#!/bin/sh
set -e
command -v update-mime-database >/dev/null 2>&1 && update-mime-database /usr/share/mime || true
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database /usr/share/applications || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -f -t /usr/share/icons/hicolor || true
exit 0
EOF
chmod 0755 "$STAGE_DIR/DEBIAN/postinst" "$STAGE_DIR/DEBIAN/postrm"

dpkg-deb --root-owner-group --build "$STAGE_DIR" "$DEB"

if [[ $UNSIGNED -eq 0 ]]; then
  command -v gpg >/dev/null 2>&1 || { echo "ERROR: gpg is required for release signing." >&2; exit 1; }
  [[ -n "${FTITC_GPG_KEY_ID:-}" ]] || { echo "ERROR: Set FTITC_GPG_KEY_ID or explicitly use --unsigned." >&2; exit 1; }
  gpg --batch --yes --local-user "$FTITC_GPG_KEY_ID" --armor --detach-sign "$DEB"
  gpg --verify "$DEB.asc" "$DEB"
fi

sha256sum "$DEB" > "$DEB.sha256"
echo "Created $DEB"
