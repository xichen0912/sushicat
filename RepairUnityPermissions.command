#!/bin/bash
set -euo pipefail

EDITOR_APP='/Applications/Unity/Unity-6000.6.3f1/Unity.app'
pause_on_exit() {
  local result=$?
  if [[ $result -ne 0 ]]; then
    echo "Repair or verification did not complete (exit $result). Keep this window open for diagnosis."
  fi
  if [[ -t 0 ]]; then
    read -r -p 'Press Return to close. ' || true
  fi
}
trap pause_on_exit EXIT

if [[ ! -d "$EDITOR_APP" ]]; then
  echo "Editor not found: $EDITOR_APP"
  exit 1
fi
version=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleVersion' "$EDITOR_APP/Contents/Info.plist")
if [[ "$version" != '6000.6.3f1' ]]; then
  echo "Unexpected Editor version: $version"
  exit 1
fi

echo 'Repairing read and execute permissions on the installed Unity 6000.6.3f1 app.'
echo 'Enter your macOS administrator password at the local sudo prompt. Typing is hidden.'
echo 'This command adds no write permissions and changes no file contents.'
/usr/bin/sudo /bin/chmod -R a+rX "$EDITOR_APP"
echo 'Verifying the Unity app signature...'
/usr/bin/codesign --verify --deep --strict "$EDITOR_APP"
echo 'Permissions repaired; app signature verified.'
echo 'Open DigiPhant_P2 from Unity Hub to continue importing the project.'
