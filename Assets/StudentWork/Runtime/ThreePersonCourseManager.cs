using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DigiPhant;
using UnityEngine;

namespace StudentWork
{
    public enum CourseStage { Tree, Water, Log, Finish, Complete }

    [DefaultExecutionOrder(580)]
    public sealed class ThreePersonCourseManager : MonoBehaviour
    {
        public DigiPhantController controller;
        public DigiPhantLocomotion locomotion;
        public TreeInteraction treeInteraction;
        public LogInteraction logInteraction;
        public WaterCupInteraction waterInteraction;
        public BoxCollider waterZone;
        public Transform waterBasin;
        [Min(.1f)] public float maximumWaterBasinDistance = 2.8f;
        public BoxCollider finishZone;
        [Min(0)] public float stationarySpeedThreshold = .12f;
        [Min(.1f)] public float maximumSampleDistance = 2f;
        [Header("Trunk log pickup")]
        [Tooltip("Enable the P3 trunk pickup interaction at the Log stage.")]
        public bool enableTrunkLogPickup = true;
        [Tooltip("Optional scene reference. When empty, the original fallen log is used.")]
        public Transform pickupLogTransform;
        [Tooltip("Optional elephant trunk tip reference. When empty, the end of Trunk curl is used.")]
        public Transform pickupTrunkTip;
        [Min(.25f)] public float pickupApproachDistance = 2.6f;
        [Range(.05f, 1)] public float pickupFistConfidence = .55f;
        [Min(.05f)] public float pickupGestureHoldSeconds = .35f;
        [Header("Nearby P3 fist trigger (water and log)")]
        [Tooltip("Within this distance of the basin/log, P3's closed right fist completes the stage directly.")]
        [Min(1)] public float nearbyTriggerRadius = 10f;
        [Tooltip("On: reaching nearbyTriggerRadius completes the water/log stage at once, no P3 fist needed. Off: P3 must clench the right fist there.")]
        public bool autoTriggerNearby = true;
        [Tooltip("With auto trigger: if P3 does nothing, the stage completes once the elephant is this close (after the snap pulls it in).")]
        [Min(.5f)] public float autoTriggerRadius = 4f;
        [Header("P3 touch zone (water and log)")]
        [Tooltip("Within nearbyTriggerRadius, P3 puts either wrist inside this camera-image box to trigger the water/log stage.")]
        public bool enableTouchZone = true;
        [Tooltip("Normalized area of the unmirrored camera image: x 0 (left) to 1 (right), y 0 (top) to 1 (bottom). Drawn on the camera preview.")]
        public Rect touchZone = new Rect(.70f, .02f, .27f, .22f);
        [Min(.05f)] public float touchHoldSeconds = .2f;
        public bool TouchZoneArmed { get; private set; }
        public bool TouchZoneOccupied { get; private set; }
        public string LastTriggerSource { get; private set; } = "";
        [Tooltip("With auto trigger: passing within this distance of the tree trunk completes the tree stage. The start is about 14 from the trunk, so keep it under 12.")]
        [Min(1)] public float treeTriggerRadius = 8f;
        [Tooltip("With auto trigger: reaching the water or log landmark jumps the course to that stage, so a missed tree/water check never blocks the route. The finish only counts after the log stage.")]
        public bool skipToReachedLandmark = true;
        [Min(1)] public float finishTriggerRadius = 7f;
        [Range(.05f, 1)] public float nearbyFistConfidence = .4f;
        [Min(.05f)] public float nearbyFistHoldSeconds = .2f;
        public bool showPanel = true;
        [Tooltip("Show the 'Test water stage' / 'Test log pickup' shortcut buttons in the course panel.")]
        public bool showTestButtons = false;
        public string sessionLabel = "Three-person upper-body course";
        public CourseStage CurrentStage { get; private set; } = CourseStage.Tree;
        public string Status { get; private set; } = "P1 drives with left hand; P2 steers with shoulder tilt. Pass beside the tree.";
        public float ElapsedSeconds { get; private set; }
        public float WaterDwellSeconds { get; private set; }
        public float PreparationSeconds { get; private set; }
        public float TrackingPauseSeconds { get; private set; }
        public float FrameGapSeconds { get; private set; }
        public bool CourseStarted { get; private set; }
        public int AcceptedTeamCupGestures { get; private set; }
        public bool WaterGateAllowed { get; private set; }
        public bool SoloPracticePaused { get; private set; }
        public bool LogPickedUp { get; private set; }
        // Set each frame by WaterDrinkAssist while it holds the elephant on a water/log/finish spot.
        // A held elephant is stationary by construction, so only P3's own readiness gates the fist.
        public bool AssistHolding { get; set; }
        public string PickupStatus { get; private set; } = "Approach the log, then P3 clenches the right fist to pick it up.";
        public string SessionFolder { get; private set; }
        public string SaveStatus { get; private set; } = "Signal and course events only; no camera frames are saved.";

        [Serializable]
        public class CourseEvent
        {
            public float relativeSeconds;
            public string eventName;
            public string stage;
            public string detail;
            public float x, z, signedSpeed;
            public bool inputReady;
            public float treeClearance;
            public float logCrossingSeconds;
            public float waterDwellSeconds;
            public float treeHeadingChangeDegrees;
            public int cupGestures;
            public bool driverSignalReady, steeringSignalReady, headSignalReady, trunkSignalReady;
            public float driverLeftHand, steeringShoulderTilt, p3HeadYaw, p3RightHand;
        }
        [Serializable]
        class CourseResult
        {
            public string dataKind = "upper_body_three_person_virtual_course";
            public string limitation = "Tree success requires configured clearance and demonstrated heading change; when trunk pickup is enabled, log success requires P3's calibrated closed right fist near the timber. Water success requires a closed-fist interaction near the original basin. No physical obstacle collision, foot contact, terrain stepping, real water or liquid transfer is simulated. No image/video frames are stored.";
            public string timingDefinition = "Course elapsed time begins on the first calibrated team movement and includes tracking interruptions and frame stalls until completion. Preparation is separate; solo practice is excluded. Tracking pause and frame gap times overlap and are diagnostics, not deductions.";
            public string sessionLabel, startedUtc, lastSavedUtc, finalStage, finalStatus, saveReason;
            public bool courseComplete, logPickedUp;
            public float elapsedSeconds, treeClearance, logCrossingSeconds, waterDwellSeconds;
            public float preparationSeconds, trackingPauseSeconds, frameGapSeconds, treeHeadingChangeDegrees, treePausedSeconds, logPausedSeconds;
            public int treeAttempts, treeFailures, logAttempts, logFailures, completedCupGestures;
            public CourseEvent[] events;
        }
        readonly List<CourseEvent> events = new List<CourseEvent>();
        StreamWriter csv;
        string startedUtc;
        float previousTime, lastSaved;
        Vector3 lastPosition, finishPrevious;
        float lastSpeed;
        bool lastReady, finishHasPrevious, finishArmed, savedWaterPanel, runtimePanelHidden;
        int seenWaterCompletionRevision, seenTreeAttempts, seenTreeFailures, seenLogAttempts, seenLogFailures;
        int synchronizedSoloRole = -1;
        Vector2 scroll;
        bool panelExpanded; // the course panel starts collapsed to a small button
        Transform pickupOriginalParent;
        Vector3 pickupOriginalLocalPosition, pickupOriginalLocalScale;
        Quaternion pickupOriginalLocalRotation;
        bool pickupOriginalSaved;
        float pickupGestureSeconds, waterFistSeconds, touchSeconds;

        public void Configure(DigiPhantController configuredController, DigiPhantLocomotion configuredLocomotion,
            TreeInteraction tree, LogInteraction log, WaterCupInteraction water, BoxCollider configuredWaterZone, BoxCollider configuredFinishZone)
        {
            controller = configuredController; locomotion = configuredLocomotion;
            treeInteraction = tree; logInteraction = log; waterInteraction = water;
            waterZone = configuredWaterZone; finishZone = configuredFinishZone;
            ResolvePickupReferences();
            ResetCourse(false);
            SetSoloPracticePaused(controller && controller.SoloRole > 0, controller ? controller.SoloRole : 0);
        }
        void OnEnable()
        {
            if (!Application.isPlaying) return;
            DigiPhantCameraPreview.PreviewDrawn -= DrawTouchZone;
            DigiPhantCameraPreview.PreviewDrawn += DrawTouchZone;
            ResolvePickupReferences();
            if (waterInteraction) { savedWaterPanel = waterInteraction.showPanel; waterInteraction.showPanel = false; runtimePanelHidden = true; }
            ResetCourse(false);
            SetSoloPracticePaused(controller && controller.SoloRole > 0, controller ? controller.SoloRole : 0);
        }
        public void ResetCourse(bool resetPosition = true)
        {
            if (Application.isPlaying && !string.IsNullOrEmpty(SessionFolder)) SaveResults("course_reset");
            RestorePickedUpLog();
            soloLogPractice = false;
            csv?.Dispose(); csv = null; SessionFolder = null; events.Clear();
            CurrentStage = CourseStage.Tree; ElapsedSeconds = WaterDwellSeconds = 0; SoloPracticePaused = false;
            PreparationSeconds = TrackingPauseSeconds = FrameGapSeconds = 0;
            CourseStarted = false; AcceptedTeamCupGestures = 0;
            LogPickedUp = false; pickupGestureSeconds = waterFistSeconds = 0;
            PickupStatus = "Approach the log, then P3 clenches the right fist to pick it up.";
            seenTreeAttempts = seenTreeFailures = seenLogAttempts = seenLogFailures = 0;
            treeInteraction?.ResetInteraction(); logInteraction?.ResetInteraction();
            finishHasPrevious = finishArmed = WaterGateAllowed = false;
            Status = "P1 drives with left hand; P2 steers with shoulder tilt. Pass beside the tree.";
            if (waterInteraction)
            {
                waterInteraction.SetCourseGate(false, "First pass the tree before drinking in the original water zone.");
                waterInteraction.ResetPractice();
            }
            seenWaterCompletionRevision = WaterCompletionRevision();
            previousTime = Time.realtimeSinceStartup;
            if (resetPosition && locomotion) locomotion.ResetPosition();
            lastPosition = TravelPosition(); lastSpeed = 0; lastReady = false;
            if (Application.isPlaying) StartResultSession();
        }
        // Test shortcut: skip the tree and place the elephant beside the basin, facing it, in the
        // Water stage. The results record "test_jump_to_water", so this is never a full course run.
        public void JumpToWaterForTesting()
        {
            if (!locomotion || !locomotion.travelRoot || !waterBasin) { Status = "Water test needs the elephant and the water basin."; return; }
            ResetCourse(false);
            locomotion.StopMotion();
            Vector3 basin = waterBasin.position, start = locomotion.travelRoot.position;
            Vector3 away = start - basin; away.y = 0;
            away = away.sqrMagnitude > .01f ? away.normalized : Vector3.back;
            Vector3 spot = start;
            foreach (float share in new[] { .6f, .4f, .2f })
            {
                Vector3 candidate = basin + away * maximumWaterBasinDistance * share;
                candidate.y = start.y;
                if (IsInsideWaterInteractionArea(candidate)) { spot = candidate; break; }
            }
            Vector3 facing = basin - spot; facing.y = 0;
            locomotion.travelRoot.SetPositionAndRotation(spot,
                facing.sqrMagnitude > .0001f ? Quaternion.LookRotation(facing, Vector3.up) : locomotion.travelRoot.rotation);
            lastPosition = spot; lastSpeed = 0;
            CurrentStage = CourseStage.Water;
            Status = "TEST: jumped to the water stage (tree skipped). P1 holds still; P3 opens, then clenches the right fist.";
            Record("test_jump_to_water", "Tree stage skipped for a water test; not a full course run.");
            waterInteraction?.TryAnotherGesture();
            seenWaterCompletionRevision = WaterCompletionRevision();
        }
        // Test shortcut: skip the tree and water and stop the elephant beside the log, facing it,
        // in the Log stage. Recorded as "test_jump_to_log"; never a full course run.
        public void JumpToLogForTesting()
        {
            ResolvePickupReferences();
            if (!locomotion || !locomotion.travelRoot || !pickupLogTransform) { Status = "Log test needs the elephant and the log."; return; }
            ResetCourse(false);
            locomotion.StopMotion();
            Vector3 log = pickupLogTransform.position, start = locomotion.travelRoot.position;
            Vector3 away = start - log; away.y = 0;
            away = away.sqrMagnitude > .01f ? away.normalized : Vector3.back;
            Vector3 spot = log + away * pickupApproachDistance * .6f;
            spot.y = start.y;
            Vector3 facing = log - spot; facing.y = 0;
            locomotion.travelRoot.SetPositionAndRotation(spot,
                facing.sqrMagnitude > .0001f ? Quaternion.LookRotation(facing, Vector3.up) : locomotion.travelRoot.rotation);
            lastPosition = spot; lastSpeed = 0;
            CurrentStage = CourseStage.Log;
            Status = "TEST: jumped to the log stage (tree and water skipped). Stay still; P3 clenches the right fist to pick up the log.";
            Record("test_jump_to_log", "Tree and water stages skipped for a log test; not a full course run.");
            waterInteraction?.SetCourseGate(false, "Water skipped for a log test.");
        }
        public void StartResultSession()
        {
            csv?.Dispose(); csv = null;
            startedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            try
            {
#if UNITY_EDITOR
                string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/ThreePersonCourse"));
#else
                string root = Path.Combine(Application.persistentDataPath, "Logs/ThreePersonCourse");
#endif
                SessionFolder = Path.Combine(root, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                    + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(SessionFolder);
                csv = new StreamWriter(Path.Combine(SessionFolder, "events.csv"), false, new UTF8Encoding(false));
                csv.WriteLine("relative_time_s,event,stage,detail,x,z,signed_speed,input_ready,tree_clearance,log_crossing_s,water_dwell_s,cup_gestures,p1_hand_ready,p1_left_hand,p2_tilt_ready,p2_tilt,p3_head_ready,p3_head_yaw,p3_hand_ready,p3_right_hand");
                Record("session_started", Status); SaveResults("in_progress");
            }
            catch (Exception error) { ReportSaveError(error); }
        }
        // Solo gestures are retained in their own cup log but never advance the team course.
        public void SetSoloPracticePaused(bool paused, int soloRole = 3)
        {
            int desiredRole = paused ? soloRole : 0;
            bool roleChanged = synchronizedSoloRole != desiredRole;
            synchronizedSoloRole = desiredRole;
            if (waterInteraction)
            {
                waterInteraction.sessionLabel = paused ? "P3 upper body solo fist practice" : "P3 upper body course fist water";
                bool waterEnabled = !paused || soloRole == 3;
                if (waterInteraction.enabled != waterEnabled) waterInteraction.enabled = waterEnabled;
                waterInteraction.showPanel = paused && soloRole == 3;
                // A solo fist trial must have its own result file, input mode and count.
                if (roleChanged && paused && soloRole == 3)
                { waterInteraction.ClearCourseGate(); waterInteraction.ResetPractice(); }
            }
            if (SoloPracticePaused == paused)
            {
                if (paused && waterInteraction) waterInteraction.showPanel = soloRole == 3;
                return;
            }
            SoloPracticePaused = paused;
            WaterGateAllowed = false;
            finishArmed = finishHasPrevious = false;
            if (CurrentStage == CourseStage.Tree && treeInteraction)
                treeInteraction.CancelAttempt("Tree attempt cancelled for solo practice; begin a new team trial on return.");
            if (CurrentStage == CourseStage.Log && logInteraction)
                logInteraction.Evaluate(lastPosition, lastSpeed, false, .01f);
            if (waterInteraction)
            {
                waterInteraction.TryAnotherGesture();
                seenWaterCompletionRevision = WaterCompletionRevision();
                waterInteraction.showPanel = paused && soloRole == 3;
                if (paused) waterInteraction.ClearCourseGate();
                else waterInteraction.SetCourseGate(false, "Team course resumed. Return to the current stage and hold neutral.");
            }
            previousTime = Time.realtimeSinceStartup;
            Status = paused ? "Course paused for solo practice. Choose Team to return to the original start and begin a new calibrated trial."
                : "Team course resumed at " + CurrentStage + ". Partial attempts were cancelled; solo fist water interactions do not count toward the course.";
            Record(paused ? "solo_practice_paused_course" : "team_course_resumed", Status);
            if (!paused)
            {
                // Team from any practice scene starts a fresh, comparable course trial.
                ResetCourse(true);
                Status = "Team course restarted at the original start. Set neutral pose again, then raise P1's left hand to start timing.";
            }
        }

        void ResolvePickupReferences()
        {
            if (!pickupLogTransform && logInteraction) pickupLogTransform = logInteraction.logTransform;
            if (!pickupTrunkTip && controller)
            {
                BoneControl trunk = null;
                foreach (var control in controller.controls)
                    if (control != null && control.label == "Trunk curl") { trunk = control; break; }
                if (trunk != null && trunk.bones != null && trunk.bones.Length > 0)
                {
                    var last = trunk.bones[trunk.bones.Length - 1];
                    if (last != null)
                    {
                        // The supplied rig ends with a Trunk7-nub child. Use it so the
                        // carried log follows the visible end of the animated trunk.
                        for (int i = 0; i < last.childCount; i++)
                            if (last.GetChild(i).name.IndexOf("nub", StringComparison.OrdinalIgnoreCase) >= 0)
                            { pickupTrunkTip = last.GetChild(i); break; }
                        if (!pickupTrunkTip) pickupTrunkTip = last;
                    }
                }
            }
            if (pickupLogTransform && !pickupOriginalSaved)
            {
                pickupOriginalParent = pickupLogTransform.parent;
                pickupOriginalLocalPosition = pickupLogTransform.localPosition;
                pickupOriginalLocalRotation = pickupLogTransform.localRotation;
                pickupOriginalLocalScale = pickupLogTransform.localScale;
                pickupOriginalSaved = true;
            }
        }

        void RestorePickedUpLog()
        {
            if (!pickupOriginalSaved || !pickupLogTransform) return;
            pickupLogTransform.SetParent(pickupOriginalParent, false);
            pickupLogTransform.localPosition = pickupOriginalLocalPosition;
            pickupLogTransform.localRotation = pickupOriginalLocalRotation;
            pickupLogTransform.localScale = pickupOriginalLocalScale;
        }

        bool TryReadTrunkPickupFist(float now, out bool closed, out float confidence)
        {
            closed = false; confidence = 0;
            if (!controller) return false;
            return controller.TryReadFist(controller.P3InputSlot, true, now, out closed, out confidence);
        }

        bool TryPickUpLog(Vector3 elephantPosition, float signedSpeed, bool inputReady, float dt)
        {
            ResolvePickupReferences();
            if (LogPickedUp)
            {
                PickupStatus = "Log held on the trunk. Carry it to the finish marker.";
                return true;
            }
            if (!enableTrunkLogPickup || !pickupLogTransform || !pickupTrunkTip)
            {
                PickupStatus = "Trunk pickup setup is missing; use the marked log crossing.";
                return false;
            }
            if (!controller || !controller.upperBodyTeamMode || controller.SoloRole != 0 || controller.performerCount < 3)
            {
                pickupGestureSeconds = 0;
                PickupStatus = "Choose Team mode before picking up the log.";
                return false;
            }
            if (!TryNearbyFist(pickupLogTransform.position, elephantPosition, ref pickupGestureSeconds, dt, "the log", out string fistStatus))
            {
                PickupStatus = fistStatus;
                return false;
            }
            AttachLogToTrunk();
            PickupStatus = "Log picked up (" + LastTriggerSource + "). Carry it to the finish marker.";
            return true;
        }

        void AttachLogToTrunk()
        {
            pickupLogTransform.SetParent(pickupTrunkTip, false);
            pickupLogTransform.localPosition = Vector3.zero;
            pickupLogTransform.localRotation = Quaternion.identity;
            pickupLogTransform.localScale = pickupOriginalLocalScale;
            LogPickedUp = true;
            pickupGestureSeconds = 0;
        }

        // Solo P3 camera practice for the log pickup. Same real right-fist rules as the course
        // (calibrated, tracked, confidence >= pickupFistConfidence held for pickupGestureHoldSeconds);
        // Camera mode only, so there is no simulated or TestSliders fist path here.
        bool soloLogPractice;
        public bool SoloLogPracticeActive => soloLogPractice;
        public void StartSoloLogPractice()
        {
            ResolvePickupReferences();
            if (!controller || controller.SoloRole != 3 || controller.inputMode != InputMode.Camera)
            { PickupStatus = "Choose Solo P3 and Camera before starting the solo log test."; return; }
            if (!locomotion || !locomotion.travelRoot || !pickupLogTransform || !pickupTrunkTip)
            { PickupStatus = "Log test needs the elephant, the log and the trunk tip."; return; }
            RestorePickedUpLog();
            LogPickedUp = false; pickupGestureSeconds = 0;
            locomotion.StopMotion();
            Vector3 log = pickupLogTransform.position, start = locomotion.travelRoot.position;
            Vector3 away = start - log; away.y = 0;
            away = away.sqrMagnitude > .01f ? away.normalized : Vector3.back;
            Vector3 spot = log + away * pickupApproachDistance * .6f;
            spot.y = start.y;
            Vector3 facing = log - spot; facing.y = 0;
            locomotion.travelRoot.SetPositionAndRotation(spot,
                facing.sqrMagnitude > .0001f ? Quaternion.LookRotation(facing, Vector3.up) : locomotion.travelRoot.rotation);
            soloLogPractice = true;
            PickupStatus = "Solo test: open the right hand, then clench and hold to pick up the log.";
        }
        public void StopSoloLogPractice()
        {
            soloLogPractice = false;
            RestorePickedUpLog();
            LogPickedUp = false; pickupGestureSeconds = 0;
        }
        void UpdateSoloLogPractice(float now, float dt)
        {
            if (LogPickedUp) { PickupStatus = "Success: a real fist was recognized and the trunk picked up the log. Click \"Put the log back\" to test again."; return; }
            int slot = controller.P3InputSlot;
            if (!controller.IsCalibrated)
            { pickupGestureSeconds = 0; PickupStatus = "Click Set neutral pose to calibrate first."; return; }
            if (!controller.IsTracked(slot, now))
            { pickupGestureSeconds = 0; PickupStatus = "No one detected: sit in the P3 region with the right hand in frame."; return; }
            if (!TryReadTrunkPickupFist(now, out bool closed, out float confidence))
            { pickupGestureSeconds = 0; PickupStatus = "Right hand unclear: keep the whole right hand in frame. Fist detector: " + controller.FistDetectorStatus; return; }
            if (!closed || confidence < Mathf.Clamp01(pickupFistConfidence))
            {
                pickupGestureSeconds = 0;
                PickupStatus = (closed ? "Fist closed, confidence " : "Right hand open, confidence ") + confidence.ToString("0.00")
                    + " (need a closed fist with confidence >= " + pickupFistConfidence.ToString("0.00") + ")";
                return;
            }
            pickupGestureSeconds += Mathf.Max(0, dt);
            if (pickupGestureSeconds < Mathf.Max(.05f, pickupGestureHoldSeconds))
            { PickupStatus = "Keep the fist closed... " + pickupGestureSeconds.ToString("0.00") + " / " + pickupGestureHoldSeconds.ToString("0.00") + " s"; return; }
            AttachLogToTrunk();
        }

        void LateUpdate()
        {
            if (!Application.isPlaying) return;
            ResolvePickupReferences();
            float now = Time.realtimeSinceStartup, dt = now - previousTime;
            previousTime = now;
            SetSoloPracticePaused(controller && controller.SoloRole > 0, controller ? controller.SoloRole : 0);
            if (soloLogPractice && (!controller || controller.SoloRole != 3 || controller.inputMode != InputMode.Camera))
                StopSoloLogPractice();
            if (SoloPracticePaused)
            {
                // Solo P3: the touch zone stays visible and lights up as practice feedback (it triggers nothing).
                TouchZoneArmed = enableTouchZone && controller && controller.SoloRole == 3;
                TouchZoneOccupied = TouchZoneArmed && P3WristInTouchZone(now);
                if (soloLogPractice) UpdateSoloLogPractice(now, dt);
                return;
            }
            bool ready = IsCourseInputReady(now);
            EvaluateAt(TravelPosition(), locomotion ? locomotion.CurrentSpeed : 0, ready, dt);
            if (now - lastSaved >= 5) { SaveResults("in_progress"); lastSaved = now; }
        }
        // Navigation uses driver/steering readiness and all three performers' presence.
        // Head/ear occlusion while bringing a cup to the mouth must not revoke the water gate;
        // WaterCupInteraction independently requires a fresh cup and visible right hand.
        public bool IsCourseInputReady(float now)
        {
            if (!controller || !locomotion || !controller.upperBodyTeamMode || controller.SoloRole != 0
                || controller.performerCount != 3 || !controller.upperBodyOnly
                || controller.inputMode != InputMode.Camera || !controller.IsCalibrated) return false;
            // A two-person team (P1 + P2) leaves the P3 seat empty; P3's water/log gestures then stay unavailable.
            for (int slot = 1; slot <= 3; slot++) if (controller.IsRoleSeatActive(slot) && !controller.IsTracked(slot, now)) return false;
            return controller.IsMovementVisible(1, Movement.LeftHandHeight, now)
                && (!controller.IsRoleSeatActive(2) || controller.IsMovementVisible(2, Movement.Lean, now));
        }
        // Deterministic geometry path used by the editor validator; callers supply current pose readiness.
        public void EvaluateAt(Vector3 position, float signedSpeed, bool inputReady, float dt, float headingDegrees = float.NaN)
        {
            if (SoloPracticePaused) return;
            lastPosition = position; lastSpeed = signedSpeed; lastReady = inputReady;
            bool validFrame = CourseZoneUtility.Finite(position) && CourseZoneUtility.Finite(signedSpeed)
                && CourseZoneUtility.Finite(dt) && dt > 0 && dt <= .35f;
            bool timedFrame = CourseZoneUtility.Finite(dt) && dt > 0;
            if (CurrentStage != CourseStage.Complete && timedFrame)
            {
                if (!CourseStarted && inputReady && validFrame && Mathf.Abs(signedSpeed) > .05f) CourseStarted = true;
                if (CourseStarted)
                {
                    ElapsedSeconds += dt;
                    if (!inputReady) TrackingPauseSeconds += dt;
                    if (dt > .35f) FrameGapSeconds += dt;
                }
                else PreparationSeconds += dt;
            }
            bool held = Application.isPlaying && AssistHolding;
            WaterGateAllowed = CurrentStage == CourseStage.Water && validFrame && IsInsideWaterInteractionArea(position)
                && (held ? IsP3Ready(Time.realtimeSinceStartup)
                    : inputReady && Mathf.Abs(signedSpeed) <= stationarySpeedThreshold);
            if (waterInteraction)
                waterInteraction.SetCourseGate(WaterGateAllowed, CurrentStage != CourseStage.Water
                    ? "Complete the tree; drinking is available only in the water stage."
                    : !IsInsideWaterInteractionArea(position) ? "Move close to the original water basin (within " + maximumWaterBasinDistance.ToString("0.0") + " units), then stop."
                    : "Stop the elephant and keep all three upper bodies visible and calibrated.");
            TouchZoneArmed = TouchZoneOccupied = false;
            if (CurrentStage == CourseStage.Complete) return;
            if (Application.isPlaying && autoTriggerNearby) ApplyProximityProgress(position);
            switch (CurrentStage)
            {
                case CourseStage.Tree:
                    if (!treeInteraction) { Status = "Missing tree interaction setup."; break; }
                    if (treeInteraction.Evaluate(position, signedSpeed, inputReady, dt, headingDegrees, controller ? controller.CalibrationRevision : 0))
                    {
                        Advance(CourseStage.Water, "Tree avoided — clearance verified. Stop inside the original water zone for P3's closed-fist water interaction.");
                        if (waterInteraction) waterInteraction.TryAnotherGesture();
                        seenWaterCompletionRevision = WaterCompletionRevision();
                    }
                    else Status = treeInteraction.Status;
                    RecordAttemptChanges();
                    break;
                case CourseStage.Log:
                    if (Application.isPlaying && enableTrunkLogPickup && pickupLogTransform && pickupTrunkTip)
                    {
                        if (TryPickUpLog(position, held ? 0 : signedSpeed, held ? IsP3Ready(Time.realtimeSinceStartup) : inputReady, dt))
                            Advance(CourseStage.Finish, "Log picked up (" + LastTriggerSource + "). Carry it to the finish zone.");
                        else Status = PickupStatus;
                    }
                    else
                    {
                        if (!logInteraction) { Status = "Missing log interaction setup."; break; }
                        if (logInteraction.Evaluate(position, signedSpeed, inputReady, dt))
                            Advance(CourseStage.Finish, "Log crossed — steady slow gait verified. Drive forward through the finish zone.");
                        else Status = logInteraction.Status;
                        RecordAttemptChanges();
                    }
                    break;
                case CourseStage.Water:
                    if (WaterGateAllowed) WaterDwellSeconds += dt;
                    string waterFist = null;
                    if (Application.isPlaying && waterBasin
                        && TryNearbyFist(waterBasin.position, position, ref waterFistSeconds, dt, "the water", out waterFist))
                    {
                        AcceptedTeamCupGestures++;
                        Advance(CourseStage.Log, "Water Interaction Complete (" + LastTriggerSource + "). Continue to the log.");
                        WaterGateAllowed = false;
                        waterInteraction?.SetCourseGate(false, "Water stage completed. Continue to the original log.");
                        break;
                    }
                    else if (Application.isPlaying && waterBasin) { Status = waterFist; if (waterInteraction) AcceptWaterCompletion(waterInteraction.LatestCompletion); break; }
                    Status = WaterGateAllowed ? "P3: clench the right fist for the water interaction. P1 holds neutral to stay stopped."
                        : !IsInsideWaterInteractionArea(position) ? "Approach the water basin inside the highlighted drinking area and stop." : "Stop and restore calibrated tracking before the fist interaction.";
                    if (waterInteraction) AcceptWaterCompletion(waterInteraction.LatestCompletion);
                    break;
                case CourseStage.Finish:
                    Status = LogPickedUp ? "Log held on the trunk. Drive forward through the finish zone to its far side."
                        : "Log crossing complete. Drive forward through the finish zone to its far side.";
                    if (!finishZone) { Status = "Missing finish-zone setup."; break; }
                    if ((held || Application.isPlaying && autoTriggerNearby) && CourseZoneUtility.Contains(finishZone, position))
                    {
                        Advance(CourseStage.Complete, "Course Complete — Three Performers. Lower P1's left hand to neutral to stop.");
                        break;
                    }
                    if (!validFrame || !inputReady || finishHasPrevious && Vector3.Distance(finishPrevious, position) > Mathf.Max(.1f, maximumSampleDistance))
                    { finishArmed = finishHasPrevious = false; Status = "Finish paused: restore tracking, approach the finish entry again."; break; }
                    if (finishHasPrevious && signedSpeed > .05f)
                    {
                        if (CourseZoneUtility.CrossesFace(finishZone, finishPrevious, position, false)) finishArmed = true;
                        if (finishArmed && CourseZoneUtility.CrossesFace(finishZone, finishPrevious, position, true))
                            Advance(CourseStage.Complete, "Course Complete — Three Performers. Lower P1's left hand to neutral to stop.");
                        else if (finishArmed && !CourseZoneUtility.Contains(finishZone, position)) finishArmed = false;
                    }
                    finishPrevious = position; finishHasPrevious = true;
                    break;
            }
        }
        // Auto mode: the tree completes when passed nearby, and reaching a later landmark jumps straight to its
        // stage, so a failed or skipped earlier check can never leave the finish (and its celebration) inactive.
        void ApplyProximityProgress(Vector3 position)
        {
            if (skipToReachedLandmark)
            {
                // The finish is never skipped to: it sits about 10 units from the tree, and the course must be
                // driven in full (Tree, Water, Log) before the finish can end it. Only water/log can be caught up.
                for (var stage = CourseStage.Log; stage > CurrentStage; stage--)
                {
                    if (!NearStageLandmark(stage, position)) continue;
                    Record("stage_reached_by_proximity", "Reached the " + stage + " landmark; earlier stages skipped.");
                    if (stage > CourseStage.Water) { WaterGateAllowed = false; waterInteraction?.SetCourseGate(false, "Water stage passed."); }
                    Advance(stage, "Reached the " + stage + " landmark.");
                    return;
                }
            }
            if (CurrentStage == CourseStage.Tree && NearStageLandmark(CourseStage.Tree, position))
            {
                Advance(CourseStage.Water, "Tree passed. Continue to the water basin.");
                waterInteraction?.TryAnotherGesture();
                seenWaterCompletionRevision = WaterCompletionRevision();
            }
        }
        bool NearStageLandmark(CourseStage stage, Vector3 position)
        {
            Transform anchor = null;
            float radius = nearbyTriggerRadius;
            switch (stage)
            {
                case CourseStage.Tree: anchor = treeInteraction ? treeInteraction.treeTrunk : null; radius = treeTriggerRadius; break;
                case CourseStage.Water: anchor = waterBasin; break;
                case CourseStage.Log: anchor = pickupLogTransform ? pickupLogTransform : logInteraction ? logInteraction.logTransform : null; break;
                case CourseStage.Finish:
                    if (!finishZone) return false;
                    Vector3 centre = finishZone.bounds.center; centre.y = position.y;
                    return Vector3.Distance(centre, position) <= finishTriggerRadius;
            }
            // A log already carried on the trunk moves with the elephant; it is no longer a landmark.
            if (!anchor || stage == CourseStage.Log && LogPickedUp) return false;
            Vector3 point = anchor.position; point.y = position.y;
            return Vector3.Distance(point, position) <= radius;
        }
        // Water/log: within nearbyTriggerRadius of the anchor, P3's closed right fist held briefly completes
        // the stage. The elephant may still be moving or being pulled in; P1/P2 readiness is not required.
        // Water/log trigger, checked within nearbyTriggerRadius of the anchor, in this order:
        //   1. P3 holds either wrist inside the on-screen touch zone for touchHoldSeconds;
        //   2. P3 holds a closed right fist for nearbyFistHoldSeconds;
        //   3. auto: the elephant is within autoTriggerRadius (the snap pulls it there).
        // P1/P2 readiness and a stopped elephant are not required.
        bool TryNearbyFist(Vector3 anchor, Vector3 position, ref float heldSeconds, float dt, string target, out string status)
        {
            float now = Time.realtimeSinceStartup;
            anchor.y = position.y = 0;
            float distance = Vector3.Distance(anchor, position);
            if (distance > nearbyTriggerRadius)
            {
                heldSeconds = touchSeconds = 0;
                status = "Approach " + target + " (" + (distance - nearbyTriggerRadius).ToString("0.0") + " units until the trigger range).";
                return false;
            }
            float step = CourseZoneUtility.Finite(dt) && dt > 0 ? Mathf.Min(dt, .25f) : 0;
            bool testMode = controller && controller.inputMode == InputMode.TestSliders;
            bool p3Ready = controller && (testMode || IsP3Ready(now));

            TouchZoneArmed = enableTouchZone;
            TouchZoneOccupied = enableTouchZone && p3Ready && P3WristInTouchZone(now);
            if (TouchZoneOccupied)
            {
                touchSeconds += step;
                if (touchSeconds >= Mathf.Max(.05f, touchHoldSeconds)) { LastTriggerSource = "P3 touch zone"; status = "P3 reached into the green box: triggered!"; return true; }
            }
            else touchSeconds = 0;

            bool fist = p3Ready && controller.TryReadFist(controller.P3InputSlot, true, now, out bool closed, out float confidence)
                && closed && confidence >= Mathf.Clamp01(nearbyFistConfidence);
            if (fist)
            {
                heldSeconds += step;
                if (heldSeconds >= Mathf.Max(.05f, nearbyFistHoldSeconds)) { LastTriggerSource = "P3 fist"; status = "P3 fist: triggered!"; return true; }
            }
            else heldSeconds = 0;

            if (autoTriggerNearby && distance <= autoTriggerRadius) { LastTriggerSource = "auto on arrival"; status = "Reached " + target + ": triggered."; return true; }

            status = !p3Ready ? "Near " + target + ": P3 not detected, stand in the P3 region."
                : TouchZoneOccupied ? "P3 hand in the green box, hold it..."
                : "Near " + target + ": P3 reaches into the green box at the top right of the preview (or clenches the right fist)"
                    + ".";
            return false;
        }
        bool P3WristInTouchZone(float now)
        {
            if (!controller || !controller.TryReadUpperBodyGeometry(controller.P3InputSlot, now, out var g, out _) || g == null || !g.present) return false;
            return g.leftHandConfidence > 0 && touchZone.Contains(new Vector2(g.leftWristX, g.leftWristY))
                || g.rightHandConfidence > 0 && touchZone.Contains(new Vector2(g.rightWristX, g.rightWristY));
        }
        // Drawn on the camera preview (unmirrored image coordinates): faint when idle, green when it can
        // trigger, solid green while P3's hand is inside.
        void DrawTouchZone(Rect picture)
        {
            if (!enableTouchZone || CurrentStage == CourseStage.Complete && !SoloPracticePaused) return;
            if (SoloPracticePaused && (!controller || controller.SoloRole != 3)) return;
            var r = new Rect(picture.x + touchZone.x * picture.width, picture.y + touchZone.y * picture.height,
                touchZone.width * picture.width, touchZone.height * picture.height);
            Color line = TouchZoneOccupied ? new Color(.2f, 1, .3f, 1) : TouchZoneArmed ? new Color(.2f, 1, .3f, .9f) : new Color(1, 1, 1, .35f);
            Color fill = TouchZoneOccupied ? new Color(.2f, 1, .3f, .45f) : TouchZoneArmed ? new Color(.2f, 1, .3f, .15f) : new Color(1, 1, 1, .05f);
            var saved = GUI.color;
            GUI.color = fill; GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = line;
            float t = TouchZoneArmed ? 3 : 1;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), Texture2D.whiteTexture);
            if (TouchZoneArmed)
                GUI.Label(new Rect(r.x + 4, r.y + 2, r.width, 18), TouchZoneOccupied ? "P3 ✓" : "P3 TOUCH",
                    new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold });
            GUI.color = saved;
        }
        // P3 is calibrated and visible; used while the elephant is held on an interaction spot.
        bool IsP3Ready(float now)
        {
            if (!controller || !controller.upperBodyTeamMode || controller.SoloRole != 0 || controller.performerCount < 3
                || controller.inputMode != InputMode.Camera || !controller.IsCalibrated) return false;
            return controller.IsTracked(controller.P3InputSlot, now);
        }
        int WaterCompletionRevision() => waterInteraction && waterInteraction.LatestCompletion != null ? waterInteraction.LatestCompletion.revision : 0;
        public bool IsInsideWaterInteractionArea(Vector3 position)
        {
            if (!CourseZoneUtility.Contains(waterZone, position)) return false;
            Vector3 basin = waterBasin ? waterBasin.position : waterZone.transform.TransformPoint(waterZone.center);
            position.y = basin.y = 0;
            return Vector3.Distance(position, basin) <= Mathf.Max(.1f, maximumWaterBasinDistance);
        }
        public bool AcceptWaterCompletion(CupGestureCompletion completion)
        {
            if (SoloPracticePaused || CurrentStage != CourseStage.Water || completion == null || completion.revision <= seenWaterCompletionRevision) return false;
            seenWaterCompletionRevision = completion.revision;
            if (!completion.courseGateEnabled || !completion.courseGateAllowed || !IsInsideWaterInteractionArea(completion.position))
            {
                Record("water_gesture_rejected", "Completion occurred outside the ready, stationary water gate. Restart inside the water zone.");
                waterInteraction?.TryAnotherGesture();
                return false;
            }
            AcceptedTeamCupGestures++;
            Advance(CourseStage.Log, "Water Interaction Complete — Person 3 held a closed fist. Approach the original log slowly from its entry side.");
            WaterGateAllowed = false;
            waterInteraction?.SetCourseGate(false, "Water stage completed. Continue to the original log.");
            return true;
        }
        void Advance(CourseStage stage, string detail)
        {
            CurrentStage = stage; Status = detail; touchSeconds = 0;
            Record(stage == CourseStage.Complete ? "course_completed" : "stage_completed", detail);
            if (!string.IsNullOrEmpty(SessionFolder)) SaveResults(stage == CourseStage.Complete ? "course_completed" : "stage_completed");
        }
        void RecordAttemptChanges()
        {
            if (treeInteraction && (seenTreeAttempts != treeInteraction.AttemptCount || seenTreeFailures != treeInteraction.FailureCount))
            {
                Record(seenTreeFailures != treeInteraction.FailureCount ? "tree_attempt_failed" : "tree_attempt_started", treeInteraction.Status);
                seenTreeAttempts = treeInteraction.AttemptCount; seenTreeFailures = treeInteraction.FailureCount;
            }
            if (logInteraction && (seenLogAttempts != logInteraction.AttemptCount || seenLogFailures != logInteraction.FailureCount))
            {
                Record(seenLogFailures != logInteraction.FailureCount ? "log_attempt_failed" : "log_attempt_started", logInteraction.Status);
                seenLogAttempts = logInteraction.AttemptCount; seenLogFailures = logInteraction.FailureCount;
            }
        }
        Vector3 TravelPosition() => locomotion && locomotion.travelRoot ? locomotion.travelRoot.position : Vector3.zero;
        void Record(string eventName, string detail)
        {
            var item = new CourseEvent {
                relativeSeconds = ElapsedSeconds, eventName = eventName, stage = CurrentStage.ToString(), detail = detail,
                x = CourseZoneUtility.Finite(lastPosition.x) ? lastPosition.x : 0, z = CourseZoneUtility.Finite(lastPosition.z) ? lastPosition.z : 0,
                signedSpeed = CourseZoneUtility.Finite(lastSpeed) ? lastSpeed : 0, inputReady = lastReady,
                treeClearance = treeInteraction ? treeInteraction.MinimumClearance : -1,
                treeHeadingChangeDegrees = treeInteraction ? treeInteraction.MaximumHeadingChangeDegrees : 0,
                logCrossingSeconds = logInteraction ? logInteraction.CrossingSeconds : 0, waterDwellSeconds = WaterDwellSeconds,
                cupGestures = AcceptedTeamCupGestures
            };
            if (controller)
            {
                float now = Time.realtimeSinceStartup;
                item.driverSignalReady = controller.TryReadMovement(1, Movement.LeftHandHeight, now, out item.driverLeftHand);
                item.steeringSignalReady = controller.TryReadMovement(2, Movement.Lean, now, out item.steeringShoulderTilt);
                item.headSignalReady = controller.TryReadMovement(3, Movement.HeadYaw, now, out item.p3HeadYaw);
                item.trunkSignalReady = controller.TryReadMovement(3, Movement.RightHandHeight, now, out item.p3RightHand);
            }
            try
            {
                csv?.WriteLine(string.Join(",", new[] { N(item.relativeSeconds), Csv(item.eventName), Csv(item.stage), Csv(item.detail), N(item.x), N(item.z),
                    N(item.signedSpeed), item.inputReady ? "true" : "false", N(item.treeClearance), N(item.logCrossingSeconds), N(item.waterDwellSeconds), item.cupGestures.ToString(CultureInfo.InvariantCulture),
                    item.driverSignalReady ? "true" : "false", N(item.driverLeftHand), item.steeringSignalReady ? "true" : "false", N(item.steeringShoulderTilt),
                    item.headSignalReady ? "true" : "false", N(item.p3HeadYaw), item.trunkSignalReady ? "true" : "false", N(item.p3RightHand) }));
                csv?.Flush();
                events.Add(item);
            }
            catch (Exception error) { ReportSaveError(error); }
        }
        public void SaveResults() => SaveResults("user_saved");
        void SaveResults(string reason)
        {
            if (string.IsNullOrEmpty(SessionFolder)) return;
            try
            {
                var result = new CourseResult {
                    sessionLabel = sessionLabel, startedUtc = startedUtc, lastSavedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    finalStage = CurrentStage.ToString(), finalStatus = Status, saveReason = reason, courseComplete = CurrentStage == CourseStage.Complete,
                    elapsedSeconds = ElapsedSeconds, treeClearance = treeInteraction ? treeInteraction.MinimumClearance : -1,
                    preparationSeconds = PreparationSeconds, trackingPauseSeconds = TrackingPauseSeconds, frameGapSeconds = FrameGapSeconds,
                    treeHeadingChangeDegrees = treeInteraction ? treeInteraction.MaximumHeadingChangeDegrees : 0,
                    treePausedSeconds = treeInteraction ? treeInteraction.PausedSeconds : 0,
                    logPausedSeconds = logInteraction ? logInteraction.PausedSeconds : 0,
                    logCrossingSeconds = logInteraction ? logInteraction.CrossingSeconds : 0, waterDwellSeconds = WaterDwellSeconds,
                    treeAttempts = treeInteraction ? treeInteraction.AttemptCount : 0, treeFailures = treeInteraction ? treeInteraction.FailureCount : 0,
                    logAttempts = logInteraction ? logInteraction.AttemptCount : 0, logFailures = logInteraction ? logInteraction.FailureCount : 0,
                    completedCupGestures = AcceptedTeamCupGestures, logPickedUp = LogPickedUp, events = events.ToArray()
                };
                File.WriteAllText(Path.Combine(SessionFolder, "results.json"), JsonUtility.ToJson(result, true), new UTF8Encoding(false));
                SaveStatus = "Results saved: Logs/ThreePersonCourse/" + Path.GetFileName(SessionFolder);
            }
            catch (Exception error) { ReportSaveError(error); }
        }
        void ReportSaveError(Exception error) { SaveStatus = "Could not save course results: " + error.Message; Debug.LogWarning(SaveStatus); }
        void OnDisable()
        {
            DigiPhantCameraPreview.PreviewDrawn -= DrawTouchZone;
            SaveResults("course_disabled_or_play_stopped"); csv?.Dispose(); csv = null;
            RestorePickedUpLog();
            LogPickedUp = false;
            if (waterInteraction) { waterInteraction.ClearCourseGate(); if (runtimePanelHidden) waterInteraction.showPanel = savedWaterPanel; }
            runtimePanelHidden = false;
        }
        void OnApplicationQuit() => SaveResults("application_quit");
        void OnGUI()
        {
            if (showPanel && SoloPracticePaused && controller && controller.SoloRole == 3) DrawSoloLogPanel();
            if (!showPanel || SoloPracticePaused) return;
            if (!panelExpanded)
            {
                float buttonWidth = Mathf.Min(260, Screen.width - 24);
                string summary = "Course: " + CurrentStage + " | " + ElapsedSeconds.ToString("0.0") + " s   ▲ Show";
                if (GUI.Button(new Rect(Screen.width - buttonWidth - 12, Screen.height - 40, buttonWidth, 28), summary)) panelExpanded = true;
                return;
            }
            float width = Mathf.Min(330, Screen.width - 24), height = Mathf.Min(310, Screen.height - 24);
            var box = new Rect(Screen.width - width - 12, Screen.height - height - 12, width, height);
            GUI.Box(box, GUIContent.none);
            GUILayout.BeginArea(new Rect(box.x + 10, box.y + 8, box.width - 20, box.height - 16));
            scroll = GUILayout.BeginScrollView(scroll);
            var wrapped = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 12 };
            GUILayout.BeginHorizontal();
            GUILayout.Label("UPPER-BODY TEAM COURSE", wrapped);
            if (GUILayout.Button("▼ Hide", GUILayout.Width(70))) panelExpanded = false;
            GUILayout.EndHorizontal();
            GUILayout.Label("Tree → Water → Log → Finish | " + CurrentStage + " | " + ElapsedSeconds.ToString("0.0") + " s", wrapped);
            GUILayout.Label(CourseStarted ? "Trial running; tracking pauses and lag stay included in elapsed time."
                : "Preparing (" + PreparationSeconds.ToString("0.0") + " s). Timing starts on the first calibrated team movement.", wrapped);
            GUILayout.Label("Tree: " + ((int)CurrentStage > (int)CourseStage.Tree ? "DONE" : "waiting")
                + " | Water: " + ((int)CurrentStage > (int)CourseStage.Water ? "DONE" : "waiting")
                + " | Log: " + ((int)CurrentStage > (int)CourseStage.Log ? "DONE" : "waiting")
                + " | Finish: " + (CurrentStage == CourseStage.Complete ? "DONE" : "waiting"), wrapped);
            GUILayout.Label("P1: left hand speed + all legs. P2: shoulder tilt steering. P3: head turn; at Log, approach and hold the right fist to pick up the wood.", wrapped);
            GUILayout.Label(Status, wrapped);
            if (CurrentStage == CourseStage.Log || LogPickedUp) GUILayout.Label(PickupStatus, wrapped);
            if (CurrentStage == CourseStage.Water && waterInteraction && waterInteraction.State != null)
                GUILayout.Label(waterInteraction.State.Status + " | Fist detector: " + (controller ? controller.FistDetectorStatus : "missing"), wrapped);
            if (GUILayout.Button("Restart course / return to start")) ResetCourse();
            if (showTestButtons)
            {
                if (GUILayout.Button("Test water stage")) JumpToWaterForTesting();
                if (GUILayout.Button("Test log pickup")) JumpToLogForTesting();
            }
            if (GUILayout.Button("Save course results")) { SaveResults(); waterInteraction?.SaveResults(); }
            GUILayout.Label(SaveStatus, wrapped);
            GUILayout.Label("Detection zones evaluate symbolic interactions; obstacles do not block the elephant.", wrapped);
            GUILayout.EndScrollView(); GUILayout.EndArea();
        }
        void DrawSoloLogPanel()
        {
            var box = new Rect(Screen.width - 342, 270, 330, 132);
            GUI.Box(box, GUIContent.none);
            GUILayout.BeginArea(new Rect(box.x + 10, box.y + 8, box.width - 20, box.height - 16));
            var wrapped = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 12 };
            GUILayout.Label("Solo log pickup test (live camera, real fist)", wrapped);
            if (!soloLogPractice)
            {
                if (GUILayout.Button("Start: place the elephant at the log")) StartSoloLogPractice();
            }
            else if (GUILayout.Button(LogPickedUp ? "Put the log back, test again" : "End test, put the log back"))
            {
                bool again = LogPickedUp;
                StopSoloLogPractice();
                if (again) StartSoloLogPractice();
            }
            GUILayout.Label(PickupStatus, wrapped);
            GUILayout.EndArea();
        }
        static string N(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);
        static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    }
}
