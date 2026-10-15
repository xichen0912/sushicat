#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
REQUIRED_VERSION='6000.6.3f1'
EDITOR_BINARY=''

select_editor() {
  local candidate="$1" app version
  case "$candidate" in
    *.app) app="$candidate" ;;
    *.app/Contents/MacOS/Unity) app="${candidate%/Contents/MacOS/Unity}" ;;
    *) return 1 ;;
  esac
  [[ -x "$app/Contents/MacOS/Unity" && -f "$app/Contents/Info.plist" ]] || return 1
  version=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleVersion' "$app/Contents/Info.plist" 2>/dev/null) || return 1
  [[ "$version" == "$REQUIRED_VERSION" ]] || return 1
  EDITOR_BINARY="$app/Contents/MacOS/Unity"
}

# UNITY_EDITOR may point to the app bundle or its Contents/MacOS/Unity binary.
if [[ -n "${UNITY_EDITOR:-}" ]]; then
  select_editor "$UNITY_EDITOR" || {
    echo "UNITY_EDITOR must point to an installed Unity $REQUIRED_VERSION app or binary."
    exit 1
  }
else
  for candidate in \
    "/Applications/Unity/Unity-$REQUIRED_VERSION/Unity.app" \
    "/Applications/Unity/Hub/Editor/$REQUIRED_VERSION/Unity.app"; do
    if select_editor "$candidate"; then
      break
    fi
  done
fi

if [[ -z "$EDITOR_BINARY" ]]; then
  echo "Unity $REQUIRED_VERSION was not found in the offline or Hub installation folders."
  echo 'If already installed, use Unity Hub > Installs > Locate to select Unity.app.'
  echo 'For another folder, set UNITY_EDITOR to the full path of that Unity.app.'
  echo "In Hub choose Projects > Add project from disk: $PWD"
  echo 'After import: Student Work > Open Upper Body Three Person Course'
  if [[ -t 0 ]]; then
    read -r -p 'Press Return to close. ' || true
  fi
  exit 1
fi
exec "$EDITOR_BINARY" -projectPath "$PWD"
