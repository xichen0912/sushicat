using System;
using System.IO;
using System.Linq;
using DigiPhant;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StudentWork.Editor
{
    // Synthetic packets only. The saved scene is reloaded; no webcam, UDP receiver,
    // reflection, private lifecycle invocation, or test-state save is involved.
    public static class ThreePerformerValidation
    {
        [MenuItem("Student Work/Validate Three Performer Fixes Without Camera")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Stop Play and exit Prefab Mode before validating.");
            var current = SceneManager.GetActiveScene();
            if (SceneManager.sceneCount != 1 || !current.IsValid() || current.isDirty
                || string.IsNullOrEmpty(current.path) || !File.Exists(current.path))
                throw new InvalidOperationException("Open one saved, unmodified scene. Synthetic validation never saves scene state.");
            string restorePath = current.path;
            try
            {
                var scene = EditorSceneManager.OpenScene(TeamSceneSetup.ReferenceScene, OpenSceneMode.Single);
                var controllers = scene.GetRootGameObjects()
                    .SelectMany(g => g.GetComponentsInChildren<DigiPhantController>(true)).ToArray();
                Check(controllers.Length == 1, "Expected one reference controller.");
                var controller = controllers[0];
                var locomotion = controller.GetComponent<DigiPhantLocomotion>();
                Check(locomotion, "Missing reference locomotion.");
                controller.SetPerformerCount(3);
                controller.SetUpperBodyOnly(false);
                controller.inputMode = InputMode.Camera; // Direct selection avoids starting a receiver.
                locomotion.enableLocomotion = false;
                controller.CaptureRestPose();
                foreach (var control in controller.controls) control.smoothing = 0;

                ValidateIndependentBodyRoles(controller);
                ValidateReadinessDiagnostics(controller);
                ValidateGroupTravel(controller, locomotion);
                ValidateCanonicalSources(controller, locomotion);
                ValidateActiveSlotBoundary(controller);
                ValidateZeroConfidenceAbsence(controller);
            }
            finally
            {
                var restored = EditorSceneManager.OpenScene(restorePath, OpenSceneMode.Single);
                var controller = restored.GetRootGameObjects()
                    .SelectMany(g => g.GetComponentsInChildren<DigiPhantController>(true)).FirstOrDefault();
                Selection.activeGameObject = controller
                    ? controller.gameObject : restored.GetRootGameObjects().FirstOrDefault();
                ActiveEditorTracker.sharedTracker.ForceRebuild();
                EditorApplication.RepaintHierarchyWindow();
                EditorApplication.RepaintProjectWindow();
                SceneView.RepaintAll();
            }
            Debug.Log("THREE_PERFORMER_VALIDATION_OK: independent P1/P2/P3 body roles, explicit occluded-input calibration diagnosis, missing-P2/P3 travel stop, sustained-loss recalibration, foot-confidence flicker, 3-to-1-to-3 source preservation, inactive source weighting, active-slot bounds and zero-threshold absence; synthetic Edit-mode checks, no webcam or identity-tracking accuracy claim.");
        }

        [MenuItem("Student Work/Validate Three Performer Fixes Without Camera", true)]
        static bool CanRun() => !EditorApplication.isPlayingOrWillChangePlaymode;

        static PoseFrame Frame(DigiPhantController controller, params int[] slots)
        {
            if (slots.Length == 0) slots = Enumerable.Range(1, controller.performerCount).ToArray();
            return new PoseFrame {
                version = 1, performerCount = controller.performerCount,
                upperBodyOnly = controller.upperBodyOnly,
                people = slots.Select(slot => new PerformerFrame {
                    slot = slot, values = new float[6], confidence = Enumerable.Repeat(1f, 6).ToArray()
                }).ToArray()
            };
        }

        static void Accept(DigiPhantController controller, PoseFrame frame, float now)
            => Check(controller.AcceptPacket(JsonUtility.ToJson(frame), now), "Valid synthetic packet rejected.");

        static void ValidateIndependentBodyRoles(DigiPhantController controller)
        {
            var roles = new[] {
                controller.controls.Single(c => c.label == "Front left leg"),
                controller.controls.Single(c => c.label == "Rear left leg"),
                controller.controls.Single(c => c.label == "Trunk curl")
            };
            Check(roles.Select(c => c.performer).SequenceEqual(new[] { 1, 2, 3 }), "Default three-person body roles changed.");
            Check(roles.All(c => c.bones != null && c.bones.Length > 0 && c.bones[0]), "Missing test rig references.");
            var initial = roles.Select(c => c.bones[0].localRotation).ToArray();
            Accept(controller, Frame(controller), 10);
            Check(controller.CalibrateAt(10), "Three-person baseline calibration failed.");
            for (int role = 0; role < roles.Length; role++)
            {
                var frame = Frame(controller);
                frame.people[roles[role].performer - 1].values[(int)roles[role].movement] = 1;
                Accept(controller, frame, 10 + role * .1f);
                controller.ApplyControls(10 + role * .1f, 1);
                for (int bone = 0; bone < roles.Length; bone++)
                {
                    float angle = Quaternion.Angle(initial[bone], roles[bone].bones[0].localRotation);
                    Check(bone == role ? angle > 10 : angle < .01f,
                        "P" + (role + 1) + " packet did not independently drive its assigned body role.");
                }
            }
            controller.ResetControls();
        }

        static void ValidateReadinessDiagnostics(DigiPhantController controller)
        {
            controller.InvalidateCalibration();
            var frame = Frame(controller);
            frame.people[0].confidence[(int)Movement.LeftFootLift] = .4f;
            Accept(controller, frame, 20);
            Check(controller.IsTracked(1, 20), "A visible P1 should still report presence when one foot is occluded.");
            Check(!controller.AreMappedInputsReady(20, out string missing)
                && missing.Contains("P1") && missing.Contains("LeftFootLift"),
                "Readiness must identify the exact occluded P1 input.");
            Check(!controller.CalibrateAt(20) && !controller.IsCalibrated
                && controller.Status.Contains("P1") && controller.Status.Contains("LeftFootLift"),
                "Occluded calibration should fail with an actionable P1/input diagnosis.");
        }

        static void ValidateGroupTravel(DigiPhantController controller, DigiPhantLocomotion locomotion)
        {
            locomotion.enableLocomotion = true;
            locomotion.requireAllPerformersForTravel = true;
            locomotion.inputSmoothing = 0;
            locomotion.animationBlendSeconds = .01f;
            locomotion.stageRadius = 30;
            Check(locomotion.Initialise(), "Missing travel setup.");
            Accept(controller, Frame(controller), 100);
            Check(controller.CalibrateAt(100), "Three-person travel baseline calibration failed.");
            PoseFrame Moving(params int[] slots)
            {
                var frame = Frame(controller, slots);
                frame.people.Single(p => p.slot == 1).values[(int)Movement.LeftHandHeight] = .4f;
                return frame;
            }
            Accept(controller, Moving(), 100);
            controller.ApplyControls(100, 1f / 60);
            Check(locomotion.CurrentSpeed > 0, "Fully tracked three-person group failed to travel.");
            var before = locomotion.travelRoot.position;
            Accept(controller, Moving(1, 3), 100.1f);
            controller.ApplyControls(100.1f, .1f);
            Check(locomotion.CurrentSpeed == 0 && Vector3.Distance(before, locomotion.travelRoot.position) < .001f,
                "Missing P2 must immediately stop group travel despite visible P1.");
            Check(controller.IsCalibrated, "Brief role loss must not discard calibration immediately.");
            Accept(controller, Moving(), 100.2f);
            controller.ApplyControls(100.2f, .1f);
            Check(locomotion.CurrentSpeed > 0, "Brief-loss recovery failed to resume the calibrated group.");
            before = locomotion.travelRoot.position;
            Accept(controller, Moving(1, 2), 100.3f);
            controller.ApplyControls(100.3f, .1f);
            Check(locomotion.CurrentSpeed == 0 && Vector3.Distance(before, locomotion.travelRoot.position) < .001f,
                "Missing P3 must immediately stop group travel despite visible P1.");
            float sustained = 100.3f + controller.trackingTimeout + .1f;
            Accept(controller, Moving(1, 2), sustained);
            controller.ApplyControls(sustained, .1f);
            Check(!controller.IsCalibrated && locomotion.CurrentSpeed == 0,
                "Sustained P3 loss must require a new neutral pose.");

            Accept(controller, Frame(controller), 102);
            Check(controller.CalibrateAt(102), "Recovery baseline calibration failed.");
            var flicker = Moving();
            flicker.people[0].confidence[(int)Movement.LeftFootLift] = .4f;
            Accept(controller, flicker, 102.1f);
            controller.ApplyControls(102.1f, .1f);
            Accept(controller, flicker, 103.1f);
            controller.ApplyControls(103.1f, .1f);
            Check(controller.IsCalibrated && locomotion.CurrentSpeed > 0,
                "One low-confidence foot must not invalidate a still-present group or block unrelated travel inputs.");
            locomotion.ResetPosition();
        }

        static void ValidateCanonicalSources(DigiPhantController controller, DigiPhantLocomotion locomotion)
        {
            locomotion.forward.sources = Enumerable.Range(1, 3).Select(slot => new MovementSource {
                performer = slot, movement = Movement.LeftHandHeight, weight = 1
            }).ToArray();
            locomotion.forward.combination = SignalCombination.Average;
            locomotion.forward.deadZone = 0;
            controller.SetPerformerCount(1);
            Check(locomotion.forward.sources.Select(s => s.performer).SequenceEqual(new[] { 1, 2, 3 }),
                "Selecting one performer must preserve canonical P2/P3 custom sources.");
            Accept(controller, Frame(controller), 200);
            Check(controller.CalibrateAt(200), "Inactive custom sources should not prevent one-person calibration.");
            var solo = Frame(controller);
            solo.people[0].values[(int)Movement.LeftHandHeight] = .3f;
            Accept(controller, solo, 200);
            Check(locomotion.forward.TryRead(controller, 200, out float value) && Mathf.Abs(value - .3f) < .001f,
                "Inactive custom sources must contribute neither a signal nor averaging weight.");
            controller.SetPerformerCount(3);
            Check(locomotion.forward.sources.Select(s => s.performer).SequenceEqual(new[] { 1, 2, 3 }),
                "3-to-1-to-3 must restore active canonical custom roles without rewriting them as P1.");
            Accept(controller, Frame(controller), 201);
            Check(controller.CalibrateAt(201), "Restored three-person custom-source calibration failed.");
            var group = Frame(controller);
            group.people[0].values[(int)Movement.LeftHandHeight] = .1f;
            group.people[1].values[(int)Movement.LeftHandHeight] = .2f;
            group.people[2].values[(int)Movement.LeftHandHeight] = .6f;
            Accept(controller, group, 201);
            Check(locomotion.forward.TryRead(controller, 201, out value) && Mathf.Abs(value - .3f) < .001f,
                "Restored custom forward mapping must use all three independent signals.");
        }

        static void ValidateActiveSlotBoundary(DigiPhantController controller)
        {
            int received = controller.ReceivedFrames;
            var invalid = Frame(controller, 4);
            Check(!controller.AcceptPacket(JsonUtility.ToJson(invalid), 202)
                && controller.ReceivedFrames == received && !controller.IsTracked(4, 202),
                "A count-three packet must reject inactive slot four without counting a received frame.");
        }

        static void ValidateZeroConfidenceAbsence(DigiPhantController controller)
        {
            float originalMinimum = controller.minimumConfidence;
            try
            {
                controller.minimumConfidence = 0;
                Accept(controller, Frame(controller), 300);
                Check(controller.IsTracked(2, 300), "Present P2 should be tracked before the zero-threshold absence test.");
                Accept(controller, Frame(controller, 1, 3), 300.1f);
                Check(controller.IsTracked(1, 300.1f) && controller.IsTracked(3, 300.1f)
                    && !controller.IsTracked(2, 300.1f),
                    "Omitted P2 must be absent immediately even with minimum confidence zero.");
                var empty = Frame(controller);
                empty.people = Array.Empty<PerformerFrame>();
                Accept(controller, empty, 300.2f);
                Check(Enumerable.Range(1, 3).All(slot => !controller.IsTracked(slot, 300.2f)),
                    "An empty valid frame must mark every performer absent at confidence threshold zero.");
                Check(!controller.IsTracked(2, -1001), "A never-seen sentinel must not report presence for a negative test time.");

                Accept(controller, Frame(controller), 301);
                var wrongCount = Frame(controller);
                wrongCount.performerCount = 2;
                Check(!controller.AcceptPacket(JsonUtility.ToJson(wrongCount), 301.1f)
                    && Enumerable.Range(1, 3).All(slot => !controller.IsTracked(slot, 301.1f)),
                    "A group-size mismatch must clear all presence at confidence threshold zero.");
                Accept(controller, Frame(controller), 302);
                var wrongMode = Frame(controller);
                wrongMode.upperBodyOnly = !controller.upperBodyOnly;
                Check(!controller.AcceptPacket(JsonUtility.ToJson(wrongMode), 302.1f)
                    && Enumerable.Range(1, 3).All(slot => !controller.IsTracked(slot, 302.1f)),
                    "A movement-mode mismatch must clear all presence at confidence threshold zero.");
            }
            finally { controller.minimumConfidence = originalMinimum; }
        }

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
