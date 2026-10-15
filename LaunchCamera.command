#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
PYTHON_PATH="$PWD/DigiPhantStarter/Tracking/.venv312/bin/python"
if [[ ! -x "$PYTHON_PATH" ]]; then PYTHON_PATH="$PWD/DigiPhantStarter/Tracking/.venv/bin/python"; fi
if [[ ! -x "$PYTHON_PATH" ]]; then
  echo 'Python environment missing. Run SetupCamera.py or install the compatible camera environment.'
  exit 1
fi
echo 'Solo camera check. Allow camera permission if prompted. Q quits; R resets identity.'
echo 'Quit this preview before Unity auto-launches its tracker.'
exec "$PYTHON_PATH" DigiPhantStarter/Tracking/bridge.py --people 1 --upper-body-only "$@"
