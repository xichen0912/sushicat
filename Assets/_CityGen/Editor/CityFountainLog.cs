using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CityGen.EditorTools
{
    // Step 2: the stone fountain in place of the water basin, and the detailed log.
    internal static class CityFountainLog
    {
        // ---------------- Fountain ----------------
        // Inside the original 3.0 × 2.5 basin footprint (largest radius 1.25). The basin Transform itself stays
        // untouched because ThreePersonCourseManager.waterBasin measures drinking distance from it.

        public static void BuildFountain(Transform cityRoot, CityBlockout.Context ctx, CityGenSettings s)
        {
            var old = cityRoot.Find("Fountain");
            if (old) Object.DestroyImmediate(old.gameObject);
            var fountain = new GameObject("Fountain").transform;
            fountain.SetParent(cityRoot, false);
            fountain.position = new Vector3(ctx.basin.position.x, 0, ctx.basin.position.z);

            var stone = CityBuildings.Mat("City Fountain Stone", new Color(.86f, .83f, .76f), .25f);
            var water = WaterMaterial();
            var kit = new MeshKit(2); // 0 stone, 1 water
            const int sides = 28;
            kit.Lathe(0, Vector3.zero, new List<Vector2>
            {
                new Vector2(1.22f, 0), new Vector2(1.22f, .34f), new Vector2(1.25f, .38f), new Vector2(1.25f, .44f),
                new Vector2(1.12f, .44f), new Vector2(1.10f, .40f), new Vector2(1.10f, .14f), new Vector2(0, .14f)
            }, sides);                                                                           // lower basin
            kit.Lathe(0, Vector3.zero, new List<Vector2>
            {
                new Vector2(.28f, .14f), new Vector2(.28f, .22f), new Vector2(.20f, .30f), new Vector2(.15f, .95f),
                new Vector2(.22f, 1.02f), new Vector2(.22f, 1.08f), new Vector2(.12f, 1.10f)
            }, sides);                                                                           // pedestal
            kit.Lathe(0, Vector3.zero, new List<Vector2>
            {
                new Vector2(.12f, 1.10f), new Vector2(.45f, 1.18f), new Vector2(.66f, 1.32f), new Vector2(.66f, 1.38f),
                new Vector2(.60f, 1.38f), new Vector2(.56f, 1.30f), new Vector2(0, 1.28f)
            }, sides);                                                                           // middle bowl
            kit.Lathe(0, Vector3.zero, new List<Vector2>
            {
                new Vector2(.09f, 1.28f), new Vector2(.07f, 1.60f), new Vector2(.10f, 1.66f), new Vector2(.30f, 1.74f),
                new Vector2(.38f, 1.82f), new Vector2(.38f, 1.86f), new Vector2(.33f, 1.86f), new Vector2(.30f, 1.80f), new Vector2(0, 1.79f)
            }, sides);                                                                           // upper stem + top bowl
            kit.Lathe(0, Vector3.zero, new List<Vector2>
            {
                new Vector2(.05f, 1.79f), new Vector2(.05f, 1.95f), new Vector2(.08f, 2.0f), new Vector2(.03f, 2.06f), new Vector2(0, 2.08f)
            }, 12);                                                                              // finial spout
            kit.Disc(1, new Vector3(0, .36f, 0), 1.1f, sides, true);
            kit.Disc(1, new Vector3(0, 1.34f, 0), .57f, sides, true);
            kit.Disc(1, new Vector3(0, 1.83f, 0), .31f, sides, true);
            AddMesh(fountain, "Fountain body", kit, new[] { stone, water });

            // Plaza paving around the fountain (stays below the road surface where they meet).
            var plaza = new MeshKit(2);
            plaza.Disc(0, new Vector3(0, .018f, 0), 4.2f, 40, true);
            plaza.Disc(1, new Vector3(0, .02f, 0), 2.6f, 40, true);
            AddMesh(fountain, "Plaza", plaza, new[] { CityBuildings.Mat("City Plaza Stone", new Color(.80f, .77f, .70f)),
                CityBuildings.Mat("City Plaza Inlay", new Color(.62f, .58f, .52f)) });

            // Benches on the side away from the road.
            var bench = new MeshKit(2);
            bench.Box(0, new Vector3(0, .45f, 0), new Vector3(1.8f, .08f, .45f));
            bench.Box(0, new Vector3(0, .75f, -.2f), new Vector3(1.8f, .4f, .06f));
            foreach (float x in new[] { -.75f, .75f }) bench.Box(1, new Vector3(x, .22f, 0), new Vector3(.08f, .45f, .4f));
            var benchMesh = CityBlockout.SaveMesh("Props/Bench", bench.ToMesh("Bench", out _));
            foreach (float angle in new[] { 215f, 270f, 325f })
            {
                float a = angle * Mathf.Deg2Rad;
                var b = new GameObject("Bench").transform; b.SetParent(fountain, false);
                b.localPosition = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 3.3f;
                b.localRotation = Quaternion.LookRotation(-b.localPosition, Vector3.up);
                b.localScale = Vector3.one * s.propScale;
                b.gameObject.AddComponent<MeshFilter>().sharedMesh = benchMesh;
                b.gameObject.AddComponent<MeshRenderer>().sharedMaterials = new[]
                    { CityBuildings.Mat("City Wood", new Color(.46f, .31f, .18f)), CityBuildings.IronMaterial() };
            }

            // Water: an upward jet from the spout falling into the top bowl, and a ring of overflow from the middle bowl.
            var spray = CityProps.SoftParticleMaterial("City Water Spray", new Color(.82f, .93f, 1f, 1f));
            Particles(fountain, "Jet", new Vector3(0, 2.07f, 0), spray, shape =>
            {
                shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 9; shape.radius = .02f; shape.rotation = new Vector3(-90, 0, 0);
            }, rate: 140, speed: new Vector2(1.5f, 1.9f), life: new Vector2(.45f, .6f), size: new Vector2(.035f, .07f), gravity: 1.1f);
            Particles(fountain, "Overflow ring", new Vector3(0, 1.37f, 0), spray, shape =>
            {
                shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = .64f; shape.radiusThickness = 0; shape.rotation = new Vector3(90, 0, 0);
            }, rate: 160, speed: new Vector2(.12f, .2f), life: new Vector2(.35f, .45f), size: new Vector2(.03f, .05f), gravity: .9f);
            Particles(fountain, "Top overflow", new Vector3(0, 1.85f, 0), spray, shape =>
            {
                shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = .39f; shape.radiusThickness = 0; shape.rotation = new Vector3(90, 0, 0);
            }, rate: 70, speed: new Vector2(.08f, .14f), life: new Vector2(.3f, .4f), size: new Vector2(.025f, .04f), gravity: .9f);
        }

        static void Particles(Transform parent, string name, Vector3 local, Material material, System.Action<ParticleSystem.ShapeModule> shape,
            float rate, Vector2 speed, Vector2 life, Vector2 size, float gravity)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = local;
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = true; main.playOnAwake = true; main.prewarm = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startColor = new Color(.85f, .94f, 1f, .85f);
            main.gravityModifier = gravity;
            main.maxParticles = 400;
            var emission = system.emission; emission.rateOverTime = rate;
            shape(system.shape);
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            system.Play();
        }

        static Material WaterMaterial()
        {
            var m = CityBuildings.Mat("City Water", new Color(.36f, .66f, .80f, .62f), .95f);
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return m;
        }

        // ---------------- Log ----------------
        // The detail is a child of the original Timber (same local space, so exactly the same size and place)
        // and therefore travels with "03 Fallen log" when the trunk picks it up. The original Timber and its two
        // cut ends are only hidden while CityGen is active.

        public static void BuildLog(CityBlockout.Result result, CityGenSettings s)
        {
            var log = result.ctx.course.Find("Landmarks (4, decorative)/03 Fallen log");
            var timber = log.Find("Timber");
            var kit = new MeshKit(2); // 0 bark, 1 end grain
            const int sides = 20, rings = 6;
            var rnd = new System.Random(s.seed + 3);
            var radius = new float[sides];
            for (int i = 0; i < sides; i++) radius[i] = i % 2 == 0 ? .5f : .465f - (float)rnd.NextDouble() * .02f; // bark ridges
            for (int r = 0; r < rings; r++)
            {
                float y0 = -.5f + (float)r / rings, y1 = -.5f + (float)(r + 1) / rings; // meets the end caps exactly
                for (int i = 0; i < sides; i++)
                {
                    float a0 = i * Mathf.PI * 2 / sides, a1 = (i + 1) * Mathf.PI * 2 / sides;
                    Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                    float r0 = radius[i], r1 = radius[(i + 1) % sides];
                    Vector3 n = ((d0 + d1) / 2).normalized;
                    kit.AddQuad(0, d0 * r0 + Vector3.up * y0, d1 * r1 + Vector3.up * y0, d1 * r1 + Vector3.up * y1, d0 * r0 + Vector3.up * y1, n);
                }
            }
            kit.Disc(1, new Vector3(0, .5f, 0), .47f, sides, true);
            kit.Disc(1, new Vector3(0, -.5f, 0), .47f, sides, false);

            var detail = new GameObject(CityBlockout.AttachedPrefix + "Log detail");
            detail.transform.SetParent(timber, false);
            var filter = detail.AddComponent<MeshFilter>();
            filter.sharedMesh = CityBlockout.SaveMesh("Props/Log detail", kit.ToMesh("Log detail", out _));
            detail.AddComponent<MeshRenderer>().sharedMaterials = new[] { BarkMaterial(), EndGrainMaterial() };
            result.attached.Add(detail);
            result.hidden.Add(timber.GetComponent<Renderer>());
            foreach (Transform child in log)
                if (child.name == "Cut end") result.hidden.Add(child.GetComponent<Renderer>());
        }

        static Material BarkMaterial()
        {
            var tex = Texture("Log bark", 64, 256, (x, y) =>
            {
                float streak = Mathf.PerlinNoise(x * .18f, y * .02f), knot = Mathf.PerlinNoise(x * .08f + 7, y * .08f);
                return Color.Lerp(new Color(.24f, .15f, .08f), new Color(.46f, .32f, .19f), streak * .8f + (knot > .72f ? .3f : 0));
            });
            var m = CityBuildings.Mat("City Log Bark", Color.white, .1f);
            m.SetTexture("_BaseMap", tex); m.mainTexture = tex;
            m.SetTextureScale("_BaseMap", new Vector2(1.6f, .5f));
            return m;
        }

        static Material EndGrainMaterial()
        {
            var tex = Texture("Log end grain", 128, 128, (x, y) =>
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(64, 64)) / 64f;
                if (d > .94f) return new Color(.32f, .2f, .1f);                     // bark rim
                float ring = Mathf.Repeat(d * 9 + Mathf.PerlinNoise(x * .05f, y * .05f) * .6f, 1);
                return Color.Lerp(new Color(.86f, .7f, .48f), new Color(.7f, .52f, .32f), ring < .25f ? 1 : 0);
            });
            var m = CityBuildings.Mat("City Log End Grain", Color.white, .1f);
            m.SetTexture("_BaseMap", tex); m.mainTexture = tex;
            // Disc UVs are local x/z (−0.47…0.47): centre the rings on the cap.
            m.SetTextureScale("_BaseMap", new Vector2(1.06f, 1.06f));
            m.SetTextureOffset("_BaseMap", new Vector2(.5f, .5f));
            return m;
        }

        static Texture2D Texture(string name, int w, int h, System.Func<int, int, Color> pixel)
        {
            const string folder = "Assets/_CityGen/Textures";
            Directory.CreateDirectory(folder);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) tex.SetPixel(x, y, pixel(x, y));
            tex.Apply();
            string path = folder + "/" + name + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void AddMesh(Transform parent, string name, MeshKit kit, Material[] slotMaterials)
        {
            var mesh = CityBlockout.SaveMesh("Props/" + name, kit.ToMesh(name, out int[] used));
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var materials = new Material[used.Length];
            for (int i = 0; i < used.Length; i++) materials[i] = slotMaterials[used[i]];
            go.AddComponent<MeshRenderer>().sharedMaterials = materials;
        }
    }
}
