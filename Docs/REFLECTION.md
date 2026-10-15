# Group reflection — team sushicat

Xinru Lu (P1), Xi Chen (P2), Ge Huang (P3).

## Tested improvement: lighter tracking, fewer pauses

**Problem.** Walking looked jerky, as if the elephant were shuffling. The control diagnostics showed the tracker had slowed down. Whenever a pose was older than the safety limit, Unity paused travel, so the elephant kept stopping and starting.

**What we changed.** Unity now starts the tracker without the person-outline segmenter and the cup detector (`--no-segmentation --no-cup-detection`). Neither affects control: the outline was preview-only and cup detection was diagnostics-only. In the same change we raised the stale-pose limit from 0.45 s to 0.7 s.

**How we compared.** Two Play sessions on 2026-10-12, same computer, camera, scene and three performers, before and after the change. We used only samples taken while calibrated. Data: `Logs/ControlDiagnostics/20261012-202839-66d10e6e` (before) and `20261012-203554-7d53676d` (after); `Logs/` is excluded from Git, so the numbers are recorded here.

| Measure (median unless noted) | Before | After |
|---|---|---|
| Session length, calibrated samples | 163 s, 519 | 227 s, 1,728 |
| Pose updates per second | 3.1 | 6.9 |
| Pose age when used | 0.45 s | 0.19 s |
| Share of time paused as "stale" | 50% | 6% |
| Share of time with input ready | 40% | 74% |

**Interpretation.** Update rate and pose age come straight from the lighter tracker, and both more than doubled in quality. The drop in stale pauses is partly due to the relaxed limit as well, so we do not credit it to one change alone. This is one before/after pair, not repeated trials.

**Cost.** The preview no longer shows the cyan person outline. Cup diagnostics are no longer logged.

## Remaining limitation: P1's hand leaving the frame stops the elephant

**What happens.** P1 controls speed with the left hand. When P1 lowers the hand, it can drop below the frame or behind the body. The wrist is then not detected, and the safety check stops all travel at once.

**How often.** In the "after" session above, travel was paused for this reason in 19% of calibrated samples (332 of 1,728), and wrist confidence was 0 in every one of them. One episode lasted 24 s.

**Impact.** Hard stops and restarts make the walk look jerky, and the team has to wait for P1's hand to be found again.

**What we did so far.** The status now says "P1 left hand not detected" instead of the misleading "sudden hand change", and P1 sits further back so the lowered hand stays in frame.

**Next step.** Treat a briefly lost hand as "neutral" with gentle braking instead of an instant stop, while keeping the instant stop when a whole performer disappears. Mark P1's seat and hand range on the floor so framing is repeatable.
