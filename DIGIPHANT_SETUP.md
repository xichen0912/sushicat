# DigiPhant setup and test record — team sushicat

Last updated 2026-10-15. Three-person tracking fixes are described in [Docs/THREE_PERSON_BUGS.md](Docs/THREE_PERSON_BUGS.md).

## Tested environment

| Item | Version |
|---|---|
| Computer | macOS 15.7.7, Apple Silicon (arm64) |
| Unity | 6000.6.3f1, URP 17.6.0 |
| Python (tracker) | 3.14.8 in `DigiPhantStarter/Tracking/.venv` |
| Python packages | MediaPipe 0.10.35, opencv-contrib-python 4.14.0, NumPy 2.5.4 |
| Starter | `https://github.com/kommanderpi/studentstarter.git` at `4e6dab5271c618d1c0bfb2022c41c0d37cd8d69f` |

`TrackingAdaptations/dependency-lock-macos-arm64-py312.txt` pins an alternative Python 3.12 / MediaPipe 0.10.21 environment. We needed it earlier for the fist recognizer, which crashed under 3.14. With fist detection now off by default, the 3.14 environment above is what we tested.

## Data path

Camera → `bridge.py` (MediaPipe pose, frame split into three zones, one per performer) → local UDP port 5055 → Unity controller (neutral calibration, safety checks, smoothing) → elephant rig, gait and course logic. The preview JPEG travels on UDP 5057.

## Mappings and key settings

| Performer | Body signal | Elephant |
|---|---|---|
| P1 Xinru Lu | Left hand height | Forward speed, walk/run, all four legs. Holding the hand low confirms reverse. |
| P2 Xi Chen | Shoulder tilt (Lean) | Steering. Lean right turns right (weight −1). |
| P3 Ge Huang | Head yaw / right hand height / left arm extension | Head turn / trunk curl / ears |
| P3 Ge Huang | Either wrist inside the green touch box | Drink at the water, pick up the log |

Values in the performance scene:

| Setting | Value |
|---|---|
| Forward sensitivity, dead zone | 0.75, 0.12 |
| Steering sensitivity, dead zone | 1.0, 0.12 |
| Input smoothing | 3.5 |
| Walk / run speed, run threshold | 1.8 / 4.5 units/s, 0.65 |
| Minimum walking pace | 60% of walk speed |
| Turn rate | 60°/s |
| Stale-pose limit | 0.7 s |
| P1 stance tolerance (shoulder width change) | 30% |
| Landmark confidence threshold | 0.5 |
| Touch box (normalized, unmirrored image) | x 0.70–0.97, y 0.02–0.24, held 0.2 s |
| Follow camera | 13 units behind, 8 up, looking at 2.5 m (about 23° down) |

The tracker starts with `--no-segmentation --no-cup-detection --no-fist-detection`. To bring them back, use **Lightweight Tracking** and **Fist Detection** on the camera preview component.

## Course rules

- **Tree**: passing within 8 units of the tree trunk completes it. The original turn-based rule also still counts.
- **Water / Log**: within 10 units the elephant snaps into position. P3 then reaches into the green box, or clenches the right fist when fist detection is on. Water sprays from the trunk; the log attaches to the trunk tip.
- **Catch-up**: reaching the water or the log jumps the course to that stage. The finish is never skipped to.
- **Finish**: only after the log. Within 7 units the elephant is pulled into the finish zone and the course completes. Dusk, fireworks and the finish music play, and the elephant changes from the ballet dress into the suit, leaps back to its start position and dances.

All values are on the course manager and dance components in the Inspector.

## Running a performance

1. Open `Assets/StudentWork/Scenes/UpperBodyThreePersonCourse_City.unity` and press Play. The first launch of the tracker can take 30–60 s.
2. Choose **3 / Upper body / Camera**. Stand left to right as P1, P2, P3 in the unmirrored preview, with shoulders and both hands visible.
3. Click **Set neutral pose (10 seconds)**. P1 keeps the left hand at chest-to-waist height, inside the frame.
4. If the status shows "P1 left hand not detected", P1 should sit back so the lowered hand stays in frame.
5. **Reassign people** and recalibrate after anyone swaps places. **Restart course / return to start** begins a new trial.

## Recording

The built-in **Record performance** button saves `Recordings/<session>/performance.mp4`, but only at 10 fps and without audio. For the final video we use macOS screen recording of the Game view and add the music in editing. The checked video is uploaded to the team Drive and linked in `README.md`. Recordings are never committed.

## Changes made 2026-10-12 to 2026-10-13

- Camera: rebuilt the missing Python environment with `SetupCamera.py`.
- Course: tree by proximity, catch-up at water/log, finish only after the log.
- Interactions: snap at water/log, green touch box for P3, fist as an optional alternative.
- Walking: lighter tracker launch, stale-pose limit 0.7 s, forward sensitivity 0.75, smoothing 3.5, stance tolerance 0.3, minimum walking pace 60%, accurate "hand not detected" status.
- Steering: P2 un-mirrored; editor validation updated to match.
- Look: city celebration leap back to start, ballet dress at the start and suit for the dance (CityGen menu **Dress Elephant (casual start outfit)**), finish music slot, follow camera lowered, UI decluttered (simulation panel hidden, course panel collapsible). The on-screen "Preview: dusk + dance (F)" button toggles the finish preview, same as the F key.

## What we actually verified

- Every change was compiled with Unity's own C# compiler for both the runtime and editor assemblies: 0 errors.
- Live camera in Unity: 1280×720 preview, three performers tracked and calibrated, about 6.9 pose updates/s after the tracker change. See [Docs/REFLECTION.md](Docs/REFLECTION.md) for the measured comparison.
- One full course clear (about 204 s) on 2026-10-12, recorded in the course log. That run used an earlier build, before the touch box and costume changes.
- The new stage rules were checked by replaying the elephant path of a real run offline: water at about 216 s, log at 292 s, finish at 345 s.
- Fresh-checkout reconstruction: see the section below.

Not yet verified: a complete three-person run with the final build, and the editor validation menus after the latest changes.

## Fresh-checkout reconstruction test

Done on 2026-10-15 in a separate folder, leaving the working project untouched.

1. Cloned this repository into an empty folder.
2. Ran `python3 tools/restore_starter.py`. It used a clone of the starter at the pinned commit, verified 124 starter fingerprints, restored `Assets/Elephant`, copied the adapted Python, downloaded the segmenter and cup models (checksums matched), created `.venv` and installed `requirements.txt`. Result: `RESTORE_OK`; `pip check` reported no broken requirements.
3. Ran `python3 tools/check_project.py`. Result: `STATIC_OK` (566 unique GUIDs, 10 locked material imports, 3 locked runtime patches).
4. Checked every asset reference in the performance scene: 349 of 353 resolve to files in `Assets/`, 2 are Unity's built-in resources and 2 come from the URP packages that Unity installs from `Packages/manifest.json`.

Fixed during the test: the first attempt failed to download the models because python.org Python on macOS had no CA certificates. `restore_starter.py` now falls back to `curl`.

Not checked in the fresh copy: opening it in Unity, live camera tracking, and the optional fist model, which the restore script does not download.

## Known limitations

- Tracking runs at 6–7 updates/s with about 0.2 s delay. If P1's hand leaves the frame, travel stops at once.
- Crossing paths can swap performer identities; reassign and recalibrate.
- The tutu is rigid and does not sway.
- The built-in recorder is 10 fps with no audio.
- The decorative city has no colliders; the course is judged by distances and zones only.
