# DigiPhant — team sushicat

Three people steer one digital elephant through a city course using upper-body pose tracking.

| Role | Member | Moves | Controls |
|---|---|---|---|
| P1 | Xinru Lu | Left hand up / down | Speed, walk or run, all four legs |
| P2 | Xi Chen | Shoulder tilt | Steering (lean left, turn left) |
| P3 | Ge Huang | Head turn, right hand, left arm, reach into the green box | Head, trunk, ears; drinking and log pickup |

Contributions:

- **Xinru Lu**: early code experiments
- **Ge Huang**: core code, code iteration
- **Xi Chen**: city build, code refinement and polish, presentation slides

## Links

- Performance video (Drive): https://drive.google.com/file/d/1bnqCmk_K8COZDoil_Q_npeKBBZKt0xfz/view
- Setup, mappings, recording and test record: [DIGIPHANT_SETUP.md](DIGIPHANT_SETUP.md)
- Group reflection (tested improvement + remaining limitation): [Docs/REFLECTION.md](Docs/REFLECTION.md)
- Three-person tracking problems and fixes: [Docs/THREE_PERSON_BUGS.md](Docs/THREE_PERSON_BUGS.md)
- Python adaptation checks: [TrackingAdaptations/VERIFICATION.md](TrackingAdaptations/VERIFICATION.md)

## Group reflection

**Tested improvement: lighter tracking, fewer pauses.** Walking looked jerky because the tracker had slowed down, and Unity paused travel whenever a pose arrived too late. We launched the tracker without two models that do not affect control (the person-outline segmenter and the cup detector) and compared two real Play sessions before and after:

| Measure (median) | Before | After |
|---|---|---|
| Pose updates per second | 3.1 | 6.9 |
| Pose age when used | 0.45 s | 0.19 s |
| Time paused as "stale" | 50% | 6% |

We also relaxed the stale-pose limit from 0.45 s to 0.7 s in the same change, so part of the drop in pauses comes from that.

**Remaining limitation: P1's hand leaving the frame stops the elephant.** When P1 lowers the left hand, it can drop out of frame, the wrist is no longer detected, and the safety check stops all travel at once. This paused travel in 19% of calibrated samples in the "after" session. P1 now sits further back and the status names the cause; a fuller fix would brake gently when only the hand is briefly lost.

Full details: [Docs/REFLECTION.md](Docs/REFLECTION.md).

## Performance scene

`Assets/StudentWork/Scenes/UpperBodyThreePersonCourse_City.unity`

Course: Start → Tree → Water → Log → Finish. At the finish: dusk, fireworks, the elephant changes into a suit, leaps back to the start and dances.

## What this repository contains

- The performance scene and the other StudentWork scenes, with `.meta` files.
- Custom scripts: `Assets/StudentWork/` (course, interactions, camera, diagnostics) and `Assets/_CityGen/` (city generator, celebration, dance, outfits).
- Adapted starter scripts: `Assets/DigiPhant/` (controller, locomotion, camera preview). Their exact fingerprints are in `student-patch-lock.json`.
- Generated city meshes, materials and textures under `Assets/_CityGen/`.
- `Packages/` and `ProjectSettings/` (Unity 6000.6.3f1, URP 17.6.0).
- Modified Python tracking code in `TrackingAdaptations/`, with dependency locks.
- Tools: `tools/restore_starter.py` (reconstruction), `tools/check_project.py` (static check), `SetupCamera.py` (Python environment).

Excluded on purpose (see `.gitignore`): Unity `Library/`, `Temp/`, `Logs/`, `UserSettings/`, builds, local `.venv`, recordings, the starter clone, the supplied elephant models and textures, MediaPipe models, and `Assets/_Reference/` (copyrighted reference images and music).

## Reconstruct from a fresh checkout

Requirements: Git, Python 3.10 or newer, Unity Hub with Unity 6000.6.3f1, internet access.

```bash
git clone https://github.com/xichen0912/sushicat.git
cd sushicat
python3 tools/restore_starter.py
```

The restore script:

1. Clones `https://github.com/kommanderpi/studentstarter.git` as `DigiPhantStarter/` and checks out commit `4e6dab5271c618d1c0bfb2022c41c0d37cd8d69f` (from `starter-lock.json`).
2. Verifies the starter's fingerprints and copies `Elephant/` and `Elephant.meta` into `Assets/` with their original metadata.
3. Downloads the small MediaPipe models it checks by checksum, and copies the adapted Python files from `TrackingAdaptations/` into `DigiPhantStarter/Tracking/`.
4. Creates `DigiPhantStarter/Tracking/.venv` and installs `requirements.txt`. The pose model `pose_landmarker_full.task` comes with the starter.

It never overwrites existing files that differ, so the tracked, adapted `Assets/DigiPhant` scripts and scenes stay as committed. Then add the project folder in Unity Hub, open it with 6000.6.3f1, and open the performance scene.

Optional finish music: not included. Drag any audio clip into **Finish Celebration → City Celebration → Finish Music**.

## Run the performance

1. Press Play. Unity starts the tracker and shows the camera preview.
2. Choose **3 / Upper body / Camera**. Stand left to right as P1, P2, P3 in the unmirrored preview.
3. Click **Set neutral pose (10 seconds)** and hold still.
4. P1 raises the left hand to walk; P2 tilts to steer; at the water and the log, P3 reaches into the green box at the top right of the preview.

Details and tuning values: [DIGIPHANT_SETUP.md](DIGIPHANT_SETUP.md).
