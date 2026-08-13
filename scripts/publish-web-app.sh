#!/usr/bin/env bash
set -Eeuo pipefail

FTITC_PROJECT_DIR="/Users/frederiktheisen/Projects/FT-ITC-Analysis"
FTITC_HOST="ftitc-mist"
FTITC_RELEASE_ID=$(date +%Y%m%d-%H%M%S)

FTITC_PUBLISH_DIR="/tmp/ftitc-web-release-$FTITC_RELEASE_ID"
FTITC_REMOTE_RELEASE="/opt/ftitc-web-release-$FTITC_RELEASE_ID"
FTITC_REMOTE_BACKUP="/opt/ftitc-web-backup-$FTITC_RELEASE_ID"
FTITC_REMOTE_FAILED="/opt/ftitc-web-failed-$FTITC_RELEASE_ID"

cd "$FTITC_PROJECT_DIR"

echo "Running tests..."
dotnet test \
  AnalysisITC.Web.Tests/AnalysisITC.Web.Tests.csproj \
  -c Release

echo "Publishing $FTITC_RELEASE_ID..."
dotnet publish \
  AnalysisITC.Web/AnalysisITC.Web.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained false \
  -o "$FTITC_PUBLISH_DIR"

echo "Preparing server..."
ssh "$FTITC_HOST" \
  "sudo install -d -m 755 -o ubuntu -g ubuntu '$FTITC_REMOTE_RELEASE'"

echo "Uploading application..."
scp -r "$FTITC_PUBLISH_DIR/." \
  "$FTITC_HOST:$FTITC_REMOTE_RELEASE/"

echo "Activating release..."
ssh "$FTITC_HOST" bash -s -- \
  "$FTITC_REMOTE_RELEASE" \
  "$FTITC_REMOTE_BACKUP" \
  "$FTITC_REMOTE_FAILED" <<'FTITC_REMOTE_SCRIPT'
set -Eeuo pipefail

FTITC_NEW_RELEASE="$1"
FTITC_BACKUP_RELEASE="$2"
FTITC_FAILED_RELEASE="$3"
FTITC_ACTIVE_RELEASE="/opt/ftitc-web"

sudo chown -R root:root "$FTITC_NEW_RELEASE"
sudo systemctl stop ftitc-web
sudo mv "$FTITC_ACTIVE_RELEASE" "$FTITC_BACKUP_RELEASE"
sudo mv "$FTITC_NEW_RELEASE" "$FTITC_ACTIVE_RELEASE"

if sudo systemctl start ftitc-web &&
   curl --fail --silent --show-error \
     http://127.0.0.1:5000/ > /dev/null
then
    echo "New release is running."
else
    echo "Health check failed; restoring previous release."

    sudo systemctl stop ftitc-web || true
    sudo mv "$FTITC_ACTIVE_RELEASE" "$FTITC_FAILED_RELEASE"
    sudo mv "$FTITC_BACKUP_RELEASE" "$FTITC_ACTIVE_RELEASE"
    sudo systemctl start ftitc-web

    exit 1
fi
FTITC_REMOTE_SCRIPT

echo "Checking public HTTPS endpoint..."
curl --fail --silent --show-error \
  https://app.ft-itc.org/ > /dev/null

echo "Deployment $FTITC_RELEASE_ID completed successfully."