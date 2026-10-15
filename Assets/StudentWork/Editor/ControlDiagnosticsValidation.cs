using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using DigiPhant;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StudentWork.Editor
{
    // Writes only synthetic numeric diagnostics into a disposable temporary directory.
    public static class ControlDiagnosticsValidation
    {
        static int assertions;
        static int frameSequence;
        static void Check(bool condition, string message)
        { assertions++; if (!condition) throw new InvalidOperationException(message); }

        static PoseFrame Frame(DigiPhantController controller, float hand, int revision = 1)
        {
            int sequence = ++frameSequence;
            var frame = new PoseFrame {
                version = 1, performerCount = controller.performerCount, upperBodyOnly = true,
                hasCaptureAge = true, captureAgeSeconds = .01f, frameSequence = sequence,
                trackingSession = "control-diagnostics-synthetic", assignmentRevision = revision,
                trackingStatus = "Synthetic: P2 wrist, low \"confidence\"\nnext line",
                people = Enumerable.Range(1, controller.performerCount).Select(slot => new PerformerFrame {
                    slot = slot, values = new[] { hand, 0f, 0f, 0f, 0f, 0f }, confidence = Enumerable.Repeat(1f, 6).ToArray(),
                    hasLeftArm = true, leftArmConfidence = 1, hasHeadYaw = true, headYawConfidence = 1,
                    upperBodyGeometry = new UpperBodyGeometry { present = true, leftHandVertical = hand * .2f,
                        rightHandVertical = 0, shoulderWidth = .2f, shoulderConfidence = 1, leftHandConfidence = 1, rightHandConfidence = 1,
                        leftShoulderX = .05f + (slot - 1) * .33f, rightShoulderX = .25f + (slot - 1) * .33f,
                        leftShoulderY = .4f, rightShoulderY = .4f, leftWristX = .05f + (slot - 1) * .33f,
                        leftWristY = .4f - hand * .2f, rightWristX = .25f + (slot - 1) * .33f, rightWristY = .4f }
                }).ToArray()
            };
            var diagnosticsField = typeof(PoseFrame).GetFields().SingleOrDefault(field => field.FieldType == typeof(CameraTrackingDiagnostics));
            Check(diagnosticsField != null, "PoseFrame must expose optional camera diagnostics for local logs.");
            diagnosticsField.SetValue(frame, new CameraTrackingDiagnostics {
                rawDetected = 4, validPeople = 3, assignedPeople = 3, captureWidth = 1280, captureHeight = 720,
                frameSequence = sequence, captureAgeSeconds = .01f, cameraFps = 30, poseInferenceMs = 12, cupInferenceMs = 4, loopMs = 17,
                regionalTracking = true, rejectionReasons = "Synthetic rejected duplicate, confidence \"low\"",
                regions = new[] { new CameraRegionDiagnostics { slot = 1, xMin = 0, xMax = .333f, rawDetected = 1,
                    shoulderReady = true, leftHandReady = true, rightHandReady = true, headReady = true, reason = "Synthetic ready" } }
            });
            return frame;
        }

        static void Accept(DigiPhantController controller, PoseFrame frame, float time)
            => Check(controller.AcceptPacket(JsonUtility.ToJson(frame), time), "Synthetic diagnostics packet was rejected.");

        public static void RunBatch()
        {
            assertions = 0;
            frameSequence = 0;
            string output = Path.Combine(Path.GetTempPath(), "DigiPhant-ControlDiagnostics-" + Guid.NewGuid().ToString("N"));
            var scene = EditorSceneManager.OpenScene(UpperBodyCourseSceneSetup.CourseScene);
            var controller = UpperBodyCourseSceneSetup.Controller(scene);
            var locomotion = controller.GetComponent<DigiPhantLocomotion>();
            var logger = controller.GetComponent<ControlDiagnosticsLogger>();
            if (!logger) logger = controller.gameObject.AddComponent<ControlDiagnosticsLogger>();
            logger.autoStartOnPlay = false;
            logger.controller = controller; logger.locomotion = locomotion;
            logger.sampleIntervalSeconds = .1f;
            try
            {
                Check(!logger.IsLogging && !Directory.Exists(output), "Editor/batch setup must not auto-start logging.");
                controller.inputMode = InputMode.Camera;
                controller.CaptureRestPose();
                Accept(controller, Frame(controller, 0), 100);
                Check(controller.CalibrateAt(100), "Synthetic neutral calibration failed.");
                controller.ApplyControls(100, .02f);
                Vector3 initialPosition = locomotion.travelRoot.position;
                Vector3 initialForward = locomotion.travelRoot.forward;
                Check(logger.BeginSessionAt(output, 100, 100), "Explicit synthetic log could not start.");
                string first = logger.SessionFolder;
                Check(File.Exists(Path.Combine(first, "control.csv")) && File.Exists(Path.Combine(first, "summary.json")), "Start must create CSV and JSON together.");
                Check(!logger.BeginSessionAt(output, 100, 100), "An active session must not be replaced.");
                logger.RecordAt(100.01, 100.01f);
                logger.RecordAt(double.NaN, 100.01f);
                logger.RecordAt(double.PositiveInfinity, 100.01f);
                Check(logger.SampleRows == 1, "Sampling must be paced and ignore invalid clocks.");

                Accept(controller, Frame(controller, .3f), 100.15f);
                controller.ApplyControls(100.15f, .05f);
                locomotion.travelRoot.position = initialPosition + initialForward * .3f;
                logger.RecordAt(100.15, 100.15f);
                Check(logger.SampleRows == 2, "A 10 Hz sample should be written.");
                logger.RecordAt(100.1, 100.1f);
                Check(logger.SampleRows == 2, "Backwards time must not create rows.");
                Accept(controller, Frame(controller, 0, 2), 100.3f);
                logger.RecordAt(100.3, 100.3f);
                Check(!controller.IsCalibrated && !string.IsNullOrEmpty(controller.CalibrationResetReason), "Reassignment must expose why calibration cleared.");
                logger.RecordAt(100.45, 101f); // stale tracking; diagnostic counters remain separate from readiness.
                locomotion.travelRoot.position += initialForward * 20;
                logger.RecordAt(100.6, 101.1f); // a restart-like jump must not become physical speed/distance.
                locomotion.travelRoot.position += initialForward * .5f;
                logger.RecordAt(101.3, 101.3f); // a long sample gap must not become measured motion.
                logger.FinishSessionAt(101.4, "synthetic_complete");
                Check(!logger.IsLogging, "Completing must close the session.");

                var summary = JsonUtility.FromJson<ControlDiagnosticsLogger.SessionSummary>(File.ReadAllText(Path.Combine(first, "summary.json")));
                var lines = File.ReadAllLines(Path.Combine(first, "control.csv"));
                string[] header = ParseCsv(lines[0]);
                var rows = lines.Skip(1).Select(ParseCsv).ToArray();
                Check(summary.sampleRows == 6 && summary.csvRows == 7 && rows.Length == summary.csvRows, "CSV and JSON must agree on start/sample/end row counts.");
                Check(Math.Abs(summary.durationSeconds - 1.4) < .00001, "Session duration must retain the explicit monotonic elapsed time.");
                Check(Math.Abs(summary.lastSampleElapsedSeconds - 1.3) < .00001, "Last sample elapsed time is distinct from completed duration.");
                Check(rows.All(row => row.Length == header.Length), "Every quoted CSV row must have the same field count as its header.");
                Check(rows.All(row => Cell(header, row, "session_id") == summary.sessionId), "CSV and JSON must belong to the same unique session.");
                Check(Cell(header, rows.Last(), "row_kind") == "end" && Cell(header, rows.Last(), "stop_reason") == "synthetic_complete", "The terminal row must describe this session's actual stop.");
                Check(Math.Abs(double.Parse(Cell(header, rows.Last(), "elapsed_s"), CultureInfo.InvariantCulture) - summary.durationSeconds) < .00001,
                    "Terminal CSV elapsed time must match summary duration.");
                Check(summary.observedCalibrationRevisionChanges >= 1 && summary.unavailableP1Samples >= 1 && summary.notReadySamples >= 1,
                    "Reset, unavailable input and stale readiness counters must be visible.");
                Check(summary.maximumRawDetected == 4 && summary.maximumValidPeople == 3 && summary.maximumAssignedPeople == 3,
                    "Observed detection count layers must remain distinct.");
                Check(summary.latestCaptureWidth == 1280 && summary.latestCaptureHeight == 720, "Log must report actual camera dimensions.");
                Check(!summary.imagesSaved && !summary.videoSaved && Directory.GetFiles(first).Length == 2, "Diagnostics must contain only CSV/JSON numeric data.");
                Check(rows.Any(row => Cell(header, row, "tracking_status").Contains("\"confidence\"")), "Quoted diagnostic messages must survive CSV encoding.");
                Check(rows.Any(row => Cell(header, row, "fist_detector_status").Contains("fist")), "Fist detector state must be included in numeric diagnostics.");
                Check(rows.Any(row => Cell(header, row, "p1_raw_available") == "true" && Cell(header, row, "p1_relative_available") == "true"),
                    "Raw values and calibrated neutral-relative movement must have separate availability flags.");
                Check(rows.Any(row => Cell(header, row, "p1_neutral_shoulder_width") == "0.2"),
                    "The shoulder scale used for stable hand normalization must be recorded.");
                Check(rows.Any(row => Cell(header, row, "p1_current_hand_vertical") == "0.06"
                    && Cell(header, row, "p1_neutral_hand_vertical") == "0"
                    && Cell(header, row, "p1_current_shoulder_width") == "0.2"), "Current and neutral raw geometry must explain the calibrated hand delta.");
                Check(rows.Any(row => Cell(header, row, "p1_current_shoulder_confidence") == "1"
                    && Cell(header, row, "p1_current_wrist_confidence") == "1"
                    && Cell(header, row, "p1_current_left_wrist_y") == "0.34"
                    && Cell(header, row, "p1_current_left_shoulder_y") == "0.4"), "Raw XY and shoulder/wrist quality must be preserved.");
                Check(rows.Any(row => Cell(header, row, "capture_age_at_send_s") == "0.01")
                    && rows.Any(row => double.TryParse(Cell(header, row, "pose_sample_age_s"), NumberStyles.Float, CultureInfo.InvariantCulture, out double age) && age > .2),
                    "Capture age and growing Unity sample age must remain distinguishable.");
                Check(summary.reusedPoseSampleRows >= 1, "Repeated camera evidence must be visible in the numeric log.");
                Check(rows.Any(row => Cell(header, row, "observed_motion_available") == "true"
                    && Math.Abs(double.Parse(Cell(header, row, "observed_speed_abs"), CultureInfo.InvariantCulture) - 2) < .01),
                    "Independent world displacement must use the real sample interval.");
                Check(summary.positionJumpSamples == 1 && summary.motionFrameGapSamples == 1
                    && Math.Abs(summary.observedPathDistanceUnits - .3) < .01, "Position resets and long gaps must not inflate measured distance.");
                Check(rows.Where(row => Cell(header, row, "position_reset_or_jump") == "true" || Cell(header, row, "motion_frame_gap") == "true")
                    .All(row => Cell(header, row, "observed_motion_available") == "false" && Cell(header, row, "observed_speed_abs") == ""),
                    "Invalid motion intervals must be marked and leave speed empty.");
                Check(rows.All(row => Cell(header, row, "travel_world_x") != "" && Cell(header, row, "travel_heading_degrees") != ""
                    && Cell(header, row, "reverse_evidence_samples") != "" && Cell(header, row, "reverse_confirmation_progress") != ""),
                    "Position, heading and reverse confirmation evidence must be available in every snapshot.");
                byte[] csvBefore = File.ReadAllBytes(Path.Combine(first, "control.csv"));
                byte[] jsonBefore = File.ReadAllBytes(Path.Combine(first, "summary.json"));
                logger.RecordAt(300, 300); logger.FinishSessionAt(300, "must_not_append");
                Check(csvBefore.SequenceEqual(File.ReadAllBytes(Path.Combine(first, "control.csv")))
                    && jsonBefore.SequenceEqual(File.ReadAllBytes(Path.Combine(first, "summary.json"))), "Closed sessions must never receive more rows or summaries.");

                Accept(controller, Frame(controller, 0, 2), 200);
                Check(controller.CalibrateAt(200), "A later synthetic neutral must recalibrate before the one-bad-frame test.");
                Check(logger.BeginSessionAt(output, 200, 200), "A new session should start with a new output directory.");
                string second = logger.SessionFolder;
                Check(first != second, "A later session must not overwrite a previous session.");
                float savedJumpTolerance = controller.handJumpTolerance;
                controller.handJumpTolerance = 2f; // Isolate the locomotion fresh-sample guard in this logger regression.
                Accept(controller, Frame(controller, -.7f, 2), 200.05f);
                controller.ApplyControls(200.05f, .05f);
                logger.RecordAt(200.1, 200.1f);
                for (int i = 0; i < 15; i++) controller.ApplyControls(200.1f + i * .02f, .02f);
                Check(locomotion.CurrentSpeed == 0 && locomotion.ReverseConfirmationProgress < 1,
                    "Repeated Unity updates from one negative camera frame must not confirm reverse.");
                logger.RecordAt(200.4, 200.4f);
                logger.enabled = false;
                // Ordinary MonoBehaviours do not receive Play lifecycle messages in Edit-mode batch validation.
                // Invoke the actual lifecycle callback explicitly, without entering Play or opening the camera.
                typeof(ControlDiagnosticsLogger).GetMethod("OnDisable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(logger, null);
                var secondSummary = JsonUtility.FromJson<ControlDiagnosticsLogger.SessionSummary>(File.ReadAllText(Path.Combine(second, "summary.json")));
                Check(!logger.IsLogging && secondSummary.durationSeconds >= .4 - .00001, "Disabling must preserve elapsed time even if Unity's clock resets.");
                var badFrameRows = File.ReadAllLines(Path.Combine(second, "control.csv")).Skip(1).Select(ParseCsv).ToArray();
                Check(badFrameRows.Any(row => Cell(header, row, "reverse_pending") == "true")
                    && badFrameRows.All(row => double.Parse(Cell(header, row, "reverse_confirmation_progress"), CultureInfo.InvariantCulture) < 1
                        && Cell(header, row, "direction") != "reverse"), "CSV must expose one-frame pending evidence without reporting a confirmed reverse.");
                Check(secondSummary.stopReason == "component_disabled_or_play_stopped", "OnDisable must finalize the session with a real stop reason.");
                Check(!logger.BeginSessionAt(output, 300, 300), "A disabled logger must not begin another session.");
                byte[] afterDisabled = File.ReadAllBytes(Path.Combine(second, "control.csv"));
                logger.RecordAt(300, 300);
                Check(afterDisabled.SequenceEqual(File.ReadAllBytes(Path.Combine(second, "control.csv"))), "Disabled components must not append events.");
                logger.enabled = true;
                controller.handJumpTolerance = savedJumpTolerance;
                UnityEngine.Debug.Log("CONTROL_DIAGNOSTICS_VALIDATION_OK: " + assertions + " assertions; paced numeric CSV/JSON, diagnostic counts, raw/neutral signals, reset reasons, monotonic duration, finalization and no post-close events; synthetic, no camera.");
            }
            finally
            {
                logger.FinishSession("validation_cleanup");
                locomotion.StopAnimation();
                EditorSceneManager.OpenScene(UpperBodyCourseSceneSetup.CourseScene);
                if (Directory.Exists(output)) Directory.Delete(output, true);
            }
        }

        static string Cell(string[] header, string[] row, string name) => row[Array.IndexOf(header, name)];
        static string[] ParseCsv(string line)
        {
            var fields = new List<string>();
            var cell = new System.Text.StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                    else quoted = !quoted;
                }
                else if (c == ',' && !quoted) { fields.Add(cell.ToString()); cell.Clear(); }
                else cell.Append(c);
            }
            fields.Add(cell.ToString());
            return fields.ToArray();
        }
    }
}
