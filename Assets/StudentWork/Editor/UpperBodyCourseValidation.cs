using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using DigiPhant;
using StudentStarter.Savannah;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StudentWork.Editor
{
    // Synthetic Edit-mode validation only. No webcam, receiver, Play mode or recorded images.
    public static class UpperBodyCourseValidation
    {
        static int assertions;

        [MenuItem("Student Work/Validate Upper Body Course Without Camera")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Stop Play mode and exit Prefab Mode before upper-body validation.");
            var current = SceneManager.GetActiveScene();
            if (SceneManager.sceneCount != 1 || !current.IsValid() || current.isDirty || string.IsNullOrEmpty(current.path) || !File.Exists(current.path))
                throw new InvalidOperationException("Open one saved, unmodified scene before validation. Synthetic test state is never saved.");
            string restore = current.path;
            assertions = 0;
            try
            {
                ValidateSavedScenes();
                var scene = EditorSceneManager.OpenScene(UpperBodyCourseSceneSetup.CourseScene, OpenSceneMode.Single);
                ValidateTeamControls(UpperBodyCourseSceneSetup.Controller(scene));
                ValidateSoloRoles();
                ValidateCourseReadiness();
                ValidateDuoTeam();
                scene = EditorSceneManager.OpenScene(UpperBodyCourseSceneSetup.CourseScene, OpenSceneMode.Single);
                ValidateCourseGeometry(UpperBodyCourseSceneSetup.Components<ThreePersonCourseManager>(scene).Single());
            }
            finally
            {
                var restored = EditorSceneManager.OpenScene(restore, OpenSceneMode.Single);
                Selection.activeGameObject = UpperBodyCourseSceneSetup.Controller(restored).gameObject;
                ActiveEditorTracker.sharedTracker.ForceRebuild();
                SceneView.RepaintAll();
            }
            Debug.Log("UPPER_BODY_COURSE_VALIDATION_OK: " + assertions
                + " assertions; preserved original course geometry/order, upper-body role isolation, head yaw/trunk tip direction, four-leg speed-linked gaits, stop/backward/run, solo roles, two-person P1+P2 team, ordered triggers, failure guards and persisted signal results; synthetic only, no camera or Play.");
        }

        public static void RunBatch()
        {
            if (Application.isBatchMode) EditorSceneManager.OpenScene(UpperBodyCourseSceneSetup.CourseScene, OpenSceneMode.Single);
            Run();
        }

        [MenuItem("Student Work/Validate Upper Body Course Without Camera", true)]
        static bool CanValidate() => !EditorApplication.isPlayingOrWillChangePlaymode;

        static void ValidateSavedScenes()
        {
            var original = EditorSceneManager.OpenScene(TeamSceneSetup.TeamScene, OpenSceneMode.Single);
            string geometry = CourseFingerprint(original);
            Check(!UpperBodyCourseSceneSetup.Controller(original).upperBodyTeamMode, "The earlier TeamPerformance scene was unexpectedly changed to the new preset.");
            var scene = EditorSceneManager.OpenScene(UpperBodyCourseSceneSetup.CourseScene, OpenSceneMode.Single);
            var controller = UpperBodyCourseSceneSetup.Controller(scene);
            var locomotion = controller.GetComponent<DigiPhantLocomotion>();
            Check(CourseFingerprint(scene) == geometry, "The original Savannah geometry, landmarks or route were changed.");
            Check(controller.upperBodyTeamMode && controller.SoloRole == 0 && controller.performerCount == 3 && controller.upperBodyOnly
                && controller.inputMode == InputMode.Camera, "Saved course must select three camera-driven upper-body roles.");
            Check(locomotion.enableLocomotion && (locomotion.stageRadius == 0 || locomotion.stageRadius >= 45), "The course must remain reachable.");
            var manager = UpperBodyCourseSceneSetup.Components<ThreePersonCourseManager>(scene).Single();
            Check(manager.treeInteraction && manager.logInteraction && manager.waterInteraction && manager.waterZone && manager.finishZone,
                "The saved course manager has incomplete interaction references.");
            Check(manager.treeInteraction.detectionZone.isTrigger && manager.logInteraction.detectionZone.isTrigger
                && manager.waterZone.isTrigger && manager.finishZone.isTrigger, "Detection zones must use triggers.");
            var markerRoot = scene.GetRootGameObjects().Single(value => value.name == UpperBodyCourseSceneSetup.InteractionRoot);
            var markers = markerRoot.GetComponentsInChildren<TextMesh>();
            Check(markers.Length == 4, "All four interaction markers must exist.");
            foreach (var marker in markers)
                Check(marker.font && marker.GetComponent<MeshRenderer>().sharedMaterial
                    && marker.GetComponent<MeshRenderer>().sharedMaterial.shader.name != "Hidden/InternalErrorShader",
                    "Interaction marker font/material is missing or uses the error shader.");
            var course = scene.GetRootGameObjects().Single(value => value.name == SavannahCourseGenerator.RootName).transform;
            Check(!manager.transform.IsChildOf(course), "Interaction scripts must be outside the supplied course geometry hierarchy.");
            Check(Vector3.Distance(manager.waterZone.transform.position - Vector3.up * 1.5f,
                course.Find("Landmarks (4, decorative)/02 Water basin").position) < .01f,
                "Water detection must follow the original basin position.");
            Check(Vector3.Distance(manager.logInteraction.detectionZone.transform.position - Vector3.up * 1.5f,
                course.Find("Landmarks (4, decorative)/03 Fallen log").position) < .01f,
                "Log detection must follow the original log position.");
            Check(locomotion.forward.sources.Length == 1 && locomotion.forward.sources[0].performer == 1
                && locomotion.forward.sources[0].movement == Movement.LeftHandHeight,
                "Only P1's left hand may drive travel speed.");
            Check(locomotion.steering.sources.Length == 1 && locomotion.steering.sources[0].performer == 2
                && locomotion.steering.sources[0].movement == Movement.Lean && locomotion.steering.sources[0].weight < 0,
                "P2's shoulder lean must turn the elephant the way P2 leans (not mirrored).");
            foreach (var leg in controller.controls.Where(value => value.label.Contains("leg")))
                Check(leg.performer == 1 && leg.movement == Movement.LeftHandHeight && leg.locomotionWeight == 0
                    && !controller.IsControlActive(leg), "All four legs must follow speed-linked gait without a foot gesture overlay.");
            Check(Control(controller, "Head turn").movement == Movement.HeadYaw, "P3 head rotation must use actual head yaw.");
            for (int role = 1; role <= 3; role++)
            {
                var solo = EditorSceneManager.OpenScene(SoloPath(role), OpenSceneMode.Single);
                var c = UpperBodyCourseSceneSetup.Controller(solo);
                Check(c.upperBodyTeamMode && c.SoloRole == role && c.performerCount == 1 && c.upperBodyOnly
                    && c.inputMode == InputMode.Camera, "P" + role + " saved solo role is incorrect.");
                Check(CourseFingerprint(solo) == geometry, "A solo copy changed the original Savannah geometry.");
                Check(UpperBodyCourseSceneSetup.Components<ThreePersonCourseManager>(solo).Single().enabled, "Practice scenes retain the course manager so Team can restart a full course; runtime solo guards pause it.");
                Check(c.GetComponent<DigiPhantLocomotion>().enableLocomotion == (role != 3), "Solo travel availability is incorrect.");
            }
        }

        static void ValidateTeamControls(DigiPhantController controller)
        {
            var loco = controller.GetComponent<DigiPhantLocomotion>();
            controller.inputMode = InputMode.Camera; // No receiver: direct deterministic packet delivery.
            loco.inputSmoothing = 0;
            loco.animationBlendSeconds = .01f;
            controller.CaptureRestPose();
            foreach (var control in controller.controls) control.smoothing = 0;
            var frame = Frame(controller);
            Accept(controller, frame, 100);
            Check(controller.CalibrateAt(100), "Upper-body neutral must calibrate while both foot channels have zero confidence.");
            for (int slot = 1; slot <= 3; slot++) Check(controller.IsPerformerInputReady(slot, 100, out _), "Upper-body role readiness failed for P" + slot);
            frame.people[0].confidence[(int)Movement.RightHandHeight] = 0;
            for (int channel = 0; channel < 6; channel++) frame.people[1].confidence[channel] = channel == (int)Movement.Lean ? 1 : 0;
            Accept(controller, frame, 100.01f);
            Check(controller.IsPerformerInputReady(1, 100.01f, out _) && controller.IsPerformerInputReady(2, 100.01f, out _)
                && controller.CalibrateAt(100.01f), "P1 must need only the left hand; P2 only shoulder lean; neither role may require the other wrist or feet.");
            var noHead = Frame(controller);
            noHead.people[2].hasHeadYaw = false;
            Accept(controller, noHead, 100.1f);
            Check(!controller.CalibrateAt(100.1f) && controller.Status.Contains("HeadYaw"), "Missing P3 head yaw must produce a useful calibration diagnostic.");
            float minimumConfidence = controller.minimumConfidence;
            controller.minimumConfidence = 0;
            Check(!controller.IsMovementVisible(3, Movement.HeadYaw, 100.1f) && !controller.CalibrateAt(100.1f),
                "Missing optional head yaw must remain unavailable at confidence threshold zero.");
            controller.minimumConfidence = minimumConfidence;
            var invalidHead = Frame(controller);
            invalidHead.people[2].headYawConfidence = 1.1f;
            Check(!controller.AcceptPacket(JsonUtility.ToJson(invalidHead), 100.2f), "Head-yaw confidence outside 0..1 must be rejected.");
            invalidHead = Frame(controller);
            invalidHead.people[2].headYaw = float.NaN;
            Check(!controller.AcceptPacket(JsonUtility.ToJson(invalidHead), 100.3f), "Non-finite optional head yaw must be rejected.");
            Accept(controller, Frame(controller), 101);
            Check(controller.CalibrateAt(101), "Head-yaw restoration must permit calibration.");
            Vector3 origin = loco.travelRoot.position;
            Quaternion originRotation = loco.travelRoot.rotation;
            var legs = controller.controls.Where(value => value.label.Contains("leg")).Select(value => value.bones[0]).ToArray();
            float now = 102;
            void Step(float hand, float lean, int count)
            {
                for (int sample = 0; sample < count; sample++)
                {
                    frame = Frame(controller);
                    frame.people[0].values[(int)Movement.LeftHandHeight] = hand;
                    frame.people[1].values[(int)Movement.Lean] = lean;
                    now += 1 / 60f;
                    Accept(controller, frame, now);
                    controller.ApplyControls(now, 1 / 60f);
                }
            }
            Step(.3f, 0, 15);
            Check(loco.CurrentSpeed > 0 && loco.CurrentAction == "Walk", "Raising P1's left hand must start a walk.");
            float lowSpeed = loco.CurrentSpeed;
            Quaternion[] legBefore = legs.Select(value => value.localRotation).ToArray();
            Step(.3f, 0, 20);
            for (int leg = 0; leg < legs.Length; leg++)
                Check(Quaternion.Angle(legBefore[leg], legs[leg].localRotation) > .2f, "P1 speed did not animate leg " + leg);
            Step(.55f, 0, 10);
            Check(loco.CurrentSpeed > lowSpeed && loco.CurrentAction == "Walk", "Raising the left hand further must increase walking speed.");
            Step(1.8f, 0, 10);
            Check(loco.CurrentSpeed > loco.walkSpeed && loco.CurrentAction == "Run", "P1's high left hand must reach the run threshold.");
            loco.inputSmoothing = 6;
            float runSpeed = loco.CurrentSpeed;
            Step(0, 0, 1);
            Check(loco.CurrentSpeed > 0 && loco.CurrentSpeed < runSpeed, "Returning P1's left hand to neutral must decelerate gradually.");
            Step(0, 0, 120);
            Check(loco.CurrentSpeed == 0 && loco.CurrentAction == "Idle", "Holding P1 neutral must stop the elephant.");
            loco.inputSmoothing = 0;
            Vector3 beforeBackward = loco.travelRoot.position;
            Step(-.95f, 0, 45);
            Check(loco.CurrentSpeed < 0 && loco.CurrentAction == "Backward"
                && Vector3.Dot(loco.travelRoot.position - beforeBackward, originRotation * Vector3.forward) < -.1f,
                "Lowering P1's left hand below neutral must reverse.");
            Step(0, 0, 1);
            Check(loco.CurrentSpeed == 0, "Returning to neutral must stop backward movement.");
            Vector3 beforeTurn = loco.travelRoot.position;
            Quaternion beforeRotation = loco.travelRoot.rotation;
            Step(0, -.7f, 30);
            Check(Vector3.Distance(beforeTurn, loco.travelRoot.position) < .001f && loco.CurrentAction == "Turn right"
                && SignedYaw(beforeRotation, loco.travelRoot.rotation) > 10,
                "P2's own-right shoulder tilt (negative Lean) must turn the elephant right.");
            Step(0, .7f, 60);
            Check(loco.CurrentAction == "Turn left" && SignedYaw(beforeRotation, loco.travelRoot.rotation) < -10,
                "P2's own-left shoulder tilt (positive Lean) must turn the elephant left.");
            Step(.4f, -.5f, 30);
            Check(loco.CurrentAction == "Walk left" && loco.CurrentSpeed > 0, "P1 walking and P2 steering must combine.");
            var lost = Frame(controller);
            lost.people = lost.people.Take(2).ToArray();
            Accept(controller, lost, now + .01f);
            Vector3 beforeLoss = loco.travelRoot.position;
            controller.ApplyControls(now + .01f, 1 / 60f);
            Check(loco.CurrentSpeed == 0 && Vector3.Distance(beforeLoss, loco.travelRoot.position) < .001f,
                "Loss of any active team performer must stop motion immediately.");

            // Turn off the gait temporarily to isolate the model's head/trunk geometry.
            loco.enableLocomotion = false;
            loco.StopAnimation();
            controller.CaptureRestPose();
            Accept(controller, Frame(controller), 200);
            Check(controller.CalibrateAt(200), "Isolated head/trunk calibration failed.");
            controller.ApplyControls(200, .1f);
            var head = Control(controller, "Head turn");
            var trunk = Control(controller, "Trunk curl");
            var leftEar = Control(controller, "Left ear");
            Quaternion headRest = head.bones[0].localRotation, trunkRest = trunk.bones[0].localRotation, earRest = leftEar.bones[0].localRotation;
            var tip = trunk.bones[trunk.bones.Length - 1];
            Vector3 tipRest = tip.position;
            frame = Frame(controller);
            frame.people[2].headYaw = .6f;
            Accept(controller, frame, 201);
            controller.ApplyControls(201, .1f);
            Quaternion headPositive = head.bones[0].localRotation;
            Check(Quaternion.Angle(headRest, headPositive) > 5 && Quaternion.Angle(trunkRest, trunk.bones[0].localRotation) < .01f
                && Quaternion.Angle(earRest, leftEar.bones[0].localRotation) < .01f, "Head yaw must drive only the head.");
            frame.people[2].headYaw = -.6f;
            Accept(controller, frame, 202);
            controller.ApplyControls(202, .1f);
            Quaternion headNegative = head.bones[0].localRotation;
            Check(Mathf.Abs(Quaternion.Angle(headRest, headPositive) - Quaternion.Angle(headRest, headNegative)) < .01f
                && Quaternion.Angle(headPositive, headNegative) > 20, "Head left/right turns must be symmetric opposite directions.");
            frame = Frame(controller);
            frame.people[2].values[(int)Movement.RightHandHeight] = .8f;
            Accept(controller, frame, 203);
            controller.ApplyControls(203, .1f);
            Check(Quaternion.Angle(trunkRest, trunk.bones[0].localRotation) > 5 && tip.position.y > tipRest.y + .05f,
                "Raising P3's right hand must curl the trunk and lift its tip.");
            Check(Quaternion.Angle(headRest, head.bones[0].localRotation) < .01f && Quaternion.Angle(earRest, leftEar.bones[0].localRotation) < .01f,
                "P3's right hand must not move head or ears.");
            frame = Frame(controller);
            frame.people[2].leftArmExtension = .8f;
            Accept(controller, frame, 204);
            controller.ApplyControls(204, .1f);
            Check(Quaternion.Angle(earRest, leftEar.bones[0].localRotation) > 5 && Quaternion.Angle(trunkRest, trunk.bones[0].localRotation) < .01f,
                "Independent left-arm ears must remain isolated from the trunk.");
            Check(Vector3.Distance(origin, loco.travelRoot.position) > .1f, "Synthetic gait test did not exercise world travel.");
        }

        static void ValidateSoloRoles()
        {
            for (int role = 1; role <= 3; role++)
            {
                var scene = EditorSceneManager.OpenScene(UpperBodyCourseSceneSetup.CourseScene, OpenSceneMode.Single);
                var c = UpperBodyCourseSceneSetup.Controller(scene);
                var loco = c.GetComponent<DigiPhantLocomotion>();
                c.SetSoloRolePractice(role);
                c.inputMode = InputMode.Camera;
                loco.inputSmoothing = 0;
                foreach (var control in c.controls) control.smoothing = 0;
                var frame = Frame(c);
                for (int channel = 0; channel < 6; channel++) frame.people[0].confidence[channel] = 0;
                frame.people[0].confidence[role == 1 ? (int)Movement.LeftHandHeight : role == 2 ? (int)Movement.Lean : (int)Movement.RightHandHeight] = 1;
                frame.people[0].hasHeadYaw = role == 3;
                frame.people[0].hasLeftArm = role == 3;
                Accept(c, frame, 300);
                Check(c.CalibrateAt(300), "Solo P" + role + " must calibrate using only that role's upper-body signals.");
                Check(c.SoloRole == role && c.performerCount == 1 && c.upperBodyOnly, "Solo role must use one upper-body input.");
                Vector3 position = loco.travelRoot.position;
                Quaternion rotation = loco.travelRoot.rotation;
                frame.people[0].values[(int)Movement.LeftHandHeight] = .8f;
                frame.people[0].values[(int)Movement.Lean] = -.8f;
                frame.people[0].values[(int)Movement.RightHandHeight] = .8f;
                frame.people[0].headYaw = .8f;
                Accept(c, frame, 300.1f);
                c.ApplyControls(300.1f, .1f);
                if (role == 1)
                    Check(loco.CurrentSpeed > 0 && Quaternion.Angle(rotation, loco.travelRoot.rotation) < .01f,
                        "Solo P1 must travel without responding to steering/head signals.");
                if (role == 2)
                    Check(loco.CurrentSpeed == 0 && Vector3.Distance(position, loco.travelRoot.position) < .001f
                        && SignedYaw(rotation, loco.travelRoot.rotation) > 1, "Solo P2 must turn without traveling.");
                if (role == 3)
                {
                    loco.enableLocomotion = true; // External toggles must not defeat P3's travel lock.
                    c.ApplyControls(300.1f, .1f);
                    Check(loco.CurrentSpeed == 0 && Vector3.Distance(position, loco.travelRoot.position) < .001f,
                        "Solo P3 must keep travel locked while controlling head/trunk/ears.");
                }
                c.inputMode = InputMode.TestSliders;
                loco.StopMotion();
                loco.testForward = .8f; loco.testSteering = -.8f;
                Vector3 debugPosition = loco.travelRoot.position;
                Quaternion debugRotation = loco.travelRoot.rotation;
                c.ApplyControls(300.2f, .1f);
                if (role == 1) Check(loco.CurrentSpeed > 0 && Quaternion.Angle(debugRotation, loco.travelRoot.rotation) < .01f,
                    "Solo P1 debug sliders must preserve steering isolation.");
                if (role == 2) Check(loco.CurrentSpeed == 0 && Vector3.Distance(debugPosition, loco.travelRoot.position) < .001f,
                    "Solo P2 debug sliders must preserve travel isolation.");
                if (role == 3) Check(loco.CurrentSpeed == 0 && Vector3.Distance(debugPosition, loco.travelRoot.position) < .001f,
                    "Solo P3 debug sliders must preserve the travel lock.");
                c.SetSoloRolePractice(0);
                Check(c.SoloRole == 0 && c.performerCount == 3 && c.upperBodyOnly && loco.enableLocomotion
                    && loco.forward.sources.Single().performer == 1 && loco.steering.sources.Single().performer == 2
                    && Control(c, "Head turn").performer == 3, "Leaving solo practice must restore all three team roles.");
            }
        }

        static void ValidateDuoTeam()
        {
            var scene = EditorSceneManager.OpenScene(UpperBodyCourseSceneSetup.CourseScene, OpenSceneMode.Single);
            var c = UpperBodyCourseSceneSetup.Controller(scene);
            var loco = c.GetComponent<DigiPhantLocomotion>();
            c.inputMode = InputMode.Camera;
            loco.inputSmoothing = 0;
            foreach (var control in c.controls) control.smoothing = 0;
            var frame = Frame(c);
            frame.people = frame.people.Take(2).ToArray(); // only P1 and P2 are seated
            Accept(c, frame, 500);
            Check(!c.CalibrateAt(500), "The three-person team must still refuse calibration while the P3 seat is empty.");
            c.SetDuoTeam(true);
            Check(c.DuoTeam && !c.IsRoleSeatActive(3) && !c.IsControlActive(Control(c, "Head turn")),
                "The two-person team must disable P3's head/trunk/ear roles.");
            Accept(c, frame, 500.1f);
            Check(c.CalibrateAt(500.1f), "The two-person team must calibrate with only P1 and P2.");
            Check(c.UpdateGroupTrackingSafety(500.1f, out _), "An empty P3 seat must not pause two-person travel.");
            Vector3 position = loco.travelRoot.position;
            Quaternion rotation = loco.travelRoot.rotation;
            frame.people[0].values[(int)Movement.LeftHandHeight] = .8f;
            frame.people[1].values[(int)Movement.Lean] = -.8f;
            float now = 500.1f;
            for (int i = 0; i < 20; i++)
            {
                now += 1 / 60f;
                Accept(c, frame, now);
                c.ApplyControls(now, 1 / 60f);
            }
            Check(loco.CurrentSpeed > 0 && Vector3.Distance(position, loco.travelRoot.position) > .001f
                && SignedYaw(rotation, loco.travelRoot.rotation) > 1, "Two-person team: P1 must drive while P2 steers.");
            var lost = Frame(c);
            lost.people = lost.people.Take(1).ToArray();
            lost.people[0].values[(int)Movement.LeftHandHeight] = .8f;
            now += .01f;
            Accept(c, lost, now);
            c.ApplyControls(now, 1 / 60f);
            Check(loco.CurrentSpeed == 0, "Losing P2 must still stop two-person travel immediately.");
            c.SetDuoTeam(false);
            Check(!c.DuoTeam && c.IsRoleSeatActive(3) && c.IsControlActive(Control(c, "Head turn")) && !c.IsCalibrated,
                "Leaving the two-person team must restore P3 and require a new neutral pose.");

            // P1 + P3: water/log practice with the P2 seat empty; travel is straight only.
            scene = EditorSceneManager.OpenScene(UpperBodyCourseSceneSetup.CourseScene, OpenSceneMode.Single);
            c = UpperBodyCourseSceneSetup.Controller(scene);
            loco = c.GetComponent<DigiPhantLocomotion>();
            var manager = UpperBodyCourseSceneSetup.Components<ThreePersonCourseManager>(scene).Single();
            c.inputMode = InputMode.Camera;
            loco.inputSmoothing = 0;
            foreach (var control in c.controls) control.smoothing = 0;
            var full = Frame(c);
            frame = Frame(c);
            frame.people = new[] { full.people[0], full.people[2] }; // P1 and P3 seated, P2 empty
            c.SetTeamSeating(DigiPhantController.TeamSeats.P1P3);
            Check(c.IsRoleSeatActive(3) && !c.IsRoleSeatActive(2) && c.IsControlActive(Control(c, "Head turn")),
                "P1+P3 must keep P3's head/trunk/ear roles and leave the P2 seat empty.");
            Accept(c, frame, 600);
            Check(c.CalibrateAt(600), "P1+P3 must calibrate with only P1 and P3.");
            Check(c.UpdateGroupTrackingSafety(600, out _) && manager.IsCourseInputReady(600),
                "An empty P2 seat must not pause P1+P3 travel or the water/log course gates.");
            position = loco.travelRoot.position;
            rotation = loco.travelRoot.rotation;
            frame.people[0].values[(int)Movement.LeftHandHeight] = .8f;
            now = 600;
            for (int i = 0; i < 20; i++)
            {
                now += 1 / 60f;
                Accept(c, frame, now);
                c.ApplyControls(now, 1 / 60f);
            }
            Check(loco.CurrentSpeed > 0 && Vector3.Distance(position, loco.travelRoot.position) > .001f
                && Mathf.Abs(SignedYaw(rotation, loco.travelRoot.rotation)) < .01f, "P1+P3: P1 must drive straight with no P2 steering.");
        }

        static void ValidateCourseReadiness()
        {
            var scene = EditorSceneManager.OpenScene(UpperBodyCourseSceneSetup.CourseScene, OpenSceneMode.Single);
            var controller = UpperBodyCourseSceneSetup.Controller(scene);
            var manager = UpperBodyCourseSceneSetup.Components<ThreePersonCourseManager>(scene).Single();
            controller.inputMode = InputMode.Camera;
            var frame = Frame(controller);
            Accept(controller, frame, 400);
            Check(controller.CalibrateAt(400), "Course readiness calibration failed.");
            frame.people[2].hasHeadYaw = false; frame.people[2].hasLeftArm = false;
            Accept(controller, frame, 400.1f);
            Check(manager.IsCourseInputReady(400.1f), "A hand or cup occluding P3 head/ear signals must not revoke calibrated driver/steering readiness.");
            frame.people[0].confidence[(int)Movement.LeftHandHeight] = 0;
            Accept(controller, frame, 400.2f);
            Check(!manager.IsCourseInputReady(400.2f), "Missing P1 drive hand must stop course readiness.");
            frame = Frame(controller); frame.people[1].confidence[(int)Movement.Lean] = 0;
            Accept(controller, frame, 400.3f);
            Check(!manager.IsCourseInputReady(400.3f), "Missing P2 steering shoulders must stop course readiness.");
        }

        static void ValidateCourseGeometry(ThreePersonCourseManager manager)
        {
            var tree = manager.treeInteraction;
            var log = manager.logInteraction;
            var water = manager.waterInteraction;
            manager.ResetCourse(false);
            Vector3 initial = tree.detectionZone.transform.position - tree.detectionZone.transform.forward * 8;
            manager.EvaluateAt(initial, 0, false, 12);
            manager.EvaluateAt(initial, 0, true, .1f);
            Check(manager.ElapsedSeconds == 0 && manager.PreparationSeconds >= 12 && !manager.CourseStarted,
                "Camera waiting and neutral calibration belong to preparation, not the timed trial.");
            manager.EvaluateAt(initial, 1, true, .1f);
            manager.EvaluateAt(initial, 0, false, 2);
            Check(manager.CourseStarted && Mathf.Abs(manager.ElapsedSeconds - 2.1f) < .001f
                && manager.FrameGapSeconds == 2 && manager.TrackingPauseSeconds == 2,
                "Once movement starts, real tracking interruptions and long frame stalls must remain in elapsed time.");
            manager.ResetCourse(false);
            manager.SetSoloPracticePaused(true, 3);
            float elapsedBeforePause = manager.ElapsedSeconds;
            manager.EvaluateAt(manager.finishZone.transform.position, 1, true, .1f);
            Check(manager.SoloPracticePaused && manager.CurrentStage == CourseStage.Tree && manager.ElapsedSeconds == elapsedBeforePause,
                "Solo practice must pause team course timing and progression.");
            manager.SetSoloPracticePaused(false, 0);
            manager.EvaluateAt(manager.finishZone.transform.position, 1, true, .1f);
            Check(manager.CurrentStage == CourseStage.Tree && !manager.AcceptWaterCompletion(new CupGestureCompletion { revision = 100 }), "Wrong-order finish/water evidence must be rejected.");
            manager.ResetCourse(false);
            Cross(tree.detectionZone, 0, 1, (position, speed) => manager.EvaluateAt(position, speed, true, .1f));
            Check(!tree.Completed && manager.CurrentStage == CourseStage.Tree,
                "A safe straight pass without any demonstrated heading change must not count as tree steering.");
            manager.ResetCourse(false);
            CrossTree(manager);
            Check(tree.Completed && tree.MinimumClearance > 0 && manager.CurrentStage == CourseStage.Water,
                "A safe route past the original tree must advance to the original water stage.");
            manager.EvaluateAt(manager.waterZone.transform.position, .6f, true, .1f);
            Check(!manager.WaterGateAllowed && !water.EvaluateGestureSample(FistSample(true), 1, manager.waterZone.transform.position) && manager.CurrentStage == CourseStage.Water,
                "Fist completion while moving must not satisfy the water stage.");
            Vector3 farWater = manager.waterZone.transform.TransformPoint(manager.waterZone.center + new Vector3(4.5f, 0, 3.5f));
            manager.EvaluateAt(farWater, 0, true, .1f);
            Check(!manager.WaterGateAllowed, "The distant corner of the wide approach zone must not count as basin arrival.");
            manager.EvaluateAt(manager.waterZone.transform.position, 0, false, .1f);
            Check(!manager.WaterGateAllowed, "Lost tracking must prevent the water gate.");
            manager.EvaluateAt(manager.waterZone.transform.position, 0, true, .1f);
            Check(manager.WaterGateAllowed && manager.WaterDwellSeconds > 0, "Stationary tracked arrival must enable the water gesture.");
            CupGestureCompletion completed = Drink(water, manager.waterZone.transform.position);
            Check(completed != null && completed.courseGateAllowed && manager.CurrentStage == CourseStage.Water,
                "Valid fist water completion is captured during Water's later execution, before manager consumes it.");
            manager.EvaluateAt(manager.waterZone.transform.position, 1, false, .1f);
            Check(manager.CurrentStage == CourseStage.Log && manager.AcceptedTeamCupGestures == 1 && !manager.WaterGateAllowed
                && !manager.AcceptWaterCompletion(completed),
                "A next-frame speed/tracking change must not revoke a valid historical completion, or count it twice.");
            Cross(log.detectionZone, 4, 1, (position, speed) => manager.EvaluateAt(position, speed, true, .1f));
            Check(!log.Completed && manager.CurrentStage == CourseStage.Log, "Following the original road beside the timber must not count as actually crossing the log.");
            Cross(log.detectionZone, 0, 3, (position, speed) => manager.EvaluateAt(position, speed, true, .1f));
            Check(!log.Completed && manager.CurrentStage == CourseStage.Log, "Running across the log must fail the slow-gait condition.");
            Cross(log.detectionZone, 0, 1, (position, speed) => manager.EvaluateAt(position, speed, true, .1f));
            Check(log.Completed && log.CrossingSeconds > 0 && manager.CurrentStage == CourseStage.Finish,
                "Slow forward gait through the original log zone must advance to finish.");
            var finish = manager.finishZone;
            Vector3 near = finish.transform.TransformPoint(finish.center + new Vector3(0, 0, -finish.size.z * .5f - 1));
            Vector3 far = finish.transform.TransformPoint(finish.center + new Vector3(0, 0, finish.size.z * .5f + 1));
            manager.EvaluateAt(near, 1, true, .1f);
            manager.EvaluateAt(far, 1, true, .1f);
            Check(manager.CurrentStage == CourseStage.Finish, "Teleporting through finish must not complete the course.");
            Cross(finish, 0, 1, (position, speed) => manager.EvaluateAt(position, speed, true, .1f));
            Check(manager.CurrentStage == CourseStage.Complete, "Continuous forward finish entry/exit must complete the course.");
            string folder = null;
            try
            {
                manager.StartResultSession();
                folder = manager.SessionFolder;
                manager.SaveResults();
                Check(!string.IsNullOrEmpty(folder) && File.Exists(Path.Combine(folder, "results.json")) && File.Exists(Path.Combine(folder, "events.csv")),
                    "Course result JSON and CSV must be saved.");
                string result = File.ReadAllText(Path.Combine(folder, "results.json"));
                Check(result.Contains("\"courseComplete\": true") && result.Contains("\"finalStage\": \"Complete\"")
                    && result.Contains("\"treeClearance\"") && result.Contains("\"logCrossingSeconds\"") && result.Contains("\"waterDwellSeconds\""),
                    "Persisted results must retain completion and the three interaction measures.");
                Check(result.Contains("\"completedCupGestures\": 1") && result.Contains("\"timingDefinition\"")
                    && result.Contains("\"preparationSeconds\"") && result.Contains("\"frameGapSeconds\""),
                    "Persisted results must retain team-only water interaction counts and explicit preparation/lag timing definitions.");
            }
            finally
            {
                manager.enabled = false; // Close the CSV before deleting synthetic test artifacts.
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder)) Directory.Delete(folder, true);
            }

            // Individual interaction guards are tested on actual saved trigger geometry.
            tree.ResetInteraction();
            Vector3 trunkLocal = tree.detectionZone.transform.InverseTransformPoint(tree.treeTrunk.position) - tree.detectionZone.center;
            Cross(tree.detectionZone, trunkLocal.x, 1, (position, speed) => tree.Evaluate(position, speed, true, .1f));
            Check(!tree.Completed && tree.FailureCount > 0, "A route intersecting the original trunk must fail clearance.");
            tree.ResetInteraction();
            Vector3 treeStart = tree.detectionZone.transform.TransformPoint(tree.detectionZone.center + new Vector3(0, 0, -tree.detectionZone.size.z * .5f - .1f));
            tree.Evaluate(treeStart, 1, true, .1f);
            tree.Evaluate(treeStart + tree.detectionZone.transform.forward * .2f, 1, true, .1f);
            Vector3 treeGood = treeStart + tree.detectionZone.transform.forward * .2f;
            for (int paused = 0; paused < 4; paused++) tree.Evaluate(treeGood, 0, false, .1f);
            Check(tree.AttemptActive && !tree.Completed && tree.FailureCount == 0 && tree.PausedSeconds >= .39f,
                "A .4-second stopped tracking loss preserves an incomplete tree attempt.");
            for (float z = -4.8f; z < 5.2f; z += .1f)
                tree.Evaluate(tree.detectionZone.transform.TransformPoint(tree.detectionZone.center + new Vector3(0, 0, z)), 1, true, .1f,
                    tree.detectionZone.transform.eulerAngles.y + (z >= 0 ? 18 : 0));
            Check(tree.Completed && tree.FailureCount == 0, "The same calibrated performer context can recover and finish after one short tree pause.");
            tree.ResetInteraction();
            tree.Evaluate(treeStart, 1, true, .1f, 0, 7);
            tree.Evaluate(treeGood, 1, true, .1f, 0, 7);
            for (int cycle = 0; cycle < 3; cycle++)
            {
                for (int paused = 0; paused < 4; paused++) tree.Evaluate(treeGood, 0, false, .1f, 0, 7);
                if (cycle < 2) { treeGood += tree.detectionZone.transform.forward * .03f; tree.Evaluate(treeGood, 1, true, .1f, 18, 7); }
            }
            Check(tree.AttemptActive && tree.PausedSeconds >= 1.19f, "Multiple .4-second recoverable pauses can use, but cannot exceed, the total tree budget.");
            tree.Evaluate(treeGood, 0, false, .1f, 18, 7);
            Check(!tree.AttemptActive && tree.FailureCount == 1, "The cumulative tree pause budget cannot be renewed indefinitely.");
            tree.ResetInteraction();
            tree.Evaluate(treeStart, 1, true, .1f, 0, 7);
            tree.Evaluate(treeStart + tree.detectionZone.transform.forward * .2f, 1, true, .1f, 0, 7);
            tree.Evaluate(treeStart + tree.detectionZone.transform.forward * .2f, 0, false, .1f, 0, 8);
            Check(!tree.AttemptActive && tree.FailureCount == 1, "A new identity/calibration revision must cancel the old tree attempt even inside the short-pause window.");
            tree.ResetInteraction();
            tree.Evaluate(treeStart, 1, true, .1f, 0, 7);
            tree.Evaluate(treeStart + tree.detectionZone.transform.forward * .2f, 1, true, .1f, 0, 7);
            tree.Evaluate(treeStart + tree.detectionZone.transform.forward * .1f, -1, true, .1f, 0, 7);
            Check(!tree.AttemptActive && tree.FailureCount == 1, "Reverse travel still fails the tree attempt rather than borrowing pause grace.");
            log.ResetInteraction();
            Vector3 logStart = log.detectionZone.transform.TransformPoint(log.detectionZone.center + new Vector3(0, 0, -log.detectionZone.size.z * .5f - .1f));
            log.Evaluate(logStart, 1, true, .1f);
            log.Evaluate(logStart + log.detectionZone.transform.forward * .2f, 1, true, .1f);
            log.Evaluate(logStart + log.detectionZone.transform.forward * .3f, 0, true, .1f);
            Check(log.AttemptActive && log.PausedSeconds > 0 && log.FailureCount == 0, "A brief stopped tracking sample must pause the log attempt rather than immediately fail it.");
            log.Evaluate(logStart + log.detectionZone.transform.forward * .3f, 0, false, .1f);
            log.Evaluate(logStart + log.detectionZone.transform.forward * .3f, 0, false, .1f);
            log.Evaluate(logStart + log.detectionZone.transform.forward * .3f, 0, false, .1f);
            log.Evaluate(logStart + log.detectionZone.transform.forward * .3f, 0, false, .1f);
            Check(!log.AttemptActive && log.FailureCount > 0, "A prolonged log pause must still cancel the attempt.");
            log.ResetInteraction();
            log.Evaluate(logStart, 1, true, .1f);
            Vector3 goodPosition = logStart + log.detectionZone.transform.forward * .2f;
            log.Evaluate(goodPosition, 1, true, .1f);
            for (int cycle = 0; cycle < 5 && log.AttemptActive; cycle++)
            {
                for (int lost = 0; lost < 3; lost++) log.Evaluate(goodPosition, 0, false, .1f);
                goodPosition += log.detectionZone.transform.forward * .04f;
                log.Evaluate(goodPosition, 1, true, .1f);
            }
            Check(!log.AttemptActive && log.FailureCount > 0,
                "Repeated short interruptions cannot renew the pause budget indefinitely.");
            log.ResetInteraction();
            log.Evaluate(logStart, 1, true, .1f);
            goodPosition = logStart + log.detectionZone.transform.forward * .2f;
            log.Evaluate(goodPosition, 1, true, .1f);
            log.Evaluate(goodPosition, 0, false, .1f);
            for (float z = -2.8f; z < 3.2f; z += .1f)
                log.Evaluate(log.detectionZone.transform.TransformPoint(log.detectionZone.center + new Vector3(0, 0, z)), 1, true, .1f);
            Check(log.Completed && log.PausedSeconds > 0 && log.CrossingSeconds > 0,
                "A single short interruption can recover and finish a genuine timber crossing.");

            // Solo work must never enter the new team session's accepted action count.
            manager.ResetCourse(false);
            manager.SetSoloPracticePaused(true, 1);
            Check(!water.enabled, "Solo P1 must disable water interaction logic.");
            manager.SetSoloPracticePaused(true, 3);
            Check(water.enabled && water.showPanel && !water.CourseGateEnabled, "Solo P3 must enable independent fist water practice.");
            Drink(water, manager.waterZone.transform.position);
            manager.SetSoloPracticePaused(false, 0);
            Check(water.enabled && !water.showPanel && manager.CurrentStage == CourseStage.Tree
                && manager.ElapsedSeconds == 0 && manager.AcceptedTeamCupGestures == 0,
                "Team from a practice scene must restart the course, keep fist water interaction active, and clear team-only statistics.");
            CrossTree(manager);
            manager.EvaluateAt(manager.waterZone.transform.position, 0, true, .1f);
            completed = Drink(water, manager.waterZone.transform.position);
            manager.EvaluateAt(manager.waterZone.transform.position, 0, true, .1f);
            Check(manager.CurrentStage == CourseStage.Log && manager.AcceptedTeamCupGestures == 1,
                "Only the accepted team water gesture counts, even after earlier solo completions.");

            // The runtime pickup branch is Play-gated, so exercise its pure gesture
            // guard directly with TestSliders while keeping the scene unsaved.
            manager.ResetCourse(false);
            var pickupController = manager.controller;
            pickupController.inputMode = InputMode.TestSliders;
            pickupController.testRightFistClosed = true;
            Vector3 logPosition = manager.logInteraction.logTransform.position;
            MethodInfo pickup = typeof(ThreePersonCourseManager).GetMethod("TryPickUpLog",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Check(pickup != null, "Trunk pickup method is missing.");
            pickup.Invoke(manager, new object[] { logPosition, 0f, true, .4f });
            Check(!manager.LogPickedUp && manager.PickupStatus.Contains("Stop beside the log"),
                "A long frame must not satisfy the closed-fist pickup hold.");
            for (int i = 0; i < 4 && !manager.LogPickedUp; i++)
                pickup.Invoke(manager, new object[] { logPosition, 0f, true, .1f });
            Check(manager.LogPickedUp, "A stationary confident closed-fist hold should pick up the log.");
            manager.ResetCourse(false);
        }

        static CupGestureSample FistSample(bool closed) => new CupGestureSample {
            dt = .1f, contextReady = true, poseReady = true, rightHand = 0,
            fistAvailable = true, fistClosed = closed, fistConfidence = .9f
        };
        static CupGestureCompletion Drink(WaterCupInteraction water, Vector3 position)
        {
            water.TryAnotherGesture();
            float now = 0;
            now += .1f; water.EvaluateGestureSample(FistSample(false), now, position);
            for (int i = 0; i < 7; i++)
            { now += .1f; water.EvaluateGestureSample(FistSample(true), now, position); }
            return water.LatestCompletion;
        }
        static void CrossTree(ThreePersonCourseManager manager)
        {
            var zone = manager.treeInteraction.detectionZone;
            Cross(zone, 0, 1, (position, speed) => manager.EvaluateAt(position, speed, true, .1f,
                zone.transform.eulerAngles.y + (CourseZoneUtility.LocalPoint(zone, position).z >= 0 ? 18 : 0)));
        }

        static void Cross(BoxCollider zone, float x, float speed, Action<Vector3, float> step)
        {
            float start = -zone.size.z * .5f - .2f, end = zone.size.z * .5f + .2f;
            for (float z = start; z < end + .05f; z += .1f)
                step(zone.transform.TransformPoint(zone.center + new Vector3(x, -1.5f, z)), speed);
        }

        static PoseFrame Frame(DigiPhantController c) => new PoseFrame {
            version = 1, performerCount = c.performerCount, upperBodyOnly = true,
            people = Enumerable.Range(1, c.performerCount).Select(slot => new PerformerFrame {
                slot = slot, values = new float[6], confidence = new[] { 1f, 1f, 0f, 0f, 1f, 1f },
                hasLeftArm = true, leftArmConfidence = 1, hasHeadYaw = true, headYawConfidence = 1
            }).ToArray()
        };

        static void Accept(DigiPhantController c, PoseFrame frame, float now)
            => Check(c.AcceptPacket(JsonUtility.ToJson(frame), now), "A valid upper-body synthetic packet was rejected.");

        static BoneControl Control(DigiPhantController c, string label) => c.controls.Single(value => value.label == label);
        static string SoloPath(int role) => role == 1 ? UpperBodyCourseSceneSetup.P1Scene : role == 2 ? UpperBodyCourseSceneSetup.P2Scene : UpperBodyCourseSceneSetup.P3Scene;
        static float SignedYaw(Quaternion before, Quaternion after) => Vector3.SignedAngle(before * Vector3.forward, after * Vector3.forward, Vector3.up);

        static string CourseFingerprint(Scene scene)
        {
            var root = scene.GetRootGameObjects().Single(value => value.name == SavannahCourseGenerator.RootName).transform;
            var result = new StringBuilder();
            void Visit(Transform value)
            {
                result.Append(value.name).Append('|').Append(value.gameObject.activeSelf).Append('|').Append(value.childCount).Append('|');
                foreach (float number in new[] { value.localPosition.x, value.localPosition.y, value.localPosition.z, value.localRotation.x,
                    value.localRotation.y, value.localRotation.z, value.localRotation.w, value.localScale.x, value.localScale.y, value.localScale.z })
                    result.Append(number.ToString("R", CultureInfo.InvariantCulture)).Append('|');
                var filter = value.GetComponent<MeshFilter>();
                if (filter) result.Append(AssetDatabase.GetAssetPath(filter.sharedMesh)).Append('|');
                result.Append('\n');
                for (int child = 0; child < value.childCount; child++) Visit(value.GetChild(child));
            }
            Visit(root);
            return result.ToString();
        }

        static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
