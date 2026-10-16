using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace DigiPhant
{
    public enum Movement { LeftHandHeight, RightHandHeight, LeftFootLift, RightFootLift, Lean, ArmSpread, LeftArmExtension, HeadYaw }
    public enum InputMode { TestSliders, Camera }

    [Serializable] public class CupObservation
    {
        public bool detected;
        public string label;
        public string reason;
        // x/y are normalized box CENTER coordinates from the semantic detector.
        public float score, x, y, width, height, ageSeconds;
    }
    [Serializable] public class PerformerFrame
    {
        public int slot;
        public float[] values;
        public float[] confidence;
        public bool hasLeftArm;
        public float leftArmExtension, leftArmConfidence;
        public bool hasHeadYaw;
        public float headYaw, headYawConfidence;
        public CupObservation cup;
        public UpperBodyGeometry upperBodyGeometry;
    }
    [Serializable] public class UpperBodyGeometry
    {
        public bool present;
        public float leftHandVertical, rightHandVertical, shoulderWidth, leftHandConfidence, rightHandConfidence;
        public bool leftFistClosed, rightFistClosed;
        public float leftFistConfidence, rightFistConfidence;
        public float shoulderConfidence, leftShoulderX, leftShoulderY, rightShoulderX, rightShoulderY,
            leftWristX, leftWristY, rightWristX, rightWristY;
    }
    [Serializable] public class CameraRegionDiagnostics
    {
        public int slot, rawDetected;
        public float xMin, xMax;
        public bool shoulderReady, leftHandReady, rightHandReady, headReady;
        public string reason;
    }
    [Serializable] public class CameraTrackingDiagnostics
    {
        // candidatePeople is the number of bodies considered by the identity tracker
        // before assigning them to the requested performer slots.  It is deliberately
        // separate from rawDetected/validPeople/assignedPeople so the UI can explain
        // why a visible body was not assigned.  These fields are optional for older
        // bridges and therefore keep their normal JSON defaults when absent.
        public int rawDetected, validPeople, assignedPeople, candidatePeople, captureWidth, captureHeight, frameSequence;
        public float cameraFps, poseInferenceMs, cupInferenceMs, fistInferenceMs, loopMs, captureAgeSeconds;
        public bool regionalTracking, trackerAmbiguous;
        public string rejectionReasons, trackerStatus;
        public CameraRegionDiagnostics[] regions;
    }
    [Serializable] public class PoseFrame
    {
        public int version;
        public int performerCount;
        public bool upperBodyOnly;
        public PerformerFrame[] people;
        public string cupDetectorStatus;
        public string fistDetectorStatus;
        public int assignmentRevision;
        public string trackingSession, trackingStatus;
        public bool hasCaptureAge;
        public float captureAgeSeconds;
        public int frameSequence;
        public CameraTrackingDiagnostics diagnostics;
    }
    [Serializable] public class TrackingRequest
    {
        public int version = 1, performerCount;
        public bool upperBodyOnly, reset;
        public float[] regionBoundaries;
        public float regionPadding;
        // Upper-body solo practice role (1..3): the bridge tracks only that role's region.
        public int soloRegion;
    }
    [Serializable] public class BoneControl
    {
        public string label;
        [Range(1, 4)] public int performer = 1;
        public Movement movement;
        public Transform[] bones;
        [Tooltip("Rotation axis in each bone's local coordinates.")]
        public Vector3 localAxis = Vector3.forward;
        [Tooltip("Maximum rotation per bone for an input of 1. Negative values invert direction.")]
        public float degrees = 20;
        [Range(0, 3)] public float sensitivity = 1;
        [Range(0, 20)] public float smoothing = 8;
        [Range(-1, 1)] public float testValue;
        [Tooltip("How much of this gesture is added over locomotion animation. Zero lets the animation own this joint.")]
        [Range(0, 1)] public float locomotionWeight = 1;
        [NonSerialized] public float current;
    }

    public class DigiPhantController : MonoBehaviour
    {
        public InputMode inputMode = InputMode.TestSliders;
        [HideInInspector] public int performerCount = 3;
        public bool upperBodyOnly;
        [Tooltip("Use left-arm extension alone for both ears; requires the extended camera bridge.")]
        public bool independentP3Ears;
        [Tooltip("One upper-body performer controls only head, trunk and ears; travel is locked.")]
        public bool p3PracticeMode;
        [Tooltip("Three upper-body roles: P1 speed/gait, P2 shoulder steering (lean right = turn right), P3 head yaw/trunk/ears.")]
        public bool upperBodyTeamMode;
        [SerializeField, HideInInspector] int soloRolePractice;
        public int SoloRole => soloRolePractice > 0 ? soloRolePractice : p3PracticeMode ? 3 : 0;
        // Upper-body team with only two people. P1P2: P1 drives, P2 steers, P3 seat empty.
        // P1P3: P1 drives straight, P3 does the water/log fist, P2 seat empty (no steering).
        // Runtime only (resets each Play) so saved scenes keep the full three-person team.
        public enum TeamSeats { All, P1P2, P1P3 }
        public TeamSeats TeamSeating { get; private set; }
        public bool DuoTeam => TeamSeating != TeamSeats.All;
        public bool IsRoleSeatActive(int slot) => !upperBodyTeamMode || SoloRole != 0 || TeamSeating == TeamSeats.All
            || (TeamSeating == TeamSeats.P1P2 ? slot != 3 : slot != 2);
        public void SetDuoTeam(bool enabled) => SetTeamSeating(enabled ? TeamSeats.P1P2 : TeamSeats.All);
        public void SetTeamSeating(TeamSeats seating)
        {
            if (seating == TeamSeating) return;
            TeamSeating = seating;
            ResetControls();
            InvalidateCalibration("Team seating changed to " + seating);
            Status = seating == TeamSeats.P1P2 ? "Two-person team: P1 speed, P2 steering; leave the P3 seat empty. Set neutral pose."
                : seating == TeamSeats.P1P3 ? "Two-person team: P1 speed (straight only), P3 fist for water/log; leave the P2 seat empty. Set neutral pose."
                : "Three-person team restored. Set neutral pose.";
        }
        [SerializeField, HideInInspector] bool hasPracticeReturnSettings;
        [SerializeField, HideInInspector] int returnPerformerCount = 3;
        [SerializeField, HideInInspector] bool returnUpperBodyOnly, returnIndependentEars, returnLocomotion = true;
        [SerializeField, HideInInspector] int[] returnRoles;
        [SerializeField, HideInInspector] Movement[] returnMovements;
        public int P3InputSlot => p3PracticeMode ? 1 : Mathf.Min(3, performerCount);
        public int CalibrationRevision { get; private set; }
        public string CalibrationResetReason { get; private set; } = "Project opened";
        public CameraTrackingDiagnostics LatestTrackingDiagnostics { get; private set; }
        public int TrackingFrameSequence { get; private set; }
        public int FreshPoseRevision { get; private set; }
        public string TravelInputSafetyStatus { get; private set; } = "Waiting for calibration";
        [Min(.15f)] public float maximumPoseAgeSeconds = .45f;
        [Range(.08f, .4f)] public float standingScaleTolerance = .18f;
        [Range(2, 5)] public int stableHandSamples = 3;
        [Min(.05f)] public float handJumpTolerance = .35f;
        [Min(.5f)] public float maximumHandSpeed = 3.5f;
        [Tooltip("Three standing areas in the unmirrored preview. Ordered normalized X boundaries.")]
        public float[] regionBoundaries = { 0, 1f / 3, 2f / 3, 1 };
        [Range(0, .15f)] public float regionPadding = .1f;
        public string CupDetectorStatus { get; private set; } = "Waiting for cup detector";
        public string FistDetectorStatus { get; private set; } = "Waiting for fist detector";
        [Tooltip("Test-sliders fallback for the right-hand fist interaction signal.")]
        public bool testRightFistClosed;
        [Range(0, 1)] public float testFistConfidence = 1;
        public string TrackingStatus { get; private set; } = "Waiting for camera tracking";
        public BoneControl[] controls = Array.Empty<BoneControl>();
        [Tooltip("Local camera bridge port. Change outside Play mode.")]
        public int port = 5055;
        [Range(0, 1)] public float minimumConfidence = .5f;
        [Min(.1f)] public float trackingTimeout = .5f;
        public bool showControls = true;
        public bool showCameraPreview = true;
        public string Status { get; private set; } = "Test sliders ready";
        public int ReceivedFrames { get; private set; }
        public bool IsCalibrated { get; private set; }
        float calibrationDeadline = -1;
        // After the ten-second countdown, wait this long for a stable window before giving up.
        const float CalibrationWaitLimit = 15;
        float calibrationWaitStarted = -1;
        string calibrationWaitReason = "";
        public bool CalibrationWaiting => CalibrationPending && calibrationWaitStarted >= 0;
        [Min(.5f)] public float neutralSampleSeconds = 1.5f;
        [Range(.03f, .5f)] public float neutralStabilityTolerance = .15f;
        sealed class NeutralSample { public float time; public float[,] data; public UpperBodyGeometry[] geometry; }
        readonly List<NeutralSample> neutralSamples = new List<NeutralSample>();
        readonly UpperBodyGeometry[] geometry = new UpperBodyGeometry[4];
        readonly UpperBodyGeometry[] neutralGeometry = new UpperBodyGeometry[4];
        readonly UpperBodySampleGuard[,] handGuards = new UpperBodySampleGuard[4, 2];
        readonly bool[,] handBlocked = new bool[4, 2];
        readonly bool[,] handMissing = new bool[4, 2]; // blocked because the wrist was not detected (not a jump)
        bool distanceBlocked, poseRejected;
        int scaleEvidenceSamples;
        float scaleEvidenceStarted;
        float lastPacketReceived = -1000, captureAgeAtReceipt;
        bool hasCaptureAge;
        int assignmentRevision;
        string trackingSession;
        readonly float[] visualHeadSeen = { -1000, -1000, -1000, -1000 };
        readonly float[] visualHeadValue = new float[4];
        public bool CalibrationPending => calibrationDeadline >= 0;
        readonly float[,] values = new float[4, 8];
        readonly float[,] confidence = new float[4, 8];
        readonly float[,] neutral = new float[4, 8];
        readonly float[] lastSeen = { -1000, -1000, -1000, -1000 };
        readonly float[] roleLostSince = { -1000, -1000, -1000, -1000 };
        readonly Dictionary<Transform, Quaternion> rest = new Dictionary<Transform, Quaternion>();
        UdpClient socket;
        Vector2 scroll;
        float nextCountRequest;
        int previousCount;
        InputMode previousMode;
        bool previousUpperBodyOnly, previousPracticeMode, previousIndependentEars;
        readonly bool[] leftArmPresent = new bool[4];
        readonly bool[] headYawPresent = new bool[4];
        readonly CupObservation[] cups = new CupObservation[4];
        readonly string[] cupReasons = new string[4];
        readonly float[] cupReceived = { -1000, -1000, -1000, -1000 };

        public bool IsControlActive(BoneControl control) => control != null &&
            (upperBodyTeamMode ? (SoloRole == 0 && TeamSeating != TeamSeats.P1P2 || SoloRole == 3) && IsP3Control(control) : !p3PracticeMode || IsP3Control(control));
        public void SetUpperBodyTeamMode(bool enabled)
        {
            if (!enabled && upperBodyTeamMode) SetSoloRolePractice(0);
            upperBodyTeamMode = enabled;
            if (enabled)
            {
                if (p3PracticeMode && soloRolePractice == 0) SetP3PracticeMode(false);
                soloRolePractice = 0; p3PracticeMode = previousPracticeMode = false;
                performerCount = previousCount = 3;
                upperBodyOnly = previousUpperBodyOnly = true;
                independentP3Ears = previousIndependentEars = true;
                ConfigureUpperBodyMappings();
            }
            ClearTracking("Upper-body team preset changed"); ResetControls(); nextCountRequest = 0;
            Status = enabled ? "Upper-body team ready: P1 speed, P2 steering, P3 head and trunk. Set neutral pose." : "Upper-body team preset disabled. Inspect mappings before calibration.";
        }
        public void SetSoloRolePractice(int role)
        {
            role = Mathf.Clamp(role, 0, 3);
            if (!upperBodyTeamMode)
            {
                if (role == 3 || role == 0) SetP3PracticeMode(role == 3);
                return;
            }
            soloRolePractice = role;
            p3PracticeMode = previousPracticeMode = role == 3;
            performerCount = previousCount = role == 0 ? 3 : 1;
            upperBodyOnly = previousUpperBodyOnly = true;
            ConfigureUpperBodyMappings();
            ClearTracking("Practice role changed to " + role); ResetControls(); nextCountRequest = 0;
            Status = role == 0 ? "Three upper-body roles restored. Set neutral pose." : "Solo P" + role + " practice. Set neutral pose.";
        }
        void ConfigureUpperBodyMappings()
        {
            int slot = SoloRole > 0 ? 1 : 3;
            foreach (var control in controls)
            {
                if (control == null) continue;
                if (control.label.Contains("leg"))
                { control.performer = 1; control.movement = Movement.LeftHandHeight; control.locomotionWeight = 0; }
                else if (control.label == "Head turn") { control.performer = slot; control.movement = Movement.HeadYaw; }
                else if (control.label == "Trunk curl") { control.performer = slot; control.movement = Movement.RightHandHeight; }
                else if (control.label == "Left ear" || control.label == "Right ear")
                { control.performer = slot; control.movement = Movement.LeftArmExtension; }
            }
            var locomotion = GetComponent<DigiPhantLocomotion>();
            if (!locomotion) return;
            locomotion.StopMotion(); locomotion.StopAnimation();
            locomotion.enableLocomotion = SoloRole != 3;
            locomotion.forward.sources = SoloRole == 2 || SoloRole == 3 ? Array.Empty<MovementSource>() :
                new[] { new MovementSource { performer = 1, movement = Movement.LeftHandHeight, weight = 1 } };
            // Own-right shoulder tilt has negative Lean: -1 maps it to a right turn, so P2 steers the way they
            // lean (not mirrored). Use +1 to restore the previous inverted steering.
            locomotion.steering.sources = SoloRole == 1 || SoloRole == 3 ? Array.Empty<MovementSource>() :
                new[] { new MovementSource { performer = SoloRole == 2 ? 1 : 2, movement = Movement.Lean, weight = -1 } };
        }
        static bool IsP3Control(BoneControl control) => control != null &&
            (control.label == "Head turn" || control.label == "Trunk curl" || control.label == "Left ear" || control.label == "Right ear");
        void ConfigureEarMappings()
        {
            foreach (var control in controls)
                if (control != null && (control.label == "Left ear" || control.label == "Right ear"))
                    control.movement = independentP3Ears ? Movement.LeftArmExtension : Movement.ArmSpread;
        }
        public void SetIndependentP3Ears(bool enabled)
        {
            independentP3Ears = previousIndependentEars = p3PracticeMode || enabled;
            ConfigureEarMappings();
            ClearTracking(); ResetControls();
            Status = "Ear mapping changed. Set neutral pose again.";
        }
        void EnforcePracticeMode()
        {
            performerCount = previousCount = 1;
            upperBodyOnly = previousUpperBodyOnly = true;
            independentP3Ears = previousIndependentEars = true;
            foreach (var control in controls) if (IsP3Control(control)) control.performer = 1;
            ConfigureEarMappings();
            var locomotion = GetComponent<DigiPhantLocomotion>();
            if (locomotion) { locomotion.enableLocomotion = false; locomotion.StopMotion(); locomotion.StopAnimation(); }
        }
        void SavePracticeReturnSettings()
        {
            returnPerformerCount = performerCount;
            returnUpperBodyOnly = upperBodyOnly;
            returnIndependentEars = independentP3Ears;
            var locomotion = GetComponent<DigiPhantLocomotion>();
            returnLocomotion = locomotion && locomotion.enableLocomotion;
            returnRoles = new int[controls.Length]; returnMovements = new Movement[controls.Length];
            for (int i = 0; i < controls.Length; i++)
                if (controls[i] != null) { returnRoles[i] = controls[i].performer; returnMovements[i] = controls[i].movement; }
            hasPracticeReturnSettings = true;
        }
        public void SetP3PracticeMode(bool enabled)
        {
            if (upperBodyTeamMode) { SetSoloRolePractice(enabled ? 3 : 0); return; }
            if (previousPracticeMode == enabled)
            { p3PracticeMode = enabled; if (enabled) EnforcePracticeMode(); return; }
            if (enabled)
            {
                SavePracticeReturnSettings();
                p3PracticeMode = previousPracticeMode = true;
                EnforcePracticeMode();
            }
            else
            {
                p3PracticeMode = previousPracticeMode = false;
                if (hasPracticeReturnSettings)
                {
                    performerCount = previousCount = Mathf.Clamp(returnPerformerCount, 1, 4);
                    upperBodyOnly = previousUpperBodyOnly = returnUpperBodyOnly;
                    independentP3Ears = previousIndependentEars = returnIndependentEars;
                    for (int i = 0; i < controls.Length; i++)
                        if (controls[i] != null && returnRoles != null && returnMovements != null && i < returnRoles.Length && i < returnMovements.Length)
                        { controls[i].performer = returnRoles[i]; controls[i].movement = returnMovements[i]; }
                    var locomotion = GetComponent<DigiPhantLocomotion>();
                    if (locomotion) locomotion.enableLocomotion = returnLocomotion;
                }
                hasPracticeReturnSettings = false;
            }
            ClearTracking(); ResetControls(); nextCountRequest = 0;
            Status = enabled ? "P3 solo: head, trunk and ears only. Set neutral pose." : "Team mode restored. Set neutral pose again.";
        }

        void OnEnable()
        {
            if (p3PracticeMode && !hasPracticeReturnSettings) SavePracticeReturnSettings();
            previousPracticeMode = p3PracticeMode;
            previousIndependentEars = independentP3Ears;
            if (upperBodyTeamMode)
            {
                performerCount = soloRolePractice == 0 ? 3 : 1;
                upperBodyOnly = true;
                p3PracticeMode = previousPracticeMode = soloRolePractice == 3;
                ConfigureUpperBodyMappings();
            }
            else if (p3PracticeMode) EnforcePracticeMode();
            else if (independentP3Ears) ConfigureEarMappings();
            if (Application.isPlaying && !Application.isBatchMode && showCameraPreview && GetComponent<DigiPhantCameraPreview>() == null)
                gameObject.AddComponent<DigiPhantCameraPreview>();
            if (Application.isPlaying && !Application.isBatchMode && GetComponent<DigiPhantRecording>() == null)
                gameObject.AddComponent<DigiPhantRecording>();
            previousCount = performerCount;
            previousUpperBodyOnly = upperBodyOnly;
            ClearTracking();
            nextCountRequest = 0;
            ReceivedFrames = 0;
            Status = "Test sliders ready";
            CaptureRestPose();
            previousMode = inputMode;
            if (inputMode == InputMode.Camera) StartReceiver();
        }
        public void SetPerformerCount(int count)
        {
            performerCount = previousCount = upperBodyTeamMode ? (SoloRole == 0 ? 3 : 1) : p3PracticeMode ? 1 : Mathf.Clamp(count, 1, 4);
            foreach (var c in controls)
            {
                switch (c.label)
                {
                    case "Front left leg": case "Front right leg": c.performer = 1; break;
                    case "Rear left leg": case "Rear right leg": case "Tail sway":
                        c.performer = Mathf.Min(2, performerCount); break;
                    case "Head turn": case "Left ear": case "Right ear":
                        c.performer = Mathf.Min(3, performerCount); break;
                    case "Trunk curl": c.performer = performerCount; break;
                    default: c.performer = Mathf.Clamp(c.performer, 1, performerCount); break;
                }
            }
            if (upperBodyTeamMode) ConfigureUpperBodyMappings();
            GetComponent<DigiPhantLocomotion>()?.ClampPerformers(performerCount);
            ClearTracking();
            ResetControls();
            nextCountRequest = 0;
            Status = "Group size changed. Set neutral pose again in Camera mode.";
        }
        public void SetUpperBodyOnly(bool enabled)
        {
            upperBodyOnly = previousUpperBodyOnly = upperBodyTeamMode || p3PracticeMode || enabled;
            ClearTracking();
            ResetControls();
            nextCountRequest = 0;
            Status = "Movement mode changed. Wait for camera, then set neutral pose.";
        }
        public void CaptureRestPose()
        {
            rest.Clear();
            foreach (var c in controls)
                if (c.bones != null)
                    foreach (var bone in c.bones)
                        if (bone != null && !rest.ContainsKey(bone)) rest.Add(bone, bone.localRotation);
        }
        void StartReceiver()
        {
            StopReceiver();
            try
            {
                socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
                socket.Client.Blocking = false;
                Status = "Waiting for local camera bridge";
            }
            catch (Exception e) when (e is SocketException || e is ArgumentOutOfRangeException)
            { StopReceiver(); Status = "Cannot open camera port: " + e.Message; }
        }
        void StopReceiver() { socket?.Close(); socket = null; }
        public void SetInputMode(InputMode mode)
        {
            inputMode = previousMode = mode;
            ClearTracking();
            if (mode == InputMode.Camera) StartReceiver();
            else { StopReceiver(); Status = "Test sliders ready"; }
        }
        void ClearTracking(string reason = "Tracking configuration changed")
        {
            calibrationDeadline = -1;
            neutralSamples.Clear();
            GetComponent<DigiPhantLocomotion>()?.StopMotion();
            IsCalibrated = false;
            CalibrationRevision++;
            CalibrationResetReason = reason;
            TrackingFrameSequence = 0; lastPacketReceived = -1000; captureAgeAtReceipt = 0; hasCaptureAge = false;
            ResetSampleGuards(); TravelInputSafetyStatus = "Waiting for calibration"; poseRejected = false;
            Array.Clear(geometry, 0, geometry.Length);
            Array.Clear(neutralGeometry, 0, neutralGeometry.Length);
            ClearCups();
            CupDetectorStatus = "Waiting for cup detector";
            FistDetectorStatus = "Waiting for fist detector";
            Array.Clear(headYawPresent, 0, headYawPresent.Length);
            Array.Clear(leftArmPresent, 0, leftArmPresent.Length);
            Array.Clear(confidence, 0, confidence.Length);
            Array.Clear(neutral, 0, neutral.Length);
            for (int i = 0; i < 4; i++) { lastSeen[i] = -1000; roleLostSince[i] = -1000; }
            for (int i = 0; i < 4; i++) visualHeadSeen[i] = -1000;
        }
        void ClearCups()
        {
            Array.Clear(cups, 0, cups.Length);
            for (int i = 0; i < cupReceived.Length; i++) { cupReceived[i] = -1000; cupReasons[i] = "Waiting for a fresh cup observation"; }
        }
        public string GetCupAssociationStatus(int slot) => slot >= 1 && slot <= 4 ? cupReasons[slot - 1] ?? "Waiting for cup association" : "Invalid performer slot";
        static bool ValidCup(CupObservation cup) => cup != null && cup.detected && cup.label == "cup"
            && Finite(cup.score) && cup.score >= .45f && cup.score <= 1
            && Finite(cup.ageSeconds) && cup.ageSeconds >= 0
            && Finite(cup.x) && Finite(cup.y) && Finite(cup.width) && Finite(cup.height)
            && cup.x >= 0 && cup.y >= 0 && cup.width > 0 && cup.height > 0
            && cup.x - cup.width * .5f >= -.001f && cup.y - cup.height * .5f >= -.001f
            && cup.x + cup.width * .5f <= 1.001f && cup.y + cup.height * .5f <= 1.001f;
        public bool TryReadCup(int slot, float now, out CupObservation cup)
        {
            cup = null;
            int i = slot - 1;
            if (!Finite(now) || i < 0 || i >= performerCount || i >= cups.Length
                || !IsMovementVisible(slot, Movement.RightHandHeight, now) || !ValidCup(cups[i])) return false;
            float age = cups[i].ageSeconds + now - cupReceived[i];
            if (age < 0 || age > .7f) { cupReasons[i] = "Cup observation expired; keep cup visible near the right wrist"; return false; }
            var source = cups[i];
            cup = new CupObservation { detected = true, label = source.label, score = source.score,
                x = source.x, y = source.y, width = source.width, height = source.height, ageSeconds = age };
            return true;
        }
        public bool AcceptPacket(string json, float now)
        {
            if (!Finite(now)) return false;
            PoseFrame frame;
            try { frame = JsonUtility.FromJson<PoseFrame>(json); }
            catch (ArgumentException) { return false; }
            if (frame == null || frame.version != 1 || frame.people == null || frame.people.Length > 4) return false;
            if (frame.frameSequence < 0 || frame.hasCaptureAge && (!Finite(frame.captureAgeSeconds) || frame.captureAgeSeconds < 0)) return false;
            if (frame.assignmentRevision < 0 || (frame.trackingSession != null && frame.trackingSession.Length > 128)) return false;
            if (frame.performerCount != 0 && frame.performerCount != performerCount)
            {
                Array.Clear(headYawPresent, 0, headYawPresent.Length);
            Array.Clear(leftArmPresent, 0, leftArmPresent.Length);
            Array.Clear(confidence, 0, confidence.Length);
                for (int i = 0; i < lastSeen.Length; i++) lastSeen[i] = -1000;
                ClearCups();
                Status = "Updating camera group size...";
                return false;
            }
            if (frame.upperBodyOnly != upperBodyOnly)
            {
                Array.Clear(headYawPresent, 0, headYawPresent.Length);
            Array.Clear(leftArmPresent, 0, leftArmPresent.Length);
            Array.Clear(confidence, 0, confidence.Length);
                for (int i = 0; i < lastSeen.Length; i++) lastSeen[i] = -1000;
                ClearCups();
                Status = "Updating camera movement mode...";
                return false;
            }
            var slots = new HashSet<int>();
            foreach (var p in frame.people)
            {
                if (p == null || p.slot < 1 || p.slot > 4 || p.slot > performerCount || !slots.Add(p.slot) ||
                    p.values == null || p.confidence == null || p.values.Length != 6 || p.confidence.Length != 6) return false;
                if (p.hasLeftArm && (!Finite(p.leftArmExtension) || !Finite(p.leftArmConfidence)
                    || p.leftArmConfidence < 0 || p.leftArmConfidence > 1)) return false;
                if (p.hasHeadYaw && (!Finite(p.headYaw) || !Finite(p.headYawConfidence)
                    || p.headYawConfidence < 0 || p.headYawConfidence > 1)) return false;
                for (int k = 0; k < 6; k++)
                    if (!Finite(p.values[k]) || !Finite(p.confidence[k]) || p.confidence[k] < 0 || p.confidence[k] > 1) return false;
                var g = p.upperBodyGeometry;
                if (g != null && g.present && (!Finite(g.shoulderWidth) || g.shoulderWidth < .03f || g.shoulderWidth > 2
                    || !Finite(g.leftHandVertical) || !Finite(g.rightHandVertical)
                    || !Finite(g.leftHandConfidence) || g.leftHandConfidence < 0 || g.leftHandConfidence > 1
                    || !Finite(g.rightHandConfidence) || g.rightHandConfidence < 0 || g.rightHandConfidence > 1
                    || !Finite(g.leftFistConfidence) || g.leftFistConfidence < 0 || g.leftFistConfidence > 1
                    || !Finite(g.rightFistConfidence) || g.rightFistConfidence < 0 || g.rightFistConfidence > 1)) return false;
                if (g != null && g.present && (!Finite(g.shoulderConfidence) || g.shoulderConfidence < 0 || g.shoulderConfidence > 1
                    || !Finite(g.leftShoulderX) || !Finite(g.leftShoulderY) || !Finite(g.rightShoulderX) || !Finite(g.rightShoulderY)
                    || !Finite(g.leftWristX) || !Finite(g.leftWristY) || !Finite(g.rightWristX) || !Finite(g.rightWristY))) return false;
            }
            // A bridge restart or reassignment invalidates the old person's neutral pose,
            // including the external preview's R key. Older version-1 packets still work.
            bool identityChanged = !string.IsNullOrEmpty(frame.trackingSession) && !string.IsNullOrEmpty(trackingSession)
                && frame.trackingSession != trackingSession;
            identityChanged |= frame.assignmentRevision > 0 && assignmentRevision > 0
                && frame.assignmentRevision != assignmentRevision;
            if (identityChanged)
            {
                ClearTracking("Camera tracking session or assignment revision changed"); ResetControls();
                Status = "Camera identities changed. Set neutral pose again.";
            }
            // Only the top-level sequence is authoritative. Older local bridges may
            // populate diagnostic counters without guaranteeing a pose sequence.
            int sequence = frame.frameSequence > 0 ? frame.frameSequence : 0;
            if (!identityChanged && sequence > 0 && TrackingFrameSequence > 0 && sequence <= TrackingFrameSequence)
                return false; // A duplicate or reordered packet cannot refresh controls or reverse evidence.
            if (frame.hasCaptureAge && frame.captureAgeSeconds > maximumPoseAgeSeconds)
            {
                Array.Clear(confidence, 0, confidence.Length);
                Array.Clear(geometry, 0, geometry.Length);
                ClearCups();
                poseRejected = true;
                TravelInputSafetyStatus = "Camera frames are stale; travel paused until fresh poses arrive";
                TrackingStatus = TravelInputSafetyStatus;
                lastPacketReceived = now; captureAgeAtReceipt = frame.captureAgeSeconds; hasCaptureAge = true;
                return false;
            }
            if (!string.IsNullOrEmpty(frame.trackingSession)) trackingSession = frame.trackingSession;
            if (frame.assignmentRevision > 0) assignmentRevision = frame.assignmentRevision;
            TrackingStatus = string.IsNullOrEmpty(frame.trackingStatus) ? "Camera tracking active"
                : frame.trackingStatus.Substring(0, Mathf.Min(180, frame.trackingStatus.Length));
            LatestTrackingDiagnostics = frame.diagnostics;
            TrackingFrameSequence = sequence; FreshPoseRevision++;
            lastPacketReceived = now; captureAgeAtReceipt = frame.hasCaptureAge ? frame.captureAgeSeconds : 0;
            hasCaptureAge = frame.hasCaptureAge; poseRejected = false;
            Array.Clear(headYawPresent, 0, headYawPresent.Length);
            Array.Clear(leftArmPresent, 0, leftArmPresent.Length);
            Array.Clear(confidence, 0, confidence.Length);
            ClearCups();
            Array.Clear(geometry, 0, geometry.Length);
            CupDetectorStatus = string.IsNullOrEmpty(frame.cupDetectorStatus) ? "Cup detector unavailable in this bridge" : frame.cupDetectorStatus.Substring(0, Mathf.Min(180, frame.cupDetectorStatus.Length));
            FistDetectorStatus = string.IsNullOrEmpty(frame.fistDetectorStatus) ? "Fist detector unavailable in this bridge" : frame.fistDetectorStatus.Substring(0, Mathf.Min(180, frame.fistDetectorStatus.Length));
            // A valid packet defines the currently present slots, even when the confidence
            // threshold is zero. Omitted slots must not inherit a fresh previous timestamp.
            for (int i = 0; i < lastSeen.Length; i++) lastSeen[i] = -1000;
            foreach (var p in frame.people)
            {
                int i = p.slot - 1;
                lastSeen[i] = now;
                if (p.upperBodyGeometry != null && p.upperBodyGeometry.present) geometry[i] = CopyGeometry(p.upperBodyGeometry);
                for (int k = 0; k < 6; k++)
                { values[i, k] = Mathf.Clamp(p.values[k], -3, 3); confidence[i, k] = p.confidence[k]; }
                leftArmPresent[i] = p.hasLeftArm;
                if (p.hasLeftArm)
                { values[i, 6] = Mathf.Clamp(p.leftArmExtension, -3, 3); confidence[i, 6] = p.leftArmConfidence; }
                headYawPresent[i] = p.hasHeadYaw;
                if (p.hasHeadYaw)
                { values[i, 7] = Mathf.Clamp(p.headYaw, -3, 3); confidence[i, 7] = p.headYawConfidence; }
                if (p.cup != null && !string.IsNullOrEmpty(p.cup.reason))
                    cupReasons[i] = p.cup.reason.Substring(0, Mathf.Min(180, p.cup.reason.Length));
                if (ValidCup(p.cup)) { cups[i] = p.cup; cupReceived[i] = now; cupReasons[i] = "Fresh cup associated with right wrist"; }
            }
            ReceivedFrames++;
            UpdateSampleGuards(now);
            CollectNeutralSample(now);
            return true;
        }
        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        public void PollCamera(float now)
        {
            if (socket == null) return;
            try
            {
                if (now >= nextCountRequest)
                {
                    var request = Encoding.UTF8.GetBytes(JsonUtility.ToJson(CameraRequest(false)));
                    socket.Send(request, request.Length, new IPEndPoint(IPAddress.Loopback, port + 1));
                    nextCountRequest = now + .5f;
                }
                // Bound per-frame work; use the newest datagram from this batch.
                byte[] latest = null;
                IPEndPoint sender = new IPEndPoint(IPAddress.Loopback, 0);
                for (int i = 0; i < 64 && socket.Available > 0; i++) latest = socket.Receive(ref sender);
                if (latest != null && latest.Length < 16000 && !AcceptPacket(Encoding.UTF8.GetString(latest), now))
                    Status = "Ignored invalid camera packet";
            }
            catch (SocketException e) { Status = "Camera connection: " + e.Message; }
        }
        public bool IsTracked(int slot, float now)
        {
            int i = slot - 1;
            if (i < 0 || i >= 4 || i >= performerCount || lastSeen[i] == -1000
                || poseRejected || now - lastSeen[i] > trackingTimeout
                || hasCaptureAge && LatestPoseAgeSeconds(now) > maximumPoseAgeSeconds) return false;
            for (int k = 0; k < 6; k++) if (confidence[i, k] > 0 && confidence[i, k] >= minimumConfidence) return true;
            return false;
        }
        public bool AreAllPerformersTracked(float now, out string diagnostic)
        {
            var missing = new List<string>();
            for (int slot = 1; slot <= performerCount; slot++)
                if (IsRoleSeatActive(slot) && !IsTracked(slot, now)) missing.Add("P" + slot + " tracking");
            diagnostic = string.Join(", ", missing);
            return missing.Count == 0;
        }
        public bool IsPerformerInputReady(int slot, float now, out string diagnostic)
        {
            if (slot < 1 || slot > performerCount)
            { diagnostic = "P" + slot + " is outside the active group"; return false; }
            if (!IsTracked(slot, now))
            { diagnostic = "P" + slot + " tracking"; return false; }
            var required = new HashSet<Movement>();
            foreach (var control in controls)
                if (IsControlActive(control) && control.performer == slot) required.Add(control.movement);
            var locomotion = GetComponent<DigiPhantLocomotion>();
            if (!p3PracticeMode && locomotion && locomotion.isActiveAndEnabled && locomotion.enableLocomotion)
                foreach (var driver in new[] { locomotion.forward, locomotion.steering })
                    foreach (var source in driver.sources)
                        if (source != null && source.performer == slot && Mathf.Abs(source.weight) > .0001f)
                            required.Add(source.movement);
            var missing = new List<string>();
            foreach (var movement in required)
                if (!IsMovementVisible(slot, movement, now)) missing.Add("P" + slot + " " + movement);
            diagnostic = string.Join(", ", missing);
            return missing.Count == 0;
        }
        public bool AreMappedInputsReady(float now, out string diagnostic)
        {
            var missing = new List<string>();
            foreach (var control in controls)
                if (IsControlActive(control) && (control.performer < 1 || control.performer > performerCount))
                    missing.Add("Invalid body-role mapping");
            for (int slot = 1; slot <= performerCount; slot++)
                if (IsRoleSeatActive(slot) && !IsPerformerInputReady(slot, now, out string reason)) missing.Add(reason);
            diagnostic = string.Join("; ", missing);
            return missing.Count == 0;
        }
        // Called by locomotion before movement. A missing role stops group travel immediately;
        // sustained role loss clears the neutral pose. One occluded joint does not clear it
        // while other signals still establish that performer's presence.
        public bool UpdateGroupTrackingSafety(float now, out string diagnostic)
        {
            bool ready = AreAllPerformersTracked(now, out diagnostic);
            bool sustainedLoss = false;
            for (int slot = 1; slot <= performerCount; slot++)
            {
                int i = slot - 1;
                if (!IsRoleSeatActive(slot) || IsTracked(slot, now)) roleLostSince[i] = -1000;
                else
                {
                    if (roleLostSince[i] < 0) roleLostSince[i] = now;
                    float lossGrace = upperBodyTeamMode && LatestTrackingDiagnostics != null && LatestTrackingDiagnostics.regionalTracking
                        ? Mathf.Max(2f, trackingTimeout) : trackingTimeout;
                    if (now - roleLostSince[i] >= lossGrace) sustainedLoss = true;
                }
            }
            if (!ready && IsCalibrated)
            {
                // Fixed upper-body seats identify each role by its region, so a performer who steps
                // out only pauses travel and resumes with the same neutral pose on returning.
                bool keepNeutral = upperBodyTeamMode && LatestTrackingDiagnostics != null && LatestTrackingDiagnostics.regionalTracking;
                if (keepNeutral)
                    Status = "Group travel paused: " + diagnostic + " — return to your seat to continue (no recalibration needed).";
                else if (sustainedLoss)
                {
                    InvalidateCalibration("Sustained role loss: " + diagnostic);
                    Status = "Group tracking lost: " + diagnostic + ". Set neutral pose again when everyone is ready.";
                }
                else Status = "Group travel paused: " + diagnostic;
            }
            else if (ready && IsCalibrated && Status.StartsWith("Group travel paused:", StringComparison.Ordinal))
                Status = "Neutral pose saved; group travel ready";
            return ready;
        }
        public void ReassignPeople()
        {
            ClearTracking("Reassign people button");
            if (socket == null) return;
            try
            {
                var request = Encoding.UTF8.GetBytes(JsonUtility.ToJson(CameraRequest(true)));
                socket.Send(request, request.Length, new IPEndPoint(IPAddress.Loopback, port + 1));
                Status = "Reassigning people. Set neutral pose when visible.";
            }
            catch (SocketException e) { Status = e.Message; }
        }
        TrackingRequest CameraRequest(bool reset) => new TrackingRequest {
            performerCount = performerCount, upperBodyOnly = upperBodyOnly, reset = reset,
            regionBoundaries = regionBoundaries, regionPadding = regionPadding,
            soloRegion = upperBodyTeamMode ? SoloRole : 0
        };
        public void InvalidateCalibration(string reason = "Return to start or calibration invalidated")
        {
            calibrationDeadline = -1;
            neutralSamples.Clear();
            IsCalibrated = false;
            CalibrationRevision++;
            CalibrationResetReason = reason;
            ResetSampleGuards(); TravelInputSafetyStatus = reason;
            for (int i = 0; i < 4; i++) visualHeadSeen[i] = -1000;
            GetComponent<DigiPhantLocomotion>()?.StopMotion();
        }
        public bool IsMovementVisible(int performer, Movement movement, float now)
        {
            int i = performer - 1, k = (int)movement;
            if (i < 0 || i >= performerCount || k < 0 || k >= 8 || (k == 6 && !leftArmPresent[i]) || (k == 7 && !headYawPresent[i]) ||
                !IsTracked(performer, now) || confidence[i, k] <= 0 || confidence[i, k] < minimumConfidence) return false;
            if (upperBodyTeamMode && (k == 0 || k == 1) && (geometry[i] != null || neutralGeometry[i] != null))
            {
                if (geometry[i] == null || !geometry[i].present) return false;
                float quality = k == 0 ? geometry[i].leftHandConfidence : geometry[i].rightHandConfidence;
                if (quality <= 0 || quality < minimumConfidence) return false;
                if (IsCalibrated && (handBlocked[i, k] || i == 0 && distanceBlocked)) return false;
            }
            return true;
        }
        public bool TryReadMovement(int performer, Movement movement, float now, out float value)
        {
            value = 0;
            if (!IsCalibrated || !IsMovementVisible(performer, movement, now)) return false;
            int slot = performer - 1, channel = (int)movement;
            if (upperBodyTeamMode && (channel == 0 || channel == 1) && neutralGeometry[slot] != null)
            {
                var current = geometry[slot]; var origin = neutralGeometry[slot];
                if (current == null || !current.present || origin.shoulderWidth < .03f) return false;
                float quality = channel == 0 ? current.leftHandConfidence : current.rightHandConfidence;
                if (quality <= 0 || quality < minimumConfidence) return false;
                value = ((channel == 0 ? current.leftHandVertical : current.rightHandVertical)
                    - (channel == 0 ? origin.leftHandVertical : origin.rightHandVertical)) / origin.shoulderWidth;
            }
            else value = values[slot, channel] - neutral[slot, channel];
            return true;
        }
        public bool TryReadRawMovement(int performer, Movement movement, float now, out float value, out float neutralValue)
        {
            value = neutralValue = 0;
            if (!IsMovementVisible(performer, movement, now)) return false;
            value = values[performer - 1, (int)movement]; neutralValue = neutral[performer - 1, (int)movement]; return true;
        }
        public bool TryReadFist(int performer, bool rightHand, float now, out bool closed, out float signalConfidence)
        {
            closed = false; signalConfidence = 0;
            if (inputMode == InputMode.TestSliders)
            {
                closed = rightHand ? testRightFistClosed : false;
                signalConfidence = Mathf.Clamp01(testFistConfidence);
                return signalConfidence > 0;
            }
            int index = performer - 1;
            if (index < 0 || index >= performerCount || !IsTracked(performer, now)) return false;
            var current = geometry[index];
            if (current == null || !current.present) return false;
            signalConfidence = rightHand ? current.rightFistConfidence : current.leftFistConfidence;
            if (!Finite(signalConfidence) || signalConfidence < minimumConfidence) return false;
            closed = rightHand ? current.rightFistClosed : current.leftFistClosed;
            return true;
        }
        public float NeutralShoulderWidth(int performer) => performer >= 1 && performer <= 4 && neutralGeometry[performer - 1] != null
            ? neutralGeometry[performer - 1].shoulderWidth : 0;
        public float CurrentShoulderWidth(int performer) => performer >= 1 && performer <= 4 && geometry[performer - 1] != null
            ? geometry[performer - 1].shoulderWidth : 0;
        public float LatestPoseAgeSeconds(float now) => lastPacketReceived < 0 ? -1
            : Mathf.Max(0, captureAgeAtReceipt + now - lastPacketReceived);
        public float LatestPoseSampleTime => lastPacketReceived - captureAgeAtReceipt;
        public bool TryReadUpperBodyGeometry(int performer, float now, out UpperBodyGeometry current, out UpperBodyGeometry origin)
        {
            current = origin = null;
            int slot = performer - 1;
            if (slot < 0 || slot >= performerCount || !IsTracked(performer, now) || geometry[slot] == null) return false;
            current = CopyGeometry(geometry[slot]); origin = CopyGeometry(neutralGeometry[slot]); return true;
        }
        public bool TravelInputSafetyReady(float now)
        {
            if (!upperBodyTeamMode || inputMode != InputMode.Camera || SoloRole == 2 || SoloRole == 3) return true;
            if (poseRejected || hasCaptureAge && LatestPoseAgeSeconds(now) > maximumPoseAgeSeconds)
            { TravelInputSafetyStatus = "Camera frames are stale; travel paused until fresh poses arrive"; return false; }
            if (!IsCalibrated) { TravelInputSafetyStatus = "Recalibration needed: hold a comfortable left-hand stop pose"; return false; }
            if (distanceBlocked) return false;
            if (handBlocked[0, 0])
            {
                TravelInputSafetyStatus = handMissing[0, 0] ? "P1 left hand not detected (too low, out of frame or hidden); paused. Keep the left hand in frame"
                    : "P1 left hand jumped suddenly; paused. Hold the movement steady";
                return false;
            }
            if (!IsMovementVisible(1, Movement.LeftHandHeight, now))
            { TravelInputSafetyStatus = "P1 left hand unavailable; paused. Keep shoulders and left hand fully in frame"; return false; }
            TravelInputSafetyStatus = "Input stable; keep the calibrated distance"; return true;
        }
        void ResetSampleGuards()
        {
            Array.Clear(handBlocked, 0, handBlocked.Length);
            for (int slot = 0; slot < 4; slot++) for (int hand = 0; hand < 2; hand++) handGuards[slot, hand]?.Reset();
            distanceBlocked = false; scaleEvidenceSamples = 0;
        }
        void SeedSampleGuards(float now)
        {
            ResetSampleGuards();
            for (int slot = 0; slot < performerCount; slot++) for (int hand = 0; hand < 2; hand++)
            {
                if (geometry[slot] == null || neutralGeometry[slot] == null) continue;
                handGuards[slot, hand] = new UpperBodySampleGuard();
                handGuards[slot, hand].Seed(0, now);
            }
            TravelInputSafetyStatus = "Input stable; keep the calibrated distance";
        }
        void UpdateSampleGuards(float now)
        {
            if (!upperBodyTeamMode || !IsCalibrated) return;
            if (SoloRole == 0 || SoloRole == 1)
            {
                var source = geometry[0]; var origin = neutralGeometry[0];
                bool changed = source != null && origin != null
                    && Mathf.Abs(source.shoulderWidth / origin.shoulderWidth - 1) > standingScaleTolerance;
                distanceBlocked = changed;
                if (changed)
                {
                    TravelInputSafetyStatus = "P1 distance or shoulder angle changed a lot; paused. Return to the calibrated spot or recalibrate";
                    if (scaleEvidenceSamples++ == 0) scaleEvidenceStarted = now;
                    if (scaleEvidenceSamples >= 3 && now - scaleEvidenceStarted >= .2f)
                    { InvalidateCalibration("P1 distance or shoulder angle changed a lot; please recalibrate"); return; }
                }
                else scaleEvidenceSamples = 0;
            }
            for (int slot = 0; slot < performerCount; slot++) for (int hand = 0; hand < 2; hand++)
            {
                var source = geometry[slot]; var origin = neutralGeometry[slot];
                if (origin == null) { handBlocked[slot, hand] = false; continue; }
                var gate = handGuards[slot, hand] ?? (handGuards[slot, hand] = new UpperBodySampleGuard());
                float quality = source == null ? 0 : hand == 0 ? source.leftHandConfidence : source.rightHandConfidence;
                if (source == null || quality <= 0 || quality < minimumConfidence)
                { gate.MarkUnavailable(); handBlocked[slot, hand] = handMissing[slot, hand] = true; continue; }
                handMissing[slot, hand] = false;
                float value = ((hand == 0 ? source.leftHandVertical : source.rightHandVertical)
                    - (hand == 0 ? origin.leftHandVertical : origin.rightHandVertical)) / origin.shoulderWidth;
                handBlocked[slot, hand] = !gate.Observe(value, now, handJumpTolerance, maximumHandSpeed, stableHandSamples);
            }
        }
        static UpperBodyGeometry CopyGeometry(UpperBodyGeometry source) => source == null ? null : new UpperBodyGeometry {
            present = source.present, shoulderWidth = source.shoulderWidth, leftHandVertical = source.leftHandVertical,
            rightHandVertical = source.rightHandVertical, leftHandConfidence = source.leftHandConfidence, rightHandConfidence = source.rightHandConfidence,
            leftFistClosed = source.leftFistClosed, rightFistClosed = source.rightFistClosed,
            leftFistConfidence = source.leftFistConfidence, rightFistConfidence = source.rightFistConfidence,
            shoulderConfidence = source.shoulderConfidence, leftShoulderX = source.leftShoulderX, leftShoulderY = source.leftShoulderY,
            rightShoulderX = source.rightShoulderX, rightShoulderY = source.rightShoulderY, leftWristX = source.leftWristX,
            leftWristY = source.leftWristY, rightWristX = source.rightWristX, rightWristY = source.rightWristY
        };
        public void BeginCalibrationCountdown(float now)
        {
            if (inputMode != InputMode.Camera) return;
            InvalidateCalibration("Neutral pose countdown started");
            ResetControls();
            calibrationDeadline = now + 10;
            calibrationWaitStarted = -1; calibrationWaitReason = "";
            Status = "Get into your neutral pose and hold still.";
        }
        public void CancelCalibrationCountdown()
        {
            calibrationDeadline = -1;
            neutralSamples.Clear();
            Status = "Calibration cancelled. Set neutral pose when ready.";
        }
        public void UpdateCalibrationCountdown(float now)
        {
            if (!CalibrationPending || now < calibrationDeadline) return;
            if (!TryStableNeutral(now, out string reason))
            {
                if (calibrationWaitStarted < 0) calibrationWaitStarted = now;
                if (reason != calibrationWaitReason)
                {
                    calibrationWaitReason = reason;
                    Debug.Log("Neutral pose waiting: " + reason);
                }
                if (now - calibrationWaitStarted >= CalibrationWaitLimit)
                {
                    calibrationDeadline = -1;
                    neutralSamples.Clear();
                    Status = "Neutral pose NOT saved (no stable " + CalibrationWaitLimit.ToString("0") + " s window): " + reason
                        + ". Fix this, then set neutral pose again.";
                    Debug.LogWarning(Status);
                    return;
                }
                calibrationDeadline = now + .5f;
                Status = "Hold neutral pose steady: " + reason;
                return;
            }
            calibrationDeadline = -1;
            IsCalibrated = true; CalibrationRevision++;
            SeedSampleGuards(now);
            neutralSamples.Clear();
            Status = "Neutral pose saved from stable camera samples";
        }
        void CollectNeutralSample(float now)
        {
            if (!CalibrationPending) return;
            if (!AreMappedInputsReady(now, out _)) { neutralSamples.Clear(); return; }
            var capturedGeometry = new UpperBodyGeometry[4];
            for (int i = 0; i < 4; i++) capturedGeometry[i] = CopyGeometry(geometry[i]);
            neutralSamples.Add(new NeutralSample { time = now, data = (float[,])values.Clone(), geometry = capturedGeometry });
            float cutoff = now - Mathf.Max(.5f, neutralSampleSeconds);
            neutralSamples.RemoveAll(sample => sample.time < cutoff);
            // Bound memory if an external sender floods the local UDP port.
            if (neutralSamples.Count > 180) neutralSamples.RemoveRange(0, neutralSamples.Count - 180);
        }
        bool TryStableNeutral(float now, out string reason)
        {
            if (!AreMappedInputsReady(now, out reason)) return false;
            neutralSamples.RemoveAll(sample => sample.time < now - Mathf.Max(.5f, neutralSampleSeconds));
            if (neutralSamples.Count < 4 || now - neutralSamples[neutralSamples.Count - 1].time > .35f
                || neutralSamples[neutralSamples.Count - 1].time - neutralSamples[0].time < Mathf.Max(.4f, neutralSampleSeconds * .65f))
            { reason = "waiting for a continuous sample window"; return false; }
            var required = new HashSet<int>();
            foreach (var control in controls)
                if (IsControlActive(control) && control.performer >= 1 && control.performer <= performerCount)
                    required.Add((control.performer - 1) * 8 + (int)control.movement);
            var locomotion = GetComponent<DigiPhantLocomotion>();
            if (locomotion && locomotion.enableLocomotion && !p3PracticeMode)
                foreach (var driver in new[] { locomotion.forward, locomotion.steering })
                    foreach (var source in driver.sources)
                        if (source != null && source.performer >= 1 && source.performer <= performerCount && Mathf.Abs(source.weight) > .0001f)
                            required.Add((source.performer - 1) * 8 + (int)source.movement);
            var nextNeutral = new float[4, 8];
            var nextGeometry = new UpperBodyGeometry[4];
            for (int slot = 0; slot < performerCount; slot++)
            {
                bool hasGeometry = upperBodyTeamMode && neutralSamples.TrueForAll(sample => sample.geometry[slot] != null && sample.geometry[slot].present);
                if (hasGeometry)
                {
                    float Median(Func<UpperBodyGeometry, float> read)
                    {
                        var samples = new float[neutralSamples.Count];
                        for (int n = 0; n < samples.Length; n++) samples[n] = read(neutralSamples[n].geometry[slot]);
                        Array.Sort(samples); int mid = samples.Length / 2;
                        return samples.Length % 2 == 0 ? (samples[mid - 1] + samples[mid]) * .5f : samples[mid];
                    }
                    nextGeometry[slot] = new UpperBodyGeometry { present = true, shoulderWidth = Median(g => g.shoulderWidth),
                        leftHandVertical = Median(g => g.leftHandVertical), rightHandVertical = Median(g => g.rightHandVertical),
                        leftHandConfidence = 1, rightHandConfidence = 1 };
                }
                for (int channel = 0; channel < 8; channel++)
                {
                    var samples = new float[neutralSamples.Count];
                    for (int n = 0; n < samples.Length; n++) samples[n] = neutralSamples[n].data[slot, channel];
                    Array.Sort(samples);
                    float spread = samples[Mathf.FloorToInt((samples.Length - 1) * .9f)] - samples[Mathf.FloorToInt((samples.Length - 1) * .1f)];
                    if (hasGeometry && (channel == 0 || channel == 1))
                    {
                        var stable = new float[samples.Length];
                        for (int n = 0; n < stable.Length; n++) stable[n] = (channel == 0 ? neutralSamples[n].geometry[slot].leftHandVertical
                            : neutralSamples[n].geometry[slot].rightHandVertical) / nextGeometry[slot].shoulderWidth;
                        Array.Sort(stable);
                        spread = stable[Mathf.FloorToInt((stable.Length - 1) * .9f)] - stable[Mathf.FloorToInt((stable.Length - 1) * .1f)];
                    }
                    if (required.Contains(slot * 8 + channel) && spread > neutralStabilityTolerance)
                    { reason = "P" + (slot + 1) + " " + (Movement)channel + " is moving"; return false; }
                    int mid = samples.Length / 2;
                    nextNeutral[slot, channel] = samples.Length % 2 == 0 ? (samples[mid - 1] + samples[mid]) * .5f : samples[mid];
                }
            }
            Array.Copy(nextNeutral, neutral, neutral.Length);
            Array.Copy(nextGeometry, neutralGeometry, nextGeometry.Length);
            reason = ""; return true;
        }
        public void Calibrate() => CalibrateAt(Time.realtimeSinceStartup);
        public bool CalibrateAt(float now)
        {
            calibrationDeadline = -1;
            neutralSamples.Clear();
            IsCalibrated = false;
            // Presence and mapped-input readiness are separate: a visible shoulder does not
            // make an occluded foot or hand a valid neutral-pose sample.
            if (!AreMappedInputsReady(now, out string missing))
            { Status = "Calibration waiting for: " + missing; return false; }
            Array.Copy(values, neutral, values.Length);
            for (int slot = 0; slot < 4; slot++) neutralGeometry[slot] = upperBodyTeamMode ? CopyGeometry(geometry[slot]) : null;
            IsCalibrated = true;
            CalibrationRevision++;
            SeedSampleGuards(now);
            Status = "Neutral pose saved";
            return true;
        }
        public void ResetControls()
        {
            foreach (var c in controls) { c.testValue = 0; c.current = 0; }
            foreach (var pair in rest) if (pair.Key != null) pair.Key.localRotation = pair.Value;
        }
        public void ApplyControls(float now, float dt)
        {
            foreach (var pair in rest) if (pair.Key != null) pair.Key.localRotation = pair.Value;
            var locomotion = GetComponent<DigiPhantLocomotion>();
            bool animated = locomotion != null && locomotion.isActiveAndEnabled && locomotion.Evaluate(now, dt);
            foreach (var c in controls)
            {
                if (!IsControlActive(c)) { if (c != null) c.current = 0; continue; }
                float target = 0;
                int i = c.performer - 1, k = (int)c.movement;
                if (i >= 0 && i < performerCount && k >= 0 && k < 8)
                {
                    if (inputMode == InputMode.TestSliders) target = c.testValue;
                    else if (TryReadMovement(c.performer, c.movement, now, out float freshTarget))
                    {
                        target = freshTarget;
                        if (c.movement == Movement.HeadYaw) { visualHeadSeen[i] = now; visualHeadValue[i] = target; }
                    }
                    else if (IsCalibrated && c.movement == Movement.HeadYaw && now - visualHeadSeen[i] <= .25f)
                        target = visualHeadValue[i]; // Visual grace only; readiness/travel never uses this cached value.
                }
                target = Mathf.Clamp(target * c.sensitivity, -1, 1);
                c.current = c.smoothing <= 0 ? target : Mathf.Lerp(c.current, target, 1 - Mathf.Exp(-c.smoothing * dt));
                if (c.bones == null || c.localAxis.sqrMagnitude < .0001f) continue;
                foreach (var bone in c.bones)
                    if (bone != null && rest.ContainsKey(bone))
                        bone.localRotation *= Quaternion.AngleAxis(c.current * c.degrees * (animated ? c.locomotionWeight : 1), c.localAxis.normalized);
            }
        }
        void LateUpdate()
        {
            if (previousPracticeMode != p3PracticeMode) SetP3PracticeMode(p3PracticeMode);
            if (p3PracticeMode) EnforcePracticeMode();
            if (upperBodyTeamMode) { performerCount = previousCount = SoloRole == 0 ? 3 : 1; upperBodyOnly = previousUpperBodyOnly = true; }
            if (previousIndependentEars != independentP3Ears) SetIndependentP3Ears(independentP3Ears);
            if (previousUpperBodyOnly != upperBodyOnly) SetUpperBodyOnly(upperBodyOnly);
            if (previousCount != performerCount) SetPerformerCount(performerCount);
            if (previousMode != inputMode) SetInputMode(inputMode);
            if (inputMode == InputMode.Camera) PollCamera(Time.realtimeSinceStartup);
            if (inputMode == InputMode.Camera && IsCalibrated && upperBodyTeamMode)
                UpdateGroupTrackingSafety(Time.realtimeSinceStartup, out _);
            UpdateCalibrationCountdown(Time.realtimeSinceStartup);
            ApplyControls(Time.realtimeSinceStartup, Time.deltaTime);
            var stageCamera = Camera.main;
            if (stageCamera != null)
            {
                float left = showControls ? (Mathf.Min(350, Screen.width * .32f) + 20) / Mathf.Max(1, Screen.width) : 0;
                stageCamera.rect = new Rect(left, 0, 1 - left, 1);
            }
        }
        void OnDisable() { calibrationDeadline = -1; StopReceiver(); GetComponent<DigiPhantLocomotion>()?.StopAnimation(); ResetControls(); }
        void OnGUI()
        {
            if (!showControls) return;
            float width = Mathf.Min(350, Screen.width * .32f);
            GUI.DrawTexture(new Rect(0, 0, width + 20, Screen.height), Texture2D.blackTexture, ScaleMode.StretchToFill, false);
            var labelStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            void Label(string text) => GUILayout.Label(text, labelStyle, GUILayout.Width(width - 32));
            GUILayout.BeginArea(new Rect(10, 10, width, Screen.height - 20), GUI.skin.box);
            GetComponent<DigiPhantCameraPreview>()?.DrawInline(width - 20);
            GetComponent<DigiPhantRecording>()?.DrawControls(width - 32);
            scroll = GUILayout.BeginScrollView(scroll);
            Label("DIGIPHANT | collective digital twin");
            if (upperBodyTeamMode)
            {
                Label("Three-person upper body | left P1 speed/legs; middle P2 steering; right P3 head/trunk/ears");
                int role = GUILayout.Toolbar(SoloRole, new[] { "Team", "Solo P1", "Solo P2", "Solo P3" });
                if (role != SoloRole) SetSoloRolePractice(role);
                if (SoloRole == 0)
                {
                    var seating = (TeamSeats)GUILayout.Toolbar((int)TeamSeating, new[] { "Three", "Two: P1+P2 drive", "Two: P1+P3 water/log" });
                    if (seating != TeamSeating) SetTeamSeating(seating);
                    if (TeamSeating == TeamSeats.P1P3) Label("P1+P3: the P2 seat is empty, so the elephant only walks straight; use this for water and log.");
                }
                Label("P1: raise the left hand slowly to walk, return to the calibrated height to stop, hold it clearly lower to reverse. P2: tilt your shoulders the way you want to turn. P3: turn the head slightly, raise the right hand to curl the trunk, extend the left arm to open the ears.");
            }
            else
            {
                if (GUILayout.Button(p3PracticeMode ? "Return to team mode" : "P3 solo practice (head / trunk / ears)")) SetP3PracticeMode(!p3PracticeMode);
                if (p3PracticeMode) Label("P3 SOLO | one person, shoulders and hands visible. Travel, legs and tail are locked.");
                else
                {
                    Label("Choose your group size");
                    int count = GUILayout.Toolbar(performerCount - 1, new[] { "1", "2", "3", "4" }) + 1;
                    if (count != performerCount) SetPerformerCount(count);
                    bool upper = GUILayout.Toolbar(upperBodyOnly ? 1 : 0, new[] { "Full body", "Seated / upper body" }) == 1;
                    if (upper != upperBodyOnly) SetUpperBodyOnly(upper);
                    if (upperBodyOnly) Label("Keep shoulders and hands visible. Raise hands for legs; tilt shoulders for steering, head and tail.");
                }
            }
            if (independentP3Ears && (!upperBodyTeamMode || SoloRole == 0 || SoloRole == 3))
                Label("P3 ears: extend LEFT arm sideways, then return. Trunk: raise/lower RIGHT hand. Head: " + (upperBodyTeamMode ? "turn your head left/right." : "tilt shoulders left/right."));
            int mode = GUILayout.Toolbar((int)inputMode, new[] { "Test sliders", "Camera" });
            if (mode != (int)inputMode) SetInputMode((InputMode)mode);
            Label(Status);
            if (!p3PracticeMode) GetComponent<DigiPhantLocomotion>()?.DrawControls(width - 32);
            if (inputMode == InputMode.Camera)
            {
                Label(TrackingStatus);
                if (LatestTrackingDiagnostics != null)
                {
                    var diagnostic = LatestTrackingDiagnostics;
                    Label((diagnostic.regionalTracking ? "3 STANDING REGIONS" : "GLOBAL TRACKING") + " | detected " + diagnostic.rawDetected
                        + " | candidates " + diagnostic.candidatePeople + " | shoulders ready " + diagnostic.validPeople
                        + " | assigned " + diagnostic.assignedPeople);
                    if (!string.IsNullOrEmpty(diagnostic.trackerStatus))
                        Label("Tracker: " + diagnostic.trackerStatus);
                    if (diagnostic.trackerAmbiguous)
                        Label("Tracker identity is ambiguous; stop, restore the marked positions, then reassign people.");
                    Label("Capture " + diagnostic.captureWidth + " x " + diagnostic.captureHeight + " | "
                        + diagnostic.cameraFps.ToString("0.0") + " tracking fps | pose " + diagnostic.poseInferenceMs.ToString("0") + " ms");
                    if (!string.IsNullOrEmpty(diagnostic.rejectionReasons)) Label(diagnostic.rejectionReasons);
                    if (diagnostic.cameraFps > 0 && diagnostic.cameraFps < 8) Label("Tracking is slow: move gently; reversing waits for more fresh frames.");
                }
                float poseAge = LatestPoseAgeSeconds(Time.realtimeSinceStartup);
                if (poseAge >= 0) Label("Pose age " + poseAge.ToString("0.00") + " s");
                Label(TravelInputSafetyStatus);
                if (TryReadMovement(1, Movement.LeftHandHeight, Time.realtimeSinceStartup, out float hand))
                    Label("P1 hand from neutral: " + hand.ToString("+0.00;-0.00;0.00") + " | calibrated scale " + NeutralShoulderWidth(1).ToString("0.000"));
                Label(upperBodyTeamMode && SoloRole > 0 ? "Solo P" + SoloRole + ": only the P" + SoloRole + " region of the camera is tracked; teammates beside it are ignored."
                    : "Camera setup: run SetupCamera.py in the project folder. Solo practice requires exactly one visible person.");
                for (int i = 1; i <= performerCount; i++)
                {
                    if (!IsRoleSeatActive(i)) { Label("P" + i + " · not used in two-person team"); continue; }
                    float now = Time.realtimeSinceStartup;
                    bool tracked = IsTracked(i, now);
                    bool ready = IsPerformerInputReady(i, now, out string missing);
                    Label((SoloRole > 0 ? "P" + SoloRole + " solo practice (input 1)" : "P" + i) + (!tracked ? " · waiting / lost" : ready
                        ? " · inputs ready" : " · tracked, but inputs not ready: " + missing));
                }
                if (!IsCalibrated) Label("Calibrate in a comfortable stop pose, then keep your distance.");
                if (CalibrationPending)
                {
                    if (CalibrationWaiting)
                    {
                        int left = Mathf.Max(0, Mathf.CeilToInt(CalibrationWaitLimit - (Time.realtimeSinceStartup - calibrationWaitStarted)));
                        Label("Countdown finished, waiting for a steady pose (" + left + " s left): " + calibrationWaitReason);
                    }
                    else
                    {
                        int seconds = Mathf.Max(0, Mathf.CeilToInt(calibrationDeadline - Time.realtimeSinceStartup));
                        Label("Setting neutral pose in " + seconds + " seconds — hold your resting pose.");
                    }
                    if (GUILayout.Button("Cancel countdown")) CancelCalibrationCountdown();
                }
                else if (GUILayout.Button("Set neutral pose (10 seconds)"))
                    BeginCalibrationCountdown(Time.realtimeSinceStartup);
                if (GUILayout.Button("Reassign people")) ReassignPeople();
                Label("Hold a comfortable pose, then set neutral.");
            }
            else
            {
                Label("Move a slider to test its elephant part.");
                foreach (var c in controls)
                {
                    if (!IsControlActive(c)) continue;
                    Label(c.label + "  · P" + c.performer);
                    c.testValue = GUILayout.HorizontalSlider(c.testValue, -1, 1);
                }
                if (GUILayout.Button("Reset body sliders")) ResetControls();
            }
            GUILayout.Space(8);
            Label("To adapt: stop Play, select DigiPhant Controls,\nthen edit Controls in the Inspector.");
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
