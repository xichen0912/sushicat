using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DigiPhant;
using StudentStarter.Savannah;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StudentWork.Editor
{
    // Explicit setup creates student-owned scene copies. Instructor and earlier student scenes are preserved.
    public static class UpperBodyCourseSceneSetup
    {
        public const string CourseScene = "Assets/StudentWork/Scenes/UpperBodyThreePersonCourse.unity";
        public const string P1Scene = "Assets/StudentWork/Scenes/P1UpperBodyPractice.unity";
        public const string P2Scene = "Assets/StudentWork/Scenes/P2UpperBodyPractice.unity";
        public const string P3Scene = "Assets/StudentWork/Scenes/P3UpperBodyPractice.unity";
        public const string InteractionRoot = "Upper Body Course Interactions";

        [MenuItem("Student Work/Prepare Upper Body Course and Solo Scenes")]
        public static void Prepare() { RequireCleanEditor(); PrepareScenes(); }

        public static void PrepareBatch()
        {
            RequireNotPlaying();
            if (!Application.isBatchMode) RequireCleanEditor();
            PrepareScenes();
        }

        public static void PrepareAndValidateBatch()
        {
            PrepareBatch();
            UpperBodyCourseValidation.Run();
        }

        // Upgrade only student additions/settings in saved scenes; the supplied course
        // hierarchy and every landmark transform remain untouched.
        public static void UpgradeSafetyBatch()
        {
            RequireNotPlaying();
            if (!Application.isBatchMode) RequireCleanEditor();
            foreach (string path in new[] { CourseScene, P1Scene, P2Scene, P3Scene })
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var controller = Controller(scene);
                var locomotion = controller.GetComponent<DigiPhantLocomotion>();
                var manager = Components<ThreePersonCourseManager>(scene).Single();
                var course = scene.GetRootGameObjects().Single(value => value.name == SavannahCourseGenerator.RootName).transform;
                manager.enabled = true;
                manager.enableTrunkLogPickup = true;
                manager.treeInteraction.avatarHeading = locomotion.travelRoot;
                manager.treeInteraction.minimumHeadingChangeDegrees = 12;
                manager.treeInteraction.trackingPauseToleranceSeconds = .45f;
                manager.treeInteraction.maximumTotalPauseSeconds = 1.2f;
                manager.logInteraction.logTransform = course.Find("Landmarks (4, decorative)/03 Fallen log");
                manager.logInteraction.corridorHalfWidth = 1.4f;
                manager.logInteraction.trackingPauseToleranceSeconds = .4f;
                manager.logInteraction.maximumTotalPauseSeconds = 1.2f;
                manager.waterBasin = course.Find("Landmarks (4, decorative)/02 Water basin");
                manager.maximumWaterBasinDistance = 2.8f;
                controller.neutralSampleSeconds = 1.5f;
                controller.neutralStabilityTolerance = .15f;
                controller.regionBoundaries = new[] { 0f, 1f / 3, 2f / 3, 1f };
                controller.regionPadding = .1f;
                controller.maximumPoseAgeSeconds = .45f;
                controller.standingScaleTolerance = .18f;
                controller.stableHandSamples = 3;
                controller.handJumpTolerance = .35f;
                controller.maximumHandSpeed = 3.5f;
                locomotion.runHysteresis = .06f;
                locomotion.forward.sensitivity = .55f;
                locomotion.reverseEntryThreshold = .18f;
                locomotion.reverseHoldSeconds = .25f;
                locomotion.reverseMinimumSamples = 3;
                locomotion.reverseExitThreshold = .08f;
                var diagnostics = controller.GetComponent<ControlDiagnosticsLogger>();
                if (!diagnostics) diagnostics = controller.gameObject.AddComponent<ControlDiagnosticsLogger>();
                diagnostics.controller = controller;
                diagnostics.locomotion = locomotion;
                EditorUtility.SetDirty(diagnostics);
                foreach (var component in new UnityEngine.Object[] { manager, manager.treeInteraction,
                    manager.logInteraction, controller, locomotion, manager.waterInteraction }) EditorUtility.SetDirty(component);
                Save(scene);
            }
            var ordered = EditorBuildSettings.scenes.Where(value => value.path != CourseScene).ToList();
            ordered.Insert(0, new EditorBuildSettingsScene(CourseScene, true));
            EditorBuildSettings.scenes = ordered.ToArray();
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(CourseScene, OpenSceneMode.Single);
            Debug.Log("UPPER_BODY_SAFETY_SCENES_READY: original course preserved; tighter interaction references; active role-aware managers in solo copies; upper-body main scene first.");
        }

        [MenuItem("Student Work/Open Upper Body Three Person Course")]
        public static void OpenCourse()
        {
            RequireCleanEditor();
            if (!File.Exists(CourseScene)) throw new FileNotFoundException("Prepare the upper-body scenes first.", CourseScene);
            EditorSceneManager.OpenScene(CourseScene, OpenSceneMode.Single);
        }

        [MenuItem("Student Work/Prepare Upper Body Course and Solo Scenes", true)]
        [MenuItem("Student Work/Open Upper Body Three Person Course", true)]
        static bool CanPrepare() => !EditorApplication.isPlayingOrWillChangePlaymode;

        static void PrepareScenes()
        {
            if (!File.Exists(TeamSceneSetup.TeamScene))
                throw new FileNotFoundException("The saved TeamPerformance scene is required.", TeamSceneSetup.TeamScene);
            bool created = !File.Exists(CourseScene);
            if (created)
            {
                if (!AssetDatabase.CopyAsset(TeamSceneSetup.TeamScene, CourseScene))
                    throw new IOException("Could not create the upper-body course copy. Existing scenes were preserved.");
                var scene = EditorSceneManager.OpenScene(CourseScene, OpenSceneMode.Single);
                ConfigureCourse(scene);
                Save(scene);
            }
            else EditorSceneManager.OpenScene(CourseScene, OpenSceneMode.Single);

            // Re-running setup keeps saved edits instead of replacing any existing practice scene.
            CreatePractice(P1Scene, 1);
            CreatePractice(P2Scene, 2);
            CreatePractice(P3Scene, 3);
            var buildScenes = EditorBuildSettings.scenes.ToList();
            foreach (string path in new[] { CourseScene, P1Scene, P2Scene, P3Scene })
                if (!buildScenes.Any(value => value.path == path)) buildScenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = buildScenes.ToArray();
            AssetDatabase.SaveAssets();
            var course = EditorSceneManager.OpenScene(CourseScene, OpenSceneMode.Single);
            Selection.activeGameObject = Controller(course).gameObject;
            Debug.Log("UPPER_BODY_COURSE_SCENES_READY: " + (created ? "new student course and role copies prepared" : "existing saved course and practice edits preserved")
                + "; P1 left hand drives speed and all four-leg gait; P2 shoulder tilt drives turn; P3 head yaw and right hand drive head/trunk"
                + "; original Tree > Water > Log > Finish route and geometry preserved; no camera or Play mode started.");
        }

        static void ConfigureCourse(Scene scene)
        {
            var controller = Controller(scene);
            var locomotion = controller.GetComponent<DigiPhantLocomotion>();
            if (!locomotion || !locomotion.travelRoot)
                throw new InvalidOperationException("The copied scene is missing the existing travel rig.");
            controller.SetUpperBodyTeamMode(true);
            controller.SetSoloRolePractice(0);
            controller.inputMode = InputMode.Camera;
            controller.showCameraPreview = true;
            locomotion.stageRadius = 0;
            locomotion.enableLocomotion = true;
            foreach (var leg in controller.controls.Where(value => value.label.Contains("leg")))
            {
                leg.performer = 1;
                leg.movement = Movement.LeftHandHeight;
                leg.locomotionWeight = 0;
            }
            var course = scene.GetRootGameObjects().Single(value => value.name == SavannahCourseGenerator.RootName).transform;
            var route = ReadRoute(course);
            var landmarks = course.Find("Landmarks (4, decorative)");
            if (!landmarks) throw new InvalidOperationException("The copied Savannah landmarks are missing.");
            var log = landmarks.Find("03 Fallen log");
            var basin = landmarks.Find("02 Water basin");
            var finish = landmarks.Find("04 Finish marker");
            if (!log || !basin || !finish) throw new InvalidOperationException("Expected the saved log, basin and finish marker.");
            PointOnRoute(route, .74f, out _, out Vector3 logDirection);
            PointOnRoute(route, .49f, out _, out Vector3 waterDirection);
            Vector3 logPoint = course.InverseTransformPoint(log.position);
            Vector3 waterPoint = course.InverseTransformPoint(basin.position);
            var root = new GameObject(InteractionRoot);
            SceneManager.MoveGameObjectToScene(root, scene);
            // All additions stay outside the supplied generated course hierarchy.
            root.transform.SetPositionAndRotation(course.position, course.rotation);
            root.transform.localScale = course.lossyScale;
            var tree = course.Find("Acacia trees (18)/Acacia 18");
            if (!tree) throw new InvalidOperationException("The saved acacia tree near the first turn is missing.");
            ClosestRoutePoint(route, course.InverseTransformPoint(tree.position), out Vector3 treePoint, out Vector3 treeDirection);
            var trunk = tree.Find("Trunk");
            var treeZone = Zone(root.transform, "Tree Zone", treePoint, treeDirection, new Vector3(14, 6, 10));
            var logZone = Zone(root.transform, "Log Zone", logPoint, logDirection, new Vector3(8, 6, 6));
            var waterZone = Zone(root.transform, "Water Zone", waterPoint, waterDirection, new Vector3(10, 6, 8));
            var finalDirection = (route[route.Length - 1] - route[route.Length - 2]).normalized;
            var finishZone = Zone(root.transform, "Finish Zone", route[route.Length - 1], finalDirection, new Vector3(7, 6, 5));
            var treeInteraction = treeZone.gameObject.AddComponent<TreeInteraction>();
            treeInteraction.Configure(treeZone, trunk);
            var logInteraction = logZone.gameObject.AddComponent<LogInteraction>();
            logInteraction.Configure(logZone, locomotion.walkSpeed);
            var water = Components<WaterCupInteraction>(scene).SingleOrDefault();
            if (!water) water = controller.gameObject.AddComponent<WaterCupInteraction>();
            water.Configure(controller, locomotion);
            water.sessionLabel = "P3 upper body course fist water interaction";
            var manager = root.AddComponent<ThreePersonCourseManager>();
            manager.Configure(controller, locomotion, treeInteraction, logInteraction, water, waterZone, finishZone);
            manager.enableTrunkLogPickup = true;
            AddMarker(root.transform, "01 TREE: steer around trunk", treePoint, treeDirection, new Color(.96f, .70f, .20f));
            AddMarker(root.transform, "02 WATER: stop and clench fist", waterPoint, waterDirection, new Color(.25f, .72f, .95f));
            AddMarker(root.transform, "03 LOG: pick up with trunk", logPoint, logDirection, new Color(.97f, .42f, .20f));
            AddMarker(root.transform, "04 FINISH", route[route.Length - 1], finalDirection, new Color(.55f, .88f, .40f));
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(locomotion);
            EditorUtility.SetDirty(water);
            EditorUtility.SetDirty(manager);
            var logger = Components<SimulationTelemetryLogger>(scene).SingleOrDefault();
            if (logger)
            {
                logger.landmarkZones = new[] {
                    new LandmarkZone { label = "Tree", centre = treeZone.transform, radiusUnits = 6 },
                    new LandmarkZone { label = "Water", centre = waterZone.transform, radiusUnits = 5 },
                    new LandmarkZone { label = "Log", centre = logZone.transform, radiusUnits = 4.5f },
                    new LandmarkZone { label = "Finish", centre = finishZone.transform, radiusUnits = 4 }
                };
                EditorUtility.SetDirty(logger);
            }
        }

        static void CreatePractice(string path, int role)
        {
            if (File.Exists(path)) return;
            if (!AssetDatabase.CopyAsset(CourseScene, path)) throw new IOException("Could not create practice scene " + path);
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var controller = Controller(scene);
            controller.SetSoloRolePractice(role);
            controller.inputMode = InputMode.Camera;
            foreach (var manager in Components<ThreePersonCourseManager>(scene)) manager.enabled = false;
            var water = Components<WaterCupInteraction>(scene).SingleOrDefault();
            if (water)
            {
                water.enabled = role == 3;
                water.showPanel = role == 3;
                water.ClearCourseGate();
                water.sessionLabel = "P" + role + " upper body solo practice";
                EditorUtility.SetDirty(water);
            }
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(controller.GetComponent<DigiPhantLocomotion>());
            Save(scene);
        }

        static BoxCollider Zone(Transform parent, string name, Vector3 point, Vector3 forward, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = point + Vector3.up * 1.5f;
            go.transform.localRotation = Quaternion.LookRotation(forward);
            var collider = go.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = size;
            return collider;
        }

        static void AddMarker(Transform parent, string title, Vector3 point, Vector3 forward, Color color)
        {
            var go = new GameObject(title);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = point + Vector3.Cross(Vector3.up, forward) * -5.2f + Vector3.up * .25f;
            go.transform.localRotation = Quaternion.LookRotation(forward) * Quaternion.Euler(90, 0, 0);
            var label = go.AddComponent<TextMesh>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (!label.font) throw new InvalidOperationException("Built-in marker font could not be loaded.");
            label.GetComponent<MeshRenderer>().sharedMaterial = label.font.material;
            label.text = title;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.characterSize = .18f;
            label.fontSize = 48;
            label.color = color;
        }

        internal static Vector3[] ReadRoute(Transform course)
        {
            var path = course.Find("Open path");
            var filter = path ? path.GetComponent<MeshFilter>() : null;
            if (!filter || !filter.sharedMesh) throw new InvalidOperationException("The saved Open path mesh is missing.");
            var vertices = filter.sharedMesh.vertices;
            if (vertices.Length < 4 || vertices.Length % 2 != 0) throw new InvalidOperationException("Unexpected path mesh centreline format.");
            return Enumerable.Range(0, vertices.Length / 2).Select(i => course.InverseTransformPoint(
                path.TransformPoint((vertices[i * 2] + vertices[i * 2 + 1]) * .5f))).ToArray();
        }

        internal static void PointOnRoute(Vector3[] route, float fraction, out Vector3 point, out Vector3 direction)
        {
            float distance = Enumerable.Range(1, route.Length - 1).Sum(i => Vector3.Distance(route[i - 1], route[i])) * Mathf.Clamp01(fraction);
            for (int i = 1; i < route.Length; i++)
            {
                float segment = Vector3.Distance(route[i - 1], route[i]);
                if (distance <= segment || i == route.Length - 1)
                {
                    point = Vector3.Lerp(route[i - 1], route[i], Mathf.Clamp01(distance / segment));
                    direction = (route[i] - route[i - 1]).normalized;
                    return;
                }
                distance -= segment;
            }
            throw new InvalidOperationException("The saved route is empty.");
        }

        static void ClosestRoutePoint(Vector3[] route, Vector3 location, out Vector3 point, out Vector3 direction)
        {
            point = route[0];
            direction = Vector3.forward;
            float distance = float.PositiveInfinity;
            for (int i = 1; i < route.Length; i++)
            {
                Vector3 segment = route[i] - route[i - 1];
                float t = Mathf.Clamp01(Vector3.Dot(location - route[i - 1], segment) / segment.sqrMagnitude);
                Vector3 candidate = route[i - 1] + segment * t;
                float candidateDistance = (candidate - location).sqrMagnitude;
                if (candidateDistance >= distance) continue;
                distance = candidateDistance; point = candidate; direction = segment.normalized;
            }
        }

        internal static DigiPhantController Controller(Scene scene)
        {
            var controllers = Components<DigiPhantController>(scene).ToArray();
            if (controllers.Length != 1) throw new InvalidOperationException("Expected one controller in " + scene.path);
            return controllers[0];
        }

        internal static IEnumerable<T> Components<T>(Scene scene) where T : Component
            => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));

        static void Save(Scene scene)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save " + scene.path);
        }

        static void RequireNotPlaying()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Stop Play mode and exit Prefab Mode before upper-body setup.");
        }

        static void RequireCleanEditor()
        {
            RequireNotPlaying();
            var scene = SceneManager.GetActiveScene();
            if (SceneManager.sceneCount != 1 || !scene.IsValid() || scene.isDirty || string.IsNullOrEmpty(scene.path) || !File.Exists(scene.path))
                throw new InvalidOperationException("Open one saved, unmodified scene before upper-body setup.");
        }
    }
}
