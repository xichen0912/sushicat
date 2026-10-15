using System;
using UnityEngine;

namespace StudentWork
{
    // Water interaction is now a single deliberate closed-fist hold.  The legacy
    // phases remain in the enum so old serialized data can be read safely; live
    // camera samples use Neutral -> FistHold -> Complete.
    public enum CupGesturePhase { Neutral, FistHold, Complete, Lower, Raise, ReturnNeutral }

    [Serializable]
    public class CupGestureSettings
    {
        public float lowThreshold = -.22f;
        public float highThreshold = .28f;
        public float neutralBand = .12f;
        public float lowHoldSeconds = .5f;
        public float highHoldSeconds = .5f;
        public float neutralHoldSeconds = .3f;
        public float minimumCupScore = .45f;
        public float maximumCupAgeSeconds = .7f;
        public float maximumSampleGapSeconds = .25f;
        public float transitionTimeoutSeconds = 10f;
        [Range(.05f, 1)] public float minimumFistConfidence = .55f;
        [Min(.05f)] public float fistHoldSeconds = .45f;
        [Min(0)] public float transientSignalGraceSeconds = .12f;
    }

    public struct CupGestureSample
    {
        public float dt;
        public bool contextReady;
        public bool poseReady;
        // Kept for old result readers and deterministic validators. Live water
        // interaction ignores cupDetected and uses fistAvailable/fistClosed.
        public bool cupDetected;
        public string cupLabel;
        public float cupScore;
        public float cupAgeSeconds;
        public float rightHand;
        public bool fistAvailable;
        public bool fistClosed;
        public float fistConfidence;
    }

    // Pure state: completion describes a recognized closed-fist water interaction,
    // never liquid transfer. A cup-only sample is accepted solely for old recorded
    // validation data and is never produced by the live component.
    public sealed class CupGestureState
    {
        public CupGestureSettings Settings { get; }
        public CupGesturePhase Phase { get; private set; }
        public int CompletedCount { get; private set; }
        public float HoldSeconds { get; private set; }
        public string Status { get; private set; }
        public bool FistReady { get; private set; }
        public bool SemanticCupReady => FistReady;
        float phaseSeconds;
        float transientSignalSeconds;
        bool handOpenedSinceReset;

        public CupGestureState(CupGestureSettings settings = null)
        {
            Settings = settings ?? new CupGestureSettings();
            Reset();
        }

        public void Reset()
        {
            CompletedCount = 0;
            CancelSequence("Open hand, then clench the calibrated right fist to begin.");
        }

        public void CancelSequence(string reason = "Gesture reset. Open your hand, then clench to begin again.")
        {
            Phase = CupGesturePhase.Neutral;
            HoldSeconds = phaseSeconds = transientSignalSeconds = 0;
            FistReady = false;
            handOpenedSinceReset = false;
            Status = reason;
        }

        public bool Step(CupGestureSample sample)
        {
            float maximumGap = Mathf.Max(.05f, Settings.maximumSampleGapSeconds);
            if (!Finite(sample.dt) || sample.dt <= 0)
            { CancelSequence("Invalid frame timing: open your hand, then clench and restart the interaction."); return false; }
            if (sample.dt > maximumGap)
            {
                float overrun = sample.dt - maximumGap;
                if (Phase == CupGesturePhase.FistHold && HoldTransient(overrun,
                    "Brief frame gap: keep the right fist clenched to continue.")) return false;
                CancelSequence("Frame gap: open your hand, then clench and restart the interaction."); return false;
            }
            if (!sample.contextReady)
            { CancelSequence("Keep the elephant stationary in the water zone or P3 practice."); return false; }
            if (!sample.poseReady || !Finite(sample.rightHand))
            {
                if (Phase == CupGesturePhase.FistHold && HoldTransient(sample.dt,
                    "Right hand briefly lost: keep the fist clenched to continue.")) return false;
                CancelSequence("Waiting for calibrated, visible P3 right hand."); return false;
            }

            // Backward-compatible path for old edit-mode evidence only. The live
            // component deliberately sends cupDetected=false, so it cannot bypass
            // the fist requirement when the fist model is unavailable.
            if (!sample.fistAvailable)
                return sample.cupDetected ? StepLegacyCup(sample) : FailNoFist(sample.dt);
            FistReady = Finite(sample.fistConfidence)
                && sample.fistConfidence >= Mathf.Clamp01(Settings.minimumFistConfidence)
                && sample.fistConfidence <= 1;
            if (!FistReady)
            {
                if (Phase == CupGesturePhase.FistHold && HoldTransient(sample.dt,
                    "Fist signal briefly lost: keep the hand clenched to continue.")) return false;
                CancelSequence("Waiting for a fresh, confident right-fist signal."); return false;
            }
            if (Phase == CupGesturePhase.Complete && !sample.fistClosed)
            { RearmAfterOpen("Hand opened. Clench the right fist to try another water interaction."); return false; }
            if (Phase == CupGesturePhase.Complete)
            { Status = "Water interaction complete. Open your hand before trying again."; return false; }
            if ((Phase == CupGesturePhase.FistHold) && phaseSeconds > Mathf.Max(1, Settings.transitionTimeoutSeconds))
            { CancelSequence("Fist hold timed out. Open your hand and begin again."); return false; }
            if (!sample.fistClosed)
            {
                RearmAfterOpen("Open hand detected. Clench the right fist to interact with the water.");
                return false;
            }
            if (!handOpenedSinceReset)
            {
                CancelSequence("Open your hand first, then clench the right fist to interact with the water.");
                return false;
            }

            transientSignalSeconds = 0;
            phaseSeconds += sample.dt;
            if (Phase == CupGesturePhase.Neutral)
                MoveTo(CupGesturePhase.FistHold, "Right fist recognized: keep it clenched to interact with the water.");
            if (Phase == CupGesturePhase.FistHold)
            {
                Status = "Hold right fist: " + HoldSeconds.ToString("0.0") + " / " + Settings.fistHoldSeconds.ToString("0.0") + " s";
                HoldSeconds += sample.dt;
                if (HoldSeconds + .00001f >= Mathf.Max(.05f, Settings.fistHoldSeconds))
                {
                    CompletedCount++;
                    MoveTo(CupGesturePhase.Complete, "Water interaction complete — symbolic water use, no liquid simulation.");
                    return true;
                }
            }
            return false;
        }

        bool FailNoFist(float dt)
        {
            if (Phase == CupGesturePhase.FistHold && HoldTransient(dt,
                "Fist signal briefly lost: keep the hand clenched to continue.")) return false;
            CancelSequence("Fist detector unavailable. Keep the official gesture model enabled and clench the right fist.");
            return false;
        }

        bool HoldTransient(float lossSeconds, string status)
        {
            float grace = Mathf.Max(0, Settings.transientSignalGraceSeconds);
            transientSignalSeconds += Mathf.Max(0, Finite(lossSeconds) ? lossSeconds : float.PositiveInfinity);
            if (transientSignalSeconds <= grace + .00001f)
            {
                FistReady = false;
                Status = status + " (" + transientSignalSeconds.ToString("0.00") + " / " + grace.ToString("0.00") + " s)";
                return true;
            }
            CancelSequence("Fist signal was lost too long. Open your hand and begin again.");
            return false;
        }

        void RearmAfterOpen(string status)
        {
            CancelSequence(status);
            handOpenedSinceReset = true;
        }

        bool StepLegacyCup(CupGestureSample sample)
        {
            FistReady = false;
            if (!string.Equals((sample.cupLabel ?? "").Trim(), "cup", StringComparison.OrdinalIgnoreCase)
                || !Finite(sample.cupScore) || sample.cupScore < Mathf.Clamp01(Settings.minimumCupScore)
                || sample.cupScore > 1 || !Finite(sample.cupAgeSeconds) || sample.cupAgeSeconds < 0
                || sample.cupAgeSeconds > Mathf.Max(0, Settings.maximumCupAgeSeconds))
            { CancelSequence("Legacy cup evidence is stale; use the fist interaction in live mode."); return false; }
            if (Phase == CupGesturePhase.Complete) return false;
            phaseSeconds += sample.dt;
            float neutral = Mathf.Clamp(Settings.neutralBand, .01f, .2f);
            float low = Mathf.Min(-neutral - .01f, Settings.lowThreshold);
            float high = Mathf.Max(neutral + .01f, Settings.highThreshold);
            switch (Phase)
            {
                case CupGesturePhase.Neutral:
                    Status = "Legacy cup evidence: hold neutral.";
                    if (Hold(Mathf.Abs(sample.rightHand) <= neutral, sample.dt, Settings.neutralHoldSeconds))
                        MoveTo(CupGesturePhase.Lower, "Legacy cup evidence: lower and hold.");
                    break;
                case CupGesturePhase.Lower:
                    if (Hold(sample.rightHand <= low, sample.dt, Settings.lowHoldSeconds))
                        MoveTo(CupGesturePhase.Raise, "Legacy cup evidence: raise and hold.");
                    break;
                case CupGesturePhase.Raise:
                    if (Hold(sample.rightHand >= high, sample.dt, Settings.highHoldSeconds))
                        MoveTo(CupGesturePhase.ReturnNeutral, "Legacy cup evidence: return neutral.");
                    break;
                case CupGesturePhase.ReturnNeutral:
                    if (Hold(Mathf.Abs(sample.rightHand) <= neutral, sample.dt, Settings.neutralHoldSeconds))
                    {
                        CompletedCount++;
                        MoveTo(CupGesturePhase.Complete, "Legacy cup interaction complete.");
                        return true;
                    }
                    break;
            }
            return false;
        }

        bool Hold(bool accepted, float dt, float required)
        {
            HoldSeconds = accepted ? HoldSeconds + dt : 0;
            return HoldSeconds + .00001f >= Mathf.Max(.05f, required);
        }

        void MoveTo(CupGesturePhase next, string status)
        {
            Phase = next;
            HoldSeconds = phaseSeconds = transientSignalSeconds = 0;
            Status = status;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
