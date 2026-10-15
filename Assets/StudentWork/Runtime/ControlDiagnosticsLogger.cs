using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using DigiPhant;
using UnityEngine;

namespace StudentWork
{
    // Numeric signals only. Run after the controller and locomotion have applied this frame.
    [DefaultExecutionOrder(1000)]
    public sealed class ControlDiagnosticsLogger : MonoBehaviour
    {
        public DigiPhantController controller;
        public DigiPhantLocomotion locomotion;
        public bool autoStartOnPlay = true;
        [Min(.1f)] public float sampleIntervalSeconds = .1f;
        [Min(.1f)] public float motionFrameGapSeconds = .5f;
        [Min(.1f)] public float positionJumpThresholdUnits = 5f;
        public bool IsLogging { get; private set; }
        public string SessionFolder { get; private set; }
        public string Status { get; private set; } = "Control diagnostics save automatically during Play; no images.";
        public int SampleRows => summary == null ? 0 : summary.sampleRows;

        [Serializable]
        public sealed class SessionSummary
        {
            public int schemaVersion = 2;
            public string dataKind = "local_control_diagnostics";
            public bool imagesSaved, videoSaved;
            public string sessionId, startedUtc, completedUtc, unityVersion, sceneName;
            public string csvFile = "control.csv";
            public double durationSeconds, lastSampleElapsedSeconds;
            public int sampleRows, csvRows, observedCalibrationRevisionChanges;
            public int unavailableP1Samples, notReadySamples, reversePendingSamples, associatedCupSamples;
            public int maximumRawDetected, maximumValidPeople, maximumAssignedPeople;
            public int latestCaptureWidth, latestCaptureHeight;
            public int motionFrameGapSamples, positionJumpSamples, reusedPoseSampleRows;
            public float observedPathDistanceUnits;
            public double observedMotionSeconds;
            public bool finalCalibrated;
            public int finalCalibrationRevision;
            public string finalCalibrationResetReason, finalInputStatus, finalAction, finalDirection, finalFistDetectorStatus;
            public float finalSpeed;
            public string stopReason = "in_progress";
            public string definitions = "Controller snapshots at up to 10 Hz by default; no images or inference of physical drinking/real elephant behaviour. Geometry coordinates are normalized image coordinates. Legacy hand ratio, calibrated fixed-scale hand delta and post-dead-zone forward command are separate signals. Fist detector status, P3 closed state and confidence are recorded when available. Pose age includes capture-to-send age plus Unity receipt age. Independent motion is displacement between log samples divided by Stopwatch elapsed time; gaps/jumps are excluded, and its path length is sampled chord distance. End row retains the last observed controls; elapsed time survives Unity clock reset on Stop.";
        }

        sealed class Snapshot
        {
            public string[] cells;
            public bool calibrated, p1Available, ready, reversePending, cupVisible;
            public int revision;
            public int rawDetected, validPeople, assignedPeople, width, height;
            public string resetReason, inputStatus, action, direction;
            public float speed;
            public int freshPoseRevision;
            public bool poseAvailable, motionAvailable, motionGap, positionJump;
            public float observedDistance;
            public double observedMotionSeconds;
        }

        StreamWriter csv;
        SessionSummary summary;
        Snapshot lastSnapshot;
        Stopwatch stopwatch;
        double startedClock, lastClock, lastSampleClock, lastFlushClock;
        int lastRevision;
        bool previousPositionAvailable;
        Vector3 previousPosition;
        double previousPositionClock;

        public static string CsvHeader
        {
            get
            {
                var fields = new List<string> { "session_id", "row_kind", "elapsed_s", "input_mode", "performer_count", "solo_role",
                    "calibrated", "calibration_revision", "calibration_reset_reason", "received_pose_frames",
                    "p1_raw_available", "p1_controller_left_hand", "p1_neutral_left_hand", "p1_relative_available", "p1_left_hand_delta",
                    "p1_neutral_shoulder_width", "desired_forward", "accepted_forward", "reverse_pending", "input_status",
                    "command_speed", "direction", "action", "mapped_inputs_ready", "mapped_inputs_diagnostic" };
                for (int slot = 1; slot <= 3; slot++)
                    fields.AddRange(new[] { "p" + slot + "_active", "p" + slot + "_tracked", "p" + slot + "_ready", "p" + slot + "_readiness_reason",
                        "p" + slot + "_left_hand_visible", "p" + slot + "_right_hand_visible", "p" + slot + "_lean_visible",
                        "p" + slot + "_head_visible", "p" + slot + "_left_arm_visible" });
                fields.AddRange(new[] { "tracking_diagnostics_available", "raw_detected", "valid_people", "assigned_people", "capture_width", "capture_height",
                    "camera_fps", "frame_sequence", "pose_inference_ms", "cup_inference_ms", "fist_inference_ms", "loop_ms", "regional_tracking", "rejection_reasons",
                    "regions_json", "tracking_status", "cup_detector_status", "fist_detector_status", "p3_fist_available", "p3_fist_closed", "p3_fist_confidence", "p3_cup_association_status", "p3_cup_visible", "p3_cup_score", "p3_cup_age_s",
                    "p1_geometry_readable", "p1_geometry_present", "p1_neutral_geometry_present", "p1_current_hand_vertical", "p1_neutral_hand_vertical",
                    "p1_current_shoulder_width", "p1_current_shoulder_confidence", "p1_current_wrist_confidence",
                    "p1_current_left_shoulder_x", "p1_current_left_shoulder_y", "p1_current_right_shoulder_x", "p1_current_right_shoulder_y",
                    "p1_current_left_wrist_x", "p1_current_left_wrist_y", "p1_neutral_left_shoulder_x", "p1_neutral_left_shoulder_y",
                    "p1_neutral_right_shoulder_x", "p1_neutral_right_shoulder_y", "p1_neutral_left_wrist_x", "p1_neutral_left_wrist_y",
                    "p1_neutral_shoulder_confidence", "p1_neutral_wrist_confidence", "pose_sample_available", "pose_sample_age_s", "capture_age_at_send_s",
                    "tracking_frame_sequence", "fresh_pose_revision", "calibration_pending", "controller_status", "travel_input_safe", "travel_input_safety_status",
                    "reverse_evidence_samples", "reverse_evidence_s", "reverse_confirmation_progress", "locomotion_actual_speed_signed", "applied_delta_s", "real_delta_s",
                    "travel_position_available", "travel_world_x", "travel_world_y", "travel_world_z", "travel_heading_degrees",
                    "observed_motion_available", "observed_speed_abs", "observed_speed_signed", "observed_displacement_units", "motion_sample_interval_s",
                    "motion_frame_gap", "position_reset_or_jump", "stop_reason" });
                return string.Join(",", fields);
            }
        }

        void OnEnable()
        {
            if (autoStartOnPlay && Application.isPlaying && !Application.isBatchMode) BeginSession();
        }

        public bool BeginSession()
        {
            if (!Application.isPlaying || Application.isBatchMode) return false;
            return BeginSessionAt(DefaultOutputRoot(), Time.realtimeSinceStartupAsDouble, Time.realtimeSinceStartup);
        }

        // Explicit clocks permit deterministic Editor validation without camera access or Play mode.
        public bool BeginSessionAt(string outputRoot, double clockSeconds, float signalNow)
        {
            if (IsLogging || !enabled || !gameObject.activeInHierarchy || !Finite(clockSeconds)) return false;
            if (!controller) controller = GetComponent<DigiPhantController>();
            if (!locomotion) locomotion = GetComponent<DigiPhantLocomotion>();
            if (!controller || !locomotion) { Status = "Control diagnostics need controller and locomotion references."; return false; }
            try
            {
                startedClock = lastClock = lastSampleClock = lastFlushClock = clockSeconds;
                previousPositionAvailable = false;
                stopwatch = Stopwatch.StartNew();
                summary = new SessionSummary {
                    sessionId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    startedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), unityVersion = Application.unityVersion,
                    sceneName = gameObject.scene.name
                };
                SessionFolder = Path.Combine(outputRoot, summary.sessionId);
                Directory.CreateDirectory(SessionFolder);
                csv = new StreamWriter(Path.Combine(SessionFolder, summary.csvFile), false, new UTF8Encoding(false));
                csv.WriteLine(CsvHeader);
                lastRevision = controller.CalibrationRevision;
                IsLogging = true;
                WriteSnapshot("start", 0, Capture(signalNow, clockSeconds));
                FlushSummary("in_progress", 0);
                Status = "Control diagnostics: " + SessionFolder;
                UnityEngine.Debug.Log("CONTROL_DIAGNOSTICS_STARTED: " + SessionFolder);
                return true;
            }
            catch (Exception error) { Fail(error); return false; }
        }

        void LateUpdate()
        {
            if (!IsLogging) return;
            RecordAt(startedClock + stopwatch.Elapsed.TotalSeconds, Time.realtimeSinceStartup);
        }

        public void RecordAt(double clockSeconds, float signalNow)
        {
            if (!IsLogging || !Finite(clockSeconds) || clockSeconds < lastClock) return;
            lastClock = clockSeconds;
            if (!controller || !locomotion) { FinishSessionAt(clockSeconds, "missing_reference"); return; }
            if (clockSeconds - lastSampleClock < Math.Max(.1, sampleIntervalSeconds) - .000001) return;
            try
            {
                WriteSnapshot("sample", clockSeconds - startedClock, Capture(signalNow, clockSeconds));
                lastSampleClock = clockSeconds;
                if (clockSeconds - lastFlushClock >= 1)
                {
                    FlushSummary("in_progress", clockSeconds - startedClock);
                    lastFlushClock = clockSeconds;
                }
            }
            catch (Exception error) { Fail(error); }
        }

        Snapshot Capture(float now, double clockSeconds)
        {
            bool rawAvailable = controller.TryReadRawMovement(1, Movement.LeftHandHeight, now, out float raw, out float neutral);
            bool relativeAvailable = controller.TryReadMovement(1, Movement.LeftHandHeight, now, out float delta);
            bool ready = controller.AreMappedInputsReady(now, out string readiness);
            var diagnostics = controller.LatestTrackingDiagnostics;
            float speed = Safe(locomotion.CurrentSpeed);
            string direction = speed > .001f ? "forward" : speed < -.001f ? "reverse" : "stationary";
            var cells = new List<string> {
                controller.inputMode.ToString(), Integer(controller.performerCount), Integer(controller.SoloRole), Boolean(controller.IsCalibrated),
                Integer(controller.CalibrationRevision), controller.CalibrationResetReason, Integer(controller.ReceivedFrames),
                Boolean(rawAvailable), rawAvailable ? Number(raw) : "", rawAvailable && controller.IsCalibrated ? Number(neutral) : "",
                Boolean(relativeAvailable), relativeAvailable ? Number(delta) : "", Number(controller.NeutralShoulderWidth(1)),
                Number(locomotion.DesiredForward), Number(locomotion.AcceptedForward), Boolean(locomotion.ReversePending), locomotion.InputStatus,
                Number(speed), direction, locomotion.CurrentAction, Boolean(ready), readiness
            };
            for (int slot = 1; slot <= 3; slot++)
            {
                bool slotReady = controller.IsPerformerInputReady(slot, now, out string reason);
                cells.AddRange(new[] { Boolean(slot <= controller.performerCount), Boolean(controller.IsTracked(slot, now)), Boolean(slotReady), reason,
                    Boolean(controller.IsMovementVisible(slot, Movement.LeftHandHeight, now)), Boolean(controller.IsMovementVisible(slot, Movement.RightHandHeight, now)),
                    Boolean(controller.IsMovementVisible(slot, Movement.Lean, now)), Boolean(controller.IsMovementVisible(slot, Movement.HeadYaw, now)),
                    Boolean(controller.IsMovementVisible(slot, Movement.LeftArmExtension, now)) });
            }
            cells.Add(Boolean(diagnostics != null));
            cells.AddRange(diagnostics == null ? new string[14] : new[] {
                Integer(diagnostics.rawDetected), Integer(diagnostics.validPeople), Integer(diagnostics.assignedPeople), Integer(diagnostics.captureWidth), Integer(diagnostics.captureHeight),
                Number(diagnostics.cameraFps), Integer(diagnostics.frameSequence), Number(diagnostics.poseInferenceMs), Number(diagnostics.cupInferenceMs), Number(diagnostics.fistInferenceMs), Number(diagnostics.loopMs),
                Boolean(diagnostics.regionalTracking), diagnostics.rejectionReasons, JsonUtility.ToJson(new RegionWrapper { regions = diagnostics.regions })
            });
            bool cupVisible = controller.TryReadCup(controller.P3InputSlot, now, out CupObservation cup);
            bool fistVisible = controller.TryReadFist(controller.P3InputSlot, true, now, out bool fistClosed, out float fistConfidence);
            cells.AddRange(new[] { controller.TrackingStatus, controller.CupDetectorStatus,
                controller.FistDetectorStatus, Boolean(fistVisible), Boolean(fistVisible && fistClosed), fistVisible ? Number(fistConfidence) : "",
                controller.GetCupAssociationStatus(controller.P3InputSlot), Boolean(cupVisible), cupVisible ? Number(cup.score) : "", cupVisible ? Number(cup.ageSeconds) : "" });
            bool geometryReadable = controller.TryReadUpperBodyGeometry(1, now, out UpperBodyGeometry current, out UpperBodyGeometry origin);
            bool currentPresent = current != null && current.present, originPresent = origin != null && origin.present;
            bool currentWrist = currentPresent && current.leftHandConfidence > 0, originWrist = originPresent && origin.leftHandConfidence > 0;
            cells.AddRange(new[] {
                Boolean(geometryReadable), Boolean(currentPresent), Boolean(originPresent), currentWrist ? Number(current.leftHandVertical) : "", originWrist ? Number(origin.leftHandVertical) : "",
                currentPresent ? Number(current.shoulderWidth) : "", currentPresent ? Number(current.shoulderConfidence) : "", currentPresent ? Number(current.leftHandConfidence) : "",
                currentPresent ? Number(current.leftShoulderX) : "", currentPresent ? Number(current.leftShoulderY) : "", currentPresent ? Number(current.rightShoulderX) : "", currentPresent ? Number(current.rightShoulderY) : "",
                currentWrist ? Number(current.leftWristX) : "", currentWrist ? Number(current.leftWristY) : "", originPresent ? Number(origin.leftShoulderX) : "", originPresent ? Number(origin.leftShoulderY) : "",
                originPresent ? Number(origin.rightShoulderX) : "", originPresent ? Number(origin.rightShoulderY) : "", originWrist ? Number(origin.leftWristX) : "", originWrist ? Number(origin.leftWristY) : "",
                originPresent ? Number(origin.shoulderConfidence) : "", originPresent ? Number(origin.leftHandConfidence) : ""
            });
            float poseAge = controller.LatestPoseAgeSeconds(now);
            bool poseAvailable = controller.ReceivedFrames > 0 && poseAge >= 0 && Finite(poseAge);
            cells.AddRange(new[] {
                Boolean(poseAvailable), poseAvailable ? Number(poseAge) : "", diagnostics != null ? Number(diagnostics.captureAgeSeconds) : "",
                Integer(controller.TrackingFrameSequence), Integer(controller.FreshPoseRevision), Boolean(controller.CalibrationPending), controller.Status,
                Boolean(controller.TravelInputSafetyReady(now)), controller.TravelInputSafetyStatus, Integer(locomotion.ReverseEvidenceSamples),
                Number(locomotion.ReverseEvidenceSeconds), Number(locomotion.ReverseConfirmationProgress), Number(locomotion.ActualSpeedSigned), Number(locomotion.LastAppliedDeltaSeconds), Number(locomotion.LastRealDeltaSeconds)
            });
            bool positionAvailable = locomotion.travelRoot && FiniteVector(locomotion.travelRoot.position);
            Vector3 position = positionAvailable ? locomotion.travelRoot.position : Vector3.zero;
            double motionInterval = previousPositionAvailable ? clockSeconds - previousPositionClock : 0;
            Vector3 displacement = position - previousPosition; displacement.y = 0;
            float distance = positionAvailable && previousPositionAvailable ? displacement.magnitude : 0;
            bool motionGap = motionInterval > Math.Max(.1, motionFrameGapSeconds);
            bool jump = distance > Mathf.Max(.1f, positionJumpThresholdUnits);
            bool motionAvailable = positionAvailable && previousPositionAvailable && motionInterval > .000001 && !motionGap && !jump;
            float observedSpeed = motionAvailable ? (float)(distance / motionInterval) : 0;
            Vector3 facing = positionAvailable ? locomotion.travelRoot.forward : Vector3.forward; facing.y = 0;
            float observedSignedSpeed = motionAvailable ? (float)(Vector3.Dot(displacement, facing.normalized) / motionInterval) : 0;
            cells.AddRange(new[] {
                Boolean(positionAvailable), positionAvailable ? Number(position.x) : "", positionAvailable ? Number(position.y) : "", positionAvailable ? Number(position.z) : "",
                positionAvailable ? Number(locomotion.travelRoot.eulerAngles.y) : "", Boolean(motionAvailable), motionAvailable ? Number(observedSpeed) : "",
                motionAvailable ? Number(observedSignedSpeed) : "", positionAvailable && previousPositionAvailable ? Number(distance) : "",
                previousPositionAvailable ? Number(motionInterval) : "", Boolean(motionGap), Boolean(jump)
            });
            previousPositionAvailable = positionAvailable;
            if (positionAvailable) { previousPosition = position; previousPositionClock = clockSeconds; }
            return new Snapshot { cells = cells.ToArray(), calibrated = controller.IsCalibrated, revision = controller.CalibrationRevision,
                resetReason = controller.CalibrationResetReason, p1Available = relativeAvailable, ready = ready, reversePending = locomotion.ReversePending, cupVisible = cupVisible,
                rawDetected = diagnostics == null ? 0 : diagnostics.rawDetected, validPeople = diagnostics == null ? 0 : diagnostics.validPeople,
                assignedPeople = diagnostics == null ? 0 : diagnostics.assignedPeople, width = diagnostics == null ? 0 : diagnostics.captureWidth,
                height = diagnostics == null ? 0 : diagnostics.captureHeight, inputStatus = locomotion.InputStatus, speed = speed, direction = direction, action = locomotion.CurrentAction,
                poseAvailable = poseAvailable, freshPoseRevision = controller.FreshPoseRevision, motionAvailable = motionAvailable, motionGap = motionGap, positionJump = jump,
                observedDistance = motionAvailable ? distance : 0, observedMotionSeconds = motionAvailable ? motionInterval : 0 };
        }

        [Serializable] sealed class RegionWrapper { public CameraRegionDiagnostics[] regions; }

        void WriteSnapshot(string kind, double elapsed, Snapshot snapshot)
        {
            WriteRow(kind, elapsed, snapshot, "");
            summary.sampleRows++;
            summary.lastSampleElapsedSeconds = elapsed;
            if (snapshot.revision != lastRevision) summary.observedCalibrationRevisionChanges++;
            lastRevision = snapshot.revision;
            if (!snapshot.p1Available) summary.unavailableP1Samples++;
            if (!snapshot.ready) summary.notReadySamples++;
            if (snapshot.reversePending) summary.reversePendingSamples++;
            if (snapshot.cupVisible) summary.associatedCupSamples++;
            if (lastSnapshot != null && snapshot.poseAvailable && snapshot.freshPoseRevision == lastSnapshot.freshPoseRevision) summary.reusedPoseSampleRows++;
            if (snapshot.motionGap) summary.motionFrameGapSamples++;
            if (snapshot.positionJump) summary.positionJumpSamples++;
            summary.observedPathDistanceUnits += snapshot.observedDistance;
            summary.observedMotionSeconds += snapshot.observedMotionSeconds;
            summary.maximumRawDetected = Math.Max(summary.maximumRawDetected, snapshot.rawDetected);
            summary.maximumValidPeople = Math.Max(summary.maximumValidPeople, snapshot.validPeople);
            summary.maximumAssignedPeople = Math.Max(summary.maximumAssignedPeople, snapshot.assignedPeople);
            if (snapshot.width > 0) summary.latestCaptureWidth = snapshot.width;
            if (snapshot.height > 0) summary.latestCaptureHeight = snapshot.height;
            summary.finalCalibrated = snapshot.calibrated;
            summary.finalCalibrationRevision = snapshot.revision;
            summary.finalCalibrationResetReason = snapshot.resetReason;
            summary.finalInputStatus = snapshot.inputStatus;
            summary.finalAction = snapshot.action;
            summary.finalDirection = snapshot.direction;
            summary.finalSpeed = snapshot.speed;
            summary.finalFistDetectorStatus = controller.FistDetectorStatus;
            lastSnapshot = snapshot;
        }

        void WriteRow(string kind, double elapsed, Snapshot snapshot, string reason)
        {
            var fields = new List<string> { summary.sessionId, kind, Number(elapsed) };
            fields.AddRange(snapshot.cells);
            fields.Add(reason);
            csv.WriteLine(string.Join(",", fields.ConvertAll(Csv)));
            summary.csvRows++;
        }

        void FlushSummary(string reason, double elapsed)
        {
            csv?.Flush();
            summary.durationSeconds = elapsed;
            summary.stopReason = reason;
            File.WriteAllText(Path.Combine(SessionFolder, "summary.json"), JsonUtility.ToJson(summary, true), new UTF8Encoding(false));
        }

        public void FinishSession(string reason = "stopped")
        {
            if (!IsLogging) return;
            FinishSessionAt(Math.Max(lastClock, startedClock + stopwatch.Elapsed.TotalSeconds), reason);
        }

        public void FinishSessionAt(double clockSeconds, string reason)
        {
            if (!IsLogging) return;
            IsLogging = false; // LateUpdate and further explicit samples cannot append after completion.
            stopwatch?.Stop();
            double elapsed = Math.Max(lastClock, Finite(clockSeconds) ? clockSeconds : lastClock) - startedClock;
            try
            {
                // Controller OnDisable may already have reset Unity state; preserve the last observed signals.
                if (lastSnapshot != null) WriteRow("end", elapsed, lastSnapshot, reason);
                summary.completedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                FlushSummary(reason, elapsed);
                Status = "Saved control diagnostics: " + SessionFolder;
            }
            catch (Exception error) { Status = "Control diagnostics could not finish: " + error.Message; UnityEngine.Debug.LogWarning(Status); }
            finally { csv?.Dispose(); csv = null; }
        }

        void Fail(Exception error)
        {
            IsLogging = false;
            stopwatch?.Stop();
            csv?.Dispose(); csv = null;
            Status = "Control diagnostics write failed: " + error.Message;
            UnityEngine.Debug.LogWarning(Status);
        }

        static string DefaultOutputRoot()
        {
#if UNITY_EDITOR
            return Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/ControlDiagnostics"));
#else
            return Path.Combine(Application.persistentDataPath, "Logs/ControlDiagnostics");
#endif
        }
        void OnDisable() => FinishSession("component_disabled_or_play_stopped");
        void OnApplicationQuit() => FinishSession("application_quit");
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        static bool FiniteVector(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        static float Safe(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : value;
        static string Number(double value) => Finite(value) ? value.ToString("0.######", CultureInfo.InvariantCulture) : "";
        static string Integer(int value) => value.ToString(CultureInfo.InvariantCulture);
        static string Boolean(bool value) => value ? "true" : "false";
        static string Csv(string value)
        {
            value = (value ?? "").Replace('\r', ' ').Replace('\n', ' ');
            return value.IndexOfAny(new[] { ',', '"' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }
    }
}
