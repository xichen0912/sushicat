using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CityGen.EditorTools
{
    // Step 3: PBR materials, the asphalt road with lane paint, afternoon sun + blue sky, and URP post-processing
    // (ACES, light bloom, a little more saturation, warm balance) with SSAO on a city-only renderer.
    internal static class CityLook
    {
        const string RenderingFolder = "Assets/_CityGen/Rendering";
        const string CityRendererPath = RenderingFolder + "/CityURP_Renderer.asset";
        const string ProfilePath = RenderingFolder + "/CityPost.asset";
        const string SkyPath = "Assets/_CityGen/Materials/City Sky.mat";

        public static void Apply(CityBlockout.Result result, CityGenSettings s)
        {
            Directory.CreateDirectory(RenderingFolder);
            ApplyMaterials();
            Road(result);
            Lighting(result);
            int rendererIndex = EnsureCityRenderer();
            Camera(result, rendererIndex);
            PostVolume(result.root.transform);
        }

        // ---------------- Materials ----------------

        static void ApplyMaterials()
        {
            var brick = CityTextures.Brick();
            var stone = CityTextures.Stone();
            var concrete = CityTextures.Concrete();
            var asphalt = CityTextures.Asphalt();
            var roof = CityTextures.Roof();
            var planks = CityTextures.Planks();
            // name → (textures, tiling per metre, smoothness, metallic, normal strength)
            var map = new Dictionary<string, (CityTextures.Set set, float tiling, float smooth, float metal, float bump)>
            {
                ["City Brick"] = (brick, 1, .12f, 0, 1),
                ["City Brick Dark"] = (brick, 1, .12f, 0, 1),
                ["City Buff Brick"] = (brick, 1, .14f, 0, .9f),
                ["City Brownstone"] = (stone, 1, .15f, 0, .8f),
                ["City Limestone"] = (stone, 1, .2f, 0, .6f),
                ["City Gray Stone"] = (stone, 1, .18f, 0, .7f),
                ["City Trim"] = (stone, 2, .25f, 0, .4f),
                ["City Trim Stone"] = (stone, 2, .25f, 0, .4f),
                ["City Roof"] = (roof, 1, .1f, 0, .8f),
                ["City Wood"] = (planks, 2, .15f, 0, .8f),
                ["City Kerb"] = (stone, 2, .2f, 0, .5f),
                ["City Fountain Stone"] = (stone, 2, .3f, 0, .5f),
                ["City Plaza Stone"] = (concrete, .5f, .2f, 0, .7f),
                ["City Plaza Inlay"] = (stone, 1, .25f, 0, .6f),
                ["Blockout Sidewalk"] = (concrete, .5f, .18f, 0, .8f),
                ["Blockout Paving"] = (concrete, .5f, .15f, 0, .6f),
                ["Blockout Asphalt"] = (asphalt, .5f, .22f, 0, .5f),
            };
            foreach (var (name, (set, tiling, smooth, metal, bump)) in map)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(CityBlockout.MaterialFolder + "/" + name + ".mat");
                if (!m) continue; // not used by this layout
                m.SetTexture("_BaseMap", set.albedo); m.mainTexture = set.albedo;
                m.SetTextureScale("_BaseMap", Vector2.one * tiling);
                m.SetTexture("_BumpMap", set.normal); m.SetFloat("_BumpScale", bump); m.EnableKeyword("_NORMALMAP");
                m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
                EditorUtility.SetDirty(m);
            }
            Tune("City Glass", .92f, .25f);
            Tune("City Glass Curtain", .35f, 0);
            Tune("City Iron", .45f, .6f);
            Tune("City Chrome", .9f, 1);
            Tune("City Taxi Yellow", .65f, .1f);
            Tune("City Storefront", .5f, .4f);
            // Windows can glow at dusk (CityCelebration sets the colour per building); black = no glow by day.
            foreach (var name in new[] { "City Glass", "City Glass Curtain" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(CityBlockout.MaterialFolder + "/" + name + ".mat");
                if (!m) continue;
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                EditorUtility.SetDirty(m);
            }
        }

        static void Tune(string name, float smoothness, float metallic)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(CityBlockout.MaterialFolder + "/" + name + ".mat");
            if (!m) return;
            m.SetFloat("_Smoothness", smoothness); m.SetFloat("_Metallic", metallic);
            EditorUtility.SetDirty(m);
        }

        // ---------------- Road: asphalt + white paint (geometry untouched) ----------------

        static void Road(CityBlockout.Result result)
        {
            var ctx = result.ctx;
            var road = ctx.course.Find("Open path").GetComponent<MeshRenderer>();
            // The course mesh has no UVs (and must not be modified), so the road gets a plain asphalt colour.
            var asphalt = CityBuildings.Mat("City Asphalt Road", new Color(.20f, .20f, .21f), .25f);
            result.swaps.Add(new CityGenSwap.MaterialSwap { renderer = road, original = road.sharedMaterials, replacement = new[] { asphalt } });

            var kit = new MeshKit(1);
            float half = ctx.roadHalf, y = .046f; // just above the road surface, below the start line and arrows
            var route = ctx.route;
            for (int i = 1; i < route.Length; i++)
            {
                Vector3 a = route[i - 1], b = route[i]; a.y = b.y = y;
                float length = Vector3.Distance(a, b);
                Vector3 dir = (b - a) / length, n = Vector3.Cross(Vector3.up, dir);
                var rot = Quaternion.LookRotation(dir, Vector3.up);
                // Dashed centre line (1 m dash, 1 m gap), kept clear of corners and the start line.
                for (float t = 2.5f; t + 1f < length - 2.5f; t += 2f)
                    kit.Box(0, a + dir * (t + .5f), new Vector3(.1f, .004f, 1f), rot);
                // Solid edge lines inside the road edges.
                float trim = 1.6f;
                if (length > 2 * trim)
                    foreach (float side in new[] { -1f, 1f })
                        kit.Box(0, (a + b) / 2 + n * side * (half - .25f), new Vector3(.08f, .004f, length - 2 * trim), rot);
            }
            var go = new GameObject("Lane markings");
            go.transform.SetParent(result.root.transform.Find("Ground"), false);
            go.AddComponent<MeshFilter>().sharedMesh = CityBlockout.SaveMesh("Lane markings", kit.ToMesh("Lane markings", out _));
            go.AddComponent<MeshRenderer>().sharedMaterial = CityBuildings.Mat("City Road Paint", new Color(.93f, .92f, .88f), .3f);
        }

        // ---------------- Light, sky, ambient ----------------

        static void Lighting(CityBlockout.Result result)
        {
            // A warm afternoon sun from the south-west; the original key light is switched off while CityGen is on.
            var sunGo = new GameObject("City Sun");
            sunGo.transform.SetParent(result.root.transform, false);
            sunGo.transform.rotation = Quaternion.Euler(42, 38, 0);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, .91f, .78f);
            sun.intensity = 1.5f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = .82f;
            foreach (var root in result.scene.GetRootGameObjects())
                if (root.name == "Key Light" && root.TryGetComponent<Light>(out var key)) result.disabled.Add(key);

            var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
            if (!sky)
            {
                sky = new Material(Shader.Find("Skybox/Procedural")) { name = "City Sky" };
                AssetDatabase.CreateAsset(sky, SkyPath);
            }
            sky.SetFloat("_SunSize", .035f);
            sky.SetFloat("_SunSizeConvergence", 6);
            sky.SetFloat("_AtmosphereThickness", .85f);
            sky.SetColor("_SkyTint", new Color(.42f, .60f, .95f));
            sky.SetColor("_GroundColor", new Color(.55f, .53f, .50f));
            sky.SetFloat("_Exposure", 1.25f);
            EditorUtility.SetDirty(sky);

            // Scene-level settings of the City copy only.
            RenderSettings.skybox = sky;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.62f, .72f, .90f);
            RenderSettings.ambientEquatorColor = new Color(.66f, .62f, .56f);
            RenderSettings.ambientGroundColor = new Color(.36f, .32f, .28f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.fog = true;                                  // light haze gives the skyline depth
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(.76f, .83f, .92f);
            RenderSettings.fogStartDistance = 70;
            RenderSettings.fogEndDistance = 280;
            DynamicGI.UpdateEnvironment();
        }

        // ---------------- Renderer (SSAO, city only) and camera ----------------

        static UniversalRenderPipelineAsset Pipeline()
        {
            var current = QualitySettings.renderPipeline;
            if (!current) current = GraphicsSettings.defaultRenderPipeline; // Unity objects: no ?? (fake null)
            var asset = current as UniversalRenderPipelineAsset;
            if (!asset) throw new InvalidOperationException("The project is not using a URP pipeline asset.");
            return asset;
        }

        // Adds a city-only renderer (a copy of the project renderer plus SSAO) to the pipeline and returns its index.
        // Other scenes keep using renderer 0, unchanged.
        internal static int EnsureCityRenderer()
        {
            var pipeline = Pipeline();
            // HDR lets lit windows and bloom read properly; a longer shadow distance reaches the skyline.
            pipeline.supportsHDR = true;
            pipeline.shadowDistance = Mathf.Max(pipeline.shadowDistance, 90);
            EditorUtility.SetDirty(pipeline);

            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(CityRendererPath);
            if (!data)
            {
                var so0 = new SerializedObject(pipeline);
                var source = so0.FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue;
                if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), CityRendererPath))
                    throw new InvalidOperationException("Could not copy the project renderer.");
                data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(CityRendererPath);
            }
            if (!data.rendererFeatures.Any(f => f && f.GetType().Name == "ScreenSpaceAmbientOcclusion"))
            {
                // Created by name: the feature type is not public in every URP version.
                var type = typeof(UniversalRendererData).Assembly.GetType("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion");
                var ssao = (ScriptableRendererFeature)ScriptableObject.CreateInstance(type);
                ssao.name = "City SSAO";
                AssetDatabase.AddObjectToAsset(ssao, data);
                var so = new SerializedObject(data);
                var features = so.FindProperty("m_RendererFeatures");
                features.arraySize++;
                features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = ssao;
                var map = so.FindProperty("m_RendererFeatureMap");
                if (map != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ssao, out string _, out long id))
                {
                    map.arraySize++;
                    map.GetArrayElementAtIndex(map.arraySize - 1).longValue = id;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                var settings = new SerializedObject(ssao);
                SetFloat(settings, "m_Settings.Intensity", .9f);
                SetFloat(settings, "m_Settings.Radius", .25f);
                SetFloat(settings, "m_Settings.DirectLightingStrength", .3f);
                settings.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(data);
            }

            var list = new SerializedObject(pipeline).FindProperty("m_RendererDataList");
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == data) return i;
            var pso = new SerializedObject(pipeline);
            var renderers = pso.FindProperty("m_RendererDataList");
            renderers.arraySize++;
            renderers.GetArrayElementAtIndex(renderers.arraySize - 1).objectReferenceValue = data;
            pso.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            return renderers.arraySize - 1;
        }

        static void SetFloat(SerializedObject so, string path, float value)
        {
            var p = so.FindProperty(path);
            if (p != null && p.propertyType == SerializedPropertyType.Float) p.floatValue = value;
        }

        static void Camera(CityBlockout.Result result, int rendererIndex)
        {
            foreach (var root in result.scene.GetRootGameObjects())
            {
                if (root.name != "Main Camera" || !root.TryGetComponent<UnityEngine.Camera>(out var camera)) continue;
                camera.allowHDR = true;
                var data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.SetRenderer(rendererIndex);
                EditorUtility.SetDirty(data);
            }
        }

        // ---------------- Post-processing ----------------

        static void PostVolume(Transform root)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (!profile)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            T Get<T>() where T : VolumeComponent
            {
                if (profile.TryGet(out T existing)) return existing;
                var added = profile.Add<T>(true);
                added.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(added, profile);
                return added;
            }
            Get<Tonemapping>().mode.Override(TonemappingMode.ACES);
            var bloom = Get<Bloom>();
            bloom.threshold.Override(1.0f); bloom.intensity.Override(.35f); bloom.scatter.Override(.6f);
            var color = Get<ColorAdjustments>();
            color.postExposure.Override(.25f); color.contrast.Override(8); color.saturation.Override(12);
            Get<WhiteBalance>().temperature.Override(6);
            Get<Vignette>().intensity.Override(.16f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var go = new GameObject("Post Volume");
            go.transform.SetParent(root, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10;
            volume.sharedProfile = profile;
        }

        internal static int CityRendererIndex()
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(CityRendererPath);
            var list = new SerializedObject(Pipeline()).FindProperty("m_RendererDataList");
            for (int i = 0; i < list.arraySize; i++)
                if (data && list.GetArrayElementAtIndex(i).objectReferenceValue == data) return i;
            return 0;
        }
    }
}
