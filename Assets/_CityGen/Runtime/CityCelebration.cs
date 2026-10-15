using System.Collections.Generic;
using StudentWork;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityGen
{
    // Finish-line show for the city level (Play mode only):
    //   1. From the moment the course reaches its Finish stage, the afternoon slowly turns into a purple-pink
    //      dusk (faster as the elephant nears the finish line): low warm sun, gradient sky with moon and stars,
    //      pink-violet ambient and haze, warmer post-processing, and windows / billboards lighting up building by
    //      building.
    //   2. Crossing the finish line (Course Complete) completes the dusk and starts the celebration: fireworks
    //      over the skyline, confetti, sweeping searchlights, an orbiting camera and the elephant's dance, which
    //      keeps going until the course is restarted. Press F (or the on-screen button) to preview.
    // Material assets are never edited: glow uses per-renderer property blocks and the sky a material instance.
    public sealed class CityCelebration : MonoBehaviour
    {
        public ThreePersonCourseManager course;
        public ElephantDance dance;
        public Light sun;
        public Material daySky, duskSkyTemplate;
        public Volume duskVolume;
        public Material fireworkMaterial, confettiMaterial;
        public Transform[] searchlights = new Transform[0];
        [Tooltip("Seconds from entering the Finish stage until the dusk is complete.")]
        public float finishRampSeconds = 35;
        [Tooltip("The dusk also completes as the elephant comes within this distance of the finish line.")]
        public float approachDistance = 25;
        [Tooltip("Music that starts when the course is completed at the finish (and with the F preview), loops during the celebration and fades out on restart.")]
        public AudioClip finishMusic;
        [Range(0, 1)] public float finishMusicVolume = .8f;
        [Tooltip("Seconds to fade the finish music in and out.")]
        public float finishMusicFadeSeconds = 1f;

        [Header("Dusk palette")]
        public Color duskTop = new Color(.16f, .12f, .36f), duskMid = new Color(.50f, .27f, .60f), duskHorizon = new Color(1f, .58f, .50f);
        public Color duskSun = new Color(1f, .56f, .42f), duskAmbientSky = new Color(.50f, .40f, .74f),
            duskAmbientEquator = new Color(.86f, .52f, .60f), duskAmbientGround = new Color(.24f, .19f, .30f), duskFog = new Color(.58f, .42f, .64f);

        static readonly Color DayTop = new Color(.32f, .52f, .88f), DayMid = new Color(.50f, .68f, .93f), DayHorizon = new Color(.78f, .86f, .96f);
        static readonly Color WindowGlow = new Color(1f, .72f, .42f);

        float t, finishStarted = -1;
        bool preview, celebrating;
        AudioSource finishMusicSource;
        Material sky;
        Quaternion daySunRotation, duskSunRotation;
        Color daySunColor, dayAmbientSky, dayAmbientEquator, dayAmbientGround, dayFog;
        float daySunIntensity, dayFogStart, dayFogEnd;
        float lastApplied = -1;
        readonly List<(Renderer renderer, int index, float threshold, float strength)> glows = new List<(Renderer, int, float, float)>();
        MaterialPropertyBlock block;
        readonly List<ParticleSystem> rockets = new List<ParticleSystem>();
        ParticleSystem confetti;
        StageSpotlights stage;
        float nextLaunch;
        int nextRocket;
        System.Random rnd = new System.Random(1930);

        void Start()
        {
            if (!course) course = FindAnyObjectByType<ThreePersonCourseManager>();
            block = new MaterialPropertyBlock();
            if (duskSkyTemplate) sky = new Material(duskSkyTemplate);
            if (sun)
            {
                daySunRotation = sun.transform.rotation; daySunColor = sun.color; daySunIntensity = sun.intensity;
            }
            // Dusk sun low in the north-west, so the glow sits behind the skyline seen from the course.
            duskSunRotation = Quaternion.LookRotation(new Vector3(.55f, -.08f, -.83f), Vector3.up);
            dayAmbientSky = RenderSettings.ambientSkyColor; dayAmbientEquator = RenderSettings.ambientEquatorColor;
            dayAmbientGround = RenderSettings.ambientGroundColor; dayFog = RenderSettings.fogColor;
            dayFogStart = RenderSettings.fogStartDistance; dayFogEnd = RenderSettings.fogEndDistance;
            CollectGlows();
            foreach (var light in searchlights) if (light) light.gameObject.SetActive(false);
            BuildFireworks();
            if (dance && course && !dance.finishZone) dance.finishZone = course.finishZone;
            // Stage spotlights on the dancing elephant (built at runtime; reuse the additive glow material).
            if (fireworkMaterial && course && course.locomotion && course.locomotion.travelRoot)
            {
                stage = gameObject.AddComponent<StageSpotlights>();
                stage.Init(fireworkMaterial, dance, course.locomotion.travelRoot);
            }
            Apply(0, true);
        }

        void CollectGlows()
        {
            foreach (var r in transform.root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                var materials = r.sharedMaterials;
                // Each building lights up at its own point in the dusk.
                float threshold = (float)rnd.NextDouble() * .7f;
                for (int i = 0; i < materials.Length; i++)
                {
                    var m = materials[i];
                    if (!m) continue;
                    if (m.name == "City Glass") glows.Add((r, i, threshold, .9f));
                    else if (m.name == "City Glass Curtain") glows.Add((r, i, threshold, .55f));
                    else if (m.name == "City Glass Lit") glows.Add((r, i, 0, 2.6f));
                    else if (m.name.StartsWith("City Poster")) glows.Add((r, i, 0, 1.3f));
                    else if (m.name == "City Lamp Glass") glows.Add((r, i, .2f, 3f));
                }
            }
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F)) preview = !preview;
            var stage = course ? course.CurrentStage : CourseStage.Tree;
            bool finishStage = stage == CourseStage.Finish, complete = stage == CourseStage.Complete;
            if (finishStage || complete) { if (finishStarted < 0) finishStarted = Time.time; }
            else finishStarted = -1;

            float target = 0;
            if (preview || complete) target = 1;
            else if (finishStage)
            {
                float byTime = (Time.time - finishStarted) / Mathf.Max(1, finishRampSeconds);
                float byDistance = 0;
                if (course.finishZone && course.locomotion && course.locomotion.travelRoot)
                {
                    float d = Vector3.Distance(Flat(course.locomotion.travelRoot.position), Flat(course.finishZone.bounds.center));
                    byDistance = 1 - d / Mathf.Max(1, approachDistance);
                }
                target = Mathf.Clamp(Mathf.Max(byTime, byDistance), 0, .9f); // the last 10 % arrives at the finish line
            }
            t = Mathf.MoveTowards(t, target, Time.deltaTime * .5f); // at most 2 s for a full change
            Apply(t, false);

            bool shouldCelebrate = preview || complete;
            if (shouldCelebrate != celebrating) SetCelebrating(shouldCelebrate);
            if (celebrating) Celebrate();
            UpdateFinishMusic();
        }

        // Starts from the beginning each time the celebration starts; fades out when it ends.
        void UpdateFinishMusic()
        {
            if (!finishMusicSource) return;
            float step = Time.deltaTime * finishMusicVolume / Mathf.Max(.05f, finishMusicFadeSeconds);
            finishMusicSource.volume = Mathf.MoveTowards(finishMusicSource.volume, celebrating ? finishMusicVolume : 0, step);
            if (!celebrating && finishMusicSource.volume <= 0 && finishMusicSource.isPlaying) finishMusicSource.Stop();
        }

        void StartFinishMusic()
        {
            if (!finishMusic) return;
            if (!finishMusicSource)
            {
                finishMusicSource = gameObject.AddComponent<AudioSource>();
                finishMusicSource.playOnAwake = false;
                finishMusicSource.loop = true;
                finishMusicSource.spatialBlend = 0; // 2D: same volume wherever the camera is
            }
            finishMusicSource.clip = finishMusic;
            finishMusicSource.volume = 0;
            finishMusicSource.Play();
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

        void Apply(float k, bool force)
        {
            if (!force && Mathf.Abs(k - lastApplied) < .002f) return;
            lastApplied = k;
            float e = Mathf.SmoothStep(0, 1, k);
            if (sun)
            {
                sun.transform.rotation = Quaternion.Slerp(daySunRotation, duskSunRotation, e);
                sun.color = Color.Lerp(daySunColor, duskSun, e);
                sun.intensity = Mathf.Lerp(daySunIntensity, .6f, e);
            }
            if (sky)
            {
                RenderSettings.skybox = k > .001f || !daySky ? sky : daySky;
                sky.SetColor("_TopColor", Color.Lerp(DayTop, duskTop, e));
                sky.SetColor("_MidColor", Color.Lerp(DayMid, duskMid, e));
                sky.SetColor("_HorizonColor", Color.Lerp(DayHorizon, duskHorizon, e));
                sky.SetColor("_SunColor", Color.Lerp(new Color(1f, .9f, .75f) * .6f, duskSun * 1.3f, e));
                if (sun) sky.SetVector("_SunDir", -sun.transform.forward);
                sky.SetFloat("_MoonAlpha", Mathf.SmoothStep(0, 1, (k - .55f) / .45f));
                sky.SetFloat("_StarAlpha", Mathf.SmoothStep(0, 1, (k - .7f) / .3f) * .8f);
            }
            RenderSettings.ambientSkyColor = Color.Lerp(dayAmbientSky, duskAmbientSky, e);
            RenderSettings.ambientEquatorColor = Color.Lerp(dayAmbientEquator, duskAmbientEquator, e);
            RenderSettings.ambientGroundColor = Color.Lerp(dayAmbientGround, duskAmbientGround, e);
            RenderSettings.fogColor = Color.Lerp(dayFog, duskFog, e);
            RenderSettings.fogStartDistance = Mathf.Lerp(dayFogStart, 40, e);
            RenderSettings.fogEndDistance = Mathf.Lerp(dayFogEnd, 210, e);
            if (duskVolume) duskVolume.weight = e;

            foreach (var (renderer, index, threshold, strength) in glows)
            {
                if (!renderer) continue;
                if (k <= .001f) { renderer.SetPropertyBlock(null, index); continue; } // back to the shared material
                float on = Mathf.SmoothStep(0, 1, (k - threshold) / .25f);
                renderer.GetPropertyBlock(block, index);
                var baseEmission = renderer.sharedMaterials[index].GetColor("_EmissionColor");
                block.SetColor("_EmissionColor", Color.Lerp(baseEmission, WindowGlow * strength, on));
                renderer.SetPropertyBlock(block, index);
            }
        }

        void SetCelebrating(bool on)
        {
            celebrating = on;
            HighFollowCamera.Orbit = on;
            if (stage) stage.SetOn(on);
            foreach (var light in searchlights) if (light) light.gameObject.SetActive(on);
            if (on) { if (dance) dance.Begin(); nextLaunch = Time.time + .5f; if (confetti) confetti.Play(); StartFinishMusic(); }
            else { if (dance) dance.End(); if (confetti) confetti.Stop(true, ParticleSystemStopBehavior.StopEmitting); }
        }

        void Celebrate()
        {
            for (int i = 0; i < searchlights.Length; i++)
                if (searchlights[i])
                    searchlights[i].localRotation = Quaternion.Euler(Mathf.Sin(Time.time * .5f + i) * 22, Time.time * 18 + i * 60, 0);
            if (confetti && dance && course && course.locomotion && course.locomotion.travelRoot)
                confetti.transform.position = course.locomotion.travelRoot.position + Vector3.up * 12;
            if (Time.time >= nextLaunch && rockets.Count > 0)
            {
                Launch(rockets[nextRocket]);
                nextRocket = (nextRocket + 1) % rockets.Count;
                nextLaunch = Time.time + Range(.3f, .9f);
            }
        }

        // ---------------- Fireworks and confetti (built at runtime, pooled) ----------------

        static readonly Color[] FireworkColors =
        {
            new Color(1f, .45f, .7f), new Color(1f, .82f, .35f), new Color(.45f, .9f, 1f), new Color(.75f, .5f, 1f), new Color(1f, .55f, .3f)
        };

        void BuildFireworks()
        {
            if (!fireworkMaterial) return;
            for (int i = 0; i < 10; i++)
            {
                var go = new GameObject("Firework " + i);
                go.transform.SetParent(transform, false);
                var rocket = go.AddComponent<ParticleSystem>();
                rocket.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = rocket.main;
                main.loop = false; main.playOnAwake = false; main.duration = .1f;
                // Bursts at about 16–20 m: well above the 3–6 m streets, low enough to stay in the orbit camera's frame.
                main.startLifetime = new ParticleSystem.MinMaxCurve(.95f, 1.2f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(16f, 20f);
                main.startSize = .35f;
                main.gravityModifier = .6f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var emission = rocket.emission; emission.rateOverTime = 0;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0, 1) });
                var shape = rocket.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 6; shape.radius = .05f; shape.rotation = new Vector3(-90, 0, 0);
                var trails = rocket.trails; trails.enabled = true; trails.lifetime = .25f; trails.widthOverTrail = .5f;
                var renderer = go.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = fireworkMaterial; renderer.trailMaterial = fireworkMaterial;

                var burstGo = new GameObject("Burst");
                burstGo.transform.SetParent(go.transform, false);
                var burst = burstGo.AddComponent<ParticleSystem>();
                burst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var bm = burst.main;
                bm.loop = false; bm.playOnAwake = false; bm.duration = .2f;
                bm.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.1f);
                bm.startSpeed = new ParticleSystem.MinMaxCurve(5f, 8f);
                bm.startSize = new ParticleSystem.MinMaxCurve(.25f, .45f);
                bm.gravityModifier = .35f;
                bm.maxParticles = 300;
                bm.simulationSpace = ParticleSystemSimulationSpace.World;
                var be = burst.emission; be.rateOverTime = 0; be.SetBursts(new[] { new ParticleSystem.Burst(0, 140) });
                var bs = burst.shape; bs.shapeType = ParticleSystemShapeType.Sphere; bs.radius = .1f;
                var drag = burst.limitVelocityOverLifetime; drag.enabled = true; drag.drag = 1.2f;
                var fade = burst.colorOverLifetime; fade.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                    new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(.9f, .5f), new GradientAlphaKey(0, 1) });
                fade.color = gradient;
                var bt = burst.trails; bt.enabled = true; bt.lifetime = .3f; bt.widthOverTrail = .4f;
                var br = burstGo.GetComponent<ParticleSystemRenderer>();
                br.sharedMaterial = fireworkMaterial; br.trailMaterial = fireworkMaterial;
                var subs = rocket.subEmitters; subs.enabled = true;
                subs.AddSubEmitter(burst, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing);
                rockets.Add(rocket);
            }

            if (!confettiMaterial) return;
            var c = new GameObject("Confetti");
            c.transform.SetParent(transform, false);
            confetti = c.AddComponent<ParticleSystem>();
            confetti.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var cm = confetti.main;
            cm.loop = true; cm.playOnAwake = false;
            cm.startLifetime = 7; cm.startSpeed = .3f;
            cm.startSize = new ParticleSystem.MinMaxCurve(.12f, .2f);
            cm.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            cm.gravityModifier = .06f;
            cm.simulationSpace = ParticleSystemSimulationSpace.World;
            cm.maxParticles = 600;
            cm.startColor = new ParticleSystem.MinMaxGradient(Color.white, new Color(1f, .8f, .3f)) { mode = ParticleSystemGradientMode.TwoColors }; // white and gold
            var ce = confetti.emission; ce.rateOverTime = 60;
            var cs = confetti.shape; cs.shapeType = ParticleSystemShapeType.Box; cs.scale = new Vector3(16, 1, 16);
            var spin = confetti.rotationOverLifetime; spin.enabled = true; spin.z = new ParticleSystem.MinMaxCurve(-3, 3);
            var noise = confetti.noise; noise.enabled = true; noise.strength = .6f; noise.frequency = .4f;
            confetti.GetComponent<ParticleSystemRenderer>().sharedMaterial = confettiMaterial;
        }

        void Launch(ParticleSystem rocket)
        {
            // In front of the camera (within ±60° of where it looks), 18–30 m beyond the elephant: over the low
            // streets and in front of the taller skyline, so no tower hides a burst.
            var cam = Camera.main;
            Vector3 centre = course && course.locomotion && course.locomotion.travelRoot ? course.locomotion.travelRoot.position : Vector3.zero;
            Vector3 forward = cam ? cam.transform.forward : Vector3.forward; forward.y = 0;
            float facing = Mathf.Atan2(forward.z, forward.x) + Range(-1.05f, 1.05f), distance = Range(18, 30);
            rocket.transform.position = new Vector3(centre.x + Mathf.Cos(facing) * distance, Range(1, 4), centre.z + Mathf.Sin(facing) * distance);
            var burst = rocket.transform.GetChild(0).GetComponent<ParticleSystem>();
            var colour = FireworkColors[rnd.Next(FireworkColors.Length)];
            var main = burst.main; main.startColor = colour;
            var rocketMain = rocket.main; rocketMain.startColor = Color.Lerp(colour, Color.white, .6f);
            rocket.Play(true);
        }

        float Range(float min, float max) => min + (float)rnd.NextDouble() * (max - min);

        void OnDisable()
        {
            if (!Application.isPlaying) return;
            if (celebrating) SetCelebrating(false);
            foreach (var (renderer, index, _, _) in glows) if (renderer) renderer.SetPropertyBlock(null, index);
            if (daySky) RenderSettings.skybox = daySky;
            HighFollowCamera.Orbit = false;
        }

    }
}
