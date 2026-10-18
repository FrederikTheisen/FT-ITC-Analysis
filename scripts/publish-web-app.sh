#!/usr/bin/env bash
set -Eeuo pipefail

FTITC_PROJECT_DIR="${1:-/Users/frederiktheisen/Projects/FT-ITC-Analysis}"
FTITC_HOST="ftitc-mist"
FTITC_RELEASE_ID="${FTITC_RELEASE_ID:-$(date +%Y%m%d-%H%M%S)}"

FTITC_PUBLISH_DIR="/tmp/ftitc-web-release-$FTITC_RELEASE_ID"
FTITC_REMOTE_RELEASE="/opt/ftitc-web-release-$FTITC_RELEASE_ID"
FTITC_REMOTE_BACKUP="/opt/ftitc-web-backup-$FTITC_RELEASE_ID"
FTITC_REMOTE_FAILED="/opt/ftitc-web-failed-$FTITC_RELEASE_ID"

cd "$FTITC_PROJECT_DIR"

if [[ "${FTITC_ACTIVATE_ONLY:-0}" != 1 ]]; then
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
fi

# Staging is separate from activation: rehearse this exact candidate first.
if [[ "${FTITC_STAGE_ONLY:-0}" == 1 ]]; then
    echo "Staged $FTITC_REMOTE_RELEASE. Rehearse ledger compatibility before activation."
    exit 0
fi
: "${FTITC_DEPLOYMENT_STATE:?Set the prepared state path after the ledger rehearsal, or use FTITC_STAGE_ONLY=1.}"
scp "$FTITC_PROJECT_DIR/scripts/activate-web-release.py" \
    "$FTITC_HOST:/tmp/ftitc-activate-$FTITC_RELEASE_ID.py"
# Keep the runner outside the release directories it moves.
ssh "$FTITC_HOST" sudo install -m 0755 -o root -g root \
    "/tmp/ftitc-activate-$FTITC_RELEASE_ID.py" /usr/local/sbin/ftitc-activate-web-release
ssh "$FTITC_HOST" sudo systemd-run \
    --unit="ftitc-web-activate-$FTITC_RELEASE_ID" --no-block \
    --property=Type=exec --property=Restart=on-abnormal \
    --property=RestartSec=5 --property=TimeoutStartSec=infinity \
    /usr/bin/python3 /usr/local/sbin/ftitc-activate-web-release activate "$FTITC_DEPLOYMENT_STATE"
echo "Activation runs independently on MIST. Inspect the unit journal and prepared state for completion."
echo "Manual recovery: sudo python3 /usr/local/sbin/ftitc-activate-web-release recover $FTITC_DEPLOYMENT_STATE"
