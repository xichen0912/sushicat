using System.Linq;
using DigiPhant;
using StudentWork;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CityGen.EditorTools
{
    // Step 3 (finish show): builds the assets and the "Finish Celebration" object that CityCelebration and
    // ElephantDance run in Play mode — dusk sky, dusk post-processing, firework / confetti / searchlight materials
    // and searchlight beams on the tallest towers.
    internal static class CityCelebrationSetup
    {
        const string DuskSkyPath = "Assets/_CityGen/Materials/City Dusk Sky.mat";
        const string DuskProfilePath = "Assets/_CityGen/Rendering/CityDusk.asset";

        public static void Build(CityBlockout.Result result)
        {
            var root = result.root.transform;
            var go = new GameObject("Finish Celebration");
            go.transform.SetParent(root, false);
            var celebration = go.AddComponent<CityCelebration>();
            var dance = go.AddComponent<ElephantDance>();

            var controller = result.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<DigiPhantController>(true)).FirstOrDefault();
            dance.controller = controller;
            dance.locomotion = controller ? controller.GetComponent<DigiPhantLocomotion>() : null;
            celebration.dance = dance;
            celebration.course = result.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ThreePersonCourseManager>(true)).FirstOrDefault();
            var sun = root.Find("City Sun");
            celebration.sun = sun ? sun.GetComponent<Light>() : null;
            celebration.daySky = RenderSettings.skybox;
            celebration.duskSkyTemplate = DuskSky();
            celebration.duskVolume = DuskVolume(root);
            celebration.fireworkMaterial = CityProps.SoftParticleMaterial("City Firework", Color.white, additive: true);
            celebration.confettiMaterial = CityProps.SoftParticleMaterial("City Confetti", Color.white);
            celebration.searchlights = Searchlights(root.Find("Buildings"));
            CityJazzSuit.Build(dance, go.transform);
            CityCasualOutfit.Build(dance, go.transform);
        }

        static Material DuskSky()
        {
            var shader = Shader.Find("CityGen/Gradient Sky");
            if (!shader) { Debug.LogWarning("CityGen: Gradient Sky shader not found; dusk keeps the day sky."); return null; }
            var m = AssetDatabase.LoadAssetAtPath<Material>(DuskSkyPath);
            if (!m) { m = new Material(shader) { name = "City Dusk Sky" }; AssetDatabase.CreateAsset(m, DuskSkyPath); }
            m.shader = shader;
            m.SetVector("_MoonDir", new Vector4(.42f, .40f, .81f, 0)); // north-east, in view of the course cameras
            m.SetFloat("_MoonSize", .085f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Volume DuskVolume(Transform root)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(DuskProfilePath);
            if (!profile) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, DuskProfilePath); }
            T Get<T>() where T : VolumeComponent
            {
                if (profile.TryGet(out T existing)) return existing;
                var added = profile.Add<T>(true);
                added.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(added, profile);
                return added;
            }
            var bloom = Get<Bloom>();
            bloom.threshold.Override(.85f); bloom.intensity.Override(1.1f); bloom.scatter.Override(.7f);
            var color = Get<ColorAdjustments>();
            color.saturation.Override(18); color.colorFilter.Override(new Color(1f, .94f, 1f)); color.postExposure.Override(.1f);
            var white = Get<WhiteBalance>();
            white.temperature.Override(-6); white.tint.Override(14);
            Get<Vignette>().intensity.Override(.24f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var go = new GameObject("Dusk Volume");
            go.transform.SetParent(root, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 20;   // over the day post volume, blended in by CityCelebration
            volume.weight = 0;
            volume.sharedProfile = profile;
            return volume;
        }

        // Beams on the six tallest towers; hidden until the celebration starts.
        static Transform[] Searchlights(Transform buildings)
        {
            if (!buildings) return new Transform[0];
            var settings = CityGenSettings.Load();
            var kit = new MeshKit(1);
            kit.Lathe(0, Vector3.zero, new[] { new Vector2(.12f, 0), new Vector2(2.4f, 38f) }, 16);
            var mesh = CityBlockout.SaveMesh("Props/Searchlight beam", kit.ToMesh("Searchlight beam", out _));
            var material = CityProps.SoftParticleMaterial("City Searchlight", new Color(1f, .92f, .8f, .16f), additive: true);
            var towers = buildings.GetComponentsInChildren<CityBuildingInfo>()
                .Where(b => b.style == CityBuildingInfo.Style.Tower).OrderByDescending(b => b.floors).ThenBy(b => b.name).Take(6);
            return towers.Select(tower =>
            {
                float roof = (4f + (tower.floors - 1) * 3.2f) * settings.heightScale;
                var beam = new GameObject("Searchlight");
                beam.transform.SetParent(tower.transform, false);
                beam.transform.localPosition = Vector3.up * roof;
                beam.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = beam.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = ShadowCastingMode.Off;
                beam.SetActive(false);
                return beam.transform;
            }).ToArray();
        }
    }
}
