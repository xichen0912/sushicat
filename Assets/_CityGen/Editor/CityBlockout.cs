using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using StudentStarter.Savannah;

namespace CityGen.EditorTools
{
    // Step 1: 1930s city blockout (white cubes) around the unchanged Savannah course.
    // Works only on the copy UpperBodyThreePersonCourse_City.unity. Re-running clears "CityGen" first;
    // a fixed seed gives the same layout every time. Original objects are never moved, deleted or
    // re-componented: hidden originals are listed on CityGenSwap and come back when CityGen is off.
    public static class CityBlockout
    {
        public const string SourceScene = "Assets/StudentWork/Scenes/UpperBodyThreePersonCourse.unity";
        public const string CityScene = "Assets/StudentWork/Scenes/UpperBodyThreePersonCourse_City.unity";
        public const string RootName = "CityGen";
        const string CourseRoot = "Savannah Course [layout 4]";
        const string Interactions = "Upper Body Course Interactions";
        internal const string MaterialFolder = "Assets/_CityGen/Materials";
        internal const string MeshFolder = "Assets/_CityGen/Generated";
        const string ReportPath = "Assets/_CityGen/overlap_report.md";

        // ---- Tunables ----
        public static int Seed = 1930;
        const float SidewalkWidth = 2f;      // added outside the unchanged 4.2 m road
        const float FacadeSetback = .5f;     // between sidewalk edge and facade
        const float GroundFloor = 4f, UpperFloor = 3.2f;   // real-world storey heights …
        // … shrunk into a miniature city so the elephant (about 3.5 m tall, 5 m long) towers over the
        // streets: a 3-storey row house is about 3 m, a 30-storey tower about 29 m.
        public static float HeightScale = .3f;
        public static float FootprintScale = .5f;
        const float PlayHalf = 30f;          // original 60 × 60 ground
        const float AvenueWidth = 6f;        // perimeter avenue just outside the original ground
        const float SkylineOuter = 60f;
        const float KeepoutMargin = 1f;
        static readonly int[] KeptTrees = { 15, 16, 17, 18 }; // 18 is gameplay (TreeInteraction); 15–17 become park trees

        internal struct Box
        {
            public float xMin, xMax, zMin, zMax;
            public Box(float x0, float x1, float z0, float z1) { xMin = Mathf.Min(x0, x1); xMax = Mathf.Max(x0, x1); zMin = Mathf.Min(z0, z1); zMax = Mathf.Max(z0, z1); }
            public static Box From(Bounds b) => new Box(b.min.x, b.max.x, b.min.z, b.max.z);
            public Box Grow(float m) => new Box(xMin - m, xMax + m, zMin - m, zMax + m);
            public bool Overlaps(Box o) => xMin < o.xMax && xMax > o.xMin && zMin < o.zMax && zMax > o.zMin;
            public float Width => xMax - xMin; public float Depth => zMax - zMin;
            public Vector3 Center => new Vector3((xMin + xMax) / 2, 0, (zMin + zMax) / 2);
            public override string ToString() => $"x {xMin:0.0}…{xMax:0.0}, z {zMin:0.0}…{zMax:0.0}";
        }

        internal sealed class Context
        {
            public Vector3[] route;
            public float roadHalf;
            public readonly List<(string name, Box box)> keepouts = new List<(string, Box)>();
            public readonly List<(string name, Box box)> buildings = new List<(string, Box)>();
            public Transform course, basin;
            public System.Random random;
        }

        // ---------------- Menu ----------------

        [MenuItem("CityGen/0 Open City Scene (copy if missing)")]
        public static void OpenCityScene()
        {
            if (!File.Exists(CityScene))
            {
                if (!AssetDatabase.CopyAsset(SourceScene, CityScene)) throw new InvalidOperationException("Could not copy " + SourceScene);
                Debug.Log("CITYGEN_SCENE_COPIED " + CityScene);
            }
            if (SceneManager.GetActiveScene().path == CityScene) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) throw new OperationCanceledException("Kept the current scene open.");
            EditorSceneManager.OpenScene(CityScene, OpenSceneMode.Single);
        }

        // Result of the blockout pass; the detail pass (step 2) builds on it before saving.
        internal sealed class Result
        {
            public Scene scene;
            public Context ctx;
            public GameObject root;
            public CityGenSwap swap;
            public readonly List<Renderer> hidden = new List<Renderer>();
            public readonly List<GameObject> attached = new List<GameObject>(); // detail parented to original objects
            public readonly List<CityGenSwap.MaterialSwap> swaps = new List<CityGenSwap.MaterialSwap>(); // e.g. road asphalt
            public readonly List<Behaviour> disabled = new List<Behaviour>();   // e.g. the original key light
        }
        public const string AttachedPrefix = "CityGen · ";

        [MenuItem("CityGen/1 Generate Blockout")]
        public static void GenerateBlockout()
        {
            var result = BuildBlockout();
            Finish(result, "CITYGEN_BLOCKOUT_DONE");
        }

        internal static Result BuildBlockout()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before generating.");
            var settings = CityGenSettings.Load();
            Seed = settings.seed; HeightScale = settings.heightScale; FootprintScale = settings.footprintScale;
            OpenCityScene();
            var scene = SceneManager.GetActiveScene();
            if (scene.path != CityScene) throw new InvalidOperationException("CityGen only edits " + CityScene);
            Clear(scene);
            var ctx = ReadCourse(scene);
            var result = new Result { scene = scene, ctx = ctx, root = new GameObject(RootName) };
            result.swap = result.root.AddComponent<CityGenSwap>();
            Directory.CreateDirectory(MaterialFolder); Directory.CreateDirectory(MeshFolder);

            BuildGround(result.root.transform, ctx, result.hidden);
            BuildFountainBlockout(result.root.transform, ctx, result.hidden);
            AddCourseKeepouts(scene, ctx);
            var buildings = new GameObject("Buildings").transform; buildings.SetParent(result.root.transform, false);
            BuildBuildings(buildings, ctx);
            HideTreesThatGaveWay(ctx, result.hidden);
            return result;
        }

        internal static void Finish(Result result, string label)
        {
            result.swap.hideWhileActive = result.hidden.Where(r => r).Distinct().ToArray();
            result.swap.activeWhileEnabled = result.attached.Where(g => g).ToArray();
            result.swap.materialSwaps = result.swaps.ToArray();
            result.swap.disableWhileActive = result.disabled.Where(b => b).ToArray();
            result.swap.enabled = false; result.swap.enabled = true; // apply now in Edit mode
            EditorSceneManager.MarkSceneDirty(result.scene);
            EditorSceneManager.SaveScene(result.scene);
            AssetDatabase.SaveAssets();
            string summary = RunOverlapCheck();
            Debug.Log(label + " seed " + Seed + " | " + result.ctx.buildings.Count + " buildings | " + summary);
        }

        [MenuItem("CityGen/Clear CityGen")]
        public static void ClearMenu()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != CityScene) throw new InvalidOperationException("Open " + CityScene + " first.");
            Clear(scene);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        internal static void Clear(Scene scene)
        {
            foreach (var go in scene.GetRootGameObjects().Where(g => g.name == RootName).ToArray())
                UnityEngine.Object.DestroyImmediate(go); // CityGenSwap.OnDisable shows the originals again
            // Detail that had to live under an original object (the log, so it is carried when picked up).
            foreach (var go in scene.GetRootGameObjects())
                foreach (var t in go.GetComponentsInChildren<Transform>(true).Where(t => t && t.name.StartsWith(AttachedPrefix)).ToArray())
                    if (t) UnityEngine.Object.DestroyImmediate(t.gameObject);
        }

        // ---------------- Course reading ----------------

        static Context ReadCourse(Scene scene)
        {
            var ctx = new Context { random = new System.Random(Seed) };
            ctx.course = scene.GetRootGameObjects().Single(g => g.name == CourseRoot).transform;
            var path = ctx.course.Find("Open path").GetComponent<MeshFilter>().sharedMesh;
            var v = path.vertices;
            ctx.roadHalf = Vector3.Distance(v[0], v[1]) / 2; // first segment is straight: ±half-width
            ctx.route = SavannahCourseGenerator.Route.Select(p => ctx.course.TransformPoint(p)).ToArray();
            ctx.basin = ctx.course.Find("Landmarks (4, decorative)/02 Water basin");
            return ctx;
        }

        static void AddCourseKeepouts(Scene scene, Context ctx)
        {
            var interactions = scene.GetRootGameObjects().Single(g => g.name == Interactions).transform;
            foreach (var zone in new[] { "Tree Zone", "Water Zone", "Log Zone", "Finish Zone" })
            {
                var box = interactions.Find(zone).GetComponent<BoxCollider>();
                ctx.keepouts.Add((zone, ColliderBox(box).Grow(KeepoutMargin)));
            }
            foreach (Transform landmark in ctx.course.Find("Landmarks (4, decorative)"))
                ctx.keepouts.Add((landmark.name, RendererBox(landmark).Grow(KeepoutMargin)));
            foreach (int index in KeptTrees)
            {
                var tree = ctx.course.Find("Acacia trees (18)/Acacia " + index.ToString("00"));
                ctx.keepouts.Add((tree.name, RendererBox(tree).Grow(.5f)));
            }
            ctx.keepouts.Add(("Elephant start", new Box(-5, 5, -5, 5)));
            Vector3 b = ctx.basin.position;
            ctx.keepouts.Add(("Fountain plaza", new Box(b.x - 4.5f, b.x + 4.5f, b.z - 4.5f, b.z + 4.5f)));
        }

        static Box ColliderBox(BoxCollider c)
        {
            var t = c.transform; var lo = new Vector3(float.MaxValue, 0, float.MaxValue); var hi = -lo;
            for (int i = 0; i < 8; i++)
            {
                var corner = c.center + Vector3.Scale(c.size / 2, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var w = t.TransformPoint(corner);
                lo = Vector3.Min(lo, w); hi = Vector3.Max(hi, w);
            }
            return new Box(lo.x, hi.x, lo.z, hi.z);
        }

        // From mesh data, so it is correct even while CityGenSwap has hidden the renderer.
        internal static Box RendererBox(Transform t)
        {
            var lo = new Vector3(float.MaxValue, 0, float.MaxValue); var hi = -lo;
            foreach (var filter in t.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.sharedMesh) continue;
                var b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var w = filter.transform.TransformPoint(corner);
                    lo = Vector3.Min(lo, w); hi = Vector3.Max(hi, w);
                }
            }
            return new Box(lo.x, hi.x, lo.z, hi.z);
        }

        // ---------------- Ground, sidewalks, avenue ----------------

        static void BuildGround(Transform root, Context ctx, List<Renderer> hidden)
        {
            var ground = new GameObject("Ground").transform; ground.SetParent(root, false);
            var course = ctx.course;
            hidden.Add(course.Find("Ground").GetComponent<Renderer>()); // ochre ground → city paving
            float size = SkylineOuter * 2 + 20;
            // Flat surfaces with UVs in metres, so paving / asphalt textures tile at the same scale everywhere.
            Surface(ground, "City paving", new Vector3(0, .02f, 0), size, size, Mat("Blockout Paving", new Color(.52f, .54f, .57f)));
            // Perimeter avenue just outside the original 60 × 60 course.
            var asphalt = Mat("Blockout Asphalt", new Color(.32f, .33f, .35f));
            float a = PlayHalf + AvenueWidth / 2, len = (PlayHalf + AvenueWidth) * 2;
            Surface(ground, "Avenue E", new Vector3(a, .03f, 0), AvenueWidth, len, asphalt);
            Surface(ground, "Avenue W", new Vector3(-a, .03f, 0), AvenueWidth, len, asphalt);
            Surface(ground, "Avenue N", new Vector3(0, .03f, a), len, AvenueWidth, asphalt);
            Surface(ground, "Avenue S", new Vector3(0, .03f, -a), len, AvenueWidth, asphalt);
            // Sidewalks hug the unchanged road from its edge outwards; they sit just below the road surface.
            var sidewalk = Mat("Blockout Sidewalk", new Color(.8f, .8f, .78f));
            Ribbon(ground, "Sidewalk left", ctx, ctx.roadHalf, ctx.roadHalf + SidewalkWidth, .03f, sidewalk);
            Ribbon(ground, "Sidewalk right", ctx, -ctx.roadHalf - SidewalkWidth, -ctx.roadHalf, .03f, sidewalk);
        }

        static void Surface(Transform parent, string name, Vector3 center, float sizeX, float sizeZ, Material material)
        {
            var kit = new MeshKit(1);
            kit.Quad(0, Vector3.zero, Vector3.up, Vector3.forward, sizeX, sizeZ);
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.position = center;
            go.AddComponent<MeshFilter>().sharedMesh = SaveMesh(name, kit.ToMesh(name, out _));
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        static void Ribbon(Transform parent, string name, Context ctx, float from, float to, float y, Material material)
        {
            var route = ctx.route; var vertices = new List<Vector3>(); var triangles = new List<int>();
            for (int i = 0; i < route.Length; i++)
            {
                Vector3 incoming = i == 0 ? (route[1] - route[0]).normalized : (route[i] - route[i - 1]).normalized;
                Vector3 outgoing = i == route.Length - 1 ? incoming : (route[i + 1] - route[i]).normalized;
                Vector3 normal = Vector3.Cross(Vector3.up, outgoing);
                Vector3 miter = (Vector3.Cross(Vector3.up, incoming) + normal).normalized;
                float scale = 1 / Vector3.Dot(miter, normal); // same mitre as the road mesh
                Vector3 p = new Vector3(route[i].x, y, route[i].z);
                vertices.Add(p + miter * from * scale); vertices.Add(p + miter * to * scale);
                if (i == route.Length - 1) continue;
                int a = i * 2, b = (i + 1) * 2;
                triangles.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals();
            mesh.SetUVs(0, vertices.Select(v => new Vector2(v.x, v.z)).ToList()); // world metres
            if (mesh.normals[0].y < 0) { triangles.Reverse(); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); }
            mesh.RecalculateBounds();
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = SaveMesh(name, mesh);
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        // ---------------- Fountain (in place of the water basin) ----------------

        static void BuildFountainBlockout(Transform root, Context ctx, List<Renderer> hidden)
        {
            // The basin Transform stays (ThreePersonCourseManager.waterBasin references it); only its two
            // renderers are hidden. The fountain stays inside the original 3.0 × 2.5 footprint.
            foreach (var r in ctx.basin.GetComponentsInChildren<Renderer>(true)) hidden.Add(r);
            var fountain = new GameObject("Fountain").transform; fountain.SetParent(root, false);
            fountain.position = ctx.basin.position;
            var stone = Mat("Blockout Stone", new Color(.86f, .84f, .8f));
            var water = Mat("Blockout Water", new Color(.45f, .78f, .9f));
            Cylinder(fountain, "Plaza", new Vector3(0, .015f, 0), 8.4f, .02f, Mat("Blockout Plaza", new Color(.84f, .82f, .78f)));
            Cylinder(fountain, "Lower basin", new Vector3(0, .23f, 0), 2.5f, .45f, stone);
            Cylinder(fountain, "Lower water", new Vector3(0, .44f, 0), 2.2f, .02f, water);
            Cylinder(fountain, "Pedestal", new Vector3(0, .9f, 0), .45f, 1.0f, stone);
            Cylinder(fountain, "Middle bowl", new Vector3(0, 1.45f, 0), 1.3f, .18f, stone);
            Cylinder(fountain, "Upper pedestal", new Vector3(0, 1.8f, 0), .25f, .55f, stone);
            Cylinder(fountain, "Top bowl", new Vector3(0, 2.1f, 0), .7f, .14f, stone);
            Cylinder(fountain, "Finial", new Vector3(0, 2.35f, 0), .12f, .35f, stone);
        }

        // ---------------- Buildings ----------------

        static void BuildBuildings(Transform parent, Context ctx)
        {
            float inner = PlayHalf - .4f;
            float east = ctx.route.Max(p => p.x) + ctx.roadHalf + SidewalkWidth + FacadeSetback;   // 23.6
            float west = ctx.route.Min(p => p.x) - ctx.roadHalf - SidewalkWidth - FacadeSetback;   // −23.6
            float north = ctx.route.Max(p => p.z) + ctx.roadHalf + SidewalkWidth + FacadeSetback;  // 22.6
            float south = ctx.route.Min(p => p.z) - ctx.roadHalf - SidewalkWidth - FacadeSetback;  // −21.6
            float f = FootprintScale;
            // 1. Brick row houses and stone apartments between the course roads and the original edge.
            Fill(parent, ctx, new Box(east, inner, -inner, inner), false, 3, 6, 5.5f * f, 10f * f, 99, 0, Vector3.left);
            Fill(parent, ctx, new Box(-inner, west, -inner, inner), false, 3, 6, 5.5f * f, 10f * f, 99, 0, Vector3.right);
            Fill(parent, ctx, new Box(west, east, north, inner), true, 3, 6, 5.5f * f, 10f * f, 99, 0, Vector3.back);
            Fill(parent, ctx, new Box(west, east, -inner, south), true, 3, 6, 5.5f * f, 10f * f, 99, 0, Vector3.forward);
            // 2. Low blocks inside the loop, around the kept park trees and gameplay zones.
            float r = ctx.roadHalf + SidewalkWidth + FacadeSetback;
            Fill(parent, ctx, new Box(-19 + r, 19 - r, -17 + r, 18 - r), true, 3, 5, 5f * f, 8f * f, 7f * f, 1.5f * f, Vector3.zero);
            // 3. Skyline beyond the perimeter avenue: mid-rise first, 15–30 storey towers at the edge.
            float s = PlayHalf + AvenueWidth;
            Fill(parent, ctx, new Box(s, SkylineOuter, -SkylineOuter, SkylineOuter), false, 8, 30, 12f * f, 20f * f, 16f * f, 5f * f, Vector3.left, true);
            Fill(parent, ctx, new Box(-SkylineOuter, -s, -SkylineOuter, SkylineOuter), false, 8, 30, 12f * f, 20f * f, 16f * f, 5f * f, Vector3.right, true);
            Fill(parent, ctx, new Box(-s, s, s, SkylineOuter), true, 8, 30, 12f * f, 20f * f, 16f * f, 5f * f, Vector3.back, true);
            Fill(parent, ctx, new Box(-s, s, -SkylineOuter, -s), true, 8, 30, 12f * f, 20f * f, 16f * f, 5f * f, Vector3.forward, true);
        }

        // Packs lots into a region in rows. alongX: lots are laid side by side along X (rows step in Z).
        static void Fill(Transform parent, Context ctx, Box region, bool alongX, int minFloors, int maxFloors,
            float minWidth, float maxWidth, float maxDepth, float alley, Vector3 front, bool skyline = false)
        {
            var rnd = ctx.random;
            float rowStart = alongX ? region.zMin : region.xMin, rowEnd = alongX ? region.zMax : region.xMax;
            float lineStart = alongX ? region.xMin : region.zMin, lineEnd = alongX ? region.xMax : region.zMax;
            float smallest = Mathf.Max(1.5f, minWidth * .6f);
            for (float row = rowStart; row < rowEnd - smallest;)
            {
                float depth = Mathf.Min(rowEnd - row, Range(rnd, Mathf.Min(maxDepth, 6f), maxDepth));
                for (float pos = lineStart; pos < lineEnd - smallest;)
                {
                    // Try the random width first, then narrower lots, then slide along.
                    bool placed = false;
                    for (float width = Mathf.Min(lineEnd - pos, Range(rnd, minWidth, maxWidth)); width >= smallest && !placed; width -= .5f)
                    {
                        var box = alongX ? new Box(pos, pos + width, row, row + depth) : new Box(row, row + depth, pos, pos + width);
                        if (!Allowed(ctx, box, out _)) continue;
                        Place(parent, ctx, box, minFloors, maxFloors, front, skyline);
                        pos += width;
                        placed = true;
                    }
                    if (!placed) pos += .5f;
                }
                row += depth + alley;
            }
        }

        internal static bool Allowed(Context ctx, Box box, out string reason)
        {
            float clearance = ctx.roadHalf + SidewalkWidth + FacadeSetback - .01f;
            float road = DistanceToRoute(ctx, box);
            if (road < clearance) { reason = $"road {road:0.00} m from centreline (needs {clearance:0.00})"; return false; }
            foreach (var (name, k) in ctx.keepouts) if (box.Overlaps(k)) { reason = "overlaps " + name; return false; }
            foreach (var (name, b) in ctx.buildings) if (box.Overlaps(b.Grow(-.01f))) { reason = "overlaps " + name; return false; }
            reason = null; return true;
        }

        static void Place(Transform parent, Context ctx, Box box, int minFloors, int maxFloors, Vector3 front, bool skyline)
        {
            var rnd = ctx.random;
            float edge = Mathf.Max(Mathf.Abs(box.Center.x), Mathf.Abs(box.Center.z));
            int floors;
            CityBuildingInfo.Style style;
            if (skyline)
            {
                // Mid-rise along the avenue, towers (15–30) towards the outer edge.
                bool tower = edge > PlayHalf + AvenueWidth + 14f || rnd.NextDouble() < .25;
                floors = tower ? rnd.Next(15, maxFloors + 1) : rnd.Next(minFloors, 15);
                style = tower ? CityBuildingInfo.Style.Tower : CityBuildingInfo.Style.Apartment;
            }
            else
            {
                floors = rnd.Next(minFloors, maxFloors + 1);
                style = floors <= 4 && Mathf.Min(box.Width, box.Depth) < 9f ? CityBuildingInfo.Style.RowHouse : CityBuildingInfo.Style.Apartment;
            }
            float height = (GroundFloor + (floors - 1) * UpperFloor) * HeightScale;
            string name = $"B{ctx.buildings.Count + 1:000} {style} {floors}F";
            // Still a white model; a faint tint per style keeps the top-down review readable.
            var tint = style == CityBuildingInfo.Style.RowHouse ? new Color(.97f, .93f, .9f)
                : style == CityBuildingInfo.Style.Apartment ? new Color(.93f, .94f, .96f) : new Color(.86f, .9f, .97f);
            var go = Block(parent, name, box.Center + Vector3.up * height / 2, new Vector3(box.Width, height, box.Depth),
                Mat("Blockout " + style, tint));
            var info = go.AddComponent<CityBuildingInfo>();
            info.style = style; info.floors = floors;
            info.bays = Mathf.Max(2, Mathf.RoundToInt(Mathf.Max(box.Width, box.Depth) / 3f));
            info.footprint = new Vector2(box.Width, box.Depth);
            info.frontDirection = front != Vector3.zero ? front : NearestRoadDirection(ctx, box);
            info.seed = rnd.Next();
            ctx.buildings.Add((name, box));
        }

        static Vector3 NearestRoadDirection(Context ctx, Box box)
        {
            Vector3 c = box.Center, best = Vector3.forward; float bestDistance = float.MaxValue;
            for (int i = 1; i < ctx.route.Length; i++)
            {
                Vector3 p = ClosestOnSegment(c, ctx.route[i - 1], ctx.route[i]); p.y = 0;
                float d = Vector3.Distance(c, p);
                if (d < bestDistance) { bestDistance = d; best = p - c; }
            }
            best.y = 0;
            return Mathf.Abs(best.x) > Mathf.Abs(best.z) ? new Vector3(Mathf.Sign(best.x), 0, 0) : new Vector3(0, 0, Mathf.Sign(best.z));
        }

        static void HideTreesThatGaveWay(Context ctx, List<Renderer> hidden)
        {
            foreach (Transform tree in ctx.course.Find("Acacia trees (18)"))
            {
                int index = int.Parse(tree.name.Substring("Acacia ".Length));
                if (KeptTrees.Contains(index)) continue;
                hidden.AddRange(tree.GetComponentsInChildren<Renderer>(true));
            }
        }

        // ---------------- Overlap check ----------------

        [MenuItem("CityGen/Run Overlap Check")]
        public static string RunOverlapCheck()
        {
            var scene = SceneManager.GetActiveScene();
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
            if (!root) throw new InvalidOperationException("No CityGen in " + scene.path);
            var ctx = ReadCourse(scene);
            AddCourseKeepouts(scene, ctx);
            var problems = new List<string>();
            var boxes = new List<(string, Box)>();
            foreach (var info in root.GetComponentsInChildren<CityBuildingInfo>(true))
            {
                // The lot (building body); cornices, awnings and fire escapes are checked below as "over the road".
                var p = info.transform.position;
                var box = new Box(p.x - info.footprint.x / 2, p.x + info.footprint.x / 2, p.z - info.footprint.y / 2, p.z + info.footprint.y / 2).Grow(-.01f);
                if (!AllowedForCheck(ctx, box, boxes, out string reason)) problems.Add($"- {info.name} ({box}): {reason}");
                boxes.Add((info.name, box));
            }
            // Nothing new may reach over the unchanged road surface (half width + 0.15 m).
            foreach (var group in new[] { "Buildings", "Props", "Trees" })
            {
                var parent = root.transform.Find(group);
                if (!parent) continue;
                foreach (var r in parent.GetComponentsInChildren<Renderer>(true))
                {
                    // Inactive renderers (searchlights before the celebration) have no meaningful bounds.
                    if (r is ParticleSystemRenderer || r.GetComponent<TextMesh>() || !r.gameObject.activeInHierarchy) continue;
                    float d = DistanceToRoute(ctx, Box.From(r.bounds));
                    if (d < ctx.roadHalf + .15f) problems.Add($"- {group}/{r.transform.parent.name}/{r.name} reaches over the road ({d:0.00} m from centreline)");
                }
            }
            // The fountain must stay inside the original basin footprint (3.0 × 2.5).
            var basinBox = Box.From(new Bounds(ctx.basin.position, new Vector3(3.0f, 1, 2.5f)));
            foreach (var r in root.transform.Find("Fountain").GetComponentsInChildren<Renderer>())
                if (!(r is ParticleSystemRenderer) && !r.name.StartsWith("Plaza") && !r.name.StartsWith("Bench")
                    && !Inside(Box.From(r.bounds), basinBox.Grow(.01f))) problems.Add($"- Fountain/{r.name} leaves the original basin footprint");
            var report = new StringBuilder();
            report.AppendLine("# CityGen overlap check").AppendLine();
            report.AppendLine($"Scene: `{scene.path}`, seed {Seed}, {boxes.Count} buildings.").AppendLine();
            report.AppendLine($"Rules: buildings at least {ctx.roadHalf + SidewalkWidth + FacadeSetback:0.0} m from the road centreline (half road {ctx.roadHalf:0.0} + sidewalk {SidewalkWidth} + setback {FacadeSetback}); "
                + "no overlap with the 4 trigger zones, 4 landmarks, kept trees (including gameplay tree Acacia 18), the elephant start or the fountain plaza (each grown by 0.5–1 m); no building overlaps another; the fountain stays within the original 3.0 × 2.5 basin.").AppendLine();
            report.AppendLine(problems.Count == 0 ? "**Result: 0 overlaps.**" : $"**Result: {problems.Count} problems:**");
            foreach (var p in problems) report.AppendLine(p);
            report.AppendLine().AppendLine("## Protected areas checked");
            foreach (var (name, box) in ctx.keepouts) report.AppendLine($"- {name}: {box}");
            File.WriteAllText(ReportPath, report.ToString());
            AssetDatabase.ImportAsset(ReportPath);
            string summary = problems.Count == 0 ? "overlap check: 0 problems" : "overlap check: " + problems.Count + " problems (see " + ReportPath + ")";
            if (problems.Count == 0) Debug.Log("CITYGEN_OVERLAP_OK " + summary); else Debug.LogWarning("CITYGEN_OVERLAP_PROBLEMS " + summary);
            return summary;
        }

        static bool AllowedForCheck(Context ctx, Box box, List<(string, Box)> others, out string reason)
        {
            float clearance = ctx.roadHalf + SidewalkWidth + FacadeSetback - .02f;
            float road = DistanceToRoute(ctx, box);
            if (road < clearance) { reason = $"too close to the road ({road:0.00} m from centreline)"; return false; }
            foreach (var (name, k) in ctx.keepouts) if (box.Overlaps(k)) { reason = "overlaps " + name; return false; }
            foreach (var (name, b) in others) if (box.Overlaps(b)) { reason = "overlaps building " + name; return false; }
            reason = null; return true;
        }

        static bool Inside(Box a, Box b) => a.xMin >= b.xMin && a.xMax <= b.xMax && a.zMin >= b.zMin && a.zMax <= b.zMax;

        // ---------------- Geometry helpers ----------------

        internal static float DistanceToRoute(Context ctx, Box box)
        {
            float best = float.MaxValue;
            for (int i = 1; i < ctx.route.Length; i++) best = Mathf.Min(best, SegmentBoxDistance(ctx.route[i - 1], ctx.route[i], box));
            return best;
        }

        static float SegmentBoxDistance(Vector3 a, Vector3 b, Box box)
        {
            Vector2 p = new Vector2(a.x, a.z), q = new Vector2(b.x, b.z);
            if (PointInBox(p, box) || PointInBox(q, box)) return 0;
            Vector2[] c = { new Vector2(box.xMin, box.zMin), new Vector2(box.xMax, box.zMin), new Vector2(box.xMax, box.zMax), new Vector2(box.xMin, box.zMax) };
            float best = float.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                Vector2 e0 = c[i], e1 = c[(i + 1) % 4];
                if (SegmentsIntersect(p, q, e0, e1)) return 0;
                best = Mathf.Min(best, Mathf.Min(PointSegment(e0, p, q), PointSegment(p, e0, e1)));
                best = Mathf.Min(best, PointSegment(q, e0, e1));
            }
            return best;
        }

        static bool PointInBox(Vector2 p, Box b) => p.x >= b.xMin && p.x <= b.xMax && p.y >= b.zMin && p.y <= b.zMax;
        static float PointSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a; float t = ab.sqrMagnitude < 1e-6f ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }
        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        static bool SegmentsIntersect(Vector2 p, Vector2 p2, Vector2 q, Vector2 q2)
        {
            Vector2 r = p2 - p, s = q2 - q; float d = Cross(r, s);
            if (Mathf.Abs(d) < 1e-6f) return false;
            float t = Cross(q - p, s) / d, u = Cross(q - p, r) / d;
            return t >= 0 && t <= 1 && u >= 0 && u <= 1;
        }
        internal static Vector3 ClosestOnSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a; float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return a + ab * t;
        }
        static float Range(System.Random r, float min, float max) => min + (float)r.NextDouble() * (max - min);

        // ---------------- Object / asset helpers ----------------

        internal static GameObject Block(Transform parent, string name, Vector3 position, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>()); // visual only: never blocks gameplay
            go.name = name; go.transform.SetParent(parent, false);
            go.transform.position = position; go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        static GameObject Cylinder(Transform parent, string name, Vector3 local, float diameter, float height, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = local; go.transform.localScale = new Vector3(diameter, height / 2, diameter);
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        public static Material Mat(string name, Color color)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", .15f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        internal static Mesh SaveMesh(string name, Mesh mesh)
        {
            string path = MeshFolder + "/" + name + ".asset";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!existing) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            existing.Clear();
            EditorUtility.CopySerialized(mesh, existing);
            return existing;
        }
    }
}
