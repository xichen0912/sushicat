using UnityEngine;

namespace StudentWork
{
    public sealed class LogInteraction : MonoBehaviour
    {
        public BoxCollider detectionZone;
        public Transform logTransform;
        [Min(.1f)] public float corridorHalfWidth = 1.4f;
        [Min(.1f)] public float logHalfLength = 1.5f;
        [Min(0)] public float trackingPauseToleranceSeconds = .4f;
        [Min(0)] public float maximumTotalPauseSeconds = 1.2f;
        [Min(0)] public float maximumPauseMovement = .15f;
        [Min(.01f)] public float minimumCrossingSpeed = .08f;
        [Min(.02f)] public float maximumCrossingSpeed = 1.8f;
        [Min(.05f)] public float maximumSampleGap = .35f;
        [Min(.1f)] public float maximumSampleDistance = 2f;
        public bool Completed { get; private set; }
        public bool AttemptActive { get; private set; }
        public int AttemptCount { get; private set; }
        public int FailureCount { get; private set; }
        public float CrossingSeconds { get; private set; }
        public float PausedSeconds { get; private set; }
        public string Status { get; private set; } = "Approach the log from its entry side and walk forward slowly without stopping.";
        Vector3 previousPosition;
        bool hasPrevious;
        bool crossedTimber;
        float pausedFor;

        public void Configure(BoxCollider zone, float configuredMaximumSpeed = 1.8f)
        { detectionZone = zone; maximumCrossingSpeed = configuredMaximumSpeed; ResetInteraction(); }
        public void ResetInteraction()
        {
            Completed = AttemptActive = hasPrevious = false;
            AttemptCount = FailureCount = 0; CrossingSeconds = 0;
            PausedSeconds = pausedFor = 0; crossedTimber = false;
            Status = "Approach the log from its entry side and walk forward slowly without stopping.";
        }
        public bool Evaluate(Vector3 position, float signedSpeed, bool inputReady, float dt)
        {
            if (Completed) return false;
            if (!detectionZone || !CourseZoneUtility.Finite(position))
            { Fail("Log zone setup is missing or position is invalid."); hasPrevious = false; return false; }
            Vector3 before = previousPosition;
            bool hadPrevious = hasPrevious;
            bool validFrame = CourseZoneUtility.Finite(dt) && dt > 0 && dt <= Mathf.Max(.05f, maximumSampleGap)
                && CourseZoneUtility.Finite(signedSpeed) && (!hadPrevious || Vector3.Distance(before, position) <= Mathf.Max(.1f, maximumSampleDistance));
            Vector3 a = CourseZoneUtility.LocalPoint(detectionZone, before), b = CourseZoneUtility.LocalPoint(detectionZone, position);
            bool inCorridor = Mathf.Abs(b.x) <= Mathf.Max(.1f, corridorHalfWidth);
            bool briefPause = !inputReady || signedSpeed >= 0 && signedSpeed < Mathf.Max(.01f, minimumCrossingSpeed);
            if (AttemptActive && validFrame && briefPause && inCorridor
                && Vector3.Distance(before, position) <= Mathf.Max(0, maximumPauseMovement) && b.z >= a.z - .02f)
            {
                pausedFor += dt; PausedSeconds += dt;
                if (pausedFor <= Mathf.Max(0, trackingPauseToleranceSeconds)
                    && PausedSeconds <= Mathf.Max(0, maximumTotalPauseSeconds))
                { Status = "Log paused briefly: restore tracking and slow forward movement."; return false; }
                Fail("Log pause exceeded the continuous or total tolerance. Return before the entry and retry.");
                previousPosition = position; hasPrevious = true; return false;
            }
            previousPosition = position; hasPrevious = true;
            bool slowForward = signedSpeed >= Mathf.Max(.01f, minimumCrossingSpeed)
                && signedSpeed <= Mathf.Max(minimumCrossingSpeed, maximumCrossingSpeed);
            if (!validFrame || !inputReady || !slowForward)
            { Fail(!validFrame ? "Tracking/frame gap: return before the log entry and retry." : "Walk slowly forward without stopping, reversing or running. Return before the log entry to retry."); return false; }
            if (!inCorridor)
            { Fail("Missed the timber: use the narrow marked crossing corridor through the log centre."); return false; }
            pausedFor = 0;
            if (!hadPrevious) return false;
            if (!AttemptActive)
            {
                if (!CourseZoneUtility.CrossesFace(detectionZone, before, position, false)) return false;
                AttemptActive = true; AttemptCount++; CrossingSeconds = 0;
                PausedSeconds = 0; crossedTimber = false;
                Status = "Log attempt: keep a steady slow forward walk; all four legs follow the gait.";
            }
            if (b.z < a.z - .02f)
            { Fail("Course progress stopped or reversed. Return before the log entry and retry."); return false; }
            CrossingSeconds += dt;
            crossedTimber |= CrossesTimber(before, position);
            if (CourseZoneUtility.CrossesFace(detectionZone, before, position, true))
            {
                if (!crossedTimber)
                { Fail("The route did not cross the actual timber centreline. Return before the entry and retry."); return false; }
                Completed = true; AttemptActive = false;
                Status = "Log crossed in " + CrossingSeconds.ToString("0.0") + " s — symbolic gait traversal, no physical stepping.";
                return true;
            }
            if (!CourseZoneUtility.Contains(detectionZone, position))
                Fail("Exited the log zone through the entry/side. Return before the entry to retry.");
            return false;
        }
        bool CrossesTimber(Vector3 before, Vector3 after)
        {
            Transform timber = logTransform ? logTransform : detectionZone.transform;
            Vector3 a = timber.InverseTransformPoint(before), b = timber.InverseTransformPoint(after);
            if (a.z > 0 || b.z < 0 || b.z <= a.z) return false;
            float along = Mathf.Lerp(a.x, b.x, -a.z / (b.z - a.z));
            return Mathf.Abs(along) <= Mathf.Max(.1f, logHalfLength);
        }
        void Fail(string reason)
        {
            if (AttemptActive) FailureCount++;
            AttemptActive = false; Status = reason;
        }
    }
}
