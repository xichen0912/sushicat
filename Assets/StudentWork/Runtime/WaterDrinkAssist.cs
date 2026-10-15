using System;
using DigiPhant;
using UnityEngine;

namespace StudentWork
{
    // Play-only helpers for the water stage, attached automatically beside each WaterCupInteraction.
    // 1. Snap: during the course's Water stage, an elephant that comes within snapRadius of the basin
    //    is pulled to a drinking spot inside the original water gate and turned to face the basin,
    //    then held there for P3's fist. Driving backward releases it.
    // 2. Spray: each accepted water completion sprays water from the trunk tip (press P to preview).
    // Runs after the controller has moved the elephant and before the course manager (580) reads it.
    // Purely visual/positional help; it never marks the water interaction complete by itself.
    [DefaultExecutionOrder(550)]
    public sealed class WaterDrinkAssist : MonoBehaviour
    {
        public bool snapToWater = true;
        [Tooltip("Water and log: within this distance of the basin/log the elephant is pulled to the interaction spot.")]
        [Min(1)] public float snapRadius = 10;
        [Tooltip("Finish: within this distance of the finish zone centre the elephant is pulled in and the course completes.")]
        [Min(1)] public float finishSnapRadius = 7;
        [Min(.1f)] public float pullSpeed = 2.5f;
        [Min(10)] public float turnDegreesPerSecond = 120;
        public bool sprayOnSuccess = true;
        [Min(.5f)] public float spraySeconds = 2.2f;
        [Tooltip("Spray pose: rotation of each trunk bone (base to tip) about the Trunk curl axis. Negative lifts the "
            + "trunk forward and up; positive curls it back under the face. Checked against the elephant prefab: the tip "
            + "ends above and in front of the head, pointing about 38 degrees above the horizon.")]
        public float[] sprayTrunkPose = { -50, -40, -30, -20, -15, -10, -5 };
        [Min(.05f)] public float trunkLiftSeconds = .45f;

        WaterCupInteraction water;
        ThreePersonCourseManager course;
        Transform trunkTip;
        BoneControl trunk;
        bool captured, released, arrived;
        int snapStage, seenCourseDrinks;
        int seenCompletion;
        ParticleSystem jet, mist;
        float sprayStarted = -1, lift;
        bool emitting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AttachToSceneWater()
        {
            foreach (var found in FindObjectsByType<WaterCupInteraction>(FindObjectsSortMode.None))
                if (!found.GetComponent<WaterDrinkAssist>()) found.gameObject.AddComponent<WaterDrinkAssist>();
        }

        void OnEnable()
        {
            water = GetComponent<WaterCupInteraction>();
            foreach (var manager in FindObjectsByType<ThreePersonCourseManager>(FindObjectsSortMode.None))
                if (manager.waterInteraction == water) { course = manager; break; }
            seenCompletion = water && water.LatestCompletion != null ? water.LatestCompletion.revision : 0;
        }

        void LateUpdate()
        {
            if (!water) return;
            if (snapToWater) Snap(Time.deltaTime);
            var completion = water.LatestCompletion;
            if (completion != null && completion.revision > seenCompletion)
            {
                seenCompletion = completion.revision;
                // Solo practice has no course gate; in the course only a gated, accepted drink sprays.
                if (sprayOnSuccess && (!completion.courseGateEnabled || completion.courseGateAllowed)) Spray();
            }
            // The course can also complete the water stage directly from P3's nearby fist.
            if (course && course.AcceptedTeamCupGestures != seenCourseDrinks)
            {
                if (course.AcceptedTeamCupGestures > seenCourseDrinks && sprayOnSuccess) Spray();
                seenCourseDrinks = course.AcceptedTeamCupGestures;
            }
            if (Input.GetKeyDown(KeyCode.P)) Spray();
            UpdateSpray();
        }

        void Snap(float dt)
        {
            var locomotion = water.locomotion;
            var root = locomotion ? locomotion.travelRoot : null;
            bool active = course && !course.SoloPracticePaused;
            bool waterStage = active && course.CurrentStage == CourseStage.Water && course.waterBasin;
            // The log stage gets the same help: stop beside the log, facing it, for P3's pickup fist.
            bool logStage = active && course.enableTrunkLogPickup && course.CurrentStage == CourseStage.Log
                && !course.LogPickedUp && course.pickupLogTransform;
            // The finish gets it too: pull into the finish zone, where the course completes on arrival.
            bool finishStage = active && course.CurrentStage == CourseStage.Finish && course.finishZone;
            int stage = waterStage ? 1 : logStage ? 2 : finishStage ? 3 : 0;
            if (stage != snapStage) { captured = arrived = released = false; snapStage = stage; }
            if (course) course.AssistHolding = false;
            if (stage == 0 || !root || dt <= 0) { captured = arrived = false; return; }

            Vector3 anchor = stage == 1 ? course.waterBasin.position : stage == 2 ? course.pickupLogTransform.position
                : course.finishZone.bounds.center;
            Vector3 position = root.position;
            Vector3 away = position - anchor; away.y = 0;
            float distance = away.magnitude;
            float radius = stage == 3 ? finishSnapRadius : snapRadius;
            if (released && distance > radius + 1) released = false; // re-arm after leaving
            if (!captured && !released && distance <= radius) captured = true;
            if (!captured) { arrived = false; return; }
            // Only a deliberate, confirmed reverse (P1 holds the left hand low) leaves the spot.
            if (locomotion.CurrentSpeed < -.05f) { captured = arrived = false; released = true; return; }

            Vector3 direction = distance > .01f ? away / distance : -root.forward;
            Vector3 target = position;
            if (stage == 2)
            {
                target = anchor + direction * course.pickupApproachDistance * .6f;
                target.y = position.y;
            }
            else if (stage == 3)
            {
                target = anchor;
                target.y = position.y;
            }
            else
            {
                bool found = false;
                foreach (float share in new[] { .6f, .4f, .2f })
                {
                    Vector3 candidate = anchor + direction * course.maximumWaterBasinDistance * share;
                    candidate.y = position.y;
                    if (course.IsInsideWaterInteractionArea(candidate)) { target = candidate; found = true; break; }
                }
                if (!found && course.waterZone)
                {
                    // Approached from a side outside the drinking box: use the side facing the box centre instead.
                    Vector3 toZone = course.waterZone.bounds.center - anchor; toZone.y = 0;
                    if (toZone.sqrMagnitude > .0001f)
                        foreach (float share in new[] { .6f, .4f, .2f })
                        {
                            Vector3 candidate = anchor + toZone.normalized * course.maximumWaterBasinDistance * share;
                            candidate.y = position.y;
                            if (course.IsInsideWaterInteractionArea(candidate)) { target = candidate; break; }
                        }
                }
            }
            // Arrived: hold exactly on the spot. Forward/neutral hand input is cancelled so the elephant
            // idles; a lowered hand is left alone so the reverse can still be confirmed and release it.
            if (arrived && locomotion.DesiredForward >= 0) locomotion.StopMotion();
            Vector3 next = arrived ? target : Vector3.MoveTowards(position, target, pullSpeed * dt);
            Vector3 facing = stage == 3 ? root.forward : anchor - next; facing.y = 0;
            Quaternion turn = facing.sqrMagnitude > .0001f ? Quaternion.LookRotation(facing, Vector3.up) : root.rotation;
            root.SetPositionAndRotation(next, Quaternion.RotateTowards(root.rotation, turn, turnDegreesPerSecond * dt));
            if (!arrived) arrived = Vector3.Distance(next, target) < .05f && Quaternion.Angle(root.rotation, turn) < 3;
            course.AssistHolding = arrived;
        }

        void Spray()
        {
            if (!EnsureSpray()) return;
            sprayStarted = Time.time; // the trunk lifts first, then water leaves the raised tip
        }

        // Runs after the controller has reset and posed the trunk this frame, so the lift is
        // added on top of P3's own trunk curl without accumulating between frames.
        void UpdateSpray()
        {
            if (!jet || trunk == null || trunk.bones == null || trunk.bones.Length < 2) return;
            float elapsed = sprayStarted >= 0 ? Time.time - sprayStarted : float.PositiveInfinity;
            bool raising = elapsed < trunkLiftSeconds + spraySeconds;
            lift = Mathf.MoveTowards(lift, raising ? 1 : 0, Time.deltaTime / trunkLiftSeconds);
            if (!raising && lift <= 0) sprayStarted = -1;
            if (lift > 0 && sprayTrunkPose != null)
            {
                // The controller has just added P3's curl (current * degrees per bone) about this axis.
                // Blend that curl out and the spray pose in, so the trunk really lifts forward instead of
                // curling back under the face.
                Vector3 axis = trunk.localAxis.sqrMagnitude > .0001f ? trunk.localAxis.normalized : Vector3.forward;
                float p3Curl = trunk.current * trunk.degrees;
                for (int i = 0; i < trunk.bones.Length; i++)
                {
                    var bone = trunk.bones[i];
                    if (!bone) continue;
                    float pose = i < sprayTrunkPose.Length ? sprayTrunkPose[i] : 0;
                    bone.localRotation *= Quaternion.AngleAxis((pose - p3Curl) * lift, axis);
                }
            }
            // Spray along the raised trunk's own direction, from its tip.
            var last = trunk.bones[trunk.bones.Length - 1];
            var before = trunk.bones[trunk.bones.Length - 2];
            Vector3 along = trunkTip && trunkTip != last ? trunkTip.position - last.position : last.position - before.position;
            var root = water.locomotion ? water.locomotion.travelRoot : null;
            Vector3 forward = root ? root.forward : transform.forward;
            Vector3 direction = along.sqrMagnitude > .000001f ? along.normalized : forward;
            if (direction.y < .25f || Vector3.Dot(direction, forward) < -.2f)
                direction = (direction + forward * .5f + Vector3.up).normalized; // never back through the head
            var aim = Quaternion.LookRotation(direction, Vector3.up);
            Vector3 origin = trunkTip ? trunkTip.position : last.position;
            jet.transform.SetPositionAndRotation(origin, aim);
            mist.transform.SetPositionAndRotation(origin, aim);
            bool shouldEmit = raising && lift >= .9f;
            if (shouldEmit && !emitting) { jet.Play(); mist.Play(); }
            if (!shouldEmit && emitting)
            {
                jet.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                mist.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            emitting = shouldEmit;
        }

        bool EnsureSpray()
        {
            if (jet) return true;
            trunk = FindTrunk(water.controller);
            trunkTip = course && course.pickupTrunkTip ? course.pickupTrunkTip : FindTrunkTip(trunk);
            if (!trunkTip || trunk == null) return false;
            var material = SprayMaterial();
            jet = CreateEmitter("Trunk water jet", material, 220, new Vector2(1.1f, 1.6f), new Vector2(5.5f, 7.5f),
                new Vector2(.07f, .18f), .9f, 8, new Color(.78f, .92f, 1f, .95f));
            mist = CreateEmitter("Trunk water mist", material, 45, new Vector2(1.2f, 1.8f), new Vector2(2f, 3.2f),
                new Vector2(.35f, .8f), .35f, 20, new Color(.85f, .95f, 1f, .28f));
            return true;
        }

        ParticleSystem CreateEmitter(string name, Material material, float rate, Vector2 lifetime, Vector2 speed,
            Vector2 size, float gravity, float coneAngle, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.x, lifetime.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.gravityModifier = gravity;
            main.startColor = color;
            main.maxParticles = 800;
            var emission = system.emission;
            emission.rateOverTime = rate;
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = coneAngle;
            shape.radius = .05f;
            var fade = system.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(.8f, .6f), new GradientAlphaKey(0, 1) });
            fade.color = gradient;
            var grow = system.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .7f, 1, 1.6f));
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            return system;
        }

        static Material SprayMaterial()
        {
            // Soft round droplet generated at runtime so no texture asset is needed.
            const int sizePx = 32;
            var texture = new Texture2D(sizePx, sizePx, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < sizePx; y++)
                for (int x = 0; x < sizePx; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(sizePx / 2f, sizePx / 2f)) / (sizePx / 2f);
                    texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(1 - d * d)));
                }
            texture.Apply();
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (!shader) shader = Shader.Find("Sprites/Default");
            var material = new Material(shader) { name = "Trunk spray (runtime)" };
            material.mainTexture = texture;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_Surface"))
            {
                // URP particle shader: transparent alpha blending.
                material.SetFloat("_Surface", 1);
                material.SetFloat("_Blend", 0);
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            return material;
        }

        static BoneControl FindTrunk(DigiPhantController controller)
        {
            if (!controller) return null;
            foreach (var control in controller.controls)
                if (control != null && control.label == "Trunk curl" && control.bones != null && control.bones.Length > 0) return control;
            return null;
        }

        static Transform FindTrunkTip(BoneControl trunk)
        {
            var last = trunk != null ? trunk.bones[trunk.bones.Length - 1] : null;
            if (!last) return null;
            for (int i = 0; i < last.childCount; i++)
                if (last.GetChild(i).name.IndexOf("nub", StringComparison.OrdinalIgnoreCase) >= 0) return last.GetChild(i);
            return last;
        }

        void OnGUI()
        {
            // Test sliders only: an on-screen stand-in for P3's right fist (same as the Inspector's
            // "Test Right Fist Closed"), so water/log can be tried without a camera.
            var controller = water ? water.controller : null;
            if (controller && controller.inputMode == InputMode.TestSliders)
            {
                string label = controller.testRightFistClosed ? "模拟握拳：已握拳（点一下松开）" : "模拟握拳：张开（点一下握拳）";
                if (GUI.Button(new Rect(Screen.width * .5f - 120, 96, 240, 30), label))
                {
                    controller.testFistConfidence = Mathf.Max(controller.testFistConfidence, .9f);
                    controller.testRightFistClosed = !controller.testRightFistClosed;
                }
            }
            if (!captured) return;
            string text = snapStage == 2
                ? (arrived ? "已停在木头旁：P3 把手伸进绿色框就能捡木头" : "正在自动靠近木头…P3 把手伸进绿色框就能直接捡")
                : snapStage == 3 ? "正在自动进入终点…"
                : (arrived ? "已停在水边：P3 把手伸进绿色框就能喝水" : "正在自动靠近水边…P3 把手伸进绿色框就能直接喝");
            var style = new GUIStyle(GUI.skin.box) { wordWrap = true, fontSize = 14 };
            GUI.Box(new Rect(Screen.width * .5f - 230, 44, 460, 46), text, style);
        }
    }
}
