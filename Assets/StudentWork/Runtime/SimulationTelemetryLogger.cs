using System;
using System.Globalization;
using System.IO;
using System.Text;
using DigiPhant;
using UnityEngine;

namespace StudentWork
{
    [Serializable]
    public class LandmarkZone
    {
        public string label;
        public Transform centre;
        [Min(.1f)] public float radiusUnits = 4.5f;
    }

    // This measures the virtual puppet. It is not evidence of real elephant behaviour.
    // Run after DigiPhantController.LateUpdate so positions reflect the current frame.
    [DefaultExecutionOrder(500)]
    public class SimulationTelemetryLogger : MonoBehaviour
    {
        public const string DataKind = "virtual_proxy_simulation";
        public DigiPhantController controller;
        public DigiPhantLocomotion locomotion;
        public Transform travelRoot;
        public Transform routeAnchor;
        public Vector3[] routeLocalPoints = Array.Empty<Vector3>();
        public LandmarkZone[] landmarkZones = Array.Empty<LandmarkZone>();
        public string sessionLabel = "Experiment_01";
        [Min(.05f)] public float sampleIntervalSeconds = .1f;
        [Min(0)] public float stationarySpeedThreshold = .1f;
        [Min(.1f)] public float positionJumpThresholdUnits = 5f;
        [Min(.1f)] public float frameGapThresholdSeconds = .5f;
        public bool showPanel = true;
        public string setupSourceScene;
        public string courseBackupScene;
        public bool IsLogging { get; private set; }
        public string Status { get; private set; } = "Simulation/proxy data. Start a log for each trial.";
        public string SessionFolder { get; private set; }

        StreamWriter trajectory;
        double started, previousTime, lastSampleTime;
        Vector3 previousPosition;
        float pathDistance, measuredSpeed, observedSeconds, stationarySeconds;
        int samples, discontinuities, frameGaps;
        ZoneResult[] zones;
        bool[] inside;
        string initialInputMode;
        int initialPerformerCount;
        bool initialUpperBodyOnly;
        string initialMappings;

        [Serializable]
        public class ZoneResult
        {
            public string label;
            public float radiusUnits;
            public float insideSeconds;
            public float stationaryInsideSeconds;
            public int entries;
        }

        [Serializable]
        class SessionResult
        {
            public string dataKind = DataKind;
            public string limitation = "Virtual puppet trajectory in Unity units; not real elephant behaviour. Landmarks are decorative and no collision events are measured.";
            public string sessionLabel;
            public string startedUtc;
            public string initialInputMode;
            public int initialPerformerCount;
            public bool initialUpperBodyOnly;
            public string initialMappings;
            public float durationSeconds;
            public float observedSeconds;
            public float pathDistanceUnits;
            public float stationarySeconds;
            public int trajectorySamples;
            public int positionDiscontinuities;
            public int frameGaps;
            public float sampleIntervalSeconds;
            public float stationarySpeedThresholdUnitsPerSecond;
            public float positionJumpThresholdUnits;
            public float frameGapThresholdSeconds;
            public string distanceDefinition = "Sum of horizontal travel-root displacement per observed frame. Jumps above the threshold and gaps above the threshold are excluded.";
            public string dwellDefinition = "Observed time inside each horizontal landmark radius; stationary dwell also requires measured speed at or below the threshold. Time in overlapping zones is counted separately.";
            public string routeDefinition = "Closest projection onto the course centreline, in Unity units. Projected route position may decrease or jump at adjacent segments; it is not confirmed ordered completion.";
            public string relativeTimeDefinition = "Real seconds since Start simulation log. Observed time excludes frame gaps; it can be shorter than session duration.";
            public string stopReason;
            public ZoneResult[] zones;
        }

        string startedUtc;

        [ContextMenu("Start Simulation Log (Play Mode)")]
        public void BeginSession()
        {
            if (!Application.isPlaying || IsLogging) return;
            if (!controller || !locomotion || !travelRoot || !routeAnchor || routeLocalPoints == null || routeLocalPoints.Length < 2)
            { Status = "Missing references. Run Student Work/Prepare Team Scene outside Play mode."; return; }
            try
            {
#if UNITY_EDITOR
                string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/Simulation"));
#else
                string output = Path.Combine(Application.persistentDataPath, "Logs/Simulation");
#endif
                startedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                string sessionId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                    + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                SessionFolder = Path.Combine(output, sessionId);
                Directory.CreateDirectory(SessionFolder);
                trajectory = new StreamWriter(Path.Combine(SessionFolder, "trajectory.csv"), false, new UTF8Encoding(false));
                trajectory.WriteLine("data_kind,row_kind,relative_time_s,world_x_units,world_y_units,world_z_units,measured_speed_units_per_s,command_speed_units_per_s,path_distance_units,projected_route_position_units,off_route_distance_units,nearest_route_segment,inside_landmark_zones,observed_time_s,stationary_time_s,input_mode,performer_count,upper_body_only,calibrated,received_frames,position_discontinuities,frame_gaps,action");
                started = previousTime = lastSampleTime = Time.realtimeSinceStartupAsDouble;
                previousPosition = travelRoot.position;
                pathDistance = measuredSpeed = observedSeconds = stationarySeconds = 0;
                samples = discontinuities = frameGaps = 0;
                initialInputMode = controller.inputMode.ToString();
                initialPerformerCount = controller.performerCount;
                initialUpperBodyOnly = controller.upperBodyOnly;
                initialMappings = JsonUtility.ToJson(new MappingSnapshot { controls = controller.controls,
                    forward = locomotion.forward, steering = locomotion.steering });
                if (landmarkZones == null) landmarkZones = Array.Empty<LandmarkZone>();
                zones = new ZoneResult[landmarkZones.Length];
                inside = new bool[landmarkZones.Length];
                for (int i = 0; i < zones.Length; i++)
                {
                    var zone = landmarkZones[i];
                    zones[i] = new ZoneResult { label = zone != null ? zone.label : "Missing zone",
                        radiusUnits = zone != null ? zone.radiusUnits : 0 };
                    inside[i] = IsInside(i, previousPosition);
                    if (inside[i]) zones[i].entries = 1;
                }
                IsLogging = true;
                WriteSample("start", started);
                WriteSummary("in_progress", started);
                Status = "Logging virtual proxy: " + sessionLabel;
                Debug.Log("SIMULATION_LOG_STARTED: " + SessionFolder);
            }
            catch (Exception error)
            {
                trajectory?.Dispose(); trajectory = null; IsLogging = false;
                Status = "Log could not start: " + error.Message;
                Debug.LogError(Status);
            }
        }

        [Serializable]
        class MappingSnapshot
        {
            public BoneControl[] controls;
            public MovementDriver forward, steering;
        }

        void LateUpdate()
        {
            if (!IsLogging) return;
            if (!travelRoot || !controller || !locomotion || !routeAnchor)
            { FinishSession("missing_reference"); return; }
            double now = Time.realtimeSinceStartupAsDouble;
            float dt = (float)(now - previousTime);
            if (dt <= 0) return;
            Vector3 position = travelRoot.position;
            float distance = HorizontalDistance(position, previousPosition);
            bool gap = dt > frameGapThresholdSeconds;
            bool jump = distance > positionJumpThresholdUnits;
            if (gap) frameGaps++;
            if (jump) discontinuities++;
            measuredSpeed = gap || jump ? 0 : distance / dt;
            if (!gap && !jump)
            {
                observedSeconds += dt;
                pathDistance += distance;
                bool stationary = measuredSpeed <= stationarySpeedThreshold;
                if (stationary) stationarySeconds += dt;
                for (int i = 0; i < zones.Length; i++)
                {
                    bool present = IsInside(i, position);
                    if (present)
                    {
                        zones[i].insideSeconds += dt;
                        if (stationary) zones[i].stationaryInsideSeconds += dt;
                        if (!inside[i]) zones[i].entries++;
                    }
                    inside[i] = present;
                }
            }
            else
            {
                // Do not turn a reset-to-start or a paused frame into travelled distance or dwell.
                for (int i = 0; i < inside.Length; i++) inside[i] = IsInside(i, position);
            }
            previousPosition = position;
            previousTime = now;
            if (now - lastSampleTime >= Mathf.Max(.05f, sampleIntervalSeconds))
            {
                try { WriteSample("sample", now); lastSampleTime = now; }
                catch (Exception error) { AbortAfterWriteFailure(error); }
            }
        }

        bool IsInside(int index, Vector3 position)
        {
            if (landmarkZones == null || index >= landmarkZones.Length) return false;
            var zone = landmarkZones[index];
            return zone != null && zone.centre && HorizontalDistance(position, zone.centre.position) <= zone.radiusUnits;
        }

        static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0;
            return Vector3.Distance(a, b);
        }

        void ProjectRoute(Vector3 position, out float along, out float away, out int segment)
        {
            along = 0; away = float.PositiveInfinity; segment = -1;
            float accumulated = 0;
            position.y = 0;
            for (int i = 0; i < routeLocalPoints.Length - 1; i++)
            {
                Vector3 a = routeAnchor.TransformPoint(routeLocalPoints[i]);
                Vector3 b = routeAnchor.TransformPoint(routeLocalPoints[i + 1]);
                a.y = b.y = 0;
                Vector3 delta = b - a;
                float length = delta.magnitude;
                if (length <= .0001f) continue;
                float fraction = Mathf.Clamp01(Vector3.Dot(position - a, delta) / delta.sqrMagnitude);
                float distance = Vector3.Distance(position, a + delta * fraction);
                if (distance < away) { away = distance; along = accumulated + fraction * length; segment = i + 1; }
                accumulated += length;
            }
        }

        void WriteSample(string rowKind, double now, Vector3? observedPosition = null)
        {
            Vector3 p = observedPosition ?? travelRoot.position;
            ProjectRoute(p, out float along, out float away, out int segment);
            var present = new StringBuilder();
            for (int i = 0; i < inside.Length; i++)
                if (inside[i]) { if (present.Length > 0) present.Append(" | "); present.Append(zones[i].label); }
            trajectory.WriteLine(string.Join(",", new[] {
                DataKind, rowKind, Number(now - started), Number(p.x), Number(p.y), Number(p.z),
                Number(measuredSpeed), Number(locomotion.CurrentSpeed), Number(pathDistance), Number(along),
                Number(away), segment.ToString(CultureInfo.InvariantCulture), Csv(present.ToString()),
                Number(observedSeconds), Number(stationarySeconds), controller.inputMode.ToString(),
                controller.performerCount.ToString(CultureInfo.InvariantCulture), controller.upperBodyOnly ? "true" : "false",
                controller.IsCalibrated ? "true" : "false", controller.ReceivedFrames.ToString(CultureInfo.InvariantCulture),
                discontinuities.ToString(CultureInfo.InvariantCulture), frameGaps.ToString(CultureInfo.InvariantCulture), Csv(locomotion.CurrentAction)
            }));
            trajectory.Flush();
            samples++;
        }

        static string Number(double number) => number.ToString("0.######", CultureInfo.InvariantCulture);
        static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";

        [ContextMenu("Stop Simulation Log")]
        public void EndSession() => FinishSession("user_stopped");

        void FinishSession(string reason)
        {
            if (!IsLogging) return;
            IsLogging = false;
            double now = Time.realtimeSinceStartupAsDouble;
            try
            {
                // Play-mode shutdown may reset the elephant before disabling this logger.
                // Retain the last observed position and its measurement time in the end row.
                if (travelRoot && controller && locomotion && routeAnchor) WriteSample("end", previousTime, previousPosition);
                trajectory?.Dispose(); trajectory = null;
                WriteSummary(reason, now);
                Status = "Saved simulation/proxy CSV: " + SessionFolder;
                Debug.Log("SIMULATION_LOG_SAVED: " + SessionFolder);
            }
            catch (Exception error) { AbortAfterWriteFailure(error); }
            finally { trajectory?.Dispose(); trajectory = null; }
        }

        void WriteSummary(string reason, double now)
        {
            var result = new SessionResult {
                sessionLabel = sessionLabel, startedUtc = startedUtc, initialInputMode = initialInputMode,
                initialPerformerCount = initialPerformerCount, initialUpperBodyOnly = initialUpperBodyOnly,
                initialMappings = initialMappings, durationSeconds = (float)(now - started),
                observedSeconds = observedSeconds, pathDistanceUnits = pathDistance, stationarySeconds = stationarySeconds,
                trajectorySamples = samples, positionDiscontinuities = discontinuities, frameGaps = frameGaps,
                sampleIntervalSeconds = sampleIntervalSeconds, stationarySpeedThresholdUnitsPerSecond = stationarySpeedThreshold,
                positionJumpThresholdUnits = positionJumpThresholdUnits, frameGapThresholdSeconds = frameGapThresholdSeconds,
                stopReason = reason, zones = zones
            };
            File.WriteAllText(Path.Combine(SessionFolder, "summary.json"), JsonUtility.ToJson(result, true), new UTF8Encoding(false));
            using (var dwell = new StreamWriter(Path.Combine(SessionFolder, "dwell.csv"), false, new UTF8Encoding(false)))
            {
                dwell.WriteLine("data_kind,landmark_zone,radius_units,inside_time_s,stationary_inside_time_s,entries");
                foreach (var zone in zones)
                    dwell.WriteLine(DataKind + "," + Csv(zone.label) + "," + Number(zone.radiusUnits) + ","
                        + Number(zone.insideSeconds) + "," + Number(zone.stationaryInsideSeconds) + "," + zone.entries);
            }
        }

        void AbortAfterWriteFailure(Exception error)
        {
            IsLogging = false;
            trajectory?.Dispose(); trajectory = null;
            Status = "Log stopped after write error: " + error.Message + ". Existing files retained: " + SessionFolder;
            Debug.LogError(Status);
        }

        void OnDisable() => FinishSession("component_disabled_or_play_stopped");
        void OnApplicationQuit() => FinishSession("application_quit");

        void OnGUI()
        {
            if (!showPanel) return;
            float width = Mathf.Min(310, Screen.width * .36f);
            GUILayout.BeginArea(new Rect(Screen.width - width - 10, 10, width, 168), GUI.skin.box);
            GUILayout.Label("SIMULATION / PROXY LOG");
            if (!IsLogging) sessionLabel = GUILayout.TextField(sessionLabel);
            else GUILayout.Label(sessionLabel + " · " + Number(Time.realtimeSinceStartupAsDouble - started) + " s");
            if (GUILayout.Button(IsLogging ? "Stop and save simulation log" : "Start simulation log"))
            { if (IsLogging) EndSession(); else BeginSession(); }
            GUILayout.Label(Status, new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 11 });
            GUILayout.EndArea();
        }
    }
}
