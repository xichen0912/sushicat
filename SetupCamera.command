#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
PYTHON_EXECUTABLE=''
for candidate in /opt/homebrew/bin/python3 /usr/local/bin/python3 "$(command -v python3 || true)"; do
  if [[ -n "$candidate" && -x "$candidate" ]] && "$candidate" -c 'import sys; sys.exit(0 if sys.version_info >= (3, 10) else 1)' >/dev/null 2>&1; then
    PYTHON_EXECUTABLE="$candidate"
    break
  fi
done
if [[ -z "$PYTHON_EXECUTABLE" ]]; then
  echo 'Install Python 3.10 or newer, then run this command again.'
  exit 1
fi
echo 'Installing camera dependencies from the Tsinghua package mirror. This does not open the camera.'
"$PYTHON_EXECUTABLE" SetupCamera.py "$@"
if [[ -t 0 ]]; then
  read -r -p 'Press Return to close. ' || true
fi
