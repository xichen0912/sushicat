using System.IO;
using UnityEditor;
using UnityEngine;

namespace CityGen.EditorTools
{
    // Fixed review viewpoints, rendered off-screen at 1920 × 1080 into Assets/_CityGen/screenshots/.
    // Every step reuses the same two views so the results can be compared side by side.
    public static class CityScreenshots
    {
        const string Folder = "Assets/_CityGen/screenshots";
        const int Width = 1920, Height = 1080;

        // Top-down: south at the top of the image, matching the original review screenshots
        // (water basin at the top, log on the left).
        public static readonly Vector3 TopPosition = new Vector3(0, 200, 0);
        public static readonly Quaternion TopRotation = Quaternion.Euler(90, 180, 0);
        const float TopOrthoSize = 46;

        // Eye level (1.7 m) on the south road centreline, looking east along the road towards the fountain.
        public static readonly Vector3 EyePosition = new Vector3(-2f, 1.7f, -17f);
        public static readonly Quaternion EyeRotation = Quaternion.Euler(-4, 90, 0);
        const float EyeFov = 60;

        // Elephant view: the default HighFollowCamera pose at the start line (9 m behind, 12 m above,
        // looking at the elephant 1.5 m above the ground) — how the performers see the city.
        static readonly Vector3 ElephantFocus = new Vector3(0, 1.5f, 0);
        public static readonly Vector3 ElephantViewPosition = ElephantFocus + new Vector3(0, 12, -9);
        public static readonly Quaternion ElephantViewRotation = Quaternion.LookRotation(ElephantFocus - ElephantViewPosition, Vector3.up);

        [MenuItem("CityGen/Capture Screenshots/Step 1 (blockout)")] static void Step1() => Capture("step1");
        [MenuItem("CityGen/Capture Screenshots/Step 2 (detail)")] static void Step2() => Capture("step2");
        [MenuItem("CityGen/Capture Screenshots/Step 3 (materials + lighting)")] static void Step3() => Capture("step3");

        public static void Capture(string prefix)
        {
            Directory.CreateDirectory(Folder);
            Render(Folder + "/" + prefix + "_top.png", TopPosition, TopRotation, true, TopOrthoSize);
            Render(Folder + "/" + prefix + "_eye_1.7m.png", EyePosition, EyeRotation, false, 0);
            Render(Folder + "/" + prefix + "_elephant_view.png", ElephantViewPosition, ElephantViewRotation, false, 0);
            AssetDatabase.Refresh();
            Debug.Log("CITYGEN_SCREENSHOTS " + Folder + "/" + prefix + "_top.png, " + prefix + "_eye_1.7m.png, " + prefix + "_elephant_view.png");
        }

        static void Render(string path, Vector3 position, Quaternion rotation, bool orthographic, float orthoSize)
        {
            var source = Camera.main;
            var go = new GameObject("CityGen screenshot camera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = go.AddComponent<Camera>();
            if (source) camera.CopyFrom(source);
            // Same URP renderer (SSAO) and post-processing as the scene camera, so screenshots match Play.
            var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(camera);
            var sourceData = source ? UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(source) : null;
            if (sourceData)
            {
                data.renderPostProcessing = sourceData.renderPostProcessing;
                data.antialiasing = sourceData.antialiasing;
                data.SetRenderer(CityLook.CityRendererIndex());
            }
            camera.allowHDR = true;
            camera.transform.SetPositionAndRotation(position, rotation);
            camera.rect = new Rect(0, 0, 1, 1); // the play panel shrinks Main Camera's viewport; screenshots use the full frame
            camera.orthographic = orthographic;
            camera.orthographicSize = orthoSize;
            camera.fieldOfView = EyeFov;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 600;
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            camera.targetTexture = target;
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(path, image.EncodeToPNG());
            camera.targetTexture = null;
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(go);
        }
    }
}
