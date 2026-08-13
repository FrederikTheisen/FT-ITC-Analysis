#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

UNSIGNED=0
NOTARIZE=0
NO_RESTORE=0
SKIP_TESTS=0
NO_CLEAN=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --unsigned) UNSIGNED=1; shift ;;
    --notarize) NOTARIZE=1; shift ;;
    --no-restore) NO_RESTORE=1; shift ;;
    --skip-tests) SKIP_TESTS=1; shift ;;
    --no-clean) NO_CLEAN=1; shift ;;
    *) echo "ERROR: Unknown option '$1'." >&2; exit 2 ;;
  esac
done

if [[ $UNSIGNED -eq 1 && $NOTARIZE -eq 1 ]]; then
  echo "ERROR: --unsigned and --notarize cannot be used together." >&2
  exit 2
fi

if [[ $NO_CLEAN -eq 0 ]]; then
  "$SCRIPT_DIR/clean.sh"
fi

set --
if [[ $UNSIGNED -eq 1 ]]; then set -- "$@" --unsigned; fi
if [[ $NO_RESTORE -eq 1 ]]; then set -- "$@" --no-restore; fi
if [[ $SKIP_TESTS -eq 1 ]]; then set -- "$@" --skip-tests; fi
"$SCRIPT_DIR/publish.sh" "$@"

"$SCRIPT_DIR/package.sh"

if [[ $UNSIGNED -eq 0 ]]; then
  if [[ $NOTARIZE -eq 1 ]]; then
    "$SCRIPT_DIR/sign.sh" --notarize
  else
    "$SCRIPT_DIR/sign.sh"
  fi
fi

echo "Release output: $SCRIPT_DIR/output"
