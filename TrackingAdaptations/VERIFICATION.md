# Setup verification of the Python adaptations

Completed 2026-10-07 in a Python 3.14.8 arm64 virtual environment on this computer.

- All 109 adaptation unit and regression tests passed, and the starter's original 11 Python tests also passed. The tests do not use a real camera or the Gesture Recognizer model.
- The ASTs of `extract_features`, `PerformerTracker`, `requested_count` and `encode_preview` match the pinned original source, and the original pose JSON construction is unchanged.
- The new `StablePerformerTracker` is wired into `main` without replacing the original tracker. Synthetic position sequences reproduce three problems of the original: automatic recovery on the frame after an ambiguity, reuse of old positions after a long absence, and a fourth bystander included at three-person start-up (pushing out the rightmost member). The new policy assigns nothing when the start-up count is wrong, and locks after an ambiguity, an extra person or a per-role gap over 2 s; only an explicit reassignment unlocks it. Detection-order changes, short absences, independent per-role timers, lock persistence and left-to-right assignment after reset were all checked. Timing uses an injectable monotonic clock, so the tests neither wait in real time nor open a camera.
- The real `selfie_segmenter.tflite` (249,537 bytes) ran two inferences through `ImageSegmenter.segment_for_video`, and a call 0.1 s later was skipped by the 5 Hz limit.
- The model's labels are `['selfie']`; the category mask is uint8 with shape `(256, 256, 1)`. A synthetic background image returns 255, the implicit background. The person foreground is category 0, so the foreground share is counted as `mask == 0`, not `mask > 0`.
- Versions: MediaPipe 0.10.35, OpenCV 4.14.0, NumPy 2.5.3. CPU XNNPACK inference succeeded; a "no GPU context" notice did not affect the test.

The synthetic test record is `synthetic_smoke_result.json`. Its image holds only a background and one colour block, so the 0.0% foreground share is not evidence of multi-person interaction, segmentation accuracy, real-time frame rate or course results.

These Python checks did not open a camera, run Unity or involve real performers.

MediaPipe printed one failed Clearcut telemetry upload message. The adaptation code itself uploads no camera images; inference runs locally and the preview uses the existing localhost UDP. We have not audited every network behaviour of the dependency.

## Synthetic three-person fist acceptance

`tests/test_three_person_fist_acceptance.py` is a camera-free three-person rehearsal. It builds shoulder and wrist coordinates for P1/P2/P3 and calls `associate_gestures`, the function the bridge uses in production, across five scenarios: all three closing and opening fists together, two wrists crossing at the midpoint, a brief occlusion of P2, open/closed jitter, and detection order flipping from left-to-right to right-to-left.

```bash
cd TrackingAdaptations
python3 -m unittest discover -s tests -p 'test_three_person_fist_acceptance.py'
```

All five tests must pass. At the crossing midpoint the hand is conservatively reported as `No fresh` rather than giving P2's hand to P1, and the next clear frame restores P2. A short occlusion clears only the missing role and never reuses the previous closed-fist signal. In the jitter sequence only the last three consecutive frames satisfy the simulated hold. Changing the detection order changes only the input order, not the assigned roles.

This proves deterministic coordinate association and packet boundaries, not the real recognition rate of the MediaPipe Gesture Recognizer, camera frame rate or Unity Play behaviour.

At runtime the Gesture Recognizer runs on its own `FistGestureWorker` thread: the bridge sends the pose packet first, then submits the newest hand frame. Model download and MediaPipe initialisation are deferred to the first background task for an assigned role. Results may lag by at most three frames or 0.30 s, and any change of identity revision, slot set or explicit reset invalidates an in-flight task.
