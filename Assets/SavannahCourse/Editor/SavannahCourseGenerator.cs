using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace StudentStarter.Savannah
{
    // Editor only. Generated scenes contain ordinary meshes, with no runtime dependency.
    public static class SavannahCourseGenerator
    {
        public const string RootName = "Savannah Course [layout 4]";
        static bool IsCourseRoot(GameObject g) => g.name == RootName || g.name == "Savannah Course [layout 1]" || g.name == "Savannah Course [layout 2]" || g.name == "Savannah Course [layout 3]";
        public const string AssetFolder = "Assets/SavannahCourse/Generated/v4";
        public const int Seed = 20261005;
        // Measured from the baked reference elephant mesh, projected onto its travel direction.
        // Keep fixed across student projects: resizing an elephant must not change the shared layout.
        public const float ReferenceElephantLength = 5.070743f;
        public const float StageHalfSize = 30;
        public const float RequiredTravelRadius = 30;
        internal static readonly Vector3[] Route = {
            new Vector3(0,0,0), new Vector3(0,0,6), new Vector3(-14,0,6),
            new Vector3(-19,0,1), new Vector3(-19,0,-12), new Vector3(-14,0,-17),
            new Vector3(14,0,-17), new Vector3(19,0,-12), new Vector3(19,0,13),
            new Vector3(14,0,18), new Vector3(-16,0,18)
        };
        public static readonly float CourseLength = Enumerable.Range(1,Route.Length-1)
            .Sum(i => Vector3.Distance(Route[i-1],Route[i]));
        static readonly Vector2[] Trees = {
            new Vector2(-26,-24), new Vector2(-26,-10), new Vector2(-26,6), new Vector2(-26,24),
            new Vector2(-14,-26), new Vector2(0,-26), new Vector2(14,-26), new Vector2(26,-24),
            new Vector2(26,-8), new Vector2(26,8), new Vector2(26,24), new Vector2(12,26),
            new Vector2(-4,26), new Vector2(-20,26), new Vector2(-10,-5), new Vector2(2,-8),
            new Vector2(9,6), new Vector2(-8,12)
        };

        static Vector3 PointAlongRoute(float fraction, float sideOffset = 0)
        {
            float distance = Mathf.Clamp01(fraction) * CourseLength;
            for (int i = 1; i < Route.Length; i++)
            {
                float segment = Vector3.Distance(Route[i-1],Route[i]);
                if (distance <= segment) return Vector3.Lerp(Route[i-1],Route[i],distance/segment)
                    + Vector3.Cross(Vector3.up,(Route[i]-Route[i-1]).normalized) * sideOffset;
                distance -= segment;
            }
            return Route[Route.Length-1];
        }

        public static string LastBackupPath { get; private set; }

        [MenuItem("DigiPhant/Savannah/Inject Course into Open Scene")]
        public static void InjectIntoOpenScene()
        {
            Generate(false);
        }

        [MenuItem("DigiPhant/Savannah/Generate Course in Scene Copy")]
        public static void GenerateSceneCopy()
        {
            Generate(true);
        }

        [MenuItem("DigiPhant/Savannah/Regenerate Course in Open Scene")]
        public static void RegenerateOpenCourse()
        {
            if (!SceneManager.GetActiveScene().GetRootGameObjects().Any(IsCourseRoot))
                throw new InvalidOperationException("This scene has no generated course. Use Inject Course into Open Scene first.");
            Generate(false);
        }

        static void Generate(bool makeCopy)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play before generating the course.");
            if (SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Open just the elephant scene before generating.");
            var source = SceneManager.GetActiveScene();
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Exit Prefab Mode and open your elephant scene before injecting.");
            if (string.IsNullOrEmpty(source.path) || !source.path.EndsWith(".unity",StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Save and open a regular .unity elephant scene before generating.");
            if (source.isDirty && (Application.isBatchMode || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()))
                throw new InvalidOperationException("Save scene changes before generating.");
            if (source.isDirty) throw new InvalidOperationException("Generation cancelled: scene changes have not been saved.");
            var existing = source.GetRootGameObjects().SingleOrDefault(IsCourseRoot);
            if (existing && (!existing.transform.Find("Acacia trees (18)")
                || !existing.transform.Find("Landmarks (4, decorative)")
                || (!existing.transform.Find("Open path") && !existing.transform.Find("Wide loop path"))))
                throw new InvalidOperationException("An unrelated object uses the reserved course name. Rename that object before injecting; it was not changed.");
            if (existing && existing.GetComponentsInChildren<Component>(true)
                .Any(c => !c || !(c is Transform || c is MeshFilter || c is MeshRenderer)))
                throw new InvalidOperationException("The existing course contains custom components. Move student controls/objects outside the generated course root before reinjecting; nothing was changed.");
            var anchor = existing ? existing.transform : FindAnchor(source);
            Vector3 position = anchor.position;
            Quaternion rotation = Quaternion.Euler(0, anchor.eulerAngles.y, 0);
            PrepareAssets(); // Check compatibility before changing the scene.
            LastBackupPath = null;
            if (!makeCopy)
            {
                LastBackupPath = AssetDatabase.GenerateUniqueAssetPath(
                    Path.ChangeExtension(source.path,null) + "_BeforeSavannah.unity");
                if (!AssetDatabase.CopyAsset(source.path,LastBackupPath))
                    throw new IOException("Could not back up the scene; injection cancelled.");
                Debug.Log("SAVANNAH_BACKUP " + LastBackupPath);
            }
            if (makeCopy)
            {
                string copy = AssetDatabase.GenerateUniqueAssetPath(
                    Path.ChangeExtension(source.path, null) + "_Savannah.unity");
                if (!EditorSceneManager.SaveScene(source, copy, true))
                    throw new IOException("Could not save the scene copy: " + copy);
                source = EditorSceneManager.OpenScene(copy, OpenSceneMode.Single);
            }
            var oldRoot = source.GetRootGameObjects().SingleOrDefault(IsCourseRoot);
            var root = BuildContents(source);
            root.transform.SetPositionAndRotation(position, rotation);
            if (oldRoot) Undo.DestroyObjectImmediate(oldRoot);
            Undo.RegisterCreatedObjectUndo(root, "Generate savannah course");
            CheckTravelBoundary(source, root.transform);
            Undo.FlushUndoRecordObjects();
            EditorSceneManager.MarkSceneDirty(source);
            if (!EditorSceneManager.SaveScene(source)) throw new IOException("Could not save the course scene.");
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = root;
            if (SceneView.lastActiveSceneView) SceneView.lastActiveSceneView.Frame(new Bounds(position, new Vector3(64,8,64)), false);
            if (!makeCopy) Debug.Log("SAVANNAH_INJECTED " + source.path + " | existing controls and settings preserved");
            Debug.Log("SAVANNAH_GENERATED " + source.path + " | layout 4 | seed " + Seed
                + " | " + CourseLength.ToString("F2") + " units (" + (CourseLength/ReferenceElephantLength).ToString("F2") + " elephant lengths), within 60 x 60 stage | 18 trees | 4 decorative landmarks | no generated colliders or runtime scripts");
        }

        // Batch entry points require an explicit scene path; injection is the default student workflow.
        public static void InjectFromCommandLine()
        {
            OpenCommandLineScene();
            InjectIntoOpenScene();
            SavannahCourseValidation.RunAll();
        }

        public static void GenerateFromCommandLine()
        {
            OpenCommandLineScene();
            GenerateSceneCopy();
            SavannahCourseValidation.RunAll();
        }

        static void OpenCommandLineScene()
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-savannahSourceScene");
            if (index < 0 || index + 1 == args.Length || !args[index + 1].StartsWith("Assets/", StringComparison.Ordinal)
                || !args[index + 1].EndsWith(".unity", StringComparison.Ordinal))
                throw new ArgumentException("Supply -savannahSourceScene Assets/path/to/ElephantScene.unity");
            EditorSceneManager.OpenScene(args[index + 1], OpenSceneMode.Single);
        }

        static Transform FindAnchor(Scene scene)
        {
            var locomotion = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MonoBehaviour>(true))
                .Where(c => c && c.GetType().FullName == "DigiPhant.DigiPhantLocomotion").ToArray();
            if (locomotion.Length == 1)
            {
                var serialized = new SerializedObject(locomotion[0]);
                var travel = serialized.FindProperty("travelRoot")?.objectReferenceValue as Transform;
                if (travel)
                {
                    return travel;
                }
            }
            if (Selection.activeTransform && Selection.activeTransform.gameObject.scene == scene)
                return Selection.activeTransform;
            throw new InvalidOperationException("Could not identify one DigiPhant travel root. Select your elephant's movement root in the Hierarchy, then run the menu command.");
        }

        static void CheckTravelBoundary(Scene scene, Transform course)
        {
            var candidates = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MonoBehaviour>(true))
                .Where(c => c && c.GetType().FullName == "DigiPhant.DigiPhantLocomotion").ToArray();
            if (candidates.Length != 1)
            {
                Debug.LogWarning("Course length is " + CourseLength + " units. Check whether your custom movement boundary reaches the finish; it was not changed.");
                return;
            }
            var settings = new SerializedObject(candidates[0]);
            var radius = settings.FindProperty("stageRadius");
            var travel = settings.FindProperty("travelRoot")?.objectReferenceValue as Transform;
            if (radius == null || !travel) return;
            float needed = RequiredTravelRadius + Vector3.Distance(travel.position,course.position);
            if (radius.floatValue > 0 && radius.floatValue < needed)
                Debug.LogWarning("SAVANNAH_BOUNDARY_CHECK: existing travel radius is " + radius.floatValue
                    + "; reaching the full course may need " + needed
                    + ". Settings were preserved. Review this separately with the student.");
        }

        static Mesh cylinder, canopy, box, road;
        static Material sand, path, bark, leaves, lightLeaves, stone, water, cream, orange;

        internal static void PrepareAssets()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            string shaderName = pipeline == null ? "Standard" :
                pipeline.GetType().Name.Contains("Universal") ? "Universal Render Pipeline/Lit" : null;
            if (shaderName == null)
                throw new NotSupportedException("Savannah supports URP and the Built-in pipeline. Adapt its materials for your pipeline before generating.");
            var shader = Shader.Find(shaderName);
            if (!shader) throw new InvalidOperationException("Required shader is unavailable: " + shaderName);
            Directory.CreateDirectory(AssetFolder);
            AssetDatabase.Refresh();
            cylinder = MeshAsset("Octagonal cylinder", () => Rings(new[] { -.5f, .5f }, new[] { .5f, .5f }, 8));
            canopy = MeshAsset("Acacia canopy", () => Rings(new[] { -.5f, -.05f, .32f, .5f }, new[] { .22f, .5f, .38f, .08f }, 8));
            box = MeshAsset("Four-sided post", () => Rings(new[] { -.5f, .5f }, new[] { .7071068f, .7071068f }, 4, 45));
            road = MeshAsset("Open path", MakeRoad);
            sand = MaterialAsset("Ochre ground", new Color(.65f,.48f,.24f), shader);
            path = MaterialAsset("Pale sand path", new Color(.90f,.74f,.43f), shader);
            bark = MaterialAsset("Bark", new Color(.29f,.19f,.10f), shader);
            leaves = MaterialAsset("Olive foliage", new Color(.33f,.40f,.12f), shader);
            lightLeaves = MaterialAsset("Sage foliage", new Color(.49f,.52f,.20f), shader);
            stone = MaterialAsset("Basin stone", new Color(.44f,.40f,.30f), shader);
            water = MaterialAsset("Still blue water", new Color(.20f,.52f,.60f), shader);
            cream = MaterialAsset("Ivory markers", new Color(.96f,.89f,.65f), shader);
            orange = MaterialAsset("Terracotta markers", new Color(.73f,.28f,.12f), shader);
        }

        static Mesh MeshAsset(string name, Func<Mesh> build)
        {
            string path = AssetFolder + "/" + name + ".asset";
            var result = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            var expected = build();
            if (result)
            {
                bool matches = result.vertices.SequenceEqual(expected.vertices)
                    && result.triangles.SequenceEqual(expected.triangles)
                    && result.normals.SequenceEqual(expected.normals);
                Object.DestroyImmediate(expected);
                if (!matches) throw new InvalidOperationException("Generated mesh was modified: " + path
                    + ". Restore the standard asset from version control or generate in a fresh project.");
                return result;
            }
            result = expected; result.name = name;
            AssetDatabase.CreateAsset(result, path);
            return result;
        }

        static Material MaterialAsset(string name, Color color, Shader shader)
        {
            string path = AssetFolder + "/" + name + ".mat";
            var result = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (result && result.shader != shader)
                throw new InvalidOperationException("Generated material pipeline differs: " + path + ". Reconcile materials before regenerating.");
            if (result)
            {
                string colorProperty = result.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
                string smoothProperty = result.HasProperty("_Smoothness") ? "_Smoothness" : "_Glossiness";
                if (((Vector4)(result.GetColor(colorProperty) - color)).sqrMagnitude > .00000001f
                    || !result.enableInstancing || (result.HasProperty(smoothProperty) && result.GetFloat(smoothProperty) != 0))
                    throw new InvalidOperationException("Generated material was modified: " + path
                        + ". Restore the standard asset from version control or generate in a fresh project.");
                return result;
            }
            result = new Material(shader) { name = name, enableInstancing = true };
            result.SetColor(result.HasProperty("_BaseColor") ? "_BaseColor" : "_Color", color);
            if (result.HasProperty("_Smoothness")) result.SetFloat("_Smoothness", 0);
            if (result.HasProperty("_Glossiness")) result.SetFloat("_Glossiness", 0);
            AssetDatabase.CreateAsset(result, path);
            return result;
        }

        internal static GameObject BuildContents(Scene scene)
        {
            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            var ground = Part(root.transform, "Ground", box, sand, new Vector3(0,-.01f,0), new Vector3(StageHalfSize*2,.06f,StageHalfSize*2));
            ground.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            Part(root.transform, "Open path", road, path, new Vector3(0,.025f,0), Vector3.one);
            Part(root.transform, "Start line", box, cream, new Vector3(0,.045f,0), new Vector3(4.2f,.015f,.20f));
            var markers = Group(root.transform, "Route markers");
            for (int i = 0; i < Route.Length - 1; i++)
            {
                Vector3 direction = (Route[i + 1] - Route[i]).normalized;
                var arrow = Group(markers, "Direction " + (i + 1));
                arrow.localPosition = Route[i] + direction * 1.5f + Vector3.up * .045f;
                arrow.localRotation = Quaternion.LookRotation(direction);
                for (int side = -1; side <= 1; side += 2)
                {
                    var bar = Part(arrow, "Chevron", box, cream, new Vector3(side * .22f,0,0), new Vector3(.10f,.015f,.65f));
                    bar.transform.localRotation = Quaternion.Euler(0, side * -45, 0);
                }
            }
            var grove = Group(root.transform, "Acacia trees (18)");
            // Local PRNG with fixed arithmetic: independent of Unity/global/System.Random versions.
            uint random = Seed;
            float Next() { random = unchecked(random * 1664525u + 1013904223u); return (random >> 8) / 16777216f; }
            for (int i = 0; i < 18; i++)
            {
                var tree = Group(grove, "Acacia " + (i + 1).ToString("00"));
                tree.localPosition = new Vector3(Trees[i].x,0,Trees[i].y);
                tree.localRotation = Quaternion.Euler(0, Next() * 360, 0);
                float height = 3.4f + Next() * 1.2f, width = 3.8f + Next() * 1.3f;
                Part(tree, "Trunk", cylinder, bark, new Vector3(0,height*.45f,0), new Vector3(.36f,height*.9f,.36f));
                for (int side = -1; side <= 1; side += 2)
                {
                    var branch = Part(tree, "Branch", cylinder, bark, new Vector3(side*.43f,height*.76f,0), new Vector3(.19f,height*.42f,.19f));
                    branch.transform.localRotation = Quaternion.Euler(0,0,side * -40);
                    Part(tree, "Flat canopy", canopy, (i % 2 == 0) ? leaves : lightLeaves,
                        new Vector3(side*.66f,height,0), new Vector3(width,.85f,width*.76f));
                }
            }
            var landmarks = Group(root.transform, "Landmarks (4, decorative)");
            var bush = Group(landmarks, "01 Feeding bush"); bush.localPosition = PointAlongRoute(.23f,-4);
            Part(bush,"Foliage",canopy,leaves,new Vector3(0,.8f,0),new Vector3(2.2f,1.6f,2));
            Part(bush,"Foliage crown",canopy,lightLeaves,new Vector3(.5f,1.25f,.1f),new Vector3(1.3f,1,1.4f));
            var basin = Group(landmarks,"02 Water basin"); basin.localPosition = PointAlongRoute(.49f,4);
            Part(basin,"Stone bowl",cylinder,stone,new Vector3(0,.23f,0),new Vector3(3,.46f,2.5f));
            Part(basin,"Still water",cylinder,water,new Vector3(0,.47f,0),new Vector3(2.55f,.025f,2.05f));
            var log = Group(landmarks,"03 Fallen log"); log.localPosition = PointAlongRoute(.74f,-4);
            log.localRotation = Quaternion.Euler(0,-25,0);
            var timber = Part(log,"Timber",cylinder,bark,new Vector3(0,.40f,0),new Vector3(.8f,3,.8f));
            timber.transform.localRotation = Quaternion.Euler(0,0,90);
            for (int side = -1; side <= 1; side += 2)
            {
                var end = Part(log,"Cut end",cylinder,path,new Vector3(side*1.51f,.40f,0),new Vector3(.64f,.025f,.64f));
                end.transform.localRotation = Quaternion.Euler(0,0,90);
            }
            var finish = Group(landmarks,"04 Finish marker"); finish.localPosition = Route[Route.Length - 1];
            finish.localRotation = Quaternion.LookRotation(Route[Route.Length - 1] - Route[Route.Length - 2]);
            for (int side = -1; side <= 1; side += 2)
                Part(finish,"Post",box,bark,new Vector3(side*2.8f,1.8f,0),new Vector3(.2f,3.6f,.2f));
            Part(finish,"Finish beam",box,orange,new Vector3(0,3.65f,0),new Vector3(5.8f,.55f,.25f));
            for (int i = 0; i < 7; i++)
                Part(finish,"Ivory stripe",box,cream,new Vector3(-2.4f+i*.8f,3.65f,-.14f),new Vector3(.35f,.4f,.04f));
            return root;
        }

        static Transform Group(Transform parent, string name)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); return go.transform;
        }
        static GameObject Part(Transform parent, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale)
        {
            var t = Group(parent,name); t.localPosition = position; t.localScale = scale;
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            t.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
            return t.gameObject;
        }

        // Flat shaded low-poly meshes; duplicated vertices keep each face crisp.
        static Mesh Rings(float[] heights, float[] radii, int sides, float offset = 0)
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            Vector3 Point(int ring, int side) {
                float a = (side * 360f / sides + offset) * Mathf.Deg2Rad;
                return new Vector3(Mathf.Cos(a)*radii[ring],heights[ring],Mathf.Sin(a)*radii[ring]);
            }
            void Tri(Vector3 a, Vector3 b, Vector3 c) {
                int n = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c);
                triangles.Add(n); triangles.Add(n+1); triangles.Add(n+2);
            }
            for (int side = 0; side < sides; side++)
            {
                for (int ring = 0; ring < heights.Length-1; ring++)
                {
                    Tri(Point(ring,side),Point(ring+1,side),Point(ring+1,side+1));
                    Tri(Point(ring,side),Point(ring+1,side+1),Point(ring,side+1));
                }
                Tri(new Vector3(0,heights[0],0),Point(0,side),Point(0,side+1));
                int top = heights.Length-1;
                Tri(new Vector3(0,heights[top],0),Point(top,side+1),Point(top,side));
            }
            var mesh = new Mesh(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles,0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        static Mesh MakeRoad()
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            for (int i = 0; i < Route.Length; i++)
            {
                Vector3 incoming = i == 0 ? (Route[1]-Route[0]).normalized : (Route[i]-Route[i-1]).normalized;
                Vector3 outgoing = i == Route.Length-1 ? incoming : (Route[i+1]-Route[i]).normalized;
                Vector3 normal = Vector3.Cross(Vector3.up, outgoing);
                Vector3 miter = (Vector3.Cross(Vector3.up,incoming)+normal).normalized;
                Vector3 offset = miter * (2.1f / Vector3.Dot(miter,normal));
                vertices.Add(Route[i]-offset); vertices.Add(Route[i]+offset);
                if (i == Route.Length - 1) continue; // Open end: no segment back to the start.
                int a=i*2, b=(i+1)*2;
                triangles.Add(a); triangles.Add(b); triangles.Add(a+1);
                triangles.Add(a+1); triangles.Add(b); triangles.Add(b+1);
            }
            var mesh = new Mesh(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles,0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
    }
}
