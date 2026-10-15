using System;
using System.Linq;
using DigiPhant;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StudentWork.Editor
{
    // Regression packets for observed failures. Never opens the camera or saves test poses.
    public static class TrackingSafetyValidation
    {
        static int assertions;
        static void Check(bool condition, string message)
        { assertions++; if (!condition) throw new InvalidOperationException(message); }
        static PoseFrame Frame(DigiPhantController controller, float hand = 0, string session = "session-a", int revision = 1)
            => new PoseFrame { version = 1, performerCount = controller.performerCount, upperBodyOnly = true,
                trackingSession = session, assignmentRevision = revision, trackingStatus = "Synthetic ready",
                people = Enumerable.Range(1, controller.performerCount).Select(slot => new PerformerFrame {
                    slot = slot, values = new[] { hand, 0f, 0f, 0f, 0f, 0f }, confidence = Enumerable.Repeat(1f, 6).ToArray(),
                    hasLeftArm = true, leftArmConfidence = 1, hasHeadYaw = true, headYawConfidence = 1
                }).ToArray() };
        static void Accept(DigiPhantController controller, PoseFrame frame, float time)
            => Check(controller.AcceptPacket(JsonUtility.ToJson(frame), time), "Valid regression packet rejected.");
        public static void RunBatch()
        {
            assertions = 0;
            var scene = EditorSceneManager.OpenScene(UpperBodyCourseSceneSetup.CourseScene);
            var controller = UpperBodyCourseSceneSetup.Controller(scene);
            var locomotion = controller.GetComponent<DigiPhantLocomotion>();
            controller.inputMode = InputMode.Camera;
            controller.CaptureRestPose();
            Accept(controller, Frame(controller), 100);
            Check(controller.CalibrateAt(100), "Initial tracking session must calibrate.");
            Accept(controller, Frame(controller), 100.1f);
            Check(controller.IsCalibrated, "Repeated unchanged assignment must retain calibration.");
            int revision = controller.CalibrationRevision;
            Accept(controller, Frame(controller, 0, "session-a", 2), 100.2f);
            Check(!controller.IsCalibrated && controller.CalibrationRevision > revision && locomotion.CurrentSpeed == 0,
                "External reassignment must clear calibration and motion.");
            Check(controller.CalibrateAt(100.2f), "Reassigned session must permit new calibration.");
            Accept(controller, Frame(controller, 0, "session-b", 2), 100.3f);
            Check(!controller.IsCalibrated, "Bridge restart at the same revision must clear calibration.");
            var noHand = Frame(controller, 0, "session-b", 2);
            noHand.people[0].confidence[0] = 0;
            controller.minimumConfidence = 0;
            Accept(controller, noHand, 100.4f);
            Check(!controller.IsMovementVisible(1, Movement.LeftHandHeight, 100.4f) && !controller.CalibrateAt(100.4f),
                "Invalid zero-confidence hand must remain invalid even with a zero confidence threshold.");
            controller.minimumConfidence = .5f;

            controller.BeginCalibrationCountdown(101);
            for (int i = 0; i <= 30; i++)
                Accept(controller, Frame(controller, i == 30 ? .8f : 0, "session-b", 2), 109.5f + i * .05f);
            controller.UpdateCalibrationCountdown(111);
            Check(controller.IsCalibrated && !controller.CalibrationPending,
                "Stable window with one final-frame outlier must use the median.");
            Accept(controller, Frame(controller, 0, "session-b", 2), 111.01f);
            Check(controller.TryReadMovement(1, Movement.LeftHandHeight, 111.01f, out float value) && Mathf.Abs(value) < .001f,
                "Last-frame jitter must not bias the neutral height.");
            controller.BeginCalibrationCountdown(112);
            for (int i = 0; i <= 30; i++)
                Accept(controller, Frame(controller, i % 2 == 0 ? 0 : .5f, "session-b", 2), 120.5f + i * .05f);
            controller.UpdateCalibrationCountdown(122);
            Check(!controller.IsCalibrated && controller.CalibrationPending && controller.Status.Contains("is moving"),
                "Moving through the calibration deadline must wait for a stable window.");
            for (int i = 1; i <= 32; i++)
                Accept(controller, Frame(controller, 0, "session-b", 2), 122 + i * .05f);
            controller.UpdateCalibrationCountdown(123.6f);
            Check(controller.IsCalibrated, "Holding still after the deadline must finish without restarting ten seconds.");

            var head = controller.controls.Single(control => control.label == "Head turn");
            head.smoothing = 0;
            var turned = Frame(controller, 0, "session-b", 2); turned.people[2].headYaw = .5f;
            Accept(controller, turned, 124); controller.ApplyControls(124, .02f);
            Check(Mathf.Abs(head.current - .5f * head.sensitivity) < .01f, "Head must follow the fresh yaw.");
            turned.people[2].headYawConfidence = 0;
            Accept(controller, turned, 124.1f); controller.ApplyControls(124.1f, .02f);
            Check(head.current > .1f && !controller.IsMovementVisible(3, Movement.HeadYaw, 124.1f),
                "Brief head occlusion may hold the visual pose but must remain invalid for readiness.");
            controller.ApplyControls(124.3f, .02f);
            Check(Mathf.Abs(head.current) < .001f, "Long head loss must return to neutral.");

            controller.SetInputMode(InputMode.TestSliders);
            locomotion.inputSmoothing = 0; locomotion.testSteering = 0;
            locomotion.testForward = .66f; controller.ApplyControls(130, .02f);
            Check(locomotion.IsRunning && locomotion.CurrentAction == "Run", "Crossing run entry must run.");
            locomotion.testForward = .63f; controller.ApplyControls(130.02f, .02f);
            Check(locomotion.IsRunning, "Threshold jitter must not switch straight back to walking.");
            locomotion.testForward = .58f; controller.ApplyControls(130.04f, .02f);
            Check(!locomotion.IsRunning && locomotion.CurrentAction == "Walk", "Lowering below the exit threshold must walk.");
            locomotion.testForward = -.2f; controller.ApplyControls(130.06f, .02f);
            Check(!locomotion.IsRunning && locomotion.CurrentSpeed < 0, "Reverse must clear running.");
            locomotion.StopAnimation();
            EditorSceneManager.OpenScene(UpperBodyCourseSceneSetup.CourseScene);
            Debug.Log("TRACKING_SAFETY_VALIDATION_OK: " + assertions + " assertions; identity revision/session, zero-confidence wrist, median/stable calibration, visual-only yaw grace, walk/run hysteresis; synthetic packets, no camera.");
        }
        public static void ValidateAllBatch()
        {
            UpperBodyCourseSceneSetup.UpgradeSafetyBatch();
            RunBatch();
            RegionalControlValidation.RunBatch();
            OptimizedControlValidation.RunBatch();
            ControlDiagnosticsValidation.RunBatch();
            UpperBodyCourseValidation.RunBatch();
            P3InteractionValidation.RunBatch();
            StudentLocomotionValidation.Run();
            EditorSceneManager.OpenScene(UpperBodyCourseSceneSetup.CourseScene);
            Debug.Log("UPPER_BODY_OPTIMIZED_ALL_VALIDATION_OK");
        }
    }
}
