using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DigiPhant;
using UnityEngine;

namespace StudentWork
{
    // Independent of Unity's Play-mode clock, which may already have reset during shutdown.
    public sealed class GestureSessionClock
    {
        readonly Func<double> monotonicSeconds;
        double accumulated, segmentStart, greatestElapsed;
        bool running;
        public GestureSessionClock(Func<double> source = null)
        {
            monotonicSeconds = source ?? (() => (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency);
        }
        public double ElapsedSeconds
        {
            get
            {
                double elapsed = accumulated + (running ? Math.Max(0, monotonicSeconds() - segmentStart) : 0);
                greatestElapsed = Math.Max(greatestElapsed, elapsed);
                return greatestElapsed;
            }
        }
        public void Start() { accumulated = greatestElapsed = 0; segmentStart = monotonicSeconds(); running = true; }
        public void Pause() { accumulated = ElapsedSeconds; running = false; }
        public void Resume() { if (!running) { segmentStart = monotonicSeconds(); running = true; } }
    }

    [Serializable]
    public sealed class CupGestureCompletion
    {
        public int revision, completedCount, inputSlot;
        public float realtimeSeconds;
        public Vector3 position;
        public bool courseGateEnabled, courseGateAllowed;
    }

    [DefaultExecutionOrder(600)]
    public class WaterCupInteraction : MonoBehaviour
    {
        public DigiPhantController controller;
        public DigiPhantLocomotion locomotion;
        public CupGestureSettings settings = new CupGestureSettings();
        [Min(0)] public float stationarySpeedThreshold = .12f;
        [Min(.1f)] public float positionJumpThreshold = 2;
        public bool showPanel = true;
        public string sessionLabel = "P3 fist water practice";
        public CupGestureState State { get; private set; }
        public string SessionFolder { get; private set; }
        public string SaveStatus { get; private set; } = "Signal results only; no camera frames are saved.";
        public bool CourseGateEnabled { get; private set; }
        public bool CourseGateAllowed { get; private set; }
        public CupGestureCompletion LatestCompletion { get; private set; }
        int completionRevision;
        string courseGateReason;

        [Serializable]
        public class GestureEvent
        {
            public float relativeSeconds;
            public string eventName;
            public string phase;
            public string detail;
            public int completedCount;
            public bool handAvailable;
            public float rightHandNeutralDelta;
            public bool fistAvailable;
            public bool fistClosed;
            public float fistConfidence;
            public bool cupDetected;
            public float cupScore;
            public float cupAgeSeconds;
            public int completionRevision;
            public bool completedUnderCourseGate;
            public string calibrationResetReason;
            public int calibrationRevision;
            public string cupAssociationStatus;
        }

        [Serializable]
        class SessionResult
        {
            public string dataKind = "closed_fist_water_interaction_virtual_puppet";
            public string limitation = "Recognized a calibrated closed right fist while stationary. No water, liquid transfer, cup weight, or water-zone arrival is simulated or verified. Image/video frames are not stored.";
            public string sessionLabel;
            public string startedUtc;
            public string lastSavedUtc;
            public float durationSeconds;
            public string timingDefinition = "Monotonic system time while this gesture session component is active; component-disabled intervals are excluded. Unity Play clock resets do not reset duration or event times.";
            public string inputMode;
            public bool p3SoloPractice;
            public bool courseGateEnabled;
            public int inputSlot;
            public int completedGestures;
            public int validObservedSamples;
            public string finalPhase;
            public string finalStatus;
            public string saveReason;
            public CupGestureSettings parameters;
            public float stationarySpeedThreshold;
            public GestureEvent[] events;
        }

        readonly List<GestureEvent> events = new List<GestureEvent>();
        StreamWriter csv;
        float previousTime, lastSaved;
        GestureSessionClock sessionClock;
        SessionResult frozenResult;
        Vector3 previousPosition;
        int calibrationRevision, modeKey, validSamples;
        string startedUtc;
        string sessionInputMode, sessionLabelAtStart;
        bool sessionP3SoloPractice, sessionCourseGateEnabled;
        int sessionInputSlot;
        bool handAvailable, fistAvailable, fistClosed, cupAvailable;
        float rightHand, fistConfidence;
        CupObservation currentCup;
        string previousStatus;
        string lastConfigurationResetReason;
        Vector2 scroll;

        public void Configure(DigiPhantController configuredController, DigiPhantLocomotion configuredLocomotion)
        {
            controller = configuredController;
            locomotion = configuredLocomotion;
        }

        public void SetCourseGate(bool allowed, string reason)
        {
            bool wasAllowed = CourseGateEnabled && CourseGateAllowed;
            CourseGateEnabled = true; CourseGateAllowed = allowed;
            courseGateReason = reason ?? "Wait for the course water stage, stop inside its zone and restore tracking.";
            if (wasAllowed && !allowed && State != null)
                State.CancelSequence(courseGateReason);
        }

        public void ClearCourseGate()
        {
            bool wasGated = CourseGateEnabled;
            CourseGateEnabled = CourseGateAllowed = false;
            courseGateReason = null;
            if (wasGated && State != null) State.CancelSequence("Course gate released. Open your hand, then clench to start an independent P3 fist interaction.");
        }

        void OnEnable()
        {
            if (State == null) State = new CupGestureState(settings);
            if (!Application.isPlaying) return;
            if (string.IsNullOrEmpty(SessionFolder)) ResetPractice();
            else
            {
                State.CancelSequence("Component resumed. Open your hand, then clench and restart the interaction.");
                ClearObservedInput();
                previousTime = Time.realtimeSinceStartup;
                previousPosition = TravelPosition();
                try
                {
                    // Manager OnEnable can start/reset this same session before this
                    // component receives its own OnEnable callback. Reuse the live
                    // writer instead of opening a second handle to events.csv.
                    if (csv == null)
                        csv = new StreamWriter(Path.Combine(SessionFolder, "events.csv"), true, new UTF8Encoding(false));
                    frozenResult = null;
                    sessionClock?.Resume();
                    Record("component_resumed", State.Status);
                }
                catch (Exception error) { ReportSaveError(error); }
            }
        }

        public void ResetPractice()
        {
            if (!Application.isPlaying) return;
            if (State == null) State = new CupGestureState(settings);
            CloseSignalSession("session_reset");
            SessionFolder = null;
            events.Clear();
            State.Reset();
            // A reset starts a fresh gesture session. Do not expose the last
            // successful completion to callers between reset and the next cup
            // gesture; keep the revision monotonic so course de-duplication stays
            // valid even when another component resets us mid-session.
            LatestCompletion = null;
            validSamples = 0;
            previousTime = lastSaved = Time.realtimeSinceStartup;
            previousPosition = TravelPosition();
            calibrationRevision = controller ? controller.CalibrationRevision : 0;
            modeKey = ModeKey();
            previousStatus = State.Status;
            ClearObservedInput();
            lastConfigurationResetReason = null;
            try
            {
#if UNITY_EDITOR
                string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/P3Water"));
#else
                string root = Path.Combine(Application.persistentDataPath, "Logs/P3Water");
#endif
                StartSignalSession(Path.Combine(root, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                    + "-" + Guid.NewGuid().ToString("N").Substring(0, 8)), null);
            }
            catch (Exception error) { ReportSaveError(error); }
        }

        public void TryAnotherGesture()
        {
            if (State == null) return;
            State.CancelSequence("Open your hand, then clench to try another interaction. Completed count is retained.");
            Record("manual_gesture_restart", State.Status);
        }

        void LateUpdate()
        {
            if (!Application.isPlaying || State == null) return;
            float now = Time.realtimeSinceStartup;
            float dt = now - previousTime;
            Vector3 position = TravelPosition();
            float distance = HorizontalDistance(previousPosition, position);
            float measuredSpeed = dt > 0 ? distance / dt : float.PositiveInfinity;
            bool jumped = distance > Mathf.Max(.1f, positionJumpThreshold);
            previousPosition = position;
            previousTime = now;

            if (!controller || !locomotion)
            { State.CancelSequence("Missing controller/locomotion references."); return; }
            int key = ModeKey();
            if (calibrationRevision != controller.CalibrationRevision || key != modeKey)
            {
                ResetForConfigurationChange(controller.CalibrationRevision, key, controller.CalibrationResetReason);
                return;
            }

            int slot = controller.P3InputSlot;
            handAvailable = controller.TryReadMovement(slot, Movement.RightHandHeight, now, out rightHand);
            fistAvailable = controller.TryReadFist(slot, true, now, out fistClosed, out fistConfidence);
            // Cup detection remains available for diagnostics and old result readers,
            // but it no longer gates the live water interaction.
            cupAvailable = controller.TryReadCup(slot, now, out currentCup);
            bool appropriateMode = controller.inputMode == InputMode.Camera
                && (controller.SoloRole == 3 || controller.performerCount >= 3);
            bool stationary = !jumped && measuredSpeed <= stationarySpeedThreshold
                && Mathf.Abs(locomotion.CurrentSpeed) <= stationarySpeedThreshold;
            bool poseReady = controller.IsCalibrated && controller.IsTracked(slot, now) && handAvailable;
            CupGesturePhase before = State.Phase;
            int countBefore = State.CompletedCount;
            bool courseReady = !CourseGateEnabled || CourseGateAllowed;
            bool complete = false;
            if (!courseReady) State.CancelSequence(courseGateReason);
            else complete = EvaluateGestureSample(new CupGestureSample {
                dt = jumped ? float.PositiveInfinity : dt,
                contextReady = appropriateMode && stationary,
                poseReady = poseReady,
                rightHand = handAvailable ? rightHand : 0,
                // Explicitly false: legacy cup samples are accepted only by direct
                // editor validation and can never bypass a missing fist model live.
                cupDetected = false,
                cupLabel = null,
                cupScore = 0,
                cupAgeSeconds = float.PositiveInfinity,
                fistAvailable = fistAvailable,
                fistClosed = fistClosed,
                fistConfidence = fistConfidence
            }, now, position);
            if (courseReady && appropriateMode && stationary && poseReady && State.FistReady) validSamples++;
            if (complete || State.Phase != before || State.Status != previousStatus && State.Phase == CupGesturePhase.Neutral)
                Record(complete ? "gesture_completed" : State.Phase != before ? "phase_changed" : "waiting_or_reset", State.Status);
            previousStatus = State.Status;
            if (countBefore != State.CompletedCount || now - lastSaved >= 5)
            { SaveResults(complete ? "gesture_completed" : "in_progress"); lastSaved = now; }
        }

        void ClearObservedInput()
        { handAvailable = fistAvailable = fistClosed = cupAvailable = false; rightHand = fistConfidence = 0; currentCup = null; }
        void ResetForConfigurationChange(int revision, int newModeKey, string reason)
        {
            calibrationRevision = revision; modeKey = newModeKey;
            ClearObservedInput();
            lastConfigurationResetReason = string.IsNullOrEmpty(reason) ? "Input mode or role configuration changed" : reason;
            State.CancelSequence(lastConfigurationResetReason + ". Open your hand, then clench and restart.");
            Record("configuration_reset", State.Status);
        }
        void StartSignalSession(string folder, GestureSessionClock suppliedClock)
        {
            SessionFolder = folder; frozenResult = null;
            if (State == null) State = new CupGestureState(settings);
            sessionClock = suppliedClock ?? new GestureSessionClock(); sessionClock.Start();
            startedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            sessionLabelAtStart = sessionLabel;
            sessionInputMode = controller ? controller.inputMode.ToString() : "missing";
            sessionP3SoloPractice = controller && controller.SoloRole == 3;
            sessionInputSlot = controller ? controller.P3InputSlot : 0;
            sessionCourseGateEnabled = CourseGateEnabled;
            Directory.CreateDirectory(SessionFolder);
            csv = new StreamWriter(Path.Combine(SessionFolder, "events.csv"), false, new UTF8Encoding(false));
            csv.WriteLine("relative_time_s,event,phase,detail,completed_count,hand_available,right_hand_neutral_delta,fist_available,fist_closed,fist_confidence,cup_detected,cup_score,cup_age_s,calibration_revision,calibration_reset_reason,completion_revision,completed_under_course_gate,cup_association_status");
            Record("session_started", State.Status); SaveResults("in_progress");
        }

        // Completion context belongs to the successful sample, not to the next frame's gate.
        // Also used by deterministic editor validation without a camera or log output.
        public bool EvaluateGestureSample(CupGestureSample sample, float now, Vector3 position)
        {
            if (State == null) State = new CupGestureState(settings);
            if (CourseGateEnabled && !CourseGateAllowed)
            { State.CancelSequence(courseGateReason); return false; }
            if (!State.Step(sample)) return false;
            LatestCompletion = new CupGestureCompletion {
                revision = ++completionRevision, completedCount = State.CompletedCount,
                inputSlot = controller ? controller.P3InputSlot : 0,
                realtimeSeconds = now, position = position,
                courseGateEnabled = CourseGateEnabled, courseGateAllowed = CourseGateAllowed
            };
            return true;
        }

        int ModeKey() => controller ? ((int)controller.inputMode * 100 + controller.performerCount * 10
            + (controller.upperBodyOnly ? 2 : 0) + (controller.p3PracticeMode ? 1 : 0)
            + controller.SoloRole * 1000 + (controller.upperBodyTeamMode ? 10000 : 0)) : -1;

        Vector3 TravelPosition() => locomotion && locomotion.travelRoot ? locomotion.travelRoot.position : Vector3.zero;

        static float HorizontalDistance(Vector3 a, Vector3 b)
        { a.y = b.y = 0; return Vector3.Distance(a, b); }

        void Record(string eventName, string detail)
        {
            if (State == null || csv == null || sessionClock == null) return;
            var item = new GestureEvent {
                relativeSeconds = (float)sessionClock.ElapsedSeconds,
                eventName = eventName, phase = State.Phase.ToString(), detail = detail,
                completedCount = State.CompletedCount, handAvailable = handAvailable,
                rightHandNeutralDelta = handAvailable ? rightHand : 0,
                fistAvailable = fistAvailable, fistClosed = fistClosed, fistConfidence = fistConfidence,
                cupDetected = cupAvailable && currentCup != null && currentCup.detected,
                cupScore = currentCup != null ? currentCup.score : 0,
                cupAgeSeconds = currentCup != null && Finite(currentCup.ageSeconds) ? currentCup.ageSeconds : -1,
                completionRevision = LatestCompletion != null ? LatestCompletion.revision : 0,
                completedUnderCourseGate = eventName == "gesture_completed" && LatestCompletion != null
                    && LatestCompletion.courseGateEnabled && LatestCompletion.courseGateAllowed,
                calibrationRevision = calibrationRevision,
                calibrationResetReason = eventName == "configuration_reset" ? lastConfigurationResetReason : null,
                cupAssociationStatus = eventName == "configuration_reset" ? "Observations cleared for configuration reset"
                    : controller ? controller.GetCupAssociationStatus(controller.P3InputSlot) : "Missing controller"
            };
            try
            {
                csv.WriteLine(string.Join(",", new[] { Number(item.relativeSeconds), Csv(item.eventName), Csv(item.phase),
                    Csv(item.detail), item.completedCount.ToString(CultureInfo.InvariantCulture), item.handAvailable ? "true" : "false",
                    Number(item.rightHandNeutralDelta), item.fistAvailable ? "true" : "false", item.fistClosed ? "true" : "false", Number(item.fistConfidence),
                    item.cupDetected ? "true" : "false", Number(item.cupScore), Number(item.cupAgeSeconds),
                    item.calibrationRevision.ToString(CultureInfo.InvariantCulture), Csv(item.calibrationResetReason),
                    item.completionRevision.ToString(CultureInfo.InvariantCulture), item.completedUnderCourseGate ? "true" : "false", Csv(item.cupAssociationStatus) }));
                csv.Flush();
                events.Add(item);
            }
            catch (Exception error)
            {
                sessionClock.Pause(); csv?.Dispose(); csv = null;
                ReportSaveError(error);
            }
        }

        public void SaveResults() => SaveResults("user_saved");

        void SaveResults(string reason)
        {
            if (State == null || string.IsNullOrEmpty(SessionFolder)) return;
            try
            {
                var result = frozenResult ?? BuildResult(reason);
                File.WriteAllText(Path.Combine(SessionFolder, "results.json"), JsonUtility.ToJson(result, true), new UTF8Encoding(false));
                SaveStatus = "Results saved: Logs/P3Water/" + Path.GetFileName(SessionFolder);
            }
            catch (Exception error) { ReportSaveError(error); }
        }
        SessionResult BuildResult(string reason)
        {
            return new SessionResult {
                    limitation = sessionCourseGateEnabled
                        ? "Recognized a calibrated closed right fist while stationary inside the course water detection zone after tree avoidance. No real water or liquid transfer is simulated. Image/video frames are not stored."
                        : "Recognized a calibrated closed right fist while stationary. No water, liquid transfer, cup weight, or water-zone arrival is simulated or verified. Image/video frames are not stored.",
                    sessionLabel = sessionLabelAtStart, startedUtc = startedUtc,
                    lastSavedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    durationSeconds = sessionClock != null ? (float)sessionClock.ElapsedSeconds : 0,
                    inputMode = sessionInputMode,
                    p3SoloPractice = sessionP3SoloPractice, courseGateEnabled = sessionCourseGateEnabled,
                    inputSlot = sessionInputSlot,
                    completedGestures = State.CompletedCount, validObservedSamples = validSamples,
                    finalPhase = State.Phase.ToString(), finalStatus = State.Status, saveReason = reason,
                    parameters = settings, stationarySpeedThreshold = stationarySpeedThreshold, events = events.ToArray()
                };
        }
        void CloseSignalSession(string reason)
        {
            sessionClock?.Pause();
            if (csv == null) return;
            Record("session_stopped", reason);
            frozenResult = BuildResult(reason);
            SaveResults(reason);
            csv?.Dispose(); csv = null;
        }

        void ReportSaveError(Exception error)
        { SaveStatus = "Could not save gesture results: " + error.Message; Debug.LogWarning(SaveStatus); }

        void OnDisable()
        {
            CloseSignalSession("component_disabled_or_play_stopped");
        }

        void OnApplicationQuit() => SaveResults("application_quit");

        void OnGUI()
        {
            if (!showPanel || State == null) return;
            float width = Mathf.Min(310, Screen.width - 24);
            float height = Mathf.Min(300, Screen.height - 24);
            var box = new Rect(Screen.width - width - 12, Screen.height - height - 12, width, height);
            GUI.Box(box, GUIContent.none);
            GUILayout.BeginArea(new Rect(box.x + 10, box.y + 8, box.width - 20, box.height - 16));
            scroll = GUILayout.BeginScrollView(scroll);
            var wrapped = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 12 };
            GUILayout.Label("P3 FIST / WATER INTERACTION", wrapped);
            GUILayout.Label(CourseGateEnabled ? "Course: stationary closed-fist interaction inside the water detection zone." : "Symbolic water interaction; no water physics or water-zone detection.", wrapped);
            int slot = controller ? controller.P3InputSlot : 0;
            GUILayout.Label("Input: P" + slot + " | Completed: " + State.CompletedCount, wrapped);
            GUILayout.Label(fistAvailable
                ? (fistClosed ? "Right fist: clenched | confidence " : "Right fist: open | confidence ") + fistConfidence.ToString("0.00")
                : "Fist: keep the right hand visible and load the gesture model", wrapped);
            GUILayout.Label("Fist detector: " + (controller ? controller.FistDetectorStatus : "missing"), wrapped);
            GUILayout.Label(handAvailable ? "Right wrist tracked: " + rightHand.ToString("0.00") : "Hand: calibrate and keep the right wrist visible", wrapped);
            GUILayout.Label("Open → clench and hold " + settings.fistHoldSeconds.ToString("0.0") + " s", wrapped);
            GUILayout.Label(State.Status, wrapped);
            if (GUILayout.Button("Try another gesture")) TryAnotherGesture();
            if (GUILayout.Button("Reset gesture session / count")) ResetPractice();
            if (GUILayout.Button("Save gesture results")) SaveResults();
            GUILayout.Label(SaveStatus, wrapped);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static string Number(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);
        static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    }
}
