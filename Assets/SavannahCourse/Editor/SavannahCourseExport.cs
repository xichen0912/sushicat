using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StudentStarter.Savannah
{
    // Maintainer tooling. Students normally use Generate Course in Scene Copy.
    public static class SavannahCourseExport
    {
        public const string ScenePath = "Assets/SavannahCourse/Scenes/SavannahEnvironment.unity";
        public const string PrefabPath = "Assets/SavannahCourse/Prefabs/SavannahCourse.prefab";

        [MenuItem("DigiPhant/Savannah/Export Shareable Environment")]
        public static void ExportEnvironment()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play before exporting.");
            SavannahCourseGenerator.PrepareAssets();
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            AssetDatabase.Refresh();
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var root = SavannahCourseGenerator.BuildContents(scene);
                var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(root,PrefabPath,InteractionMode.AutomatedAction);
                if (!prefab) throw new IOException("Could not save environment prefab.");
                var camera = new GameObject("Preview Camera (do not copy into elephant scene)").AddComponent<Camera>();
                camera.transform.SetPositionAndRotation(new Vector3(0,80,0),Quaternion.Euler(90,0,0));
                camera.orthographic = true;
                camera.orthographicSize = 34;
                camera.nearClipPlane = .3f;
                camera.farClipPlane = 200;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.23f,.27f,.25f);
                var light = new GameObject("Preview Sun (do not copy into elephant scene)").AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.2f;
                light.transform.rotation = Quaternion.Euler(50,-30,0);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.55f,.55f,.55f);
                if (!EditorSceneManager.SaveScene(scene,ScenePath)) throw new IOException("Could not save environment scene.");
                AssetDatabase.SaveAssets();
                ValidateBundle();
                Debug.Log("SAVANNAH_EXPORT_OK " + ScenePath + " | " + PrefabPath);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene,true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        [MenuItem("DigiPhant/Savannah/Validate Shareable Environment")]
        public static void ValidateBundle()
        {
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath)) throw new IOException("Missing exported scene.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!prefab) throw new IOException("Missing exported prefab.");
            foreach (var path in AssetDatabase.GetDependencies(new[] { ScenePath,PrefabPath },true))
                if (path.StartsWith("Assets/",StringComparison.Ordinal) && !path.StartsWith("Assets/SavannahCourse/",StringComparison.Ordinal))
                    throw new InvalidOperationException("Bundle depends on an external project asset: " + path);
            if (prefab.GetComponentsInChildren<Component>(true).Any(c => !(c is Transform || c is MeshFilter || c is MeshRenderer)))
                throw new InvalidOperationException("Environment prefab contains runtime behaviour or non-mesh components.");
            var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length != 130 || filters.Any(f => !f.sharedMesh)) throw new InvalidOperationException("Prefab mesh count/references differ from layout 4.");
            if (prefab.GetComponentsInChildren<MeshRenderer>(true).Any(r => !r.sharedMaterial || !r.sharedMaterial.shader))
                throw new InvalidOperationException("Prefab material or shader reference missing.");
            Debug.Log("SAVANNAH_BUNDLE_OK | all project asset dependencies are inside Assets/SavannahCourse");
        }

        // Run in a fresh project after copying the folder into Assets/SavannahCourse.
        public static void ValidateImportedBundle()
        {
            EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
            ValidateBundle();
            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            if (roots.Length != 3 || roots.Count(g => g.name == SavannahCourseGenerator.RootName) != 1)
                throw new InvalidOperationException("Exported preview scene must contain one environment, camera, and sun.");
            foreach (var root in roots)
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) != 0)
                        throw new InvalidOperationException("Imported scene has a missing script.");
            SavannahCourseValidation.RunAll();
            Debug.Log("SAVANNAH_IMPORT_OK | baked scene and generator verified in this project");
        }
    }
}
