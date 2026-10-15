using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DigiPhant;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StudentWork.Editor
{
    // Deterministic Edit-mode packets only. Tests never call SetInputMode(Camera),
    // start Play, use a webcam, or save the synthetic scene state.
    public static class P3InteractionValidation
    {
        static int assertions;

        [MenuItem("Student Work/Validate P3 Interactions Without Camera")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Stop Play and exit Prefab Mode before P3 validation.");
            var current = SceneManager.GetActiveScene();
            if (SceneManager.sceneCount != 1 || !current.IsValid() || current.isDirty
                || string.IsNullOrEmpty(current.path) || !File.Exists(current.path))
                throw new InvalidOperationException("Open one saved, unmodified scene. P3 validation never saves test state.");
            string restorePath = current.path;
            assertions = 0;
            try
            {
                // The instructor reference retains its original six-value ear mapping.
                // Run existing student checks in that compatibility configuration.
                var reference = EditorSceneManager.OpenScene(TeamSceneSetup.ReferenceScene, OpenSceneMode.Single);
                Controller(reference).SetIndependentP3Ears(false);
                TeamSceneValidation.ValidateSetupWithoutCamera();
                ThreePerformerValidation.Run();

                reference = EditorSceneManager.OpenScene(TeamSceneSetup.ReferenceScene, OpenSceneMode.Single);
                var controller = Controller(reference);
                ValidateSoloControls(controller);
                ValidateCupPacketGuards(controller);
                ValidateCupGestureState();
                ValidateClosedFistGestureState();
                ValidateGestureLogLifecycle();
                ValidateSavedSceneConfiguration();
            }
            finally
            {
                var restored = EditorSceneManager.OpenScene(restorePath, OpenSceneMode.Single);
                var controllers = restored.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<DigiPhantController>(true)).ToArray();
                if (controllers.Length == 1) Selection.activeGameObject = controllers[0].gameObject;
                ActiveEditorTracker.sharedTracker.ForceRebuild();
                EditorApplication.RepaintHierarchyWindow();
                SceneView.RepaintAll();
            }
            Debug.Log("P3_INTERACTION_VALIDATION_OK: " + assertions
                + " assertions; legacy controls, independent ears, directional head/trunk, solo body/travel isolation, role restoration, cup packet/freshness guards, closed-fist water state, legacy log compatibility, monotonic session duration and closed-log consistency; synthetic only, no camera or Play.");
        }

        public static void RunBatch()
        {
            if (Application.isBatchMode)
                EditorSceneManager.OpenScene(TeamSceneSetup.TeamScene, OpenSceneMode.Single);
            Run();
        }

        [MenuItem("Student Work/Validate P3 Interactions Without Camera", true)]
        static bool CanRun() => !EditorApplication.isPlayingOrWillChangePlaymode;

        static void ValidateSoloControls(DigiPhantController controller)
        {
            var locomotion = controller.GetComponent<DigiPhantLocomotion>();
            Check(locomotion && locomotion.travelRoot, "Missing reference travel rig.");
            controller.SetPerformerCount(3);
            controller.SetUpperBodyOnly(false);
            controller.SetIndependentP3Ears(true);
            locomotion.enableLocomotion = true;
            int[] originalRoles = controller.controls.Select(control => control.performer).ToArray();
            controller.SetP3PracticeMode(true);
            Check(controller.p3PracticeMode && controller.performerCount == 1 && controller.upperBodyOnly
                && controller.P3InputSlot == 1, "P3 practice must use one upper-body input slot.");
            Check(!locomotion.enableLocomotion, "P3 practice must disable travel.");
            string[] active = controller.controls.Where(controller.IsControlActive).Select(control => control.label).OrderBy(label => label).ToArray();
            Check(active.SequenceEqual(new[] { "Head turn", "Left ear", "Right ear", "Trunk curl" }.OrderBy(label => label)),
                "P3 practice must activate only head, trunk and both ears.");
            controller.inputMode = InputMode.Camera; // synthetic input, no receiver
            controller.CaptureRestPose();
            foreach (var control in controller.controls) control.smoothing = 0;
            Accept(controller, Frame(controller, false), 100);
            Check(!controller.CalibrateAt(100) && controller.Status.Contains("LeftArmExtension"),
                "Independent ears calibration must identify a missing left-arm signal.");
            var frame = Frame(controller);
            frame.people[0].confidence[(int)Movement.LeftFootLift] = 0;
            frame.people[0].confidence[(int)Movement.RightFootLift] = 0;
            frame.people[0].confidence[(int)Movement.LeftHandHeight] = 0;
            frame.people[0].confidence[(int)Movement.ArmSpread] = 0;
            Accept(controller, frame, 101);
            Check(controller.CalibrateAt(101), "P3 practice must not need feet, travel hand or two-hand spread.");

            var head = Control(controller, "Head turn");
            var trunk = Control(controller, "Trunk curl");
            var leftEar = Control(controller, "Left ear");
            var rightEar = Control(controller, "Right ear");
            var inactive = controller.controls.Where(control => !controller.IsControlActive(control)).ToArray();
            var all = controller.controls.Select(control => control.bones[0]).ToArray();
            var rest = all.Select(bone => bone.localRotation).ToArray();
            Quaternion Base(BoneControl control) => rest[Array.IndexOf(controller.controls, control)];

            frame = Frame(controller);
            frame.people[0].values[(int)Movement.RightHandHeight] = .8f;
            frame.people[0].values[(int)Movement.ArmSpread] = 1;
            Accept(controller, frame, 101.1f);
            controller.ApplyControls(101.1f, 1);
            Check(Angle(trunk, Base(trunk)) > 5, "Right hand must drive trunk.");
            Check(Angle(leftEar, Base(leftEar)) < .01f && Angle(rightEar, Base(rightEar)) < .01f,
                "Right hand and legacy two-hand spread must not drive independent ears.");

            frame = Frame(controller);
            frame.people[0].leftArmExtension = .8f;
            Accept(controller, frame, 101.2f);
            controller.ApplyControls(101.2f, 1);
            Check(Angle(leftEar, Base(leftEar)) > 5 && Angle(rightEar, Base(rightEar)) > 5,
                "Left-arm extension must drive both ears.");
            Check(Angle(trunk, Base(trunk)) < .01f && Angle(head, Base(head)) < .01f,
                "Left-arm extension must not drive trunk or head.");

            float originalMinimum = controller.minimumConfidence;
            try
            {
                controller.minimumConfidence = 0;
                // The last packet has a nonzero extension: an absent optional channel
                // must not inherit it even at the confidence slider's zero endpoint.
                Accept(controller, Frame(controller, false), 101.25f);
                Check(!controller.IsMovementVisible(1, Movement.LeftArmExtension, 101.25f),
                    "Missing optional left arm must remain unavailable at confidence threshold zero.");
                controller.ApplyControls(101.25f, 1);
                Check(Angle(leftEar, Base(leftEar)) < .01f && Angle(rightEar, Base(rightEar)) < .01f,
                    "Missing optional left arm must not replay a previous ear signal at threshold zero.");
                Check(!controller.CalibrateAt(101.25f) && controller.Status.Contains("LeftArmExtension"),
                    "Missing optional left arm must prevent neutral calibration at threshold zero.");
            }
            finally { controller.minimumConfidence = originalMinimum; }
            Accept(controller, Frame(controller), 101.26f);
            Check(controller.CalibrateAt(101.26f), "Restoring the visible left arm must permit recalibration.");

            foreach (var control in new[] { head, trunk })
            {
                frame = Frame(controller);
                frame.people[0].values[(int)control.movement] = .75f;
                Accept(controller, frame, 101.3f);
                controller.ApplyControls(101.3f, 1);
                Quaternion positive = control.bones[0].localRotation;
                frame.people[0].values[(int)control.movement] = -.75f;
                Accept(controller, frame, 101.4f);
                controller.ApplyControls(101.4f, 1);
                Quaternion negative = control.bones[0].localRotation;
                float positiveAngle = Quaternion.Angle(Base(control), positive);
                float negativeAngle = Quaternion.Angle(Base(control), negative);
                Check(positiveAngle > 5 && Mathf.Abs(positiveAngle - negativeAngle) < .01f
                    && Quaternion.Angle(positive, negative) > positiveAngle * 1.9f,
                    control.label + " must support symmetric opposite directions.");
            }

            // Malicious or accidental full-body input cannot re-enable inactive P3 joints/travel.
            frame = Frame(controller);
            for (int movement = 0; movement < 6; movement++) frame.people[0].values[movement] = 1;
            frame.people[0].leftArmExtension = 1;
            Accept(controller, frame, 101.5f);
            locomotion.enableLocomotion = true;
            Vector3 origin = locomotion.travelRoot.position;
            controller.ApplyControls(101.5f, 1);
            Check(Vector3.Distance(origin, locomotion.travelRoot.position) < .001f && locomotion.CurrentSpeed == 0,
                "P3 practice must prevent travel even if its locomotion checkbox is externally enabled.");
            foreach (var control in inactive)
                Check(Angle(control, Base(control)) < .01f, "P3 practice moved inactive " + control.label + ".");

            frame = Frame(controller);
            frame.people[0].leftArmConfidence = 1.1f;
            Check(!controller.AcceptPacket(JsonUtility.ToJson(frame), 102), "Invalid left-arm confidence must be rejected.");
            frame = Frame(controller);
            frame.people[0].leftArmExtension = float.NaN;
            Check(!controller.AcceptPacket(JsonUtility.ToJson(frame), 102), "Non-finite left-arm extension must be rejected.");

            controller.SetP3PracticeMode(false);
            Check(!controller.p3PracticeMode && controller.performerCount == 3 && !controller.upperBodyOnly
                && controller.P3InputSlot == 3 && locomotion.enableLocomotion,
                "Exiting practice must restore team count, body mode, travel setting and P3 slot.");
            Check(controller.controls.Select(control => control.performer).SequenceEqual(originalRoles),
                "Exiting practice must restore the original team body-role mappings.");
            controller.SetP3PracticeMode(true);
        }

        static void ValidateCupPacketGuards(DigiPhantController controller)
        {
            var frame = Frame(controller);
            frame.people[0].cup = Cup();
            Accept(controller, frame, 200);
            Check(controller.CalibrateAt(200), "Cup freshness test baseline calibration failed.");
            Check(controller.TryReadCup(1, 200, out CupObservation detected) && detected.detected && detected.label == "cup",
                "A fresh recognized cup must be readable.");
            Check(!controller.TryReadCup(1, 200 + controller.trackingTimeout + 1, out _),
                "A stale pose packet must not retain cup recognition.");
            controller.ReassignPeople();
            frame.people[0].cup.ageSeconds = 10;
            controller.AcceptPacket(JsonUtility.ToJson(frame), 201);
            Check(!controller.TryReadCup(1, 201, out _), "A stale detector observation must not be usable.");
            controller.ReassignPeople();
            frame = Frame(controller);
            frame.people[0].cup = Cup();
            frame.people[0].cup.score = 1.1f;
            Accept(controller, frame, 202);
            Check(!controller.TryReadCup(1, 202, out _), "A cup score outside 0..1 must be rejected without blocking pose controls.");
            frame.people[0].cup = Cup();
            frame.people[0].cup.ageSeconds = -1;
            Accept(controller, frame, 202.1f);
            Check(!controller.TryReadCup(1, 202.1f, out _), "Negative cup age must be rejected without blocking pose controls.");
            frame.people[0].cup = Cup();
            frame.people[0].cup.width = -1;
            Accept(controller, frame, 202.2f);
            Check(!controller.TryReadCup(1, 202.2f, out _), "Negative cup dimensions must be rejected without blocking pose controls.");
            frame = Frame(controller);
            Accept(controller, frame, 203);
            Check(!controller.TryReadCup(1, 203, out _), "Omitted cup observations must clear prior cup recognition.");
            frame.people[0].cup = Cup();
            frame.people[0].cup.x = .95f;
            frame.people[0].cup.y = .95f;
            frame.people[0].cup.width = .1f;
            frame.people[0].cup.height = .1f;
            Accept(controller, frame, 203.05f);
            Check(controller.TryReadCup(1, 203.05f, out _), "Clipped frame-edge cup coordinates must use box centers, not top-left positions.");
            frame.people[0].cup.x = .98f;
            Accept(controller, frame, 203.06f);
            Check(!controller.TryReadCup(1, 203.06f, out _), "A cup box extending outside the image must be discarded independently.");
            frame.people[0].cup = Cup();
            frame.people[0].cup.detected = false;
            Accept(controller, frame, 203.1f);
            Check(!controller.TryReadCup(1, 203.1f, out _), "A detector miss must not count as a recognized cup.");
        }

        static void ValidateClosedFistGestureState()
        {
            var state = new CupGestureState();
            CupGestureSample fist = new CupGestureSample {
                dt = .1f, contextReady = true, poseReady = true, rightHand = 0,
                fistAvailable = true, fistClosed = true, fistConfidence = .9f
            };
            Check(!state.Step(fist) && state.Phase == CupGesturePhase.Neutral,
                "A closed fist must not start until an open-hand rearm sample is observed.");
            fist.fistClosed = false;
            Check(!state.Step(fist) && state.Phase == CupGesturePhase.Neutral,
                "An open hand should arm the next deliberate fist interaction.");
            fist.fistClosed = true;
            state.Step(fist);
            Check(state.Phase == CupGesturePhase.FistHold && state.CompletedCount == 0,
                "A recognized fist must arm the water interaction hold.");
            for (int i = 0; i < 6; i++) state.Step(fist);
            Check(state.Phase == CupGesturePhase.Complete && state.CompletedCount == 1,
                "A continuous confident closed-fist hold must complete exactly once.");
            fist.fistClosed = false;
            state.Step(fist);
            Check(state.CompletedCount == 1 && state.Phase == CupGesturePhase.Neutral,
                "Opening the hand must rearm without duplicating a completed interaction.");
            fist.fistClosed = true;
            for (int i = 0; i < 6; i++) state.Step(fist);
            Check(state.CompletedCount == 2, "A second explicitly rearmed fist hold must count once.");

            state.Reset();
            fist.fistClosed = false;
            state.Step(fist);
            fist.fistClosed = true;
            state.Step(fist);
            var briefLoss = fist;
            briefLoss.fistAvailable = false;
            Check(!state.Step(briefLoss) && state.Phase == CupGesturePhase.FistHold,
                "A brief missing fist sample should preserve an in-progress hold.");
            for (int i = 0; i < 6; i++) state.Step(fist);
            Check(state.CompletedCount == 1,
                "A fist hold should complete after recovering from a brief signal loss.");
        }

        static void ValidateCupGestureState()
        {
            var state = new CupGestureState();
            CupGestureSample Sample(float hand) => new CupGestureSample {
                dt = .1f, contextReady = true, poseReady = true,
                cupDetected = true, cupLabel = "cup", cupScore = .9f,
                cupAgeSeconds = .1f, rightHand = hand
            };
            void Hold(float hand, int samples)
            {
                for (int sample = 0; sample < samples; sample++) state.Step(Sample(hand));
            }
            void ToRaise()
            {
                Hold(0, 3);
                Hold(-.4f, 5);
            }

            Hold(.5f, 10);
            Check(state.Phase == CupGesturePhase.Neutral && state.CompletedCount == 0,
                "Raising a cup alone must not complete a drinking gesture.");
            Hold(0, 3);
            Check(state.Phase == CupGesturePhase.Lower, "Neutral hold should arm the low-cup phase.");
            Hold(-.4f, 4);
            Check(state.Phase == CupGesturePhase.Lower && state.CompletedCount == 0,
                "A low pose shorter than the minimum hold must not advance.");
            Hold(0, 1);
            Hold(-.4f, 1);
            Check(state.Phase == CupGesturePhase.Lower,
                "Low hold time must reset when the hand leaves the low pose.");
            Hold(-.4f, 4);
            Check(state.Phase == CupGesturePhase.Raise, "Continuous low hold should advance to raising.");
            Hold(.5f, 5);
            Check(state.Phase == CupGesturePhase.ReturnNeutral && state.CompletedCount == 0,
                "High cup hold must still require return to neutral.");
            Hold(0, 3);
            Check(state.Phase == CupGesturePhase.Complete && state.CompletedCount == 1,
                "Recognized neutral-low-high-neutral sequence must complete exactly once.");
            Hold(.5f, 20);
            Hold(0, 20);
            Check(state.CompletedCount == 1, "Holding or moving after completion must not duplicate results.");
            var cupLost = Sample(0);
            cupLost.cupDetected = false;
            state.Step(cupLost);
            Check(state.CompletedCount == 1 && state.Phase == CupGesturePhase.Neutral,
                "Tracking loss must reset incomplete evidence without erasing completed gestures.");
            state.CancelSequence();
            Check(state.Phase == CupGesturePhase.Neutral && state.CompletedCount == 1,
                "Try another gesture must preserve the completed count.");
            ToRaise();
            Hold(.5f, 5);
            Hold(0, 3);
            Check(state.CompletedCount == 2, "A second explicitly rearmed full gesture must count once.");

            foreach (string reason in new[] { "cup absent", "stale cup", "wrong label", "low score", "pose lost", "moving context", "sample gap", "invalid dt", "invalid score", "negative age", "invalid hand" })
            {
                state.Reset();
                ToRaise();
                Check(state.Phase == CupGesturePhase.Raise, "Invalid-context test did not reach its setup phase.");
                var bad = Sample(.5f);
                switch (reason)
                {
                    case "cup absent": bad.cupDetected = false; break;
                    case "stale cup": bad.cupAgeSeconds = 2; break;
                    case "wrong label": bad.cupLabel = "bottle"; break;
                    case "low score": bad.cupScore = .1f; break;
                    case "pose lost": bad.poseReady = false; break;
                    case "moving context": bad.contextReady = false; break;
                    case "sample gap": bad.dt = .5f; break;
                    case "invalid dt": bad.dt = float.NaN; break;
                    case "invalid score": bad.cupScore = float.NaN; break;
                    case "negative age": bad.cupAgeSeconds = -1; break;
                    case "invalid hand": bad.rightHand = float.NaN; break;
                }
                Check(!state.Step(bad) && state.Phase == CupGesturePhase.Neutral && state.CompletedCount == 0,
                    reason + " must discard an incomplete gesture.");
                Hold(.5f, 8);
                Check(state.CompletedCount == 0, "A raised hand after " + reason + " must not complete the old sequence.");
            }
            state.Reset();
            ToRaise();
            Hold(.2f, 110);
            Check(state.Phase == CupGesturePhase.Neutral && state.CompletedCount == 0,
                "A timed-out transition must discard incomplete evidence.");
            state.Reset();
            Check(state.Phase == CupGesturePhase.Neutral && state.CompletedCount == 0,
                "Session reset must clear the previous completed count.");
        }

        static void ValidateSavedSceneConfiguration()
        {
            var team = EditorSceneManager.OpenScene(TeamSceneSetup.TeamScene, OpenSceneMode.Single);
            var teamController = Controller(team);
            Check(teamController.independentP3Ears && !teamController.p3PracticeMode && teamController.performerCount == 3,
                "Saved team scene must retain three roles and enable independent P3 ears.");
            var interactions = team.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<WaterCupInteraction>(true)).ToArray();
            Check(interactions.Length == 1, "Saved team scene must contain one water interaction component.");
            Check(File.Exists(P3InteractionSceneSetup.SoloScene), "P3 solo scene is missing.");
            var solo = EditorSceneManager.OpenScene(P3InteractionSceneSetup.SoloScene, OpenSceneMode.Single);
            var soloController = Controller(solo);
            Check(soloController.p3PracticeMode && soloController.independentP3Ears && soloController.performerCount == 1
                && soloController.upperBodyOnly && soloController.inputMode == InputMode.Camera,
                "Saved solo scene must select P3-only upper-body camera practice.");
            Check(!soloController.GetComponent<DigiPhantLocomotion>().enableLocomotion,
                "Saved solo scene must disable travel.");
        }

        [Serializable]
        sealed class WaterLogSnapshot
        {
            public float durationSeconds;
            public WaterCupInteraction.GestureEvent[] events;
        }
        static void ValidateGestureLogLifecycle()
        {
            double ticks = 100;
            var clock = new GestureSessionClock(() => ticks);
            clock.Start(); ticks = 112.5;
            Check(Math.Abs(clock.ElapsedSeconds - 12.5) < .0001, "Gesture clock must use its independent monotonic source.");
            clock.Pause(); ticks = 1000;
            Check(Math.Abs(clock.ElapsedSeconds - 12.5) < .0001, "Disabled component intervals must not change a frozen session duration.");
            clock.Resume(); ticks = 1002;
            Check(Math.Abs(clock.ElapsedSeconds - 14.5) < .0001, "Resumed components continue the same active-time duration without counting the disabled interval.");

            string folder = Path.Combine(Path.GetTempPath(), "DigiPhant-water-lifecycle-" + Guid.NewGuid().ToString("N"));
            var go = new GameObject("Synthetic water lifecycle validation");
            var water = go.AddComponent<WaterCupInteraction>();
            try
            {
                ticks = 500;
                PrivateCall(water, "StartSignalSession", folder, new GestureSessionClock(() => ticks));
                ticks = 512.5;
                PrivateField(water, "handAvailable", true);
                PrivateField(water, "cupAvailable", true);
                PrivateField(water, "rightHand", 1.5f);
                PrivateField(water, "currentCup", Cup());
                PrivateCall(water, "ResetForConfigurationChange", 7, 10, "People reassigned");
                water.SaveResults();
                var snapshot = JsonUtility.FromJson<WaterLogSnapshot>(File.ReadAllText(Path.Combine(folder, "results.json")));
                var reset = snapshot.events.Last();
                Check(snapshot.durationSeconds == 12.5f && reset.relativeSeconds == 12.5f,
                    "Event times and session duration must share the monotonic clock, including after a configuration reset.");
                Check(reset.eventName == "configuration_reset" && reset.calibrationResetReason == "People reassigned"
                    && reset.calibrationRevision == 7 && !reset.handAvailable && !reset.cupDetected && reset.rightHandNeutralDelta == 0 && reset.cupScore == 0,
                    "Configuration reset logs must retain the explicit reason and clear all stale hand/cup observations first.");
                ticks = 520.25;
                PrivateCall(water, "OnDisable");
                string closedJson = File.ReadAllText(Path.Combine(folder, "results.json"));
                int csvRows = File.ReadAllLines(Path.Combine(folder, "events.csv")).Length - 1;
                snapshot = JsonUtility.FromJson<WaterLogSnapshot>(closedJson);
                Check(snapshot.durationSeconds == 20.25f && snapshot.events.Last().relativeSeconds == 20.25f && csvRows == snapshot.events.Length,
                    "Play shutdown must retain a positive duration and matching JSON/CSV terminal events.");
                // Stop callbacks and course cleanup can happen after the Unity clock has reset.
                ticks = 0;
                water.TryAnotherGesture();
                water.ClearCourseGate();
                PrivateCall(water, "OnApplicationQuit");
                water.SaveResults();
                Check(File.ReadAllText(Path.Combine(folder, "results.json")) == closedJson
                    && File.ReadAllLines(Path.Combine(folder, "events.csv")).Length - 1 == csvRows,
                    "Cleanup after the CSV closes must not add JSON-only events, change the frozen outcome, or zero the duration.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }
        static object PrivateCall(object target, string name, params object[] args)
            => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
        static void PrivateField(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        static PoseFrame Frame(DigiPhantController controller, bool leftArm = true) => new PoseFrame {
            version = 1, performerCount = controller.performerCount, upperBodyOnly = controller.upperBodyOnly,
            people = Enumerable.Range(1, controller.performerCount).Select(slot => new PerformerFrame {
                slot = slot, values = new float[6], confidence = Enumerable.Repeat(1f, 6).ToArray(),
                hasLeftArm = leftArm, leftArmExtension = 0, leftArmConfidence = leftArm ? 1 : 0
            }).ToArray()
        };

        static CupObservation Cup() => new CupObservation {
            detected = true, label = "cup", score = .9f, x = .4f, y = .4f,
            width = .1f, height = .2f, ageSeconds = 0
        };

        static BoneControl Control(DigiPhantController controller, string label)
        {
            var control = controller.controls.Single(value => value.label == label);
            Check(control.bones != null && control.bones.Length > 0 && control.bones[0], "Missing test rig for " + label + ".");
            return control;
        }

        static float Angle(BoneControl control, Quaternion baseline) => Quaternion.Angle(baseline, control.bones[0].localRotation);

        static DigiPhantController Controller(Scene scene)
        {
            var controllers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<DigiPhantController>(true)).ToArray();
            Check(controllers.Length == 1, "Expected exactly one controller in " + scene.path + ".");
            return controllers[0];
        }

        static void Accept(DigiPhantController controller, PoseFrame frame, float now)
            => Check(controller.AcceptPacket(JsonUtility.ToJson(frame), now), "Valid P3 synthetic packet rejected.");

        static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
