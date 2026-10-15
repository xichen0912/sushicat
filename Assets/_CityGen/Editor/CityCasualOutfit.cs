using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CityGen.EditorTools
{
    // The elephant's start outfit, worn from the start until the finish dance swaps it for the jazz suit:
    // a ballet dress. A pink satin bodice with small cap sleeves is cut from the elephant's own skin (it reuses
    // the elephant's bones, so it deforms with walking), and a puffy three-layer tulle tutu rings the waist
    // between the front and hind legs, sized from the body there and kept off the ground. A cream baseball cap
    // sits on the head. The elephant model and the elephant in the scene are not changed.
    internal static class CityCasualOutfit
    {
        const float Offset = .05f; // cloth thickness above the skin, metres (thinner than the suit: never worn together)
        const string OutfitName = "Casual Outfit";

        static readonly Color BodiceColor = new Color(.97f, .74f, .83f);   // ballet pink satin
        static readonly Color TulleColor = new Color(.99f, .86f, .91f);    // pale pink tulle
        static readonly Color TulleLight = new Color(1f, .93f, .95f);      // lighter alternate layer
        static readonly Color CapColor = new Color(.97f, .93f, .83f);      // cream cap
        static readonly Color PeakColor = new Color(.97f, .74f, .83f);     // pink peak

        [MenuItem("CityGen/Dress Elephant (casual start outfit)")]
        static void Menu()
        {
            if (Application.isPlaying) { Debug.LogError("CityGen: stop Play before dressing the elephant."); return; }
            var scene = SceneManager.GetActiveScene();
            var celebration = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<CityCelebration>(true)).FirstOrDefault();
            if (!celebration || !celebration.dance)
            {
                Debug.LogError("CityGen: no Finish Celebration in the open scene; open the city course scene first.");
                return;
            }
            Build(celebration.dance, celebration.transform);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (celebration.dance.startOutfit) Debug.Log("CityGen: ballet dress built; the elephant wears it from Play until the finish dance.");
        }

        internal static void Build(ElephantDance dance, Transform parent)
        {
            var old = parent.Find(OutfitName);
            if (old) Object.DestroyImmediate(old.gameObject);
            dance.startOutfit = null;

            var skin = CityJazzSuit.ElephantSkin(dance);
            if (!skin || !skin.sharedMesh) { Debug.LogWarning("CityGen: elephant skinned mesh not found; no start outfit."); return; }
            var source = CityJazzSuit.ReadableCopy(skin.sharedMesh);
            if (!source) return;
            try
            {
                var root = new GameObject(OutfitName);
                root.transform.SetParent(parent, false);
                var outfit = root.AddComponent<ElephantSuit>();
                if (Dress(outfit, skin, source)) dance.startOutfit = outfit;
                else Object.DestroyImmediate(root);
                // Stored taken off in the saved scene; ElephantDance puts it on when Play starts.
                if (dance.startOutfit) outfit.Wear(false);
                EditorUtility.SetDirty(dance);
            }
            finally
            {
                if (source != skin.sharedMesh) AssetDatabase.DeleteAsset(CityJazzSuit.CopyPath);
            }
        }

        static bool Dress(ElephantSuit outfit, SkinnedMeshRenderer skin, Mesh source)
        {
            var bones = skin.bones;
            var bind = source.bindposes;
            var vertices = source.vertices;
            var normals = source.normals;
            var weights = source.boneWeights;
            int n = vertices.Length;

            var skinning = new Matrix4x4[bones.Length];
            for (int b = 0; b < bones.Length; b++) skinning[b] = bones[b] ? bones[b].localToWorldMatrix * bind[b] : Matrix4x4.identity;
            var world = new Vector3[n]; var worldNormal = new Vector3[n]; var dominant = new int[n]; var scale = new float[n];
            for (int i = 0; i < n; i++)
            {
                var w = weights[i];
                world[i] = CityJazzSuit.Skin(skinning, w, vertices[i], true);
                worldNormal[i] = CityJazzSuit.Skin(skinning, w, normals[i], false).normalized;
                dominant[i] = w.boneIndex0;
                scale[i] = Mathf.Max(1e-5f, skinning[w.boneIndex0].MultiplyVector(normals[i]).magnitude);
            }

            Transform Bone(string key) => bones.FirstOrDefault(b => b && b.name.Contains(key));
            var head = Bone("Head_bone"); var neck = Bone("Neck_bone"); var pelvis = Bone("Pelvis_bone");
            var scapula = Bone("Scapula"); var femur = Bone("Femur");
            if (!head || !neck || !pelvis) { Debug.LogWarning("CityGen: elephant head / neck / pelvis bones not found; no start outfit."); return false; }
            Vector3 forward = head.position - pelvis.position; forward.y = 0; forward.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, forward);
            Vector3 centre = (pelvis.position + neck.position) / 2; centre.y = 0;

            bool Named(int v, params string[] keys) { var b = bones[dominant[v]]; return b && keys.Any(k => b.name.Contains(k)); }
            bool Torso(int v) => Named(v, "Spine", "Neck", "Pelvis");
            bool CapSleeve(int v) => Named(v, "Scapula");                    // small sleeves over the shoulders
            bool InBodice(int v) => Torso(v) || CapSleeve(v);

            var torso = Enumerable.Range(0, n).Where(Torso).ToList();
            if (torso.Count == 0) { Debug.LogWarning("CityGen: no torso vertices found; no start outfit."); return false; }
            float Along(int v) => Vector3.Dot(world[v] - centre, forward);
            float Across(int v) => Vector3.Dot(world[v] - centre, side);

            // ---- Bodice: one satin piece cut from the skin.
            var map = new Dictionary<int, int>();
            var outVertices = new List<Vector3>(); var outNormals = new List<Vector3>(); var outUV = new List<Vector2>();
            var outWeights = new List<BoneWeight>();
            var bodice = new List<int>();
            int Use(int v)
            {
                if (map.TryGetValue(v, out int index)) return index;
                index = outVertices.Count; map[v] = index;
                outVertices.Add(vertices[v] + normals[v].normalized * (Offset / scale[v] * normals[v].magnitude));
                outNormals.Add(normals[v]);
                outUV.Add(Vector2.zero);
                outWeights.Add(weights[v]);
                return index;
            }
            for (int s = 0; s < source.subMeshCount; s++)
            {
                var tris = source.GetTriangles(s);
                for (int t = 0; t < tris.Length; t += 3)
                {
                    int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                    if (!InBodice(a) || !InBodice(b) || !InBodice(c)) continue;
                    bodice.Add(Use(a)); bodice.Add(Use(b)); bodice.Add(Use(c));
                }
            }
            if (bodice.Count == 0) { Debug.LogWarning("CityGen: no bodice triangles found on the elephant mesh."); return false; }

            const string meshPath = CityBlockout.MeshFolder + "/Props/Elephant casual outfit.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            bool created = !mesh;
            if (created) mesh = new Mesh();
            mesh.Clear();
            mesh.name = "Elephant ballet bodice";
            mesh.indexFormat = outVertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(outVertices); mesh.SetNormals(outNormals); mesh.SetUVs(0, outUV);
            mesh.boneWeights = outWeights.ToArray();
            mesh.bindposes = bind;
            mesh.subMeshCount = 1;
            mesh.SetTriangles(bodice, 0);
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            if (created) AssetDatabase.CreateAsset(mesh, meshPath);
            EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssets();

            var clothes = new GameObject("Ballet bodice");
            clothes.transform.SetParent(outfit.transform, false);
            var smr = clothes.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = skin.rootBone;
            smr.localBounds = skin.localBounds;
            smr.updateWhenOffscreen = true;
            smr.quality = SkinQuality.Bone4;
            var pink = CityBuildings.Mat("Ballet Bodice", BodiceColor, .45f);
            smr.sharedMaterials = new[] { pink };
            outfit.clothes = smr;

            var pieces = new List<ElephantSuit.Piece>();

            // ---- Tutu: a stiff, scalloped, layered ring around the waist, perpendicular to the spine.
            // The waist sits between the front and hind legs, a little towards the hips.
            float frontLegs = scapula ? Vector3.Dot(scapula.position - centre, forward) : torso.Max(Along) * .6f;
            float hindLegs = femur ? Vector3.Dot(femur.position - centre, forward) : torso.Min(Along) * .6f;
            float waistAlong = Mathf.Lerp(hindLegs, frontLegs, .42f);
            var slice = torso.Where(v => Mathf.Abs(Along(v) - waistAlong) < .2f).ToList();
            if (slice.Count < 8) slice = torso.OrderBy(v => Mathf.Abs(Along(v) - waistAlong)).Take(200).ToList();
            float top = slice.Max(v => world[v].y), bottom = slice.Min(v => world[v].y);
            float across = slice.Average(v => Across(v));
            float halfWidth = slice.Max(v => Mathf.Abs(Across(v) - across));
            float halfHeight = (top - bottom) / 2;
            Vector3 waist = centre + forward * waistAlong + side * across + Vector3.up * ((top + bottom) / 2);
            float ground = world.Min(p => p.y);
            // Puffy but clear of the ground (and the legs walk underneath it).
            float flare = Mathf.Clamp(Mathf.Min(halfWidth * .9f, (bottom - ground) - .35f), .25f, 1.1f);
            var torsoBones = bones.Where(b => b && (b.name.Contains("Spine") || b.name.Contains("Pelvis"))).ToList();
            var waistBone = torsoBones.OrderBy(b => (b.position - waist).sqrMagnitude).FirstOrDefault() ?? pelvis;
            // Local frame: +Z points backwards (the layers fan towards the tail), +Y up.
            var tutuRotation = Quaternion.LookRotation(-forward, Vector3.up);
            pieces.Add(CityJazzSuit.Piece(outfit, "Ballet tutu", Tutu(halfWidth + Offset + .03f, halfHeight + Offset + .03f, flare),
                new[] { CityBuildings.Mat("Ballet Tulle", TulleColor, .1f), CityBuildings.Mat("Ballet Tulle Light", TulleLight, .1f) },
                waistBone, waist, tutuRotation));

            // ---- Baseball cap on the crown of the head, peak forward.
            var skull = Enumerable.Range(0, n).Where(v => bones[dominant[v]] == head).ToList();
            if (skull.Count > 0)
            {
                float crownTop = skull.Max(v => world[v].y);
                Vector3 crown = CityJazzSuit.Average(skull.Where(v => world[v].y > crownTop - .06f).Select(v => world[v]));
                var upper = skull.Where(v => world[v].y > crownTop - .5f).Select(v => Vector3.Dot(world[v], side)).ToList();
                float size = Mathf.Clamp((upper.Max() - upper.Min()) * .42f, .35f, .8f) / .5f;
                pieces.Add(CityJazzSuit.Piece(outfit, "Baseball cap", BaseballCap(size),
                    new[] { CityBuildings.Mat("Casual Cap", CapColor, .2f), CityBuildings.Mat("Ballet Cap Peak", PeakColor, .3f) },
                    head, crown - Vector3.up * (.09f * size), Quaternion.LookRotation(forward, Vector3.up)));
            }
            outfit.pieces = pieces.ToArray();
            return true;
        }

        // Three stiff tulle layers around an elliptical waist (rx across, ry up) in the local XY plane. Each layer
        // flares out by `flare` with a scalloped edge and fans slightly towards +Z; both faces are built so the thin
        // layers show from any side. Slot 0 and 1 alternate the two tulle shades.
        static Mesh Tutu(float rx, float ry, float flare)
        {
            var kit = new MeshKit(2);
            const int Segments = 72, Petals = 18;
            for (int layer = 0; layer < 3; layer++)
            {
                float reach = flare * (1 - .16f * layer);
                float zInner = .05f + .1f * layer, zOuter = zInner + reach * .3f;
                float lift = .04f * layer; // upper layers sit a touch higher, so the skirt looks fuller
                int slot = layer % 2;
                for (int i = 0; i < Segments; i++)
                {
                    float a0 = i * Mathf.PI * 2 / Segments, a1 = (i + 1) * Mathf.PI * 2 / Segments;
                    Vector3 Inner(float a) => new Vector3(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry + lift, zInner);
                    Vector3 Outer(float a)
                    {
                        float r = reach * (1 + .12f * Mathf.Abs(Mathf.Sin(a * Petals / 2)) - .06f); // scalloped hem
                        return new Vector3(Mathf.Cos(a) * (rx + r), Mathf.Sin(a) * (ry + r) + lift, zOuter);
                    }
                    Vector3 i0 = Inner(a0), i1 = Inner(a1), o0 = Outer(a0), o1 = Outer(a1);
                    Vector3 normal = Vector3.Cross(i1 - i0, o0 - i0).normalized;
                    if (normal.sqrMagnitude < .5f) normal = Vector3.back;
                    foreach (var face in new[] { normal, -normal })
                    {
                        kit.Triangle(slot, i0, i1, o1, face);
                        kit.Triangle(slot, i0, o1, o0, face);
                    }
                }
            }
            return CityBlockout.SaveMesh("Props/Ballet tutu", kit.ToMesh("Ballet tutu", out _));
        }

        // Pivot at the bottom of the crown; slot 0 crown, slot 1 peak and top button. The peak points along +Z.
        static Mesh BaseballCap(float s)
        {
            var kit = new MeshKit(2);
            Vector2 P(float r, float y) => new Vector2(r * s, y * s);
            kit.Lathe(0, Vector3.zero, new[] { P(.36f, 0), P(.36f, .07f), P(.33f, .17f), P(.25f, .26f), P(.13f, .31f), P(0, .32f) }, 28);
            kit.Lathe(1, new Vector3(0, .315f * s, 0), new[] { P(.035f, 0), P(.03f, .02f), P(0, .025f) }, 12);
            kit.Box(1, new Vector3(0, .015f * s, .44f * s), new Vector3(.5f * s, .025f * s, .3f * s), Quaternion.Euler(6, 0, 0));
            return CityBlockout.SaveMesh("Props/Casual baseball cap", kit.ToMesh("Casual baseball cap", out _));
        }
    }
}
