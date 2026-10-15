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
    // Explicit setup only. This never starts Play mode, a receiver, or a webcam.
    public static class P3InteractionSceneSetup
    {
        public const string SoloScene = "Assets/StudentWork/Scenes/P3SoloPractice.unity";
        public static string LastBackupPath { get; private set; }

        [MenuItem("Student Work/Prepare P3 Interaction Scenes")]
        public static void Prepare()
        {
            RequireCleanEditor();
            PrepareScenes();
        }

        // An explicit batch target is allowed to start from Unity's empty startup scene.
        public static void PrepareBatch()
        {
            if (!Application.isBatchMode) RequireCleanEditor();
            RequireNotPlaying();
            PrepareScenes();
        }

        public static void PrepareAndValidateBatch()
        {
            PrepareBatch();
            P3InteractionValidation.Run();
        }

        [MenuItem("Student Work/Open P3 Solo Practice")]
        public static void OpenSolo()
        {
            RequireCleanEditor();
            if (!File.Exists(SoloScene))
                throw new FileNotFoundException("Prepare P3 Interaction Scenes before opening the solo scene.", SoloScene);
            EditorSceneManager.OpenScene(SoloScene, OpenSceneMode.Single);
        }

        [MenuItem("Student Work/Prepare P3 Interaction Scenes", true)]
        [MenuItem("Student Work/Open P3 Solo Practice", true)]
        static bool CanPrepare() => !EditorApplication.isPlayingOrWillChangePlaymode;

        static void PrepareScenes()
        {
            if (!File.Exists(TeamSceneSetup.TeamScene))
                throw new FileNotFoundException("Prepare the saved TeamPerformance scene first.", TeamSceneSetup.TeamScene);

            // Copy the saved asset before changing settings or adding any component.
            LastBackupPath = AssetDatabase.GenerateUniqueAssetPath(
                "Assets/StudentWork/Scenes/TeamPerformance_BeforeP3Interactions_"
                + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ") + ".unity");
            if (!AssetDatabase.CopyAsset(TeamSceneSetup.TeamScene, LastBackupPath))
                throw new IOException("Could not back up TeamPerformance. No scene settings were changed.");

            var team = EditorSceneManager.OpenScene(TeamSceneSetup.TeamScene, OpenSceneMode.Single);
            var controller = Controller(team);
            var locomotion = controller.GetComponent<DigiPhantLocomotion>();
            if (!locomotion || !locomotion.travelRoot)
                throw new InvalidOperationException("TeamPerformance needs the existing DigiPhant locomotion references.");
            Undo.RecordObject(controller, "Enable P3 independent left-hand ears");
            controller.SetIndependentP3Ears(true);
            ConfigureWaterInteraction(team, controller, locomotion);
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(team);
            if (!EditorSceneManager.SaveScene(team)) throw new IOException("Could not save the P3 team setup.");

            bool createdSolo = !File.Exists(SoloScene);
            if (createdSolo)
            {
                if (!AssetDatabase.CopyAsset(TeamSceneSetup.TeamScene, SoloScene))
                    throw new IOException("Could not create the P3 solo copy; the team scene remains saved.");
                var solo = EditorSceneManager.OpenScene(SoloScene, OpenSceneMode.Single);
                var soloController = Controller(solo);
                var soloLocomotion = soloController.GetComponent<DigiPhantLocomotion>();
                soloController.SetIndependentP3Ears(true);
                soloController.SetP3PracticeMode(true);
                // Direct assignment, unlike SetInputMode, does not create an Edit-mode receiver.
                soloController.inputMode = InputMode.Camera;
                soloController.showCameraPreview = true;
                ConfigureWaterInteraction(solo, soloController, soloLocomotion);
                EditorUtility.SetDirty(soloController);
                EditorUtility.SetDirty(soloLocomotion);
                EditorSceneManager.MarkSceneDirty(solo);
                if (!EditorSceneManager.SaveScene(solo)) throw new IOException("Could not save P3SoloPractice.");
            }

            AddSoloToBuildSettings();
            AssetDatabase.SaveAssets();
            // Keep the familiar team scene open. Solo is an explicit separate scene.
            var restored = EditorSceneManager.OpenScene(TeamSceneSetup.TeamScene, OpenSceneMode.Single);
            Selection.activeGameObject = Controller(restored).gameObject;
            Debug.Log("P3_INTERACTION_SCENES_READY: independent left-hand ears + closed-fist water interaction; team settings preserved; "
                + (createdSolo ? "P3SoloPractice created (one upper-body performer, head/trunk/ears only, no travel)" : "existing P3SoloPractice preserved")
                + "; backup=" + LastBackupPath + "; no Play mode or camera capture.");
        }

        static void ConfigureWaterInteraction(Scene scene, DigiPhantController controller, DigiPhantLocomotion locomotion)
        {
            var existing = Components<WaterCupInteraction>(scene).ToArray();
            if (existing.Length > 1)
                throw new InvalidOperationException("Multiple WaterCupInteraction components found; existing objects were preserved.");
            var interaction = existing.Length == 1 ? existing[0] : Undo.AddComponent<WaterCupInteraction>(controller.gameObject);
            Undo.RecordObject(interaction, "Configure P3 fist water practice");
            interaction.Configure(controller, locomotion);
            EditorUtility.SetDirty(interaction);
        }

        static DigiPhantController Controller(Scene scene)
        {
            var controllers = Components<DigiPhantController>(scene).ToArray();
            if (controllers.Length != 1)
                throw new InvalidOperationException("Expected exactly one DigiPhantController in " + scene.path + ".");
            return controllers[0];
        }

        static System.Collections.Generic.IEnumerable<T> Components<T>(Scene scene) where T : Component
            => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));

        static void AddSoloToBuildSettings()
        {
            if (EditorBuildSettings.scenes.Any(scene => scene.path == SoloScene)) return;
            EditorBuildSettings.scenes = EditorBuildSettings.scenes.Concat(new[] {
                new EditorBuildSettingsScene(SoloScene, true)
            }).ToArray();
        }

        static void RequireNotPlaying()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Stop Play mode and exit Prefab Mode before P3 setup.");
        }

        static void RequireCleanEditor()
        {
            RequireNotPlaying();
            var scene = SceneManager.GetActiveScene();
            if (SceneManager.sceneCount != 1 || !scene.IsValid() || scene.isDirty
                || string.IsNullOrEmpty(scene.path) || !File.Exists(scene.path))
                throw new InvalidOperationException("Open one saved, unmodified scene before P3 setup. Save or discard changes first.");
        }
    }
}
