#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
PYTHON_PATH="$PWD/DigiPhantStarter/Tracking/.venv/bin/python"
"$PYTHON_PATH" -m pip check
"$PYTHON_PATH" -m unittest discover -s DigiPhantStarter/Tracking -p 'test_*.py'
"$PYTHON_PATH" -m unittest discover -s TrackingAdaptations/tests -p 'test_*.py'
if [[ -d Workshop ]]; then "$PYTHON_PATH" -m unittest discover -s Workshop -p 'test_*.py'; fi
"$PYTHON_PATH" tools/check_project.py
echo 'These checks do not verify Unity compilation, camera calibration or a real performance.'
if [[ -t 0 ]]; then
  read -r -p 'Press Return to close. ' || true
fi
