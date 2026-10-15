using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace DigiPhant
{
    // MediaPipe owns camera capture; Unity displays its local JPEG preview.
    public class DigiPhantCameraPreview : MonoBehaviour
    {
        public bool autoStartBridge = true;
        [Range(0, 8)] public int cameraIndex;
        [Tooltip("Pass the upper-body bridge mode whenever the controller is configured for upper-body input.")]
        public bool passConfiguredInputMode = true;
        public bool showPreview = true;
        public string Status { get; private set; } = "Starting camera...";
        // Raised after the live picture is drawn, with the on-screen rect of the camera image, so other
        // components can draw overlays in unmirrored normalized image coordinates.
        public static event Action<Rect> PreviewDrawn;
        [Tooltip("Start the tracker without the person outline and cup detector (faster pose tracking). Applies on the next Play or Retry.")]
        public bool lightweightTracking = true;
        [Tooltip("Run the hand-gesture (fist) recognizer. Off saves CPU; P3 then triggers with the touch zone or auto arrival.")]
        public bool fistDetection = false;
        static Rect FitImage(Rect area, Texture image)
        {
            if (!image || image.height <= 0 || area.height <= 0) return area;
            float imageAspect = (float)image.width / image.height, areaAspect = area.width / area.height;
            if (imageAspect > areaAspect) { float h = area.width / imageAspect; return new Rect(area.x, area.y + (area.height - h) * .5f, area.width, h); }
            float w = area.height * imageAspect; return new Rect(area.x + (area.width - w) * .5f, area.y, w, area.height);
        }
        public bool HasLiveFrame => texture != null && Time.realtimeSinceStartup - lastFrame < 2;
        public Texture2D PreviewTexture => HasLiveFrame ? texture : null;
        DigiPhantController controller;
        UdpClient receiver;
        Process ownedBridge;
        Texture2D texture;
        float lastFrame = -1000;
        float startAfter;
        float nextRead;
        bool startAttempted;
        string bridgeOutput = "";
        readonly object outputLock = new object();

        void OnEnable()
        {
            if (!Application.isPlaying || Application.isBatchMode) return;
            BeginPreview();
        }
        public void BeginPreview()
        {
            StopPreview();
            controller = GetComponent<DigiPhantController>();
            if (controller == null) { Status = "Missing elephant controls"; return; }
            try
            {
                receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, controller.port + 2));
                receiver.Client.Blocking = false;
                nextRead = 0;
                startAfter = Time.realtimeSinceStartup + 1;
                startAttempted = false;
                Status = "Connecting to camera...";
            }
            catch (Exception e) when (e is SocketException || e is ArgumentOutOfRangeException)
            { Status = "Preview could not start: " + e.Message; }
        }
        public bool AcceptJpeg(byte[] bytes, float now)
        {
            if (bytes == null || bytes.Length < 4 || bytes.Length > 60000 || bytes[0] != 0xff || bytes[1] != 0xd8) return false;
            if (texture == null) texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (!ImageConversion.LoadImage(texture, bytes, false)) return false;
            lastFrame = now;
            Status = "Live camera · unmirrored";
            return true;
        }
        public void PollPreview(float now)
        {
            if (receiver == null) return;
            try
            {
                byte[] latest = null;
                var sender = new IPEndPoint(IPAddress.Loopback, 0);
                for (int i = 0; i < 32 && receiver.Available > 0; i++) latest = receiver.Receive(ref sender);
                if (latest != null) AcceptJpeg(latest, now);
            }
            catch (SocketException e) { Status = "Preview connection: " + e.Message; }
        }
        void Update()
        {
            if (receiver == null) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextRead) { PollPreview(now); nextRead = now + .08f; }
            if (!startAttempted && now >= startAfter)
            {
                startAttempted = true;
                if (autoStartBridge && !HasLiveFrame && !Application.isBatchMode) LaunchBridge();
            }
            if (ownedBridge != null && ownedBridge.HasExited)
            {
                string detail;
                lock (outputLock) detail = bridgeOutput;
                Status = "Camera stopped. Check camera permission or close an old tracking window, then Retry.";
                if (!string.IsNullOrWhiteSpace(detail)) UnityEngine.Debug.LogWarning("DigiPhant camera bridge: " + detail);
                ownedBridge.Dispose();
                ownedBridge = null;
            }
            else if (texture != null && !HasLiveFrame) Status = "Camera feed interrupted";
        }
        void LaunchBridge()
        {
#if UNITY_EDITOR
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../DigiPhantStarter/Tracking"));
            string environmentExecutable = Application.platform == RuntimePlatform.WindowsEditor
                ? ".venv312/Scripts/python.exe" : ".venv312/bin/python";
            string fallbackExecutable = Application.platform == RuntimePlatform.WindowsEditor
                ? ".venv/Scripts/python.exe" : ".venv/bin/python";
            string executable = Path.Combine(folder, environmentExecutable);
            if (!File.Exists(executable)) executable = Path.Combine(folder, fallbackExecutable);
            if (!File.Exists(executable) || !File.Exists(Path.Combine(folder, "pose_landmarker_full.task")))
            { Status = "Camera setup needed: follow DigiPhantStarter/README.md section 3."; return; }
            try
            {
                lock (outputLock) bridgeOutput = "";
                // WorkingDirectory makes all arguments fixed filenames or validated integers.
                string arguments = "-u bridge.py --no-window --people " + controller.performerCount
                    + " --camera " + Mathf.Clamp(cameraIndex, 0, 8) + " --port " + controller.port;
                if (passConfiguredInputMode && controller.upperBodyOnly) arguments += " --upper-body-only";
                // The person outline (segmentation) and cup detector are preview/diagnostic only; skipping them
                // leaves more CPU for pose tracking, so poses arrive faster and travel pauses less.
                if (lightweightTracking) arguments += " --no-segmentation --no-cup-detection";
                if (!fistDetection) arguments += " --no-fist-detection";
                var info = new ProcessStartInfo(executable, arguments)
                {
                    WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                ownedBridge = new Process { StartInfo = info };
                ownedBridge.OutputDataReceived += CaptureOutput;
                ownedBridge.ErrorDataReceived += CaptureOutput;
                ownedBridge.Start();
                ownedBridge.BeginOutputReadLine();
                ownedBridge.BeginErrorReadLine();
                Status = "Opening webcam — allow camera access if prompted";
            }
            catch (Exception e)
            {
                ownedBridge?.Dispose(); ownedBridge = null;
                Status = "Camera launch failed: " + e.Message;
            }
#else
            Status = "Start the camera bridge on this computer.";
#endif
        }
        void CaptureOutput(object sender, DataReceivedEventArgs args)
        {
            if (args.Data == null) return;
            lock (outputLock)
            {
                bridgeOutput += args.Data + "\n";
                if (bridgeOutput.Length > 4000) bridgeOutput = bridgeOutput.Substring(bridgeOutput.Length - 4000);
            }
        }
        public void StopPreview()
        {
            receiver?.Close(); receiver = null;
            if (ownedBridge != null)
            {
                try { if (!ownedBridge.HasExited) ownedBridge.Kill(); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
                ownedBridge.Dispose(); ownedBridge = null;
            }
            if (texture != null)
            {
                if (Application.isPlaying) Destroy(texture);
                else DestroyImmediate(texture);
            }
            texture = null;
            lastFrame = -1000;
        }
        void OnDisable() => StopPreview();
        public void DrawInline(float width)
        {
            if (!showPreview) return;
            GUILayout.Label("CAMERA PREVIEW");
            float height = Mathf.Min(width * .75f, Screen.height * .25f);
            Rect picture = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(picture, Texture2D.blackTexture, ScaleMode.StretchToFill, false);
            if (HasLiveFrame) { GUI.DrawTexture(picture, texture, ScaleMode.ScaleToFit); PreviewDrawn?.Invoke(FitImage(picture, texture)); }
            GUILayout.BeginHorizontal();
            GUILayout.Label(Status, new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 11 });
            if (GUILayout.Button("Retry", GUILayout.Width(54))) BeginPreview();
            GUILayout.EndHorizontal();
        }
        void OnGUI()
        {
            if (!showPreview || (controller != null && controller.showControls)) return;
            float width = Mathf.Clamp(Screen.width * .27f, 180, 350);
            float height = width * .75f;
            var box = new Rect(Screen.width - width - 12, 12, width, height + 85);
            GUI.Box(box, GUIContent.none);
            GUI.Label(new Rect(box.x + 10, box.y + 5, width - 20, 22), "CAMERA PREVIEW");
            var picture = new Rect(box.x + 8, box.y + 30, width - 16, height - 12);
            GUI.DrawTexture(picture, Texture2D.blackTexture, ScaleMode.StretchToFill, false);
            if (HasLiveFrame) { GUI.DrawTexture(picture, texture, ScaleMode.ScaleToFit); PreviewDrawn?.Invoke(FitImage(picture, texture)); }
            var labelStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 11 };
            GUI.Label(new Rect(box.x + 8, box.y + height + 22, width - 75, 55), Status, labelStyle);
            if (GUI.Button(new Rect(box.xMax - 62, box.y + height + 30, 54, 26), "Retry")) BeginPreview();
        }
    }
}
