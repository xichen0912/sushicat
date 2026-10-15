using System;
using System.Collections.Generic;
using System.IO;
using DigiPhant;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StudentWork.Editor
{
    // Explicit user-invoked local camera test. SessionState survives Play domain reloads.
    [InitializeOnLoad]
    public static class SingleCameraValidation
    {
        const string ActiveKey = "StudentWork.SingleCameraValidation.Active";
        const string ReportKey = "StudentWork.SingleCameraValidation.Report";
        const double DurationSeconds = 40;
        static DiagnosticReport report;
        static double nextSample;

        [Serializable]
        public class SignalRange
        {
            public string movement;
            public string value_kind = "calibrated_neutral_relative";
            public bool available;
            public int sample_count;
            public float minimum;
            public float maximum;
        }

        [Serializable]
        public class CalibrationAttempt
        {
            public double elapsed_seconds;
            public bool mapped_inputs_ready;
            public bool attempted;
            public bool succeeded;
            public string diagnostic;
            public string controller_status;
        }

        [Serializable]
        public class DiagnosticReport
        {
            public string data_kind = "local_single_person_camera_diagnostic";
            public string source_requested = "Unity inherited camera bridge";
            public string gesture_test = "not_assessed";
            public string three_person_test = "not_assessed";
            public bool raw_images_saved;
            public bool video_saved;
            public string requested_utc;
            public string started_utc;
            public string completed_utc;
            public string saved_scene;
            public string output_directory;
            public string stage;
            public string status;
            public string stop_reason;
            public string error = "";
            public double requested_editor_time;
            public double started_editor_time;
            public double elapsed_seconds;
            public double next_calibration_elapsed = 10;
            public int performer_count = 1;
            public bool upper_body_only = true;
            public string input_mode = "Camera";
            public int camera_index;
            public int observation_samples;
            public int received_frames;
            public int tracked_samples;
            public int ready_samples;
            public int live_preview_samples;
            public bool ever_calibrated;
            public bool final_calibrated;
            public bool restored_saved_scene;
            public string controller_status;
            public string camera_status;
            public string readiness_diagnostic;
            public List<CalibrationAttempt> calibration_attempts = new List<CalibrationAttempt>();
            public List<SignalRange> signal_ranges = new List<SignalRange>();
        }

        static SingleCameraValidation()
        {
            if (SessionState.GetBool(ActiveKey, false))
                report = JsonUtility.FromJson<DiagnosticReport>(SessionState.GetString(ReportKey, ""));
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += Update;
        }

        [MenuItem("Student Work/Run Single Person Camera Diagnostic")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(ActiveKey, false))
                throw new InvalidOperationException("Stop Play mode and finish any existing camera diagnostic first.");
            if (PrefabStageUtility.GetCurrentPrefabStage() != null || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Exit Prefab Mode and open one saved scene first.");
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path) || scene.isDirty || !File.Exists(scene.path))
                throw new InvalidOperationException("Save or discard scene changes before the camera diagnostic.");
            var controllers = UnityEngine.Object.FindObjectsByType<DigiPhantController>(FindObjectsSortMode.None);
            if (controllers.Length != 1 || !controllers[0].isActiveAndEnabled)
                throw new InvalidOperationException("The saved scene needs exactly one active DigiPhantController.");

            var utc = DateTime.UtcNow;
            report = new DiagnosticReport {
                requested_utc = utc.ToString("o"), saved_scene = scene.path,
                requested_editor_time = EditorApplication.timeSinceStartup,
                stage = "waiting_for_play", status = "waiting_for_play",
                output_directory = Path.GetFullPath(Path.Combine(Application.dataPath,
                    "../Logs/CameraDiagnostics/Unity-" + utc.ToString("yyyyMMddTHHmmssfffZ")))
            };
            foreach (Movement movement in Enum.GetValues(typeof(Movement)))
                report.signal_ranges.Add(new SignalRange { movement = movement.ToString() });
            Directory.CreateDirectory(report.output_directory);
            SessionState.SetBool(ActiveKey, true);
            Persist();
            Debug.Log("SINGLE_CAMERA_DIAGNOSTIC_STARTED: single seated person, local camera only; hold a neutral pose for calibration after 10 seconds. Stops after 40 seconds; no images or video saved.");
            try { EditorApplication.isPlaying = true; }
            catch (Exception error) { Finish("play_start_error", error.ToString()); }
        }

        [MenuItem("Student Work/Run Single Person Camera Diagnostic", true)]
        static bool CanRun() => !EditorApplication.isPlayingOrWillChangePlaymode
            && !SessionState.GetBool(ActiveKey, false);

        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(ActiveKey, false) || report == null) return;
            try
            {
                if (state == PlayModeStateChange.EnteredPlayMode && report.stage == "waiting_for_play")
                    ConfigureInPlay();
                else if (state == PlayModeStateChange.ExitingPlayMode)
                {
                    CaptureFinalState();
                    if (report.stage == "running")
                    {
                        report.stop_reason = "stopped_by_user";
                        report.error = "Play stopped before the diagnostic completed.";
                    }
                    report.stage = "stopping";
                    Persist();
                }
                else if (state == PlayModeStateChange.EnteredEditMode)
                {
                    if (report.stage == "waiting_for_play")
                    {
                        report.stop_reason = "play_did_not_start";
                        report.error = "Unity returned to Edit mode before the camera diagnostic started.";
                    }
                    RestoreSavedScene();
                }
            }
            catch (Exception error)
            {
                if (report != null) Finish("diagnostic_error", error.ToString());
                else Debug.LogException(error);
            }
        }

        static void ConfigureInPlay()
        {
            var controller = UnityEngine.Object.FindAnyObjectByType<DigiPhantController>();
            if (!controller) throw new InvalidOperationException("DigiPhant controls disappeared on entering Play.");
            controller.SetPerformerCount(1);
            controller.SetUpperBodyOnly(true);
            controller.showCameraPreview = true;
            controller.SetInputMode(InputMode.Camera);
            // The inherited component owns capture/bridge launch and cleanup; no shell launch here.
            var preview = controller.GetComponent<DigiPhantCameraPreview>();
            if (!preview) preview = controller.gameObject.AddComponent<DigiPhantCameraPreview>();
            preview.autoStartBridge = true;
            preview.enabled = true;
            report.camera_index = preview.cameraIndex;
            report.stage = "running";
            report.status = "observing_single_person_camera";
            report.started_utc = DateTime.UtcNow.ToString("o");
            report.started_editor_time = EditorApplication.timeSinceStartup;
            nextSample = 0;
            Persist();
        }

        static void Update()
        {
            if (!SessionState.GetBool(ActiveKey, false) || report == null) return;
            try
            {
                double now = EditorApplication.timeSinceStartup;
                if (report.stage == "waiting_for_play")
                {
                    if (Application.isPlaying) ConfigureInPlay();
                    else if (now - report.requested_editor_time >= DurationSeconds)
                        Finish("play_start_timeout", "Unity did not enter Play within 40 seconds.");
                    return;
                }
                if (report.stage == "stopping")
                {
                    if (!EditorApplication.isPlayingOrWillChangePlaymode) RestoreSavedScene();
                    return;
                }
                if (report.stage != "running" || !Application.isPlaying || now < nextSample) return;
                nextSample = now + .1;
                report.elapsed_seconds = now - report.started_editor_time;
                var controller = UnityEngine.Object.FindAnyObjectByType<DigiPhantController>();
                if (!controller) throw new InvalidOperationException("DigiPhant controls disappeared during the diagnostic.");
                float frameTime = Time.realtimeSinceStartup;
                bool ready = controller.AreMappedInputsReady(frameTime, out string diagnostic);
                report.observation_samples++;
                report.received_frames = Math.Max(report.received_frames, controller.ReceivedFrames);
                if (controller.IsTracked(1, frameTime)) report.tracked_samples++;
                if (ready) report.ready_samples++;
                var preview = controller.GetComponent<DigiPhantCameraPreview>();
                if (preview && preview.HasLiveFrame) report.live_preview_samples++;
                report.camera_status = preview ? preview.Status : "No preview component";
                report.readiness_diagnostic = diagnostic;

                if (!report.ever_calibrated && report.elapsed_seconds >= report.next_calibration_elapsed)
                {
                    bool calibrated = ready && controller.CalibrateAt(frameTime);
                    report.calibration_attempts.Add(new CalibrationAttempt {
                        elapsed_seconds = report.elapsed_seconds, mapped_inputs_ready = ready,
                        attempted = ready, succeeded = calibrated, diagnostic = diagnostic,
                        controller_status = controller.Status
                    });
                    report.ever_calibrated = calibrated;
                    report.next_calibration_elapsed = report.elapsed_seconds + 1;
                }
                foreach (var signal in report.signal_ranges)
                {
                    var movement = (Movement)Enum.Parse(typeof(Movement), signal.movement);
                    if (!controller.TryReadMovement(1, movement, frameTime, out float value)) continue;
                    if (float.IsNaN(value) || float.IsInfinity(value))
                        throw new InvalidOperationException("Non-finite public movement signal: " + signal.movement);
                    signal.minimum = signal.available ? Mathf.Min(signal.minimum, value) : value;
                    signal.maximum = signal.available ? Mathf.Max(signal.maximum, value) : value;
                    signal.available = true;
                    signal.sample_count++;
                }
                report.controller_status = controller.Status;
                report.final_calibrated = controller.IsCalibrated;
                Persist();
                if (report.elapsed_seconds >= DurationSeconds) Finish("duration_completed", "");
            }
            catch (Exception error)
            {
                if (report != null) Finish("diagnostic_error", error.ToString());
                else Debug.LogException(error);
            }
        }

        static void CaptureFinalState()
        {
            if (!Application.isPlaying) return;
            var controller = UnityEngine.Object.FindAnyObjectByType<DigiPhantController>();
            if (!controller) return;
            report.received_frames = Math.Max(report.received_frames, controller.ReceivedFrames);
            report.final_calibrated = controller.IsCalibrated;
            report.controller_status = controller.Status;
        }

        static void Finish(string reason, string error)
        {
            if (report == null) return;
            CaptureFinalState();
            report.stop_reason = reason;
            report.error = error;
            if (reason == "duration_completed" && string.IsNullOrEmpty(error))
            {
                if (report.received_frames == 0) report.error = "No pose packets received within 40 seconds.";
                else if (report.tracked_samples == 0) report.error = "No confident single-person tracking observed.";
                else if (!report.ever_calibrated) report.error = "Mapped inputs never supported successful neutral calibration.";
                else if (!report.final_calibrated) report.error = "Calibration was lost before the diagnostic ended.";
            }
            report.stage = "stopping";
            report.status = "stopping_and_restoring_saved_scene";
            Persist();
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
            else RestoreSavedScene();
        }

        static void RestoreSavedScene()
        {
            if (report == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            try
            {
                EditorSceneManager.OpenScene(report.saved_scene, OpenSceneMode.Single);
                report.restored_saved_scene = true;
            }
            catch (Exception error) { report.error += "\nSaved scene restore failed: " + error; }
            finally
            {
                report.stage = "completed";
                report.status = string.IsNullOrEmpty(report.error) ? "completed" : "completed_with_error";
                report.completed_utc = DateTime.UtcNow.ToString("o");
                try
                {
                    File.WriteAllText(Path.Combine(report.output_directory, "diagnostic.json"), JsonUtility.ToJson(report, true));
                    string summary = "SINGLE_CAMERA_DIAGNOSTIC_COMPLETED: " + report.output_directory
                        + " | frames=" + report.received_frames + " | finalCalibrated=" + report.final_calibrated;
                    if (string.IsNullOrEmpty(report.error)) Debug.Log(summary);
                    else Debug.LogWarning(summary + " | " + report.error);
                }
                finally
                {
                    SessionState.SetBool(ActiveKey, false);
                    SessionState.EraseString(ReportKey);
                    report = null;
                }
            }
        }

        static void Persist() => SessionState.SetString(ReportKey, JsonUtility.ToJson(report));
    }
}
