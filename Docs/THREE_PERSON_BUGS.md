# Three-person control: problems found, fixes and on-site checks

Written 2026-10-07, translated and updated 2026-10-15. These issues come from reviewing the pinned starter and this project's code. P1, P2 and P3 are control slots assigned by position, not face recognition or identity checks.

## Problems and how we handled them

| Problem or risk | Pinned starter behaviour | Our fix | Limits |
|---|---|---|---|
| Custom walking sources lost after switching 3 → 1 → 3 performers | `ClampPerformers` rewrote P2/P3 sources to P1, and switching back to three did not restore them | Keep the serialized sources; skip inactive slots when reading, and leave them out of the Average weight | Only matters for custom multi-source locomotion |
| Elephant keeps walking when P2 or P3 leaves the frame | Walking and steering are driven by P1, so P2/P3 were not required | New `requireAllPerformersForTravel = true`: in Camera mode with two or more people, a missing member pauses travel; a sustained absence requires recalibration | A team-safety policy; it does not prove the remaining slots are the right people |
| A person shows as visible but calibration fails without a clear reason | Any valid signal counted as visible; calibration rejected missing inputs with a generic message | Separate "person visible" from "all required inputs ready", list the missing input (for example `P1 LeftHandHeight`), clear the state on failed calibration | Diagnostics only; a single low-confidence joint does not count as the whole person missing |
| A bystander takes a slot at start-up | With four people detected, the leftmost three were assigned | Three- and four-person start-up requires exactly that many people; an extra person during the run locks assignment until reset | Only people the model detects can trigger this |
| Roles recovered from stale positions after long loss or ambiguity | Old positions kept with no expiry; ambiguity rejected only the current frame | `StablePerformerTracker` times each role; absence over 2 s, an ambiguous match or an extra person locks all roles until an explicit reset | 2 s is the identity threshold, separate from Unity's 0.5 s tracking timeout |
| A fourth slot accepted in a three-person packet | Packets allowed slots 1–4 regardless of team size | Three-person mode rejects P4; `IsTracked(4)` never reports an inactive slot | Protocol consistency, not person recognition |
| With a confidence threshold of 0, an omitted role still looked present | Confidence was cleared but the last-seen time kept | Every valid packet clears presence first and only refreshes roles in the packet; mode or count mismatches also clear presence | The default threshold stays 0.5 |

When a role is missing, Unity turns speed and steering input off immediately instead of letting smoothing drift the elephant forward.

## Where the changes live

- Unity: `Assets/DigiPhant/Runtime/DigiPhantController.cs` and `DigiPhantLocomotion.cs`. Namespaces, protocol and bone mappings are unchanged. The starter's originals and the teacher's editor tools are untouched.
- Python: `TrackingAdaptations/bridge.py` uses the new `identity_tracking.py`. The original `PerformerTracker` is kept for comparison; the runtime uses `StablePerformerTracker`. Pose features, the six signals and the UDP JSON schema are unchanged.
- The optional `ImageSegmenter` only draws a combined person outline and its screen share. It does not tell P1/P2/P3 apart and cannot fix identity swaps.
- `ThreePerformerValidation.cs` adds **Student Work → Validate Three Performer Fixes Without Camera**. It uses synthetic packets, does not open the camera, and restores the saved scene afterwards.

## Checks without a camera

- Python: 109 adaptation tests passed (fixed seats, detection-order changes, short and long absences, ambiguity locks, bystanders, explicit reset, fist-signal mapping, team-size limits), plus the starter's original 11 tests. These are synthetic coordinates, not real identification rates.
- Unity: the new menu reported `THREE_PERFORMER_VALIDATION_OK`, and the setup and 26-step locomotion checks reported `STUDENT_SETUP_VALIDATION_OK` and `STUDENT_LOCOMOTION_VALIDATION_OK` on 2026-10-07. Console: 0 errors, 1 warning from the unmodified teacher script `SavannahCourseValidation.cs:116`.
- Re-run the Python tests from `TrackingAdaptations/` with `../DigiPhantStarter/Tracking/.venv/bin/python -m unittest discover -s tests -v`.

## Running with three people

1. Open `Assets/StudentWork/Scenes/UpperBodyThreePersonCourse_City.unity`. Close any other tracker window using the camera.
2. Fix the camera. Stand left to right as P1, P2, P3 in the unmirrored preview, with shoulders and both hands visible. Mark the seats on the floor; keep bystanders out of frame.
3. Press Play, choose **3 / Upper body / Camera**, and check every slot shows `inputs ready`. Fix any listed missing input before calibrating.
4. Click **Set neutral pose (10 seconds)** and hold still until `Neutral pose saved`. Recalibrate after changing team size, body mode or roles.
5. Move one person at a time while the others hold neutral, then drive together slowly.
6. If the preview shows `RESET REQUIRED`, stop, return to the marked seats, click **Reassign people** and recalibrate.
7. For an emergency stop, turn off **Enable locomotion** or exit Play. Do not disable `requireAllPerformersForTravel` to hide tracking loss.

## Remaining limitations

Matching uses position only, with no appearance or identity cues. If two people swap places quickly in a way that still looks unambiguous, roles can swap without a warning. The locks remove several unsafe recovery paths but cannot make every crossing correct. Keep fixed seats, leave space between performers, avoid crossing, and reset and recalibrate after a mistake. Free crossing would need appearance tracking, markers or multiple cameras, which this project does not implement.
