using UnityEngine;

namespace CityGen
{
    // Three stage spotlights on the dancing elephant, mounted on the roofs of distant buildings (the tallest one
    // 22–60 m away in each of three directions 120° apart). Real URP spot lights light the elephant; faint haze
    // beams fade out before they reach it, so the elephant itself stays clearly visible. Built at runtime by
    // CityCelebration; fades in with the celebration and pulses gently on the dance beat.
    [DefaultExecutionOrder(570)] // after ElephantDance has moved the elephant this frame
    public sealed class StageSpotlights : MonoBehaviour
    {
        static readonly Color[] Colors = { new Color(1f, .95f, .85f), new Color(1f, .55f, .82f), new Color(.55f, .85f, 1f) };
        const float SpotAngle = 16, BeamAlpha = .22f, LampSize = 2.6f;
        // Brightness on the elephant (the dusk sun is 0.6). The light intensity is scaled by the roof distance to
        // cancel URP's inverse-square and range falloff, so it is the same whichever building the lamp is on.
        const float Illuminance = 1.3f, RangeFactor = 1.5f;
        const float MinDistance = 22, MaxDistance = 60;

        Transform target;
        ElephantDance dance;
        Light[] lights;
        Transform[] beams;
        Renderer[] beamRenderers, lampRenderers;
        Transform[] lamps;
        Vector3[] mounts;
        MaterialPropertyBlock block;
        float fade, wanted;

        public void Init(Material additive, ElephantDance dance, Transform target)
        {
            this.dance = dance; this.target = target;
            block = new MaterialPropertyBlock();
            var beamMesh = Cone();
            lights = new Light[Colors.Length]; beams = new Transform[Colors.Length]; beamRenderers = new Renderer[Colors.Length];
            lamps = new Transform[Colors.Length]; lampRenderers = new Renderer[Colors.Length];
            mounts = new Vector3[Colors.Length];
            for (int i = 0; i < Colors.Length; i++)
            {
                var light = new GameObject("Stage spot " + i).AddComponent<Light>();
                light.transform.SetParent(transform, false);
                light.type = LightType.Spot;
                light.color = Colors[i];
                light.spotAngle = SpotAngle; light.innerSpotAngle = SpotAngle * .6f;
                light.shadows = LightShadows.None;
                light.intensity = 0;
                lights[i] = light;

                var beam = new GameObject("Stage beam " + i);
                beam.transform.SetParent(transform, false);
                beam.AddComponent<MeshFilter>().sharedMesh = beamMesh;
                var r = beam.AddComponent<MeshRenderer>();
                r.sharedMaterial = additive;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                beams[i] = beam.transform; beamRenderers[i] = r;

                // Glowing lamp head on the roof, so it is clear where each beam comes from.
                var lamp = GameObject.CreatePrimitive(PrimitiveType.Quad);
                lamp.name = "Stage lamp " + i;
                Destroy(lamp.GetComponent<Collider>());
                lamp.transform.SetParent(transform, false);
                lamp.transform.localScale = Vector3.one * LampSize;
                var lr = lamp.GetComponent<Renderer>();
                lr.sharedMaterial = additive;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lamps[i] = lamp.transform; lampRenderers[i] = lr;
            }
            SetVisible(0);
        }

        public void SetOn(bool on)
        {
            wanted = on ? 1 : 0;
            if (on && target) PickRoofs();
        }

        // For each of three directions, the tallest building roof 22–60 m from the elephant.
        void PickRoofs()
        {
            Vector3 centre = target.position; centre.y = 0;
            var buildings = FindObjectsByType<CityBuildingInfo>(FindObjectsSortMode.None);
            for (int i = 0; i < mounts.Length; i++)
            {
                float sector = (90 + 120 * i) * Mathf.Deg2Rad;
                Vector3 axis = new Vector3(Mathf.Cos(sector), 0, Mathf.Sin(sector));
                float best = float.MinValue;
                mounts[i] = centre + axis * 35 + Vector3.up * 20; // fallback when no building fits
                foreach (var b in buildings)
                {
                    Vector3 offset = b.transform.position - centre; offset.y = 0;
                    float distance = offset.magnitude;
                    if (distance < MinDistance || distance > MaxDistance || Vector3.Angle(axis, offset) > 55) continue;
                    // Roof = top of the visible geometry (the detail pass may hide the blockout box); the floor
                    // formula with the default 0.3 height scale is the fallback.
                    float roof = b.transform.position.y + (4f + (b.floors - 1) * 3.2f) * .3f;
                    foreach (var r in b.GetComponentsInChildren<Renderer>())
                        if (r.enabled && !(r is ParticleSystemRenderer) && r.name != "Searchlight") roof = Mathf.Max(roof, r.bounds.max.y);
                    float score = roof - .15f * Mathf.Abs(distance - 38);
                    if (score <= best) continue;
                    best = score;
                    mounts[i] = new Vector3(b.transform.position.x, roof + .4f, b.transform.position.z);
                }
            }
        }

        void LateUpdate()
        {
            if (lights == null || !target) return;
            fade = Mathf.MoveTowards(fade, wanted, Time.deltaTime / 1.2f);
            SetVisible(fade);
            if (fade <= 0) return;
            float beat = dance ? dance.Beat : Time.time * 112 / 60f;
            float pulse = .88f + .12f * Mathf.Abs(Mathf.Sin(beat * Mathf.PI));
            Vector3 focus = target.position + Vector3.up * 1.5f;
            var cam = Camera.main;
            for (int i = 0; i < lights.Length; i++)
            {
                Vector3 position = mounts[i];
                // Aim wobbles slightly, like a hand-operated follow spot.
                Vector3 aim = focus + new Vector3(Mathf.Sin(Time.time * .9f + i * 1.7f), 0, Mathf.Cos(Time.time * .7f + i)) * .4f;
                Vector3 dir = aim - position;
                float distance = dir.magnitude;
                lights[i].transform.SetPositionAndRotation(position, Quaternion.LookRotation(dir));
                float range = distance * RangeFactor, ratio = distance / range;
                float smooth = (1 - ratio * ratio * ratio * ratio) * (1 - ratio * ratio * ratio * ratio); // URP range fade
                lights[i].range = range;
                lights[i].intensity = Illuminance * distance * distance / smooth * pulse * fade;

                // Haze beam from the roof towards the elephant, fading to nothing just before it.
                float end = Mathf.Tan(SpotAngle * .5f * Mathf.Deg2Rad) * distance;
                beams[i].SetPositionAndRotation(position, Quaternion.FromToRotation(Vector3.up, dir / distance));
                beams[i].localScale = new Vector3(end * 2, distance * .88f, end * 2);
                block.SetColor("_BaseColor", Colors[i] * new Color(1, 1, 1, BeamAlpha * fade));
                beamRenderers[i].SetPropertyBlock(block);

                if (cam) lamps[i].SetPositionAndRotation(position, Quaternion.LookRotation(position - cam.transform.position));
                block.SetColor("_BaseColor", Colors[i] * new Color(1, 1, 1, .95f * fade * pulse));
                lampRenderers[i].SetPropertyBlock(block);
            }
        }

        void SetVisible(float amount)
        {
            bool on = amount > 0;
            foreach (var l in lights) if (l) l.enabled = on;
            foreach (var r in beamRenderers) if (r) r.enabled = on;
            foreach (var r in lampRenderers) if (r) r.enabled = on;
        }

        // Open cone along +Y: narrow at the lamp (y = 0), radius 0.5 at y = 1. Vertex alpha runs from 1 at the
        // lamp to 0 at the far end, and the UVs sample the centre of the soft-dot texture, so the beam is a soft,
        // see-through shaft that vanishes before it reaches the elephant.
        static Mesh Cone()
        {
            const int sides = 24, rings = 6;
            var vertices = new Vector3[sides * (rings + 1)]; var uv = new Vector2[vertices.Length]; var colors = new Color[vertices.Length];
            var triangles = new int[sides * rings * 6];
            for (int ring = 0; ring <= rings; ring++)
            {
                float v = (float)ring / rings;
                for (int s = 0; s < sides; s++)
                {
                    float a = s * Mathf.PI * 2 / sides;
                    int i = ring * sides + s;
                    vertices[i] = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * Mathf.Lerp(.03f, .5f, v) + Vector3.up * v;
                    uv[i] = new Vector2(.5f, .5f);
                    colors[i] = new Color(1, 1, 1, Mathf.Pow(1 - v, 1.5f));
                }
            }
            int t = 0;
            for (int ring = 0; ring < rings; ring++)
                for (int s = 0; s < sides; s++)
                {
                    int a = ring * sides + s, b = ring * sides + (s + 1) % sides, c = a + sides, d = b + sides;
                    triangles[t++] = a; triangles[t++] = c; triangles[t++] = d;
                    triangles[t++] = a; triangles[t++] = d; triangles[t++] = b;
                }
            var mesh = new Mesh { name = "Stage beam", vertices = vertices, uv = uv, colors = colors, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
