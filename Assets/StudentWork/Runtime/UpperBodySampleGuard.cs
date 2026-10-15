using UnityEngine;

namespace DigiPhant
{
    // A rejected jump pauses the channel; an old value is never substituted as a command.
    public sealed class UpperBodySampleGuard
    {
        bool seeded, pending;
        float accepted, observedAt, candidate;
        int candidateSamples;
        public void Reset() { seeded = pending = false; candidateSamples = 0; }
        public void Seed(float value, float now) { seeded = true; pending = false; accepted = value; observedAt = now; candidateSamples = 0; }
        public void MarkUnavailable() { pending = true; candidateSamples = 0; }
        public bool Observe(float value, float now, float tolerance, float speed, int requiredSamples)
        {
            if (!seeded) { Seed(value, now); return true; }
            float elapsed = Mathf.Clamp(now - observedAt, 0, .2f);
            observedAt = now;
            bool nearNeutral = Mathf.Abs(value) <= .2f;
            bool continuous = Mathf.Abs(value - accepted) <= tolerance + speed * elapsed;
            if (nearNeutral || !pending && continuous)
            { accepted = value; pending = false; candidateSamples = 0; return true; }
            if (!pending || candidateSamples == 0 || Mathf.Abs(value - candidate) > .15f)
            { candidate = value; candidateSamples = 1; pending = true; }
            else { candidate = value; candidateSamples++; }
            if (candidateSamples < Mathf.Max(2, requiredSamples)) return false;
            accepted = value; pending = false; candidateSamples = 0; return true;
        }
    }
}
