using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DigiPhant.Editor
{
    public static class DigiPhantLocomotionValidation
    {
        public static void RunAll() { DigiPhantSetup.Validate(); Run(); }
        public static void Run()
        {
            EditorSceneManager.OpenScene(DigiPhantSetup.ScenePath);
            var c = UnityEngine.Object.FindAnyObjectByType<DigiPhantController>();
            var loco = c.GetComponent<DigiPhantLocomotion>();
            void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
            void Step(int count, float start = 0) { for (int i = 0; i < count; i++) c.ApplyControls(start + i / 60f, 1 / 60f); }
            Check(loco && loco.Initialise(), "Missing locomotion setup");
            c.inputMode = InputMode.TestSliders;
            c.CaptureRestPose();
            loco.inputSmoothing = 0;
            loco.animationBlendSeconds = .01f;
            Vector3 origin = loco.travelRoot.position;
            Quaternion initialRotation = loco.travelRoot.rotation;
            Vector3 cameraOrigin = loco.followCamera.transform.position;
            Vector3 modelLocalPosition = loco.elephantAnimator.transform.localPosition;
            var leg = c.controls[0].bones[0];
            loco.testForward = .4f;
            Step(15);
            Quaternion firstLeg = leg.localRotation;
            Step(30, .25f);
            Check(Quaternion.Angle(firstLeg, leg.localRotation) > 3, "Walk clip did not animate the leg");
            Step(15, .75f);
            float walkDistance = Vector3.Distance(origin, loco.travelRoot.position);
            Check(walkDistance > .9f && walkDistance < 1.3f && loco.CurrentAction == "Walk", "Walk world speed incorrect");
            Check(Vector3.Distance(loco.elephantAnimator.transform.localPosition, modelLocalPosition) < .01f, "Clip moved the model outside the travel parent");
            Check(Vector3.Distance(loco.followCamera.transform.position - cameraOrigin, loco.travelRoot.position - origin) < .001f, "Follow camera lost offset");
            loco.ResetPosition();
            loco.testForward = 1;
            Step(60);
            Check(Vector3.Distance(origin, loco.travelRoot.position) > walkDistance * 2 && loco.CurrentAction == "Run", "Run failed");
            loco.ResetPosition();
            loco.testForward = -.5f;
            Step(60);
            Check(Vector3.Dot(loco.travelRoot.position - origin, initialRotation * Vector3.forward) < -.5f && loco.CurrentAction == "Backward", "Backward failed");
            loco.ResetPosition();
            loco.testSteering = .75f;
            Step(60);
            Check(Vector3.Distance(origin, loco.travelRoot.position) < .001f, "Stationary turn translated the elephant");
            Check(Quaternion.Angle(initialRotation, loco.travelRoot.rotation) > 40 && loco.CurrentAction == "Turn right", "Turn right failed");
            loco.testSteering = -.75f;
            Step(60);
            Check(Quaternion.Angle(initialRotation, loco.travelRoot.rotation) < .1f && loco.CurrentAction == "Turn left", "Turn left failed");
            loco.ResetPosition();
            loco.testForward = .4f;
            loco.testSteering = -.5f;
            Step(60);
            Check(loco.CurrentAction == "Walk left" && Vector3.Distance(origin, loco.travelRoot.position) > .5f, "Walking and steering failed");

            var head = c.controls.Single(x => x.label == "Head turn");
            var trunk = c.controls.Single(x => x.label == "Trunk curl");
            head.smoothing = trunk.smoothing = 0;
            c.ApplyControls(10, 0);
            var baseHead = head.bones[0].localRotation;
            var baseTrunk = trunk.bones[0].localRotation;
            head.testValue = trunk.testValue = 1;
            c.ApplyControls(10, 0);
            var expectedHead = baseHead * Quaternion.AngleAxis(head.degrees, head.localAxis.normalized);
            var expectedTrunk = baseTrunk * Quaternion.AngleAxis(trunk.degrees, trunk.localAxis.normalized);
            Check(Quaternion.Angle(expectedHead, head.bones[0].localRotation) < .1f, "Head gesture overwrote or lost animation pose");
            Check(Quaternion.Angle(expectedTrunk, trunk.bones[0].localRotation) < .1f, "Trunk gesture lost during locomotion");
            for (int i = 0; i < 100; i++) c.ApplyControls(10, 0);
            Check(Quaternion.Angle(expectedHead, head.bones[0].localRotation) < .1f, "Gesture offsets accumulated");
            head.locomotionWeight = 0;
            c.ApplyControls(10, 0);
            Check(Quaternion.Angle(baseHead, head.bones[0].localRotation) < .1f, "Zero overlay weight failed");
            loco.ResetPosition();
            loco.stageRadius = .2f;
            loco.testForward = 1;
            Step(120);
            Check(Vector3.Distance(origin, loco.travelRoot.position) <= .201f, "Stage limit failed");
            loco.ResetPosition();
            loco.stageRadius = 15;

            c.SetPerformerCount(2);
            c.inputMode = InputMode.Camera; // No receiver needed for deterministic packet tests.
            loco.forward.sources = new[] { new MovementSource { performer = 1, movement = Movement.LeftHandHeight },
                new MovementSource { performer = 2, movement = Movement.LeftHandHeight } };
            loco.forward.deadZone = loco.steering.deadZone = 0;
            PoseFrame Frame(float first, float second) => new PoseFrame { version = 1, performerCount = 2,
                people = new[] { new PerformerFrame { slot = 1, values = new[] { first, 0f, 0f, 0f, 0f, 0f }, confidence = Enumerable.Repeat(1f, 6).ToArray() },
                    new PerformerFrame { slot = 2, values = new[] { second, 0f, 0f, 0f, 0f, 0f }, confidence = Enumerable.Repeat(1f, 6).ToArray() } } };
            var emptyDriver = new MovementDriver();
            Check(emptyDriver.TryRead(c, 100, out float emptyValue) && emptyValue == 0, "Empty mapping must leave a channel neutral");
            c.AcceptPacket(JsonUtility.ToJson(Frame(0, 0)), 100);
            Check(c.CalibrateAt(100), "Compound mapping calibration failed");
            c.AcceptPacket(JsonUtility.ToJson(Frame(.2f, .8f)), 100);
            c.ApplyControls(100, 1 / 60f);
            Check(Mathf.Abs(loco.CurrentSpeed - .5f / loco.runThreshold * loco.walkSpeed) < .01f, "Average of two performers is incorrect");
            loco.forward.combination = SignalCombination.Sum;
            c.ApplyControls(100, 1 / 60f);
            Check(Mathf.Abs(loco.CurrentSpeed - loco.runSpeed) < .01f, "Sum of two performers is incorrect");
            var missing = Frame(.2f, 0);
            missing.people = missing.people.Take(1).ToArray();
            c.AcceptPacket(JsonUtility.ToJson(missing), 100.1f);
            Vector3 beforeLoss = loco.travelRoot.position;
            c.ApplyControls(100.1f, 1 / 60f);
            Check(loco.CurrentSpeed == 0 && Vector3.Distance(beforeLoss, loco.travelRoot.position) < .001f, "Tracking loss did not stop travel immediately");
            c.ApplyControls(102, 1 / 60f);
            Check(loco.CurrentSpeed == 0, "Stale tracking restarted travel");
            c.AcceptPacket(JsonUtility.ToJson(Frame(0, 0)), 103);
            Check(c.CalibrateAt(103), "Recalibration setup failed");
            c.inputMode = InputMode.TestSliders;
            c.SendMessage("OnEnable");
            Check(!c.IsCalibrated && loco.CurrentSpeed == 0, "Re-entering Play retained stale movement/calibration");
            loco.ResetPosition();
            Check(Vector3.Distance(origin, loco.travelRoot.position) < .001f && !c.IsCalibrated, "Reset position/calibration failed");
            loco.enableLocomotion = false;
            c.ApplyControls(103, 1 / 60f);
            Check(!loco.AnimationActive && !loco.elephantAnimator.enabled, "Locomotion shutdown failed");
            EditorSceneManager.OpenScene(DigiPhantSetup.ScenePath);
            Debug.Log("DIGIPHANT_LOCOMOTION_VALIDATION_OK: gaits, world travel, turns, additive gestures, no accumulation, compound signals, tracking loss, limits, reset");
        }
    }
}
