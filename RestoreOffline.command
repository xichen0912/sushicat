#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
STARTER_BUNDLE="$PWD/OfflineResources/studentstarter-4e6dab5271c6.bundle"
SEGMENTER_MODEL="$PWD/OfflineResources/selfie_segmenter.tflite"
CUP_MODEL="$PWD/OfflineResources/efficientdet_lite0.tflite"
FIST_MODEL="$PWD/OfflineResources/gesture_recognizer.task"
if [[ ! -f "$STARTER_BUNDLE" || ! -f "$SEGMENTER_MODEL" ]]; then
  echo 'This command needs the resource handoff ZIP with its OfflineResources folder.'
  echo 'The lightweight Git ZIP can use tools/restore_starter.py with an existing exact starter clone instead.'
  exit 1
fi
RESTORE_ARGS=(--source "$STARTER_BUNDLE" --segmenter-model "$SEGMENTER_MODEL" --skip-env --no-download-cup-model)
if [[ -f "$CUP_MODEL" ]]; then RESTORE_ARGS+=(--cup-detector-model "$CUP_MODEL"); fi
if [[ -f "$FIST_MODEL" ]]; then RESTORE_ARGS+=(--fist-detector-model "$FIST_MODEL"); fi
python3 tools/restore_starter.py "${RESTORE_ARGS[@]}"
if [[ -f "$CUP_MODEL" ]]; then
  python3 tools/check_project.py
else
  echo 'Offline resources restored; cup and fist models were not bundled, so fist-gated interactions remain paused until local models are supplied.'
fi
echo 'Offline resources restored. Unity import and Python dependency installation remain separate.'
if [[ -t 0 ]]; then
  read -r -p 'Press Return to close. ' || true
fi
