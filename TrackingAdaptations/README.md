# DigiPhant P2: segmentation and stable performer roles

This directory contains the **complete adapted** camera bridge and its added helpers.
The baseline is DigiPhant commit `4e6dab5271c618d1c0bfb2022c41c0d37cd8d69f`.
Copy the complete adapted set (`bridge.py`, `segmentation_support.py`, `identity_tracking.py`,
`region_tracking.py`, `interaction_support.py`, `cup_detection.py`, and `fist_detection.py`)
into the starter project's `Tracking` directory. The restore script applies all seven files.

## What is implemented

- Original MediaPipe `PoseLandmarker` still supplies body controls and up to four
  pose observations; the new `StablePerformerTracker` assigns temporary roles.
- A **separate** MediaPipe Tasks `vision.ImageSegmenter` uses the official binary
  selfie segmentation model. This is an additional CV task; PoseLandmarker's own
  segmentation output is not used.
- Segmentation samples the unmodified RGB capture at a maximum of one sample per
  0.2 seconds (target **5 Hz**). Between samples the preview holds the latest mask.
  Pose detection still runs each captured frame. Its JSON controls are delivered
  before optional segmentation inference. Both detectors are synchronous, so
  segmentation can delay the next frame and change pose latency/FPS. Actual rates
  depend on machine performance; 5 Hz is a cap/target, not a measured result or
  performance guarantee.
- The original camera/pose preview adds cyan foreground outlines and an image
  occupancy percentage. `foreground 35.0%` means 35% of the sampled image pixels
  were labeled person. It is not a confidence score, body area measurement,
  performer count, or individual mask.
- Missing/invalid segmentation models or segmentation runtime errors show an
  explicit disabled status in the preview and terminal. Original pose controls
  continue. `--no-segmentation` explicitly disables it for A/B tests.
- Three/four-person roles require exactly the selected number of visible people
  when initially assigning left-to-right. An extra bystander cannot silently
  displace the rightmost teammate. After seeding, an extra visible person, an
  ambiguous proximity assignment, or a role's tracking gap exceeding **2 seconds**
  locks all assignments until explicit reassignment. The preview shows the reason
  and reset instruction; the existing pose packet carries an empty `people` list.
  Short, unambiguous dropouts retain their role positions. This is a conservative
  position policy, not recognition of a person's identity.

All camera inference runs on the user's computer. The only image transmission is
the original low-resolution JPEG preview over loopback (`127.0.0.1`) to local
Unity. Model downloads require internet; camera frames are not sent to that host.

## Models and dependencies

Install the starter's tracking requirements into its Python virtual environment.
Use the starter's setup/run scripts or that environment's Python executable.
The bridge needs `mediapipe`, `opencv-python`, and `numpy`; imports remain lazy for
CLI `--help`.

Place these assets **beside the applied `bridge.py`**, in starter `Tracking`:

| Model | Official URL |
|---|---|
| `pose_landmarker_full.task` | <https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_full/float16/latest/pose_landmarker_full.task> |
| `selfie_segmenter.tflite` | <https://storage.googleapis.com/mediapipe-models/image_segmenter/selfie_segmenter/float16/latest/selfie_segmenter.tflite> |
| `gesture_recognizer.task` | <https://storage.googleapis.com/mediapipe-models/gesture_recognizer/gesture_recognizer/float16/1/gesture_recognizer.task> |

The image task uses `ImageSegmenterOptions`, `RunningMode.VIDEO`,
`output_category_mask=True`, `output_confidence_masks=False`, and
`segment_for_video(image, monotonic_timestamp_ms)`. The official model's category
mask uses **`0=person`, `255=background`**. The API exposes one label,
`['selfie']`; the background is the implicit sentinel 255, not class 1.
This was checked against the actual downloaded model with MediaPipe 0.10.35.
The helper accepts H×W or H×W×1 integer category masks, verifies the model labels,
and rejects other values/float confidence maps.

The pose model retains upstream `--model` and `--download-model` behavior. The
segmentation model resolves adjacent to the script, independently of the working
directory. If it is missing, the bridge does not silently substitute a different
task or download a model unexpectedly.

## Run and compare

From the starter's `Tracking` directory, with the prepared virtual environment:

```sh
.venv/bin/python bridge.py --people 3
```

Adjust the environment path if the starter's setup script uses another location.
The existing flags (`--people`, `--camera`, `--port`, `--upper-body-only`,
`--no-window`, `--model`, `--download-model`) are preserved. `--no-window` sends
the same annotated JPEG into Unity without opening the OpenCV window.
`--identity-timeout` sets the positive finite loss threshold in seconds; default 2.

For the corresponding A/B baseline:

```sh
.venv/bin/python bridge.py --people 3 --no-segmentation
```

Keep camera, lighting, framing, movement sequence, and performer count the same
between runs. Quit each bridge before starting the next one; the original socket
guard prevents two bridges from using the same UDP ports. Stand side by side,
use Unity **Reassign people** (or `R` in the external preview) to release a role
lock, stand in the intended order, and set the neutral pose again in Unity.
Do not resume movement with a previous calibration after reassignment. `Q` closes
the local camera preview.

The original six movement channels remain compatible: pose JSON on UDP `5055`,
Unity commands on `5056`, and JPEG preview on `5057` (all relative to `--port`).
The adapted packet adds optional fist fields and `fistDetectorStatus`; the Unity
receiver accepts packets without them, but fist-gated student interactions wait
until the Gesture Recognizer model supplies a fresh signal. Supply
`gesture_recognizer.task` locally before a rehearsal; automatic download is only
a convenience and is never required by the restore script.
The downloaded artifact is pinned in the project-root `fist-detector-lock.json`
with its byte count and SHA-256.
On Apple Silicon, `SetupCamera.py` prefers a Python 3.12 `.venv312` using
`dependency-lock-macos-arm64-py312.txt`; the older Python 3.14 environment is
kept only as a fallback.
The original features, legacy `PerformerTracker`, count validation, and JPEG
encoding function remain unchanged. The main loop now explicitly uses the
separate stable tracker; keeping the legacy class supports pinned-source
regression and synthetic A/B comparisons without running unsafe live defaults.

## Verification and limitations

Run the meaningful unit/regression checks from this `TrackingAdaptations`
directory with the prepared starter environment:

```sh
../DigiPhantStarter/Tracking/.venv/bin/python -m unittest discover -s tests -v
```

These test foreground category handling, occupancy, mask ownership, contour
rendering/resizing (when OpenCV is installed), 5 Hz sampling and monotonic times,
missing/error/A/B states, pose features, and Unity count validation. Twelve added
role tests compare the legacy automatic ambiguity/loss recovery and extra-person
seed against the stable policy, and cover detection reordering, short dropout,
elapsed-time/per-slot expiry, latched states and explicit reset. The current
adaptation suite has 109 passing tests; the starter's separate 11 tests also passed.
They compare unchanged
functions and UDP pose packet construction
against the pinned original. The test reads `DigiPhantSource` when present;
otherwise it reads the starter's tracked `HEAD:Tracking/bridge.py` with `git show`,
never the modified working-tree copy. Preserve the starter's pinned Git baseline
when restoring the project. For another original checkout location, set
`DIGIPHANT_ORIGINAL_BRIDGE` to its original `Tracking/bridge.py`.

The selfie model segments a combined person foreground; it does not distinguish
the three/four participants. Hands at frame edges, dim light, occlusions, unusual
backgrounds, and motion can make the outlines inaccurate. The held mask may lag
movement by roughly a sampling interval, plus computation time. This preview
does not alter tracking calibration or identify individuals. The stable tracker
stops on detected ambiguity or prolonged loss, but a crossing can still produce
uniquely nearest positions with swapped people between sampled frames. Keep
performers in fixed order; three-person camera accuracy still needs actual tests.

These Python checks do not establish a live camera/group-control result. Record actual footage,
observations, processing rates, and group contributions only after running with
the real performers. Do not report the included test masks as user experiments.

`synthetic_smoke_result.json` records a successful actual MediaPipe model/API test
on a generated background image, not a performer. `synthetic_segmentation_smoke.png`
is the resulting synthetic preview. The OpenCV unit test separately verifies
foreground contours using an artificial rectangular boolean mask. See
`VERIFICATION.md` for the setup verification scope.

### Closed-fist interactions

The camera bridge uses MediaPipe Gesture Recognizer to emit optional `rightFistClosed` and `rightFistConfidence` fields. Keep P3's full right hand visible; the log is picked up by holding a closed right fist near it, and the water stage completes with a closed-fist hold while stationary. Use `--fist-model PATH` for a local model and `--no-download-fist-model` for an offline run. A missing model is reported as unavailable and never falls back to the old cup gesture.

For a deterministic three-person rehearsal without a camera, run
`python3 -m unittest discover -s tests -p 'test_three_person_fist_acceptance.py'`. It simulates
simultaneous P1/P2/P3 hands, an ambiguous hand crossing, one-frame occlusion,
open/closed jitter, and detector-order changes. This validates conservative
slot association and stale-signal clearing; it does not measure real-model
accuracy or Unity Play behavior. The expected live-camera procedure is recorded
in `VERIFICATION.md`.
