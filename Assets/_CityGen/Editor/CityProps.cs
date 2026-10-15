using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CityGen.EditorTools
{
    // Step 2: street furniture prefabs (GPU-instanced materials), their placement along the unchanged road,
    // kerbs, street trees, and original flags / billboards. Every name, slogan and pattern here is invented.
    internal static class CityProps
    {
        const string PrefabFolder = "Assets/_CityGen/Prefabs";
        const string TextureFolder = "Assets/_CityGen/Textures";

        // ---------------- Materials ----------------

        static Material Mat(string name, Color color, float smoothness = .2f) => CityBuildings.Mat(name, color, smoothness);
        static Material Emissive(string name, Color color, float intensity)
        {
            var m = Mat(name, color, .6f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * intensity);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return m;
        }
        static Material Iron => CityBuildings.IronMaterial();

        // ---------------- Prefabs ----------------

        static GameObject SavePrefab(string name, MeshKit kit, Material[] slotMaterials, Action<GameObject> extra = null)
        {
            Directory.CreateDirectory(PrefabFolder);
            Directory.CreateDirectory(CityBlockout.MeshFolder + "/Props");
            var mesh = CityBlockout.SaveMesh("Props/" + name, kit.ToMesh(name, out int[] used));
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = used.Select(i => slotMaterials[i]).ToArray();
            extra?.Invoke(go);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabFolder + "/" + name + ".prefab");
            UnityEngine.Object.DestroyImmediate(go);
            return prefab;
        }

        // All prefabs are modelled at real size (metres) and placed at settings.propScale.
        internal sealed class Prefabs
        {
            public GameObject lamp, hydrant, bin, trafficLight, sign, taxi, manhole, steam, tree;
        }

        internal static Prefabs BuildPrefabs()
        {
            var p = new Prefabs();
            var iron = Iron;
            var lampGlass = Emissive("City Lamp Glass", new Color(1f, .86f, .58f), 2f);

            var k = new MeshKit(2); // 0 iron, 1 glass
            k.Box(0, new Vector3(0, .25f, 0), new Vector3(.4f, .5f, .4f));
            k.Prism(0, new Vector3(0, .5f, 0), .07f, 3.6f, 10);
            k.Prism(0, new Vector3(0, 1.2f, 0), .11f, .12f, 10);
            k.Box(0, new Vector3(0, 4.05f, .35f), new Vector3(.06f, .06f, .8f));          // arm towards the road (+Z)
            k.Box(0, new Vector3(0, 3.98f, .72f), new Vector3(.05f, .14f, .05f));
            k.Prism(1, new Vector3(0, 3.55f, .72f), .17f, .36f, 8, false);                // lantern
            k.Cone(0, new Vector3(0, 3.9f, .72f), .24f, .18f, 8);
            p.lamp = SavePrefab("Street Lamp", k, new[] { iron, lampGlass });

            k = new MeshKit(1);
            k.Prism(0, Vector3.zero, .14f, .55f, 10);
            k.Cone(0, new Vector3(0, .55f, 0), .16f, .14f, 10);
            k.CylinderAlong(0, new Vector3(0, .38f, 0), Vector3.right, .06f, .44f, 8);
            k.CylinderAlong(0, new Vector3(0, .38f, 0), Vector3.forward, .05f, .34f, 8);
            p.hydrant = SavePrefab("Fire Hydrant", k, new[] { Mat("City Hydrant Red", new Color(.60f, .15f, .12f), .4f) });

            k = new MeshKit(1);
            k.Prism(0, Vector3.zero, .22f, .7f, 12, false);
            k.Prism(0, new Vector3(0, .66f, 0), .25f, .06f, 12);
            k.Cone(0, new Vector3(0, .72f, 0), .24f, .08f, 12);
            p.bin = SavePrefab("Trash Can", k, new[] { iron });

            k = new MeshKit(5); // 0 iron, 1 housing, 2 red, 3 amber, 4 green
            k.Prism(0, Vector3.zero, .08f, 3.4f, 10);
            k.Box(0, new Vector3(0, 3.35f, .7f), new Vector3(.08f, .08f, 1.5f));
            k.Box(1, new Vector3(0, 2.95f, 1.35f), new Vector3(.32f, .82f, .26f));
            k.Box(2, new Vector3(0, 3.22f, 1.22f), new Vector3(.16f, .16f, .04f));
            k.Box(3, new Vector3(0, 2.95f, 1.22f), new Vector3(.16f, .16f, .04f));
            k.Box(4, new Vector3(0, 2.68f, 1.22f), new Vector3(.16f, .16f, .04f));
            p.trafficLight = SavePrefab("Traffic Light", k, new[] { iron, Mat("City Signal Housing", new Color(.88f, .7f, .14f)),
                Emissive("City Signal Red", new Color(.9f, .1f, .08f), 1.5f), Emissive("City Signal Amber", new Color(1f, .6f, .1f), .4f),
                Emissive("City Signal Green", new Color(.15f, .9f, .4f), 1.5f) });

            k = new MeshKit(2);
            k.Prism(0, Vector3.zero, .05f, 2.7f, 8);
            k.Box(1, new Vector3(.45f, 2.55f, 0), new Vector3(.9f, .2f, .03f));
            k.Box(1, new Vector3(0, 2.3f, .45f), new Vector3(.03f, .2f, .9f));
            p.sign = SavePrefab("Street Sign", k, new[] { iron, Mat("City Sign Green", new Color(.10f, .42f, .28f), .4f) });

            k = new MeshKit(6); // 0 body, 1 glass, 2 tyres, 3 chrome, 4 checker, 5 roof sign
            k.Box(0, new Vector3(0, .62f, 0), new Vector3(1.78f, .58f, 4.6f));
            k.Box(0, new Vector3(0, 1.12f, -.2f), new Vector3(1.6f, .46f, 2.3f));
            k.Box(1, new Vector3(0, 1.14f, -.2f), new Vector3(1.62f, .34f, 2.1f));
            k.Box(4, new Vector3(0, .72f, 0), new Vector3(1.8f, .1f, 4.0f));
            k.Box(3, new Vector3(0, .42f, 2.32f), new Vector3(1.7f, .14f, .1f));
            k.Box(3, new Vector3(0, .42f, -2.32f), new Vector3(1.7f, .14f, .1f));
            k.Box(5, new Vector3(0, 1.44f, -.2f), new Vector3(.55f, .18f, .22f));
            foreach (var w in new[] { new Vector3(.82f, .34f, 1.45f), new Vector3(-.82f, .34f, 1.45f), new Vector3(.82f, .34f, -1.45f), new Vector3(-.82f, .34f, -1.45f) })
                k.CylinderAlong(2, w, Vector3.right, .34f, .26f, 12);
            p.taxi = SavePrefab("Taxi", k, new[] { Mat("City Taxi Yellow", new Color(.98f, .72f, .10f), .55f), Mat("City Glass", new Color(.14f, .19f, .27f), .8f),
                Mat("City Tyre", new Color(.08f, .08f, .08f)), Mat("City Chrome", new Color(.8f, .8f, .82f), .85f),
                Mat("City Checker", new Color(.1f, .1f, .1f)), Emissive("City Taxi Sign", new Color(1f, .95f, .8f), .8f) });

            k = new MeshKit(1);
            k.Prism(0, Vector3.zero, .35f, .02f, 16);
            p.manhole = SavePrefab("Manhole", k, new[] { Mat("City Manhole", new Color(.18f, .18f, .19f), .3f) });
            p.steam = SaveSteamPrefab();

            k = new MeshKit(4); // 0 soil, 1 kerb, 2 bark, 3 foliage
            k.Box(0, new Vector3(0, .03f, 0), new Vector3(1.3f, .06f, 1.3f));
            foreach (var (c, s) in new[] { (new Vector3(.68f, .05f, 0), new Vector3(.08f, .1f, 1.44f)), (new Vector3(-.68f, .05f, 0), new Vector3(.08f, .1f, 1.44f)),
                                           (new Vector3(0, .05f, .68f), new Vector3(1.44f, .1f, .08f)), (new Vector3(0, .05f, -.68f), new Vector3(1.44f, .1f, .08f)) })
                k.Box(1, c, s);
            k.Prism(2, Vector3.zero, .14f, 3f, 8, false);
            k.Lathe(3, Vector3.zero, Sphere(2.0f, 4.3f, 10), 14);
            p.tree = SavePrefab("Street Tree", k, new[] { Mat("City Soil", new Color(.25f, .19f, .14f)), Mat("City Trim", new Color(.9f, .87f, .8f)),
                Mat("City Bark", new Color(.30f, .21f, .14f)), Mat("City Foliage", new Color(.30f, .55f, .26f), .1f) });
            AssetDatabase.SaveAssets();
            return p;
        }

        // Profile for a round crown of radius r centred at height cy (bottom → equator → top).
        static List<Vector2> Sphere(float r, float cy, int rings)
        {
            var points = new List<Vector2>();
            for (int i = 0; i <= rings; i++)
            {
                float a = Mathf.PI * i / rings;
                points.Add(new Vector2(Mathf.Max(0, Mathf.Sin(a) * r), cy - Mathf.Cos(a) * r));
            }
            return points;
        }

        static GameObject SaveSteamPrefab()
        {
            var go = new GameObject("Manhole Steam");
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = true; main.playOnAwake = true; main.prewarm = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.5f, .8f);
            main.startSize = new ParticleSystem.MinMaxCurve(.35f, .6f);
            main.startColor = new Color(1, 1, 1, .4f);
            main.gravityModifier = -.02f;
            main.maxParticles = 60;
            var emission = system.emission; emission.rateOverTime = 7;
            var shape = system.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 14; shape.radius = .18f;
            shape.rotation = new Vector3(-90, 0, 0); // emit upwards
            var size = system.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .6f, 1, 2.4f));
            var color = system.colorOverLifetime; color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(.45f, .15f), new GradientAlphaKey(0, 1) });
            color.color = fade;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = SoftParticleMaterial("City Steam", new Color(1, 1, 1, 1));
            Directory.CreateDirectory(PrefabFolder);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabFolder + "/Manhole Steam.prefab");
            UnityEngine.Object.DestroyImmediate(go);
            return prefab;
        }

        internal static Material SoftParticleMaterial(string name, Color tint, bool additive = false)
        {
            string path = CityBlockout.MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (!shader) shader = Shader.Find("Sprites/Default");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            var texture = SoftDot();
            material.mainTexture = texture;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1); material.SetFloat("_Blend", 0);
                // Additive for glowing things (fireworks, beams): colours add up into light instead of covering.
                material.SetFloat("_Blend", additive ? 2 : 0);
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
                material.SetFloat("_ZWrite", 0);
                if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0); // beams are seen from inside and out
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        static Texture2D SoftDot()
        {
            return SaveTexture("Soft dot", 64, 64, (x, y) =>
            {
                float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(32, 32)) / 32f;
                return new Color(1, 1, 1, Mathf.Clamp01(1 - d * d));
            }, true);
        }

        // ---------------- Placement along the unchanged road ----------------

        internal static void Place(Transform root, CityBlockout.Context ctx, Prefabs p, CityGenSettings s)
        {
            var props = new GameObject("Props").transform; props.SetParent(root, false);
            var trees = new GameObject("Trees").transform; trees.SetParent(root, false);
            var kerbs = new GameObject("Kerbs").transform; kerbs.SetParent(root.Find("Ground"), false);
            float scale = s.propScale, half = ctx.roadHalf;
            var rnd = new System.Random(s.seed + 7);

            // Kerbs: a slim raised stone edge on both sides, outside the unchanged road surface.
            KerbRibbon(kerbs, "Kerb left", ctx, half, half + .14f);
            KerbRibbon(kerbs, "Kerb right", ctx, -half - .14f, -half);

            foreach (int side in new[] { 1, -1 })
            {
                // Lamps stand far enough back that the lantern arm stays over the kerb, not the road.
                Along(ctx, s.lampSpacing, side == 1 ? 0 : s.lampSpacing / 2, side, half + .65f, (pos, inward) => Instance(p.lamp, props, pos, inward, scale));
                Along(ctx, s.streetTreeSpacing, s.streetTreeSpacing / 2 + (side == 1 ? 0 : 3), side, half + 1.4f, (pos, inward) => Instance(p.tree, trees, pos, inward, scale));
            }
            Along(ctx, s.hydrantSpacing, 2.5f, -1, half + .5f, (pos, inward) => Instance(p.hydrant, props, pos, inward, scale));
            Along(ctx, s.binSpacing, 5.5f, 1, half + 1.7f, (pos, inward) => Instance(p.bin, props, pos, inward, scale));

            // Corners: traffic lights on the outside of each turn, street signs on the inside.
            for (int i = 1; i < ctx.route.Length - 1; i++)
            {
                Vector3 inDir = (ctx.route[i] - ctx.route[i - 1]).normalized, outDir = (ctx.route[i + 1] - ctx.route[i]).normalized;
                Vector3 bisector = (Vector3.Cross(Vector3.up, inDir) + Vector3.Cross(Vector3.up, outDir)).normalized;
                float turn = Vector3.Cross(inDir, outDir).y; // > 0: right turn
                Vector3 outside = turn > 0 ? -bisector : bisector;
                Vector3 corner = new Vector3(ctx.route[i].x, 0, ctx.route[i].z);
                TryPlace(ctx, p.trafficLight, props, corner + outside * (half + .9f), -outside, scale);
                // Inside a turn both road edges are close; step further in along the bisector.
                if (i % 2 == 1) TryPlace(ctx, p.sign, props, corner - outside * (half + .6f) * 1.45f, outside, scale);
            }

            // Perimeter avenue: parked taxis, steaming manholes and lamps along the edge.
            float a0 = 30f, a1 = 36f;
            for (int i = 0, tries = 0; i < s.taxiCount && tries < 400; tries++)
            {
                int side = rnd.Next(4); float along = (float)(rnd.NextDouble() * 2 - 1) * 33f;
                float lane = (float)(a0 + (rnd.NextDouble() < .5 ? 1.6 : 4.4));
                Vector3 pos = AvenuePoint(side, lane, along), dir = AvenueDirection(side) * (rnd.NextDouble() < .5 ? 1 : -1);
                if (props.Cast<Transform>().Any(t => t.name.StartsWith("Taxi") && Vector3.Distance(t.position, pos) < 2.2f)) continue;
                Instance(p.taxi, props, pos + Vector3.up * .03f, dir, scale);
                i++;
            }
            for (int i = 0; i < s.steamingManholes; i++)
            {
                int side = i % 4; float along = (float)(rnd.NextDouble() * 2 - 1) * 30f;
                Vector3 pos = AvenuePoint(side, (a0 + a1) / 2, along) + Vector3.up * .03f;
                Instance(p.manhole, props, pos, Vector3.forward, 1);
                Instance(p.steam, props, pos + Vector3.up * .05f, Vector3.forward, 1);
            }
            for (int side = 0; side < 4; side++)
                for (float along = -32; along <= 32; along += 8)
                    Instance(p.lamp, props, AvenuePoint(side, a0 + .35f, along), -AvenueOutward(side), scale);

            // Kept park trees (Acacia 15–17) become round street trees with a tree pit; Acacia 18 stays untouched
            // because the tree course stage measures around its trunk.
            foreach (int index in new[] { 15, 16, 17 })
            {
                var acacia = ctx.course.Find("Acacia trees (18)/Acacia " + index.ToString("00"));
                Instance(p.tree, trees, new Vector3(acacia.position.x, 0, acacia.position.z), Vector3.forward, .55f);
            }
        }

        internal static IEnumerable<Renderer> RestyledTreeRenderers(CityBlockout.Context ctx)
        {
            foreach (int index in new[] { 15, 16, 17 })
                foreach (var r in ctx.course.Find("Acacia trees (18)/Acacia " + index.ToString("00")).GetComponentsInChildren<Renderer>(true))
                    yield return r;
        }

        // Walks the road polyline placing an item every `spacing` metres at `offset` from the centreline.
        static void Along(CityBlockout.Context ctx, float spacing, float phase, int side, float offset, Action<Vector3, Vector3> place)
        {
            var route = ctx.route;
            float travelled = 0, next = phase;
            for (int i = 1; i < route.Length; i++)
            {
                Vector3 a = route[i - 1], b = route[i]; a.y = b.y = 0;
                float length = Vector3.Distance(a, b);
                Vector3 dir = (b - a) / length, normal = Vector3.Cross(Vector3.up, dir) * side;
                while (next <= travelled + length)
                {
                    float t = next - travelled;
                    next += spacing;
                    if (t < 1.8f || t > length - 1.8f) continue; // keep corners clear
                    Vector3 pos = a + dir * t + normal * offset;
                    if (Blocked(ctx, pos, .6f)) continue;
                    place(pos, -normal);
                }
                travelled += length;
            }
        }

        static void TryPlace(CityBlockout.Context ctx, GameObject prefab, Transform parent, Vector3 pos, Vector3 facing, float scale)
        {
            if (!Blocked(ctx, pos, .6f)) Instance(prefab, parent, pos, facing, scale);
        }

        // Keep-outs use the item's footprint; the road test uses its base point (the overlap check then
        // verifies that no part of any item actually reaches over the road surface).
        static bool Blocked(CityBlockout.Context ctx, Vector3 pos, float radius)
        {
            var box = new CityBlockout.Box(pos.x - radius, pos.x + radius, pos.z - radius, pos.z + radius);
            var point = new CityBlockout.Box(pos.x - .02f, pos.x + .02f, pos.z - .02f, pos.z + .02f);
            return ctx.keepouts.Any(k => k.box.Overlaps(box)) || CityBlockout.DistanceToRoute(ctx, point) < ctx.roadHalf + .25f;
        }

        static GameObject Instance(GameObject prefab, Transform parent, Vector3 position, Vector3 facing, float scale)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            facing.y = 0;
            go.transform.SetPositionAndRotation(position, facing.sqrMagnitude > .001f ? Quaternion.LookRotation(facing, Vector3.up) : Quaternion.identity);
            go.transform.localScale = Vector3.one * scale;
            return go;
        }

        static Vector3 AvenuePoint(int side, float distance, float along) => side switch
        {
            0 => new Vector3(distance, 0, along),
            1 => new Vector3(-distance, 0, along),
            2 => new Vector3(along, 0, distance),
            _ => new Vector3(along, 0, -distance),
        };
        static Vector3 AvenueDirection(int side) => side < 2 ? Vector3.forward : Vector3.right;
        static Vector3 AvenueOutward(int side) => side switch { 0 => Vector3.right, 1 => Vector3.left, 2 => Vector3.forward, _ => Vector3.back };

        static void KerbRibbon(Transform parent, string name, CityBlockout.Context ctx, float from, float to)
        {
            var route = ctx.route; var kit = new MeshKit(1);
            for (int i = 1; i < route.Length; i++)
            {
                Vector3 a = route[i - 1], b = route[i]; a.y = b.y = 0;
                Vector3 dir = (b - a).normalized, n = Vector3.Cross(Vector3.up, dir);
                float length = Vector3.Distance(a, b) + .02f;
                Vector3 c = (a + b) / 2 + n * (from + to) / 2 + Vector3.up * .05f;
                kit.Box(0, c, new Vector3(Mathf.Abs(to - from), .04f, length), Quaternion.LookRotation(dir, Vector3.up));
            }
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = CityBlockout.SaveMesh(name, kit.ToMesh(name, out _));
            go.AddComponent<MeshRenderer>().sharedMaterial = Mat("City Kerb", new Color(.82f, .80f, .76f));
        }

        // ---------------- Flags (original patterns) ----------------

        static Mesh flagPole, flagCloth;

        internal static void AddFlag(Transform building, Vector3 anchorLocal, Vector3 normal, System.Random rnd, CityGenSettings s)
        {
            if (!flagPole)
            {
                var k = new MeshKit(1);
                var tilt = Quaternion.Euler(-35, 0, 0);
                k.Box(0, tilt * new Vector3(0, 0, .35f), new Vector3(.025f, .025f, .72f), tilt, true);
                flagPole = CityBlockout.SaveMesh("Props/Flag pole", k.ToMesh("Flag pole", out _));
                k = new MeshKit(1);
                Vector3 tip = tilt * new Vector3(0, 0, .7f), mid = tilt * new Vector3(0, 0, .45f);
                Vector3 c = (mid + tip) / 2 + Vector3.down * .16f;
                k.Quad(0, c, Vector3.right, Vector3.up, .32f, .3f);
                k.Quad(0, c, Vector3.left, Vector3.up, .32f, .3f);
                flagCloth = CityBlockout.SaveMesh("Props/Flag cloth", k.ToMesh("Flag cloth", out _));
            }
            float scale = s.heightScale / .3f;
            var flag = new GameObject("Flag").transform;
            flag.SetParent(building, false);
            flag.localPosition = anchorLocal;
            flag.localRotation = Quaternion.LookRotation(normal, Vector3.up);
            flag.localScale = Vector3.one * scale;
            var pole = new GameObject("Pole"); pole.transform.SetParent(flag, false);
            pole.AddComponent<MeshFilter>().sharedMesh = flagPole;
            pole.AddComponent<MeshRenderer>().sharedMaterial = Iron;
            var cloth = new GameObject("Cloth"); cloth.transform.SetParent(flag, false);
            cloth.AddComponent<MeshFilter>().sharedMesh = flagCloth;
            cloth.AddComponent<MeshRenderer>().sharedMaterial = FlagMaterial(rnd.Next(4));
        }

        static Material FlagMaterial(int design)
        {
            var colors = new[]
            {
                (new Color(.72f, .12f, .14f), new Color(.98f, .92f, .78f)),
                (new Color(.10f, .38f, .28f), new Color(.96f, .86f, .52f)),
                (new Color(.12f, .20f, .44f), new Color(.98f, .76f, .22f)),
                (new Color(.45f, .12f, .25f), new Color(.95f, .9f, .82f)),
            };
            var (bg, fg) = colors[design];
            var texture = SaveTexture("Flag " + design, 128, 96, (x, y) =>
            {
                float u = x / 128f, v = y / 96f;
                switch (design)
                {
                    case 0: return Elephant(u * 1.33f - .2f, v - .1f, .55f) ? fg : bg;                          // elephant emblem
                    case 1: return (int)(v * 6) % 2 == 0 ? bg : Mathf.Abs(u - .5f) + Mathf.Abs(v - .5f) < .22f ? bg : fg; // stripes + diamond
                    case 2: return Sunburst(u, v, .5f, .45f, 12) ? fg : bg;                                    // sunburst
                    default: return u < .07f || u > .93f || v < .09f || v > .91f ? fg : Peanut(u, v) ? fg : bg; // bordered peanut
                }
            });
            var m = Mat("City Flag " + design, Color.white, .1f);
            m.SetTexture("_BaseMap", texture);
            m.mainTexture = texture;
            return m;
        }

        // ---------------- Billboards (original posters) ----------------

        static readonly (string title, string tagline, Color bg, Color fg, int motif)[] Posters =
        {
            ("PEANUT FIZZ", "a trunkful of bubbles!", new Color(.78f, .14f, .12f), new Color(1f, .86f, .3f), 0),
            ("SAVANNAH SWING FEST", "every night at eight", new Color(.10f, .45f, .50f), new Color(.98f, .92f, .7f), 1),
            ("TRUNK & TUSK THEATER", "now showing: THE GRAY GIANT", new Color(.36f, .12f, .44f), new Color(1f, .78f, .25f), 2),
            ("GRAY GIANT TAXI CO.", "ring 1930 for a ride", new Color(.98f, .76f, .12f), new Color(.08f, .08f, .08f), 3),
            ("MOONLIGHT MAMBO CLUB", "dance till dawn", new Color(.10f, .13f, .32f), new Color(.98f, .5f, .75f), 4),
            ("ACACIA GRAND HOTEL", "rooms from two bits", new Color(.14f, .36f, .22f), new Color(.98f, .92f, .76f), 5),
        };

        internal static void PlaceBillboards(Transform buildingsRoot, CityGenSettings s)
        {
            var rnd = new System.Random(s.seed + 11);
            // Candidates: buildings just outside the course that face it, tall enough to show above the street.
            var candidates = buildingsRoot.GetComponentsInChildren<CityBuildingInfo>()
                .Where(b => b.style != CityBuildingInfo.Style.RowHouse && Mathf.Max(Mathf.Abs(b.transform.position.x), Mathf.Abs(b.transform.position.z)) < 48f)
                .OrderBy(b => b.name).ToList();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            for (int i = 0; i < s.billboardCount && candidates.Count > 0; i++)
            {
                var b = candidates[rnd.Next(candidates.Count)];
                candidates.Remove(b);
                var poster = Posters[i % Posters.Length];
                // Low rise: on the roof. Towers: on the first setback terrace (not floating beside the spire).
                int roofFloors = b.style == CityBuildingInfo.Style.Tower ? Mathf.Max(3, Mathf.RoundToInt(b.floors * .62f)) : b.floors;
                float roof = (4f + (roofFloors - 1) * 3.2f) * s.heightScale;
                Vector3 toCourse = -new Vector3(b.transform.position.x, 0, b.transform.position.z);
                Vector3 facing = Mathf.Abs(toCourse.x) > Mathf.Abs(toCourse.z) ? new Vector3(Mathf.Sign(toCourse.x), 0, 0) : new Vector3(0, 0, Mathf.Sign(toCourse.z));
                float faceWidth = Mathf.Abs(facing.x) > .5f ? b.footprint.y : b.footprint.x;
                float halfDepth = Mathf.Abs(facing.x) > .5f ? b.footprint.x / 2 : b.footprint.y / 2;
                float width = Mathf.Min(faceWidth * .9f, 6f), height = width * .45f;

                var board = new GameObject("Billboard " + poster.title).transform;
                board.SetParent(b.transform, false);
                board.localPosition = facing * (halfDepth - .25f) + Vector3.up * (roof + .25f + height / 2);
                board.localRotation = Quaternion.LookRotation(facing, Vector3.up);
                var kit = new MeshKit(2);
                kit.Quad(0, Vector3.zero, Vector3.forward, Vector3.up, width, height);
                kit.Box(1, new Vector3(0, -height / 2 - .02f, -.05f), new Vector3(width + .1f, .05f, .08f));
                kit.Box(1, new Vector3(0, height / 2 + .02f, -.05f), new Vector3(width + .1f, .05f, .08f));
                foreach (float x in new[] { -width / 2 + .1f, width / 2 - .1f })
                    kit.Box(1, new Vector3(x, -height / 2 - .13f, -.15f), new Vector3(.05f, height + .3f, .05f));
                var go = board.gameObject;
                go.AddComponent<MeshFilter>().sharedMesh = CityBlockout.SaveMesh("Props/Billboard " + i, kit.ToMesh("Billboard " + i, out _));
                go.AddComponent<MeshRenderer>().sharedMaterials = new[] { PosterMaterial(i % Posters.Length), Iron };
                // Shrink long lines so they stay on the board (a glyph is about 0.6 × its height wide).
                float title = Mathf.Min(width * .07f, width * .86f / (poster.title.Length * .62f));
                float tagline = Mathf.Min(width * .035f, width * .86f / (poster.tagline.Length * .55f));
                AddText(board, font, poster.title, new Vector3(0, height * .12f, .02f), title, poster.fg, FontStyle.Bold);
                AddText(board, font, poster.tagline, new Vector3(0, -height * .26f, .02f), tagline, poster.fg, FontStyle.Italic);
            }
        }

        static void AddText(Transform parent, Font font, string text, Vector3 local, float size, Color color, FontStyle style)
        {
            var go = new GameObject("Text"); go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.Euler(0, 180, 0); // TextMesh reads towards -Z; the board faces +Z
            var tm = go.AddComponent<TextMesh>();
            tm.text = text; tm.font = font; tm.fontSize = 64; tm.characterSize = size / 6.4f;
            tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.color = color; tm.fontStyle = style;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }

        static Material PosterMaterial(int index)
        {
            var poster = Posters[index];
            Color bg = poster.bg, fg = poster.fg, mid = Color.Lerp(bg, fg, .35f);
            var texture = SaveTexture("Poster " + index, 512, 230, (x, y) =>
            {
                float u = x / 512f, v = y / 230f;
                // Marquee bulbs around the edge.
                if (u < .03f || u > .97f || v < .065f || v > .935f)
                {
                    float t = (u < .03f || u > .97f) ? v * 14 : u * 30;
                    return Mathf.Repeat(t, 1) < .45f ? new Color(1f, .95f, .75f) : Color.Lerp(bg, Color.black, .4f);
                }
                Color c = Color.Lerp(bg, Color.Lerp(bg, Color.black, .25f), v);
                switch (poster.motif)
                {
                    case 0: if (Sunburst(u, v, .85f, .5f, 16)) c = mid; if (Bottle(u, v)) c = fg; break;
                    case 1: if (Sunburst(u, v, .5f, -.2f, 20)) c = mid; if (Notes(u, v)) c = fg; break;
                    case 2: if (Elephant(u * 2.2f - 1.55f, v * 1.0f - .05f, .6f)) c = fg; break;
                    case 3: if (v > .78f && v < .88f && ((int)(u * 40) + (int)(v * 40)) % 2 == 0) c = fg; break;
                    case 4: if (new Vector2(u - .85f, (v - .62f) * .45f).magnitude < .1f) c = fg; break;
                    default: if (Tree(u, v)) c = fg; break;
                }
                return c;
            });
            var m = Mat("City Poster " + index, Color.white, .3f);
            m.SetTexture("_BaseMap", texture);
            m.mainTexture = texture;
            m.EnableKeyword("_EMISSION");
            m.SetTexture("_EmissionMap", texture);
            m.SetColor("_EmissionColor", Color.white * .35f); // posters read clearly in shadowed streets
            return m;
        }

        // ---------------- Tiny procedural motifs (all original) ----------------

        static bool Sunburst(float u, float v, float cx, float cy, int rays)
        {
            float a = Mathf.Atan2(v - cy, u - cx);
            return Mathf.Repeat(a / (Mathf.PI * 2) * rays * 2, 2) < 1;
        }

        static bool Elephant(float u, float v, float s)
        {
            // Side-view silhouette from ellipses: body, head, ear, trunk, legs (original drawing).
            bool In(float cx, float cy, float rx, float ry) => ((u - cx) / rx) * ((u - cx) / rx) + ((v - cy) / ry) * ((v - cy) / ry) < 1;
            float k = s;
            bool body = In(.45f * k + .1f, .5f * k + .1f, .28f * k, .2f * k);
            bool head = In(.78f * k + .1f, .62f * k + .1f, .14f * k, .14f * k);
            bool ear = In(.72f * k + .1f, .62f * k + .1f, .08f * k, .12f * k);
            bool trunk = In(.92f * k + .1f, .45f * k + .1f, .04f * k, .16f * k);
            bool legs = v > .1f && v < .38f * k + .1f && (Mathf.Abs(u - (.28f * k + .1f)) < .04f * k || Mathf.Abs(u - (.6f * k + .1f)) < .04f * k);
            return (body || head || trunk || legs) && !ear;
        }

        static bool Bottle(float u, float v) =>
            (Mathf.Abs(u - .22f) < .07f && v > .15f && v < .6f) || (Mathf.Abs(u - .22f) < .025f && v >= .6f && v < .8f);

        static bool Notes(float u, float v)
        {
            foreach (var (x, y) in new[] { (.12f, .3f), (.2f, .45f), (.86f, .35f) })
                if (new Vector2((u - x) * 2.2f, v - y).magnitude < .06f || (Mathf.Abs(u - x - .025f) < .006f && v > y && v < y + .3f)) return true;
            return false;
        }

        static bool Peanut(float u, float v) =>
            new Vector2((u - .4f) * 1.2f, v - .5f).magnitude < .17f || new Vector2((u - .62f) * 1.2f, v - .5f).magnitude < .15f;

        static bool Tree(float u, float v) =>
            (Mathf.Abs(u - .15f) < .015f && v > .15f && v < .55f) || new Vector2((u - .15f) * .9f, (v - .62f) * 1.6f).magnitude < .1f;

        static Texture2D SaveTexture(string name, int w, int h, Func<int, int, Color> pixel, bool alpha = false)
        {
            Directory.CreateDirectory(TextureFolder);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) tex.SetPixel(x, y, pixel(x, y));
            tex.Apply();
            string path = TextureFolder + "/" + name + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = alpha;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
