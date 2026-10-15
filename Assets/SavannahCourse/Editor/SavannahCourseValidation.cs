using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace StudentStarter.Savannah
{
    public static class SavannahCourseValidation
    {
        [MenuItem("DigiPhant/Savannah/Validate Generator")]
        public static void RunAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play before validating.");
            SavannahCourseGenerator.PrepareAssets();
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var first = SavannahCourseGenerator.BuildContents(scene);
                string expected = Signature(first);
                Validate(first);
                Object.DestroyImmediate(first);
                // Advance global random state: it must not affect this generator.
                var state = UnityEngine.Random.state;
                try
                {
                    UnityEngine.Random.InitState(17);
                    var second = SavannahCourseGenerator.BuildContents(scene);
                    Require(expected == Signature(second), "Generation is not deterministic.");
                    Validate(second);
                    Require(scene.GetRootGameObjects().Length == 1, "Repeated generation left duplicate roots.");
                    int triangles = second.GetComponentsInChildren<MeshFilter>().Sum(f => f.sharedMesh.triangles.Length / 3);
                    Debug.Log("SAVANNAH_VALIDATION_OK | deterministic layout | 18 trees | 4 landmarks | "
                        + second.GetComponentsInChildren<MeshRenderer>().Length + " renderers | " + triangles
                        + " triangles | 4 shared meshes | 9 shared materials | no generated physics/audio/behaviours");
                }
                finally { UnityEngine.Random.state = state; }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            ValidateSceneCopy();
            ValidateInjection();
        }

        static void ValidateSceneCopy()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                Require(!SceneManager.GetSceneAt(i).isDirty, "Save open scenes before running the scene-copy test.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            Require(setup.All(s => !string.IsNullOrEmpty(s.path)), "Save the open scene before validating.");
            string folder = "Assets/StudentWork/SavannahValidation-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            try
            {
                var source = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var sentinel = new GameObject("Student sentinel");
                sentinel.transform.position = new Vector3(20,2,30);
                sentinel.AddComponent<Light>().intensity = .37f;
                // A matching root name must still produce a copy, never modify the source scene.
                var namedRoot = SavannahCourseGenerator.BuildContents(source);
                namedRoot.transform.position = sentinel.transform.position;
                namedRoot.transform.rotation = Quaternion.Euler(0,90,0);
                string sourcePath = folder + "/Source.unity";
                Require(EditorSceneManager.SaveScene(source,sourcePath), "Could not save temporary test scene.");
                byte[] sourceBytes = File.ReadAllBytes(sourcePath);
                SavannahCourseGenerator.GenerateSceneCopy();
                string copiedPath = SceneManager.GetActiveScene().path;
                Require(copiedPath != sourcePath, "Generator overwrote its source scene.");
                Require(File.ReadAllBytes(sourcePath).SequenceEqual(sourceBytes), "Source scene bytes changed.");
                SavannahCourseGenerator.RegenerateOpenCourse();
                EditorSceneManager.OpenScene(copiedPath);
                var roots = SceneManager.GetActiveScene().GetRootGameObjects();
                Require(roots.Length == 2 && roots.Count(g => g.name == SavannahCourseGenerator.RootName) == 1,
                    "Regeneration duplicated roots or lost student objects.");
                var preserved = roots.Single(g => g.name == "Student sentinel");
                Require(preserved.transform.position == new Vector3(20,2,30) && preserved.GetComponent<Light>().intensity == .37f,
                    "Student transform or component changed.");
                var course = roots.Single(g=>g.name==SavannahCourseGenerator.RootName);
                Require(course.transform.position == new Vector3(20,2,30) && Mathf.Abs(course.transform.eulerAngles.y-90)<.01f,
                    "Course anchor was not preserved.");
                Validate(course);
                Require(File.ReadAllBytes(sourcePath).SequenceEqual(sourceBytes), "Regeneration changed source scene bytes.");
                Debug.Log("SAVANNAH_SCENE_COPY_OK | source unchanged | student component preserved | regeneration and save/reload passed");
            }
            finally
            {
                EditorSceneManager.RestoreSceneManagerSetup(setup);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        static void ValidateInjection()
        {
            var setup = EditorSceneManager.GetSceneManagerSetup();
            string folder = "Assets/StudentWork/SavannahInjectionValidation-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            try
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var student = new GameObject("Student custom setup");
                student.transform.position = new Vector3(2,1,4);
                student.AddComponent<Camera>().fieldOfView = 37;
                student.AddComponent<Light>().intensity = .37f;
                // Build a fully initialized pipeline fixture before comparing serialized state.
                // URP otherwise adds these components during the first asset refresh.
                if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
                    && UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.GetType().Name.Contains("Universal"))
                    foreach (string name in new[] { "UniversalAdditionalCameraData", "UniversalAdditionalLightData" })
                    {
                        var type = AppDomain.CurrentDomain.GetAssemblies()
                            .Select(a => a.GetType("UnityEngine.Rendering.Universal." + name)).FirstOrDefault(t => t != null);
                        if (type != null) student.AddComponent(type);
                    }
                string path = folder + "/Student.unity";
                Require(EditorSceneManager.SaveScene(scene,path), "Could not save injection test scene.");
                string[] before = student.GetComponents<Component>().Select(c => EditorJsonUtility.ToJson(c)).ToArray();
                byte[] originalBytes = File.ReadAllBytes(path);
                Selection.activeGameObject = student;
                SavannahCourseGenerator.InjectIntoOpenScene();
                Require(SceneManager.GetActiveScene().path == path, "Injection switched scene files.");
                Require(File.ReadAllBytes(SavannahCourseGenerator.LastBackupPath).SequenceEqual(originalBytes), "Backup differs from original scene.");
                var after = student.GetComponents<Component>().Select(c => EditorJsonUtility.ToJson(c)).ToArray();
                Require(after.SequenceEqual(before), "Injection changed existing objects or their serialized settings.");
                string firstBackup = SavannahCourseGenerator.LastBackupPath;
                SavannahCourseGenerator.InjectIntoOpenScene();
                Require(SavannahCourseGenerator.LastBackupPath != firstBackup, "Repeated injection overwrote a backup.");
                Require(scene.GetRootGameObjects().Length == 2, "Repeated injection duplicated the course.");
                Require(student.GetComponents<Component>().Select(c => EditorJsonUtility.ToJson(c)).SequenceEqual(before), "Reinjection changed student settings.");
                EditorSceneManager.OpenScene(path);
                var reloaded = SceneManager.GetActiveScene().GetRootGameObjects().Single(g => g.name == "Student custom setup");
                Require(reloaded.GetComponent<Camera>().fieldOfView == 37 && reloaded.GetComponent<Light>().intensity == .37f,
                    "Custom settings were not preserved after save/reload.");
                Require(reloaded.transform.position == new Vector3(2,1,4), "Student transform changed after reload.");
                var injected = SceneManager.GetActiveScene().GetRootGameObjects().Single(g => g.name == SavannahCourseGenerator.RootName);
                injected.AddComponent<Light>().intensity = .49f; // A student's added component must not be discarded.
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
                var protectedBytes = File.ReadAllBytes(path);
                bool refused = false;
                try { SavannahCourseGenerator.InjectIntoOpenScene(); }
                catch (InvalidOperationException exception) { refused = exception.Message.Contains("custom components"); }
                Require(refused && injected && injected.GetComponent<Light>().intensity == .49f
                    && File.ReadAllBytes(path).SequenceEqual(protectedBytes), "Reinjection discarded custom components.");
                Debug.Log("SAVANNAH_INJECTION_OK | same scene path | unchanged student settings | exact backup | repeat/reload passed | custom course components protected");
            }
            finally
            {
                EditorSceneManager.RestoreSceneManagerSetup(setup);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        static void Validate(GameObject root)
        {
            Require(root.transform.Find("Acacia trees (18)").childCount == 18, "Tree count changed.");
            var landmarks = root.transform.Find("Landmarks (4, decorative)");
            Require(landmarks.childCount == 4, "Landmark count changed.");
            foreach (Transform item in landmarks)
                Require(new Vector2(item.localPosition.x,item.localPosition.z).magnitude < SavannahCourseGenerator.RequiredTravelRadius, "Landmark exceeds course travel radius.");
            foreach (var point in SavannahCourseGenerator.Route)
                Require(point.magnitude + 2.1f < SavannahCourseGenerator.RequiredTravelRadius, "Route exceeds course travel radius.");
            foreach (var component in root.GetComponentsInChildren<Component>(true))
                Require(component is Transform || component is MeshFilter || component is MeshRenderer,
                    "Generated unexpected component: " + component.GetType().Name);
            var filters = root.GetComponentsInChildren<MeshFilter>();
            Require(filters.Select(f => f.sharedMesh).Distinct().Count() == 4, "Meshes are not shared.");
            Require(filters.Sum(f => f.sharedMesh.triangles.Length / 3) < 7000, "Triangle budget exceeded.");
            var renderers = root.GetComponentsInChildren<MeshRenderer>();
            Require(renderers.Length < 150, "Renderer budget exceeded.");
            Require(renderers.Select(r => r.sharedMaterial).Distinct().Count() == 9, "Material budget changed.");
            foreach (var renderer in renderers)
                Require(renderer.sharedMaterial && renderer.sharedMaterial.shader && renderer.sharedMaterial.shader.isSupported,
                    "Missing or unsupported material shader.");
            foreach (var mesh in filters.Select(f => f.sharedMesh).Distinct())
            {
                Require(AssetDatabase.Contains(mesh), "Mesh was not saved as an asset.");
                Require(mesh.vertices.All(v => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z)), "Invalid vertex.");
                Require(mesh.triangles.All(i => i >= 0 && i < mesh.vertexCount), "Invalid triangle index.");
                Require(mesh.normals.Length == mesh.vertexCount, "Mesh normals missing.");
            }
            float length = 0;
            for (int i = 1; i < SavannahCourseGenerator.Route.Length; i++)
                length += Vector3.Distance(SavannahCourseGenerator.Route[i-1],SavannahCourseGenerator.Route[i]);
            Require(Mathf.Abs(length-SavannahCourseGenerator.CourseLength) < .001f && length > 100 && length < 30*SavannahCourseGenerator.ReferenceElephantLength,
                "Course should remain long but be shortened to fit the original square.");
            foreach (var filter in filters)
                foreach (var vertex in filter.sharedMesh.vertices)
                {
                    Vector3 local = root.transform.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                    Require(Mathf.Abs(local.x) <= SavannahCourseGenerator.StageHalfSize+.001f && Mathf.Abs(local.z) <= SavannahCourseGenerator.StageHalfSize+.001f,
                        "Generated geometry extends outside the original 60 x 60 square: " + filter.name);
                }
            var road = root.transform.Find("Open path").GetComponent<MeshFilter>().sharedMesh;
            Require(road.normals.All(n => n.y > .99f), "Path faces point downwards.");
            Require(road.triangles.Length == (SavannahCourseGenerator.Route.Length - 1) * 6,
                "Path must contain only consecutive segments with no closing segment.");
            Require(root.transform.Find("Route markers").childCount == SavannahCourseGenerator.Route.Length - 1,
                "Finish must not point back to the start.");
            Require(landmarks.Find("04 Finish marker").localPosition == SavannahCourseGenerator.Route.Last(),
                "Finish marker must stand at the open end of the course.");
        }

        static string Signature(GameObject root)
        {
            var result = new StringBuilder();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                result.Append(t.name).Append('|').Append(EditorJsonUtility.ToJson(t));
                var mesh = t.GetComponent<MeshFilter>();
                var renderer = t.GetComponent<MeshRenderer>();
                // Json includes object IDs, so compare only local geometry and asset references instead.
                if (mesh) result.Append(AssetDatabase.GetAssetPath(mesh.sharedMesh));
                if (renderer) result.Append(AssetDatabase.GetAssetPath(renderer.sharedMaterial));
            }
            return System.Text.RegularExpressions.Regex.Replace(result.ToString(), "\"instanceID\":-?[0-9]+", "\"instanceID\":0");
        }
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Savannah validation: " + message);
        }
    }
}
