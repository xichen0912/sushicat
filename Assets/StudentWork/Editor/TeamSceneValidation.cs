using System;
using System.IO;
using System.Linq;
using DigiPhant;
using DigiPhant.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StudentWork.Editor
{
    public static class TeamSceneValidation
    {
        [MenuItem("Student Work/Validate Setup Without Camera")]
        public static void ValidateSetupWithoutCamera()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before validating the setup.");
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Exit Prefab Mode before validating the setup.");
            if (SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Open one saved scene before validating the setup.");
            var current = SceneManager.GetActiveScene();
            if (!current.IsValid() || string.IsNullOrEmpty(current.path) || current.isDirty)
                throw new InvalidOperationException("Save or discard scene changes before validating. Validation never saves transient test state.");
            string restorePath = current.path;
            if (!File.Exists(restorePath) || !File.Exists(DigiPhantSetup.ScenePath))
                throw new FileNotFoundException("The current saved scene or supplied DigiPhant scene is missing.");

            try
            {
                var scene = EditorSceneManager.OpenScene(DigiPhantSetup.ScenePath, OpenSceneMode.Single);
                var controllers = scene.GetRootGameObjects()
                    .SelectMany(g => g.GetComponentsInChildren<DigiPhantController>(true)).ToArray();
                Check(controllers.Length == 1, "Expected one DigiPhantController in the saved template.");
                ValidateSeatedPackets(controllers[0]);
                StudentLocomotionValidation.Run();
            }
            finally
            {
                // Reload the saved scene even if a check throws; never save synthetic test changes.
                var restored = EditorSceneManager.OpenScene(restorePath, OpenSceneMode.Single);
                var restoredController = restored.GetRootGameObjects()
                    .SelectMany(g => g.GetComponentsInChildren<DigiPhantController>(true)).FirstOrDefault();
                Selection.activeGameObject = restoredController
                    ? restoredController.gameObject : restored.GetRootGameObjects().FirstOrDefault();
                ActiveEditorTracker.sharedTracker.ForceRebuild();
                EditorApplication.RepaintHierarchyWindow();
                EditorApplication.RepaintProjectWindow();
                SceneView.RepaintAll();
            }
            Debug.Log("STUDENT_SETUP_VALIDATION_OK: seated synthetic packets, leg control, tracking timeout, locomotion and input-mode reset; no camera or Play mode required, actual Play re-entry not assessed.");
        }

        [MenuItem("Student Work/Validate Setup Without Camera", true)]
        static bool CanValidate() => !EditorApplication.isPlayingOrWillChangePlaymode;

        static void ValidateSeatedPackets(DigiPhantController controller)
        {
            var locomotion = controller.GetComponent<DigiPhantLocomotion>();
            if (locomotion) locomotion.enableLocomotion = false;
            controller.SetPerformerCount(3);
            controller.CaptureRestPose();
            // The supplied validator leaves TestSliders selected after its UDP checks,
            // so its later seated-packet check reads zero sliders instead of camera values.
            // Select Camera directly for deterministic packets without starting a UDP receiver.
            controller.inputMode = InputMode.Camera;
            controller.SetUpperBodyOnly(true);

            var leg = controller.controls.Single(c => c.label == "Front left leg");
            Check(leg.bones != null && leg.bones.Length > 0 && leg.bones[0], "Missing front-left leg rig reference.");
            var bone = leg.bones[0];
            Quaternion initial = bone.localRotation;
            var frame = new PoseFrame {
                version = 1, performerCount = 3, upperBodyOnly = true,
                people = Enumerable.Range(1, 3).Select(slot => new PerformerFrame {
                    slot = slot, values = new float[6], confidence = Enumerable.Repeat(1f, 6).ToArray()
                }).ToArray()
            };
            const float now = 40;
            Check(controller.AcceptPacket(JsonUtility.ToJson(frame), now), "Seated baseline packet rejected.");
            Check(controller.CalibrateAt(now), "Seated baseline calibration failed.");
            frame.people[leg.performer - 1].values[(int)leg.movement] = 1;
            Check(controller.AcceptPacket(JsonUtility.ToJson(frame), now), "Seated leg-control packet rejected.");
            controller.ApplyControls(now, 1);
            Check(Quaternion.Angle(initial, bone.localRotation) > 10, "Seated packet failed to drive the leg.");
            float staleTime = now + controller.trackingTimeout + 1;
            controller.ApplyControls(staleTime, 2);
            Check(!controller.IsTracked(leg.performer, staleTime), "Stale seated performer remained tracked.");
            Check(Quaternion.Angle(initial, bone.localRotation) < .01f, "Stale seated tracking did not return the leg to neutral.");
        }

        static void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
        }
    }
}
