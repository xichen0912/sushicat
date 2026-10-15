using UnityEngine;

namespace StudentWork
{
    // Detection geometry only. These zones never push, block or step the elephant.
    public static class CourseZoneUtility
    {
        public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        public static Vector3 LocalPoint(BoxCollider zone, Vector3 position) => zone.transform.InverseTransformPoint(position) - zone.center;
        public static bool Contains(BoxCollider zone, Vector3 position)
        {
            if (!zone || !Finite(position)) return false;
            Vector3 local = LocalPoint(zone, position), half = zone.size * .5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.z;
        }
        public static bool CrossesFace(BoxCollider zone, Vector3 before, Vector3 after, bool farFace)
        {
            if (!zone) return false;
            Vector3 a = LocalPoint(zone, before), b = LocalPoint(zone, after);
            float face = zone.size.z * (farFace ? .5f : -.5f);
            if (a.z > face || b.z < face || b.z <= a.z || b.z - a.z < .000001f) return false;
            float x = Mathf.Lerp(a.x, b.x, (face - a.z) / (b.z - a.z));
            return Mathf.Abs(x) <= zone.size.x * .5f;
        }
        public static float HorizontalSegmentDistance(Vector3 a, Vector3 b, Vector3 point)
        {
            a.y = b.y = point.y = 0;
            Vector3 segment = b - a;
            float t = segment.sqrMagnitude > .000001f ? Mathf.Clamp01(Vector3.Dot(point - a, segment) / segment.sqrMagnitude) : 0;
            return Vector3.Distance(a + segment * t, point);
        }
    }

    public sealed class TreeInteraction : MonoBehaviour
    {
        public BoxCollider detectionZone;
        public Transform treeTrunk;
        public Transform avatarHeading;
        [Min(0)] public float minimumHeadingChangeDegrees = 12f;
        [Min(0)] public float trunkRadius = .65f;
        [Min(0)] public float avatarClearanceRadius = 1.2f;
        [Min(.05f)] public float maximumSampleGap = .35f;
        [Min(.1f)] public float maximumSampleDistance = 2f;
        [Min(0)] public float trackingPauseToleranceSeconds = .45f;
        [Min(0)] public float maximumTotalPauseSeconds = 1.2f;
        [Min(0)] public float maximumPauseMovement = .15f;
        public bool Completed { get; private set; }
        public bool AttemptActive { get; private set; }
        public int AttemptCount { get; private set; }
        public int FailureCount { get; private set; }
        public float MinimumClearance { get; private set; } = -1;
        public float MaximumHeadingChangeDegrees { get; private set; }
        public float PausedSeconds { get; private set; }
        public string Status { get; private set; } = "Approach the tree zone from its entry side, then steer around the trunk.";
        Vector3 previousPosition;
        bool hasPrevious;
        float closestDistance;
        float entryHeading;
        float pausedFor;
        int attemptCalibrationRevision;

        public void Configure(BoxCollider zone, Transform trunk, float configuredTrunkRadius = .65f, float configuredAvatarRadius = 1.2f)
        {
            detectionZone = zone; treeTrunk = trunk;
            trunkRadius = configuredTrunkRadius; avatarClearanceRadius = configuredAvatarRadius;
            ResetInteraction();
        }
        public void ResetInteraction()
        {
            Completed = AttemptActive = hasPrevious = false;
            AttemptCount = FailureCount = 0; MinimumClearance = -1;
            MaximumHeadingChangeDegrees = 0;
            PausedSeconds = pausedFor = 0;
            closestDistance = float.PositiveInfinity;
            Status = "Approach the tree zone from its entry side, then steer around the trunk.";
        }
        public bool Evaluate(Vector3 position, float signedSpeed, bool inputReady, float dt, float headingDegrees = float.NaN, int calibrationRevision = 0)
        {
            if (Completed) return false;
            if (!detectionZone || !treeTrunk || !CourseZoneUtility.Finite(position))
            { Fail("Tree zone/trunk setup is missing or position is invalid."); hasPrevious = false; return false; }
            Vector3 before = previousPosition;
            bool hadPrevious = hasPrevious;
            bool validFrame = CourseZoneUtility.Finite(dt) && dt > 0 && dt <= Mathf.Max(.05f, maximumSampleGap)
                && CourseZoneUtility.Finite(signedSpeed)
                && (!hadPrevious || Vector3.Distance(before, position) <= Mathf.Max(.1f, maximumSampleDistance));
            if (AttemptActive && calibrationRevision != 0 && attemptCalibrationRevision != 0 && calibrationRevision != attemptCalibrationRevision)
            { Fail("Tracking identity or calibration changed. Return before the tree entry and retry."); previousPosition = position; hasPrevious = true; return false; }
            if (AttemptActive && validFrame && !inputReady && Mathf.Abs(signedSpeed) <= .05f
                && CourseZoneUtility.Contains(detectionZone, position)
                && Vector3.Distance(before, position) <= Mathf.Max(0, maximumPauseMovement)
                && CourseZoneUtility.LocalPoint(detectionZone, position).z >= CourseZoneUtility.LocalPoint(detectionZone, before).z - .02f)
            {
                pausedFor += dt; PausedSeconds += dt;
                if (pausedFor <= Mathf.Max(0, trackingPauseToleranceSeconds) + .00001f
                    && PausedSeconds <= Mathf.Max(0, maximumTotalPauseSeconds) + .00001f)
                { Status = "Tree attempt paused briefly. Hold position; restore the same calibrated people to continue."; return false; }
                Fail("Tree tracking pause exceeded its continuous or total tolerance. Return before the entry and retry.");
                previousPosition = position; hasPrevious = true; return false;
            }
            previousPosition = position; hasPrevious = true;
            if (!inputReady || !validFrame || !CourseZoneUtility.Finite(dt) || dt <= 0 || dt > Mathf.Max(.05f, maximumSampleGap)
                || !CourseZoneUtility.Finite(signedSpeed) || hadPrevious && Vector3.Distance(before, position) > Mathf.Max(.1f, maximumSampleDistance))
            { Fail("Tracking/frame gap: return before the tree entry and retry."); return false; }
            if (!hadPrevious) return false;
            Vector3 a = CourseZoneUtility.LocalPoint(detectionZone, before), b = CourseZoneUtility.LocalPoint(detectionZone, position);
            if (signedSpeed < -.02f || AttemptActive && b.z < a.z - .02f)
            { Fail("Tree attempt reversed. Return before the entry and retry."); return false; }
            pausedFor = 0;
            if (!CourseZoneUtility.Finite(headingDegrees))
                headingDegrees = avatarHeading ? avatarHeading.eulerAngles.y
                    : (position - before).sqrMagnitude > .000001f ? Mathf.Atan2(position.x - before.x, position.z - before.z) * Mathf.Rad2Deg : 0;
            if (!AttemptActive)
            {
                if (!CourseZoneUtility.CrossesFace(detectionZone, before, position, false)) return false;
                AttemptActive = true; AttemptCount++; closestDistance = float.PositiveInfinity;
                attemptCalibrationRevision = calibrationRevision; PausedSeconds = 0;
                entryHeading = headingDegrees; MaximumHeadingChangeDegrees = 0;
                Status = "Tree attempt: pass beside the trunk and continue through the far side.";
            }
            MaximumHeadingChangeDegrees = Mathf.Max(MaximumHeadingChangeDegrees, Mathf.Abs(Mathf.DeltaAngle(entryHeading, headingDegrees)));
            closestDistance = Mathf.Min(closestDistance, CourseZoneUtility.HorizontalSegmentDistance(before, position, treeTrunk.position));
            MinimumClearance = closestDistance - Mathf.Max(0, trunkRadius) - Mathf.Max(0, avatarClearanceRadius);
            if (MinimumClearance <= 0)
            { Fail("Too close to the trunk. Return before the entry and take a wider route."); return false; }
            if (CourseZoneUtility.CrossesFace(detectionZone, before, position, true))
            {
                if (MaximumHeadingChangeDegrees < Mathf.Max(0, minimumHeadingChangeDegrees))
                { Fail("Safe clearance, but no steering demonstrated. Return before the entry and make a gentle turn of at least " + minimumHeadingChangeDegrees.ToString("0") + " degrees while passing the tree."); return false; }
                Completed = true; AttemptActive = false;
                Status = "Tree avoided: " + MinimumClearance.ToString("0.00") + " units clearance; " + MaximumHeadingChangeDegrees.ToString("0") + " degrees steering demonstrated.";
                return true;
            }
            if (!CourseZoneUtility.Contains(detectionZone, position))
                Fail("Exited the tree zone through the entry/side. Return before the entry to retry.");
            return false;
        }
        void Fail(string reason)
        {
            if (AttemptActive) FailureCount++;
            AttemptActive = false; Status = reason;
        }
        public void CancelAttempt(string reason) => Fail(reason);
    }
}
