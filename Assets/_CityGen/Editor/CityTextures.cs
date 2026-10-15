using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CityGen.EditorTools
{
    // Step 3: tileable procedural PBR textures (albedo + normal). Albedo is near-white and neutral so each
    // material's colour (the New York palette from step 2) tints it; mortar / joints are the lightest part so
    // they read as mortar, not as dark gaps. One tile = 1 m of mesh UV unless a material sets its own tiling.
    internal static class CityTextures
    {
        const string Folder = "Assets/_CityGen/Textures/PBR";
        const int Size = 256;

        internal struct Set { public Texture2D albedo, normal; }

        public static Set Brick() => Make("Brick", (u, v) =>
        {
            // Running bond: 6 courses × 3 bricks per metre, every other course offset by half a brick.
            float rows = 6, cols = 3, joint = .07f;
            float y = v * rows; int row = Mathf.FloorToInt(y);
            float x = u * cols + (row % 2) * .5f; int col = Mathf.FloorToInt(x);
            float fx = x - col, fy = y - row;
            bool mortar = fx < joint * .6f || fy < joint * 1.6f;
            float tone = Hash(col, row) * .14f;
            float grain = Noise(u, v, 32) * .08f;
            return mortar ? (1f, 0f) : (.84f - tone + grain, 1f);
        });

        public static Set Stone() => Make("Stone", (u, v) =>
        {
            // Ashlar: 2 × 4 blocks per metre with fine joints and gentle surface variation.
            float rows = 4, cols = 2, joint = .025f;
            float y = v * rows; int row = Mathf.FloorToInt(y);
            float x = u * cols + (row % 2) * .5f; int col = Mathf.FloorToInt(x);
            float fx = x - col, fy = y - row;
            bool seam = fx < joint || fy < joint * 2;
            float tone = Hash(col + 17, row) * .06f;
            float grain = Noise(u, v, 24) * .06f + Noise(u, v, 96) * .04f;
            return seam ? (.82f, .2f) : (.95f - tone + grain, .9f + grain);
        });

        public static Set Concrete() => Make("Concrete", (u, v) =>
        {
            // Sidewalk flags: 2 × 2 slabs per tile (tile = 2 m with tiling 0.5).
            float fx = Mathf.Repeat(u * 2, 1), fy = Mathf.Repeat(v * 2, 1);
            bool joint = fx < .012f || fy < .012f;
            float grain = Noise(u, v, 40) * .07f + Noise(u, v, 160) * .05f;
            return joint ? (.72f, 0f) : (.93f + grain - .05f, 1f);
        });

        public static Set Asphalt() => Make("Asphalt", (u, v) =>
        {
            float speck = Hash(Mathf.FloorToInt(u * 180), Mathf.FloorToInt(v * 180)) > .93f ? .25f : 0;
            float grain = Noise(u, v, 64) * .12f + Noise(u, v, 12) * .08f;
            return (.72f + grain + speck, .5f + grain);
        });

        public static Set Roof() => Make("Roof", (u, v) =>
        {
            // Rolled tar roofing: overlapping horizontal strips.
            float strip = Mathf.Repeat(v * 3, 1);
            float grain = Noise(u, v, 48) * .1f;
            return (.85f + grain - strip * .08f, strip);
        });

        public static Set Planks() => Make("Planks", (u, v) =>
        {
            // Vertical boards, 5 per tile, with long grain.
            float x = u * 5; int board = Mathf.FloorToInt(x); float fx = x - board;
            bool gap = fx < .05f;
            float grain = Noise(u * 4, v * .5f, 16) * .14f + Hash(board, 3) * .1f;
            return gap ? (.55f, 0f) : (.82f + grain, 1f);
        });

        // ---------------- Generation ----------------

        static Set Make(string name, Func<float, float, (float albedo, float height)> sample)
        {
            Directory.CreateDirectory(Folder);
            var albedo = new Color32[Size * Size];
            var height = new float[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    var (a, h) = sample((x + .5f) / Size, (y + .5f) / Size);
                    byte g = (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255);
                    albedo[y * Size + x] = new Color32(g, g, g, 255);
                    height[y * Size + x] = h;
                }
            var normal = new Color32[Size * Size];
            const float strength = 2.5f;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    // Central differences with wrap-around so the normal map tiles too.
                    float dx = height[y * Size + (x + 1) % Size] - height[y * Size + (x + Size - 1) % Size];
                    float dy = height[((y + 1) % Size) * Size + x] - height[((y + Size - 1) % Size) * Size + x];
                    var n = new Vector3(-dx * strength, -dy * strength, 1).normalized;
                    normal[y * Size + x] = new Color32((byte)((n.x * .5f + .5f) * 255), (byte)((n.y * .5f + .5f) * 255), (byte)((n.z * .5f + .5f) * 255), 255);
                }
            return new Set { albedo = Save(name + " albedo", albedo, false), normal = Save(name + " normal", normal, true) };
        }

        static Texture2D Save(string name, Color32[] pixels, bool isNormal)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply();
            string path = Folder + "/" + name + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !isNormal;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Deterministic hash in [0, 1).
        static float Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177;
                return (h ^ (h >> 16)) / 4294967296f;
            }
        }

        // Tileable value noise in [-0.5, 0.5]: `period` cells across one tile, wrapping at the tile edge.
        static float Noise(float u, float v, int period)
        {
            float x = Mathf.Repeat(u, 1) * period, y = Mathf.Repeat(v, 1) * period;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float H(int i, int j) => Hash(((i % period) + period) % period, ((j % period) + period) % period);
            float a = Mathf.Lerp(H(x0, y0), H(x0 + 1, y0), fx), b = Mathf.Lerp(H(x0, y0 + 1), H(x0 + 1, y0 + 1), fx);
            return Mathf.Lerp(a, b, fy) - .5f;
        }
    }
}
