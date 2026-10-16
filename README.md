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
- Earlier notes (Chinese): [three-person bug report](Docs/THREE_PERSON_BUGS_中文.md), [mapping](Docs/MAPPING_中文.md), [archived setup log](Docs/history/DIGIPHANT_SETUP_zh_2026-10-11.md)

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
