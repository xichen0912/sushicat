using System;
using System.Linq;
using DigiPhant;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StudentWork.Editor
{
    // Reproduces changing shoulder scale and brief role loss without camera access.
    public static class RegionalControlValidation
    {
        static int assertions;
        static void Check(bool condition, string message)
        { assertions++; if (!condition) throw new InvalidOperationException(message); }
        static PoseFrame Frame(DigiPhantController controller, float displacement = 0, float width = .2f)
            => new PoseFrame {
                version = 1, performerCount = 3, upperBodyOnly = true,
                trackingSession = "regional-control-synthetic", assignmentRevision = 1,
                diagnostics = new CameraTrackingDiagnostics { regionalTracking = true, assignedPeople = 3 },
                people = Enumerable.Range(1, 3).Select(slot => new PerformerFrame {
                    slot = slot,
                    values = new[] { (-.2f + displacement) / width, -.2f / width, 0f, 0f, 0f, 0f },
                    confidence = new[] { 1f, 1f, 0f, 0f, 1f, 1f },
                    hasLeftArm = true, leftArmConfidence = 1, hasHeadYaw = true, headYawConfidence = 1,
                    upperBodyGeometry = new UpperBodyGeometry { present = true, shoulderWidth = width,
                        leftHandVertical = -.2f + displacement, rightHandVertical = -.2f,
                        leftHandConfidence = 1, rightHandConfidence = 1 }
                }).ToArray()
            };
        static void Accept(DigiPhantController controller, PoseFrame frame, float now)
            => Check(controller.AcceptPacket(JsonUtility.ToJson(frame), now), "Valid regional regression packet rejected.");

        public static void RunBatch()
        {
            assertions = 0;
            var scene = EditorSceneManager.OpenScene(UpperBodyCourseSceneSetup.CourseScene);
            var controller = UpperBodyCourseSceneSetup.Controller(scene);
            var loco = controller.GetComponent<DigiPhantLocomotion>();
            try
            {
                Check(Mathf.Abs(loco.forward.sensitivity - .55f) < .001f,
                    "Saved hand sensitivity must leave a usable gradual walking range.");
                Check(controller.regionBoundaries.Length == 4 && Mathf.Abs(controller.regionBoundaries[1] - 1f / 3) < .001f
                    && Mathf.Abs(controller.regionBoundaries[2] - 2f / 3) < .001f,
                    "The saved scene must request three equal standing regions.");
                controller.inputMode = InputMode.Camera;
                // Isolate calibrated geometry/reverse here. Default jump/scale guards
                // are exercised end-to-end in OptimizedControlValidation.
                controller.standingScaleTolerance = .3f;
                controller.handJumpTolerance = 2;
                loco.inputSmoothing = 0;
                controller.CaptureRestPose();
                Accept(controller, Frame(controller), 100);
                Check(controller.CalibrateAt(100), "Regional geometry must permit upper-body calibration.");
                Accept(controller, Frame(controller, 0, .16f), 100.05f);
                Check(controller.TryReadRawMovement(1, Movement.LeftHandHeight, 100.05f, out float raw, out float neutral),
                    "Raw movement remains available for diagnostics during a scale change.");
                Check(controller.TryReadMovement(1, Movement.LeftHandHeight, 100.05f, out float relative)
                    && Mathf.Abs(relative) < .001f, "Changing shoulder width alone must never change the hand throttle.");
                controller.ApplyControls(100.05f, .05f);
                Check(loco.CurrentSpeed == 0 && !loco.ReversePending, "The scale-drift repro must remain stopped.");
                Accept(controller, Frame(controller, .1f, .16f), 100.1f);
                Check(controller.TryReadMovement(1, Movement.LeftHandHeight, 100.1f, out relative)
                    && Mathf.Abs(relative - .5f) < .001f, "Actual hand movement must use the calibrated shoulder scale.");
                controller.ApplyControls(100.1f, .05f);
                Check(loco.CurrentSpeed > 0 && !loco.IsRunning, "A modest hand raise should walk, not saturate into running.");

                var missingGeometry = Frame(controller);
                missingGeometry.people[0].upperBodyGeometry = null;
                Accept(controller, missingGeometry, 100.15f);
                Check(!controller.IsMovementVisible(1, Movement.LeftHandHeight, 100.15f)
                    && !controller.TryReadMovement(1, Movement.LeftHandHeight, 100.15f, out _),
                    "A calibrated geometry channel must not silently fall back to the legacy formula.");
                controller.ApplyControls(100.15f, .05f);
                Check(loco.CurrentSpeed == 0, "Missing geometry must stop immediately.");
                var lowConfidence = Frame(controller);
                lowConfidence.people[0].upperBodyGeometry.leftHandConfidence = 0;
                Accept(controller, lowConfidence, 100.2f);
                Check(!controller.IsPerformerInputReady(1, 100.2f, out _), "Geometry quality must be reflected in role readiness.");
                var malformed = Frame(controller);
                malformed.people[0].upperBodyGeometry.shoulderWidth = .001f;
                Check(!controller.AcceptPacket(JsonUtility.ToJson(malformed), 100.25f), "Degenerate shoulder scale must be rejected.");
                malformed = Frame(controller); malformed.people[0].upperBodyGeometry.rightHandConfidence = 1.1f;
                Check(!controller.AcceptPacket(JsonUtility.ToJson(malformed), 100.25f), "Invalid geometry confidence must be rejected.");

                float now = 101;
                void Step(float displacement, int count)
                {
                    for (int i = 0; i < count; i++) {
                        now += .05f; Accept(controller, Frame(controller, displacement), now);
                        controller.ApplyControls(now, .05f);
                    }
                }
                loco.StopMotion();
                Step(-.07f, 8);
                Check(loco.DesiredForward < 0 && loco.CurrentSpeed == 0 && !loco.ReversePending,
                    "Small sustained downward noise must not reverse.");
                Step(-.2f, 2);
                Check(loco.CurrentSpeed == 0 && loco.ReversePending, "A short negative spike must wait for deliberate reverse confirmation.");
                Step(0, 1);
                Check(loco.CurrentSpeed == 0 && !loco.ReversePending, "Neutral must clear pending reverse.");
                Step(-.2f, 4);
                Check(loco.CurrentSpeed == 0 && loco.ReversePending, "Returning from neutral must restart the full reverse hold.");
                Step(-.2f, 2);
                Check(loco.CurrentSpeed < 0 && !loco.ReversePending, "A deliberate quarter-second downward hold must reverse.");
                Step(0, 1);
                Check(loco.CurrentSpeed == 0, "Returning to neutral must stop reverse with zero smoothing.");

                int revision = controller.CalibrationRevision;
                var lostMiddle = Frame(controller); lostMiddle.people = lostMiddle.people.Where(p => p.slot != 2).ToArray();
                now += .05f; Accept(controller, lostMiddle, now); controller.ApplyControls(now, .05f);
                Check(loco.CurrentSpeed == 0 && controller.IsCalibrated && controller.CalibrationRevision == revision,
                    "Regional brief loss must pause travel without discarding neutral calibration.");
                now += .3f; Accept(controller, Frame(controller, .1f), now); controller.ApplyControls(now, .05f);
                Check(loco.CurrentSpeed > 0 && controller.IsCalibrated && controller.CalibrationRevision == revision,
                    "The same middle slot returning within grace must recover safely.");
                Step(-.2f, 2);
                now += .05f; Accept(controller, lostMiddle, now); controller.ApplyControls(now, .05f);
                Check(loco.CurrentSpeed == 0 && !loco.ReversePending, "Input loss must clear reverse confirmation progress.");
                Step(-.2f, 4);
                Check(loco.CurrentSpeed == 0 && loco.ReversePending, "Reacquired input must require the complete hold again.");
                Step(-.2f, 2);
                Check(loco.CurrentSpeed < 0, "Deliberate reverse remains possible after reacquisition.");
                now += .05f; Accept(controller, lostMiddle, now); controller.ApplyControls(now, .05f);
                now += 2.01f; Accept(controller, lostMiddle, now); controller.ApplyControls(now, .05f);
                Check(controller.IsCalibrated && controller.CalibrationRevision == revision && loco.CurrentSpeed == 0,
                    "Long region loss must pause travel but keep the fixed seat's neutral pose.");
                Step(.1f, 4);
                Check(loco.CurrentSpeed > 0 && controller.IsCalibrated && controller.CalibrationRevision == revision,
                    "The same seat returning after a long loss must resume without recalibration.");

                controller.BeginCalibrationCountdown(200);
                for (int i = 0; i < 5; i++) Accept(controller, Frame(controller, 0, i % 2 == 0 ? .2f : .16f), 208.8f + i * .3f);
                controller.UpdateCalibrationCountdown(210);
                Check(controller.IsCalibrated && !controller.CalibrationPending,
                    "Low-frame-rate stable geometry should calibrate despite variable shoulder-width ratios.");
                Check(Mathf.Abs(controller.NeutralShoulderWidth(1) - .2f) < .001f, "Neutral geometry must use a median scale.");
                Accept(controller, Frame(controller, 0, .16f), 210.1f);
                Check(controller.TryReadMovement(1, Movement.LeftHandHeight, 210.1f, out relative) && Mathf.Abs(relative) < .001f,
                    "Median calibration must preserve zero under later shoulder scale changes.");
                Debug.Log("REGIONAL_CONTROL_VALIDATION_OK: " + assertions + " assertions; fixed calibrated geometry, availability, gradual speed, deliberate reverse hold, missing-role stop/recovery and low-frame-rate median calibration; synthetic only, no camera.");
            }
            finally { loco.StopAnimation(); EditorSceneManager.OpenScene(UpperBodyCourseSceneSetup.CourseScene); }
        }
    }
}
