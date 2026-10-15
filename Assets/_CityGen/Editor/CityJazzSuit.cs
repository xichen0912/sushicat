using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CityGen.EditorTools
{
    // The elephant's jazz suit for the finish dance: a plain navy jacket and trousers cut from the elephant's
    // own skin (every triangle whose vertices follow the spine, neck, shoulder or leg bones, pushed out a few
    // centimetres), a white shirt front, a crimson bow tie and a black fedora. The clothes reuse the elephant's
    // bones, so they deform with the dance. The elephant model is not readable, so it is read from a temporary
    // copy of its FBX with Read/Write enabled; the original model and the elephant in the scene are not changed.
    internal static class CityJazzSuit
    {
        internal const string CopyPath = CityBlockout.MeshFolder + "/ElephantSuitSource.fbx";
        const float Offset = .06f;        // cloth thickness above the skin, metres

        static readonly string[] SuitBones = { "Spine", "Neck", "Scapula", "Humerus", "Radius", "Pelvis", "Femur", "Tibia" };

        [MenuItem("CityGen/Dress Elephant (jazz suit)")]
        static void Menu()
        {
            var scene = SceneManager.GetActiveScene();
            var celebration = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<CityCelebration>(true)).FirstOrDefault();
            if (!celebration || !celebration.dance)
            {
                Debug.LogError("CityGen: no Finish Celebration in the open scene; run \"CityGen/3 Generate Final\" first.");
                return;
            }
            Build(celebration.dance, celebration.transform);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        internal static void Build(ElephantDance dance, Transform parent)
        {
            var old = parent.Find("Jazz Suit");
            if (old) Object.DestroyImmediate(old.gameObject);
            dance.suit = null;

            var skin = ElephantSkin(dance);
            if (!skin || !skin.sharedMesh) { Debug.LogWarning("CityGen: elephant skinned mesh not found; no jazz suit."); return; }

            var source = ReadableCopy(skin.sharedMesh);
            if (!source) return;
            try
            {
                var root = new GameObject("Jazz Suit");
                root.transform.SetParent(parent, false);
                var suit = root.AddComponent<ElephantSuit>();
                Dress(suit, skin, source);
                dance.suit = suit;
                suit.Wear(false);
            }
            finally
            {
                if (source != skin.sharedMesh) AssetDatabase.DeleteAsset(CopyPath);
            }
        }

        // The elephant's skinned mesh: the one skinned to the bones the DigiPhant controller poses (the controller
        // sits on its own "DigiPhant Controls" object, not on the model).
        internal static SkinnedMeshRenderer ElephantSkin(ElephantDance dance)
        {
            var driven = new HashSet<Transform>();
            if (dance.controller && dance.controller.controls != null)
                foreach (var c in dance.controller.controls)
                    if (c != null && c.bones != null)
                        foreach (var b in c.bones) if (b) driven.Add(b);
            var scene = dance.gameObject.scene;
            var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                .Where(s => s.sharedMesh && s.bones != null).ToList();
            var root = dance.locomotion && dance.locomotion.travelRoot ? dance.locomotion.travelRoot : null;
            // The controller may drive "_ctrl" transforms above the "_bone" transforms the mesh is skinned to.
            bool Related(Transform b) => b && driven.Any(d => b == d || b.IsChildOf(d) || d.IsChildOf(b));
            return all.Where(s => s.bones.Any(Related)).OrderByDescending(s => s.sharedMesh.vertexCount).FirstOrDefault()
                ?? all.Where(s => root && s.transform.IsChildOf(root)).OrderByDescending(s => s.sharedMesh.vertexCount).FirstOrDefault()
                ?? all.Where(s => s.sharedMesh.name.ToLowerInvariant().Contains("elephant")).OrderByDescending(s => s.sharedMesh.vertexCount).FirstOrDefault();
        }

        internal static Mesh ReadableCopy(Mesh mesh)
        {
            if (mesh.isReadable) return mesh;
            string path = AssetDatabase.GetAssetPath(mesh);
            if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase) || !AssetDatabase.CopyAsset(path, CopyPath))
            {
                Debug.LogWarning("CityGen: could not copy the elephant model " + path + "; no jazz suit.");
                return null;
            }
            var importer = (ModelImporter)AssetImporter.GetAtPath(CopyPath);
            importer.isReadable = true;
            importer.importAnimation = false;
            importer.SaveAndReimport();
            var copy = AssetDatabase.LoadAllAssetsAtPath(CopyPath).OfType<Mesh>().FirstOrDefault(m => m.name == mesh.name);
            if (!copy || copy.vertexCount != mesh.vertexCount)
            {
                Debug.LogWarning("CityGen: the readable copy of the elephant mesh does not match; no jazz suit.");
                AssetDatabase.DeleteAsset(CopyPath);
                return null;
            }
            return copy;
        }

        static void Dress(ElephantSuit suit, SkinnedMeshRenderer skin, Mesh source)
        {
            var bones = skin.bones;
            var bind = source.bindposes;
            var vertices = source.vertices;
            var normals = source.normals;
            var weights = source.boneWeights;
            int n = vertices.Length;

            // Current (rest) pose in world space, skinned on the CPU.
            var skinning = new Matrix4x4[bones.Length];
            for (int b = 0; b < bones.Length; b++) skinning[b] = bones[b] ? bones[b].localToWorldMatrix * bind[b] : Matrix4x4.identity;
            var world = new Vector3[n]; var worldNormal = new Vector3[n]; var dominant = new int[n]; var scale = new float[n];
            for (int i = 0; i < n; i++)
            {
                var w = weights[i];
                world[i] = Skin(skinning, w, vertices[i], true);
                worldNormal[i] = Skin(skinning, w, normals[i], false).normalized;
                dominant[i] = w.boneIndex0;
                // metres per mesh unit along the normal, so the cloth offset is the same everywhere
                scale[i] = Mathf.Max(1e-5f, skinning[w.boneIndex0].MultiplyVector(normals[i]).magnitude);
            }

            Transform Bone(string key) => bones.FirstOrDefault(b => b && b.name.Contains(key));
            var head = Bone("Head_bone"); var neck = Bone("Neck_bone"); var pelvis = Bone("Pelvis_bone");
            if (!head || !neck || !pelvis) { Debug.LogWarning("CityGen: elephant head / neck / pelvis bones not found; no jazz suit."); return; }
            Vector3 forward = head.position - pelvis.position; forward.y = 0; forward.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, forward);
            Vector3 centre = (pelvis.position + neck.position) / 2; centre.y = 0;

            bool[] boneInSuit = bones.Select(b => b && SuitBones.Any(k => b.name.Contains(k))).ToArray();
            bool InSuit(int v) => boneInSuit[dominant[v]];

            // Shirt front: the forward-facing middle of the chest, a V that widens towards the neck.
            float front = Enumerable.Range(0, n).Where(InSuit).Max(v => Vector3.Dot(world[v] - centre, forward));
            var chest = Enumerable.Range(0, n).Where(v => InSuit(v) && Vector3.Dot(worldNormal[v], forward) > .25f
                                                         && Vector3.Dot(world[v] - centre, forward) > front - .7f).ToList();
            float chestTop = chest.Count > 0 ? chest.Max(v => world[v].y) : 0;
            bool Shirt(int v)
            {
                if (Vector3.Dot(worldNormal[v], forward) < .25f || Vector3.Dot(world[v] - centre, forward) < front - .7f) return false;
                float up = Mathf.Clamp01((world[v].y - (chestTop - 1.3f)) / 1.3f);
                return world[v].y > chestTop - 1.3f && Mathf.Abs(Vector3.Dot(world[v] - centre, side)) < .1f + .38f * up;
            }

            // Cut the clothes out of the skin: sub-mesh 0 = suit, 1 = shirt.
            var map = new Dictionary<int, int>();
            var outVertices = new List<Vector3>(); var outNormals = new List<Vector3>(); var outUV = new List<Vector2>();
            var outWeights = new List<BoneWeight>();
            var cloth = new[] { new List<int>(), new List<int>() };
            var shirtVertices = new List<int>();
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
                    if (!InSuit(a) || !InSuit(b) || !InSuit(c)) continue;
                    bool shirt = Shirt(a) && Shirt(b) && Shirt(c);
                    var list = cloth[shirt ? 1 : 0];
                    list.Add(Use(a)); list.Add(Use(b)); list.Add(Use(c));
                    if (shirt) shirtVertices.AddRange(new[] { a, b, c });
                }
            }
            if (cloth[0].Count == 0) { Debug.LogWarning("CityGen: no suit triangles found on the elephant mesh."); return; }

            // Written straight into the saved asset: EditorUtility.CopySerialized (CityBlockout.SaveMesh) drops the
            // vertex data of a skinned mesh when the asset already exists.
            const string meshPath = CityBlockout.MeshFolder + "/Props/Elephant jazz suit.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            bool created = !mesh;
            if (created) mesh = new Mesh();
            mesh.Clear();
            mesh.name = "Elephant jazz suit";
            mesh.indexFormat = IndexFormat.UInt16;
            if (outVertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(outVertices); mesh.SetNormals(outNormals); mesh.SetUVs(0, outUV);
            mesh.boneWeights = outWeights.ToArray();
            mesh.bindposes = bind;
            mesh.subMeshCount = 2;
            mesh.SetTriangles(cloth[0], 0); mesh.SetTriangles(cloth[1], 1);
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            if (created) AssetDatabase.CreateAsset(mesh, meshPath);
            EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssets();

            var clothes = new GameObject("Suit and shirt");
            clothes.transform.SetParent(suit.transform, false);
            var smr = clothes.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = skin.rootBone;
            smr.localBounds = skin.localBounds;
            smr.updateWhenOffscreen = true;
            smr.quality = SkinQuality.Bone4;
            smr.sharedMaterials = new[] { SuitMaterial(), CityBuildings.Mat("Jazz Shirt", new Color(.93f, .92f, .88f), .25f) };
            suit.clothes = smr;

            var pieces = new List<ElephantSuit.Piece>();

            // Bow tie at the top of the shirt front, on the neck bone.
            var tieAt = shirtVertices.Count > 0 ? shirtVertices : chest;
            if (tieAt.Count > 0)
            {
                float top = tieAt.Max(v => world[v].y);
                var collar = tieAt.Where(v => world[v].y > top - .25f).ToList();
                Vector3 position = Average(collar.Select(v => world[v]));
                Vector3 normal = Average(collar.Select(v => worldNormal[v])).normalized;
                if (normal.sqrMagnitude < .01f) normal = forward;
                pieces.Add(Piece(suit, "Bow tie", BowTie(), new[] { CityBuildings.Mat("Jazz Bow Tie", new Color(.62f, .06f, .10f), .6f) },
                    neck, position + normal * (Offset + .04f), Quaternion.LookRotation(normal, Vector3.up)));
            }

            // Fedora on the crown of the head, tilted to one side.
            var skull = Enumerable.Range(0, n).Where(v => bones[dominant[v]] == head).ToList();
            if (skull.Count > 0)
            {
                float top = skull.Max(v => world[v].y);
                Vector3 crown = Average(skull.Where(v => world[v].y > top - .06f).Select(v => world[v]));
                var upper = skull.Where(v => world[v].y > top - .5f).Select(v => Vector3.Dot(world[v], side)).ToList();
                float brim = Mathf.Clamp((upper.Max() - upper.Min()) * .42f, .35f, .8f);
                var rotation = Quaternion.AngleAxis(-12, forward) * Quaternion.AngleAxis(-6, side) * Quaternion.LookRotation(forward, Vector3.up);
                pieces.Add(Piece(suit, "Fedora", Fedora(brim / .58f),
                    new[] { CityBuildings.Mat("Jazz Fedora", new Color(.06f, .06f, .07f), .3f), CityBuildings.Mat("Jazz Bow Tie", new Color(.62f, .06f, .10f), .6f) },
                    head, crown - Vector3.up * (.1f * brim / .58f), rotation));
            }
            suit.pieces = pieces.ToArray();
        }

        internal static Vector3 Skin(Matrix4x4[] m, BoneWeight w, Vector3 v, bool point)
        {
            Vector3 P(int b) => point ? m[b].MultiplyPoint3x4(v) : m[b].MultiplyVector(v);
            return P(w.boneIndex0) * w.weight0 + P(w.boneIndex1) * w.weight1 + P(w.boneIndex2) * w.weight2 + P(w.boneIndex3) * w.weight3;
        }

        internal static Vector3 Average(IEnumerable<Vector3> points)
        {
            Vector3 sum = Vector3.zero; int count = 0;
            foreach (var p in points) { sum += p; count++; }
            return count > 0 ? sum / count : Vector3.zero;
        }

        // A rigid accessory placed in world space now and stored relative to the bone it rides on.
        internal static ElephantSuit.Piece Piece(ElephantSuit suit, string name, Mesh mesh, Material[] materials, Transform bone, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject(name);
            go.transform.SetParent(suit.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = materials;
            go.transform.SetPositionAndRotation(position, rotation);
            Vector3 boneScale = bone.lossyScale;
            return new ElephantSuit.Piece
            {
                piece = go.transform,
                bone = bone,
                localPosition = bone.InverseTransformPoint(position),
                localRotation = Quaternion.Inverse(bone.rotation) * rotation,
                localScale = new Vector3(1 / boneScale.x, 1 / boneScale.y, 1 / boneScale.z),
            };
        }

        // Pivot at the bottom of the brim; slot 0 felt, slot 1 hat band.
        static Mesh Fedora(float s)
        {
            var kit = new MeshKit(2);
            Vector2 P(float r, float y) => new Vector2(r * s, y * s);
            kit.Lathe(0, Vector3.zero, new[] { P(.30f, 0), P(.56f, 0), P(.58f, .035f), P(.31f, .06f), P(.30f, .30f), P(.22f, .38f), P(0, .35f) }, 28);
            kit.Lathe(1, Vector3.zero, new[] { P(.318f, .06f), P(.312f, .13f) }, 28);
            return CityBlockout.SaveMesh("Props/Jazz fedora", kit.ToMesh("Jazz fedora", out _));
        }

        // Two flared wings and a knot, facing +Z.
        static Mesh BowTie()
        {
            var kit = new MeshKit(1);
            kit.Box(0, new Vector3(-.13f, 0, 0), new Vector3(.2f, .17f, .06f), Quaternion.Euler(0, 0, -10));
            kit.Box(0, new Vector3(.13f, 0, 0), new Vector3(.2f, .17f, .06f), Quaternion.Euler(0, 0, 10));
            kit.Box(0, new Vector3(0, 0, .015f), new Vector3(.08f, .09f, .08f));
            return CityBlockout.SaveMesh("Props/Jazz bow tie", kit.ToMesh("Jazz bow tie", out _));
        }

        // Plain navy wool.
        static Material SuitMaterial() => CityBuildings.Mat("Jazz Suit", new Color(.10f, .12f, .23f), .32f);
    }
}
