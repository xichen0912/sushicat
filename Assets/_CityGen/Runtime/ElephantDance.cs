using DigiPhant;
using UnityEngine;

namespace CityGen
{
    // A looping, fully procedural celebration dance (the elephant rig has no dance clip). It is layered on the
    // bones after DigiPhantController has posed them this frame, so nothing accumulates. The finish only counts
    // once the elephant has left the far side of the finish zone, so it first crouches and leaps back to the
    // zone's centre (spinning once in the air), then dances there. With leapToStart (default) it instead launches
    // all the way back to its starting position and dances there, ignoring performer input; End() restores
    // everything.
    [DefaultExecutionOrder(560)]
    public sealed class ElephantDance : MonoBehaviour
    {
        public DigiPhantController controller;
        public DigiPhantLocomotion locomotion;
        [Range(60, 160)] public float bpm = 112;
        [Tooltip("Trunk raised pose per bone (base → tip), the same lift used for the water spray.")]
        public float[] trunkRaise = { -45, -35, -25, -15, -10, -5, 0 };
        [Tooltip("Jazz suit put on for the dance (built by CityGen).")]
        public ElephantSuit suit;
        [Tooltip("Casual outfit worn from the start of Play until the dance (built by CityGen > Dress Elephant (casual start outfit)).")]
        public ElephantSuit startOutfit;
        [Tooltip("Optional music for the dance: loops while dancing (2D, fades in and out). Set bpm to its tempo so the moves land on the beat.")]
        public AudioClip music;
        [Range(0, 1)] public float musicVolume = .8f;
        [Tooltip("The finish zone; the elephant leaps to its centre before dancing (set at runtime by CityCelebration).")]
        public BoxCollider finishZone;
        [Tooltip("Seconds for the crouch, leap and landing.")]
        public float leapSeconds = 1.4f;
        [Tooltip("Farther than this from the finish centre (e.g. an F preview elsewhere), the elephant dances in place.")]
        public float maxLeapDistance = 14;
        [Tooltip("Launch back to the elephant's starting position (where it stood when Play began) and dance there, "
            + "instead of leaping to the finish centre.")]
        public bool leapToStart = true;
        [Tooltip("Extra leap seconds per unit of distance, so a long launch back to the start is not too abrupt.")]
        public float leapSecondsPerUnit = .04f;

        public bool Dancing { get; private set; }
        // Current beat count (for lights that pulse with the dance); 0 when not dancing.
        public float Beat => Dancing ? Mathf.Max(0, Time.time - started) * bpm / 60f : 0;
        Transform root, motionRoot;
        Vector3 anchorPosition, motionRest, leapFrom;
        Quaternion anchorRotation, leapFromRotation, homeRotation;
        Vector3 homePosition;
        bool hasHome;
        float leapDuration = 1.4f;
        bool savedLocomotion;
        float started, leapHeight;
        AudioSource musicSource;
        BoneControl head, trunk, leftEar, rightEar, tail, frontLeft, frontRight, rearLeft, rearRight;

        // Remember the start before anyone drives, so the dance can launch back to it.
        void Start()
        {
            if (locomotion && locomotion.travelRoot && !hasHome)
            { homePosition = locomotion.travelRoot.position; homeRotation = locomotion.travelRoot.rotation; hasHome = true; }
            if (startOutfit && !Dancing) startOutfit.Wear(true);
        }

        public void Begin()
        {
            if (Dancing || !locomotion || !locomotion.travelRoot) return;
            root = locomotion.travelRoot;
            motionRoot = locomotion.animationMotionRoot;
            anchorPosition = root.position; anchorRotation = root.rotation;
            if (motionRoot) motionRest = motionRoot.localPosition;
            savedLocomotion = locomotion.enableLocomotion;
            locomotion.StopMotion();
            locomotion.enableLocomotion = false; // the dance owns the body until the course restarts
            foreach (var c in controller ? controller.controls : new BoneControl[0])
            {
                if (c == null) continue;
                switch (c.label)
                {
                    case "Head turn": head = c; break;
                    case "Trunk curl": trunk = c; break;
                    case "Left ear": leftEar = c; break;
                    case "Right ear": rightEar = c; break;
                    case "Tail sway": tail = c; break;
                    case "Front left leg": frontLeft = c; break;
                    case "Front right leg": frontRight = c; break;
                    case "Rear left leg": rearLeft = c; break;
                    case "Rear right leg": rearRight = c; break;
                }
            }
            started = Time.time;
            leapFrom = anchorPosition; leapFromRotation = anchorRotation;
            leapDuration = leapSeconds;
            if (leapToStart && hasHome)
            {
                Vector3 home = homePosition; home.y = anchorPosition.y;
                Vector3 offset = home - anchorPosition; offset.y = 0;
                if (offset.magnitude > .3f || Quaternion.Angle(anchorRotation, homeRotation) > 1)
                {
                    anchorPosition = home; anchorRotation = homeRotation;    // dance on the starting spot
                    leapHeight = 2.6f + offset.magnitude * .15f;
                    leapDuration = leapSeconds + offset.magnitude * Mathf.Max(0, leapSecondsPerUnit);
                    started = Time.time + leapDuration;                    // the dance's beat 0 is the landing
                }
            }
            else if (finishZone)
            {
                Vector3 centre = finishZone.bounds.center; centre.y = anchorPosition.y;
                Vector3 offset = centre - anchorPosition; offset.y = 0;
                if (offset.magnitude > .3f && offset.magnitude < maxLeapDistance)
                {
                    anchorPosition = centre;               // dance on the middle of the finish zone
                    leapHeight = 2.6f + offset.magnitude * .15f;
                    started = Time.time + leapDuration;     // the dance's beat 0 is the landing
                }
            }
            Dancing = true;
            if (startOutfit) startOutfit.Wear(false); // change out of the casual outfit into the jazz suit
            if (suit) suit.Wear(true);
            if (music)
            {
                if (!musicSource)
                {
                    musicSource = gameObject.AddComponent<AudioSource>();
                    musicSource.playOnAwake = false;
                    musicSource.loop = true;
                    musicSource.spatialBlend = 0;
                }
                musicSource.clip = music;
                musicSource.volume = 0;
                musicSource.Play();
            }
        }

        public void End()
        {
            if (!Dancing) return;
            bool leaping = Time.time < started;
            Dancing = false;
            if (suit) suit.Wear(false);
            if (startOutfit) startOutfit.Wear(true);
            // Settle back onto the finish spot only if the elephant is still there; after "Restart course" it has
            // already been returned to the start and must stay there.
            Vector3 offset = root ? root.position - anchorPosition : Vector3.zero; offset.y = 0;
            // Stopped mid-leap: land at once (unless the course already moved it back to the start).
            if (root && (offset.magnitude < 1f || leaping && (leapToStart || offset.magnitude < maxLeapDistance + 2)))
                root.SetPositionAndRotation(anchorPosition, anchorRotation);
            if (motionRoot) motionRoot.localPosition = motionRest;
            if (locomotion) locomotion.enableLocomotion = savedLocomotion;
        }

        void OnDisable() => End();

        // A 20-beat routine, the big moves first:
        //   0–2 standing spin · 2–6 sit-down spin (floor) · 6–10 barrel roll on the ground (floor) ·
        //   10–14 rear-up freeze · 14–18 trumpet · 18–20 short groove, then again from the spin.
        const float Length = 20;
        const float BodyCentre = 1.4f; // spins and rolls turn around the body, not around the feet

        void Update()
        {
            if (!musicSource) return;
            musicSource.volume = Mathf.MoveTowards(musicSource.volume, Dancing ? musicVolume : 0, Time.deltaTime * musicVolume / 1.5f);
            if (!Dancing && musicSource.volume <= 0 && musicSource.isPlaying) musicSource.Stop();
        }

        void LateUpdate()
        {
            if (!Dancing || !root) return;
            if (Time.time < started) { Leap(1 - (started - Time.time) / Mathf.Max(.1f, leapDuration)); return; }
            float beat = (Time.time - started) * bpm / 60f;
            float phrase = Mathf.Repeat(beat, Length);
            float pulse = Mathf.Abs(Mathf.Sin(beat * Mathf.PI));          // 0 → 1 → 0 on every beat
            float side = Mathf.Sin(beat * Mathf.PI * .5f);                 // left / right every two beats

            float sitSpin = Window(phrase, 2, 6), roll = Window(phrase, 6, 10), freeze = Window(phrase, 10, 14);
            float floor = Mathf.Max(sitSpin, Mathf.Max(roll, freeze));
            float groove = 1 - floor;

            // Whole body.
            float yaw = side * 14 * groove;
            if (phrase < 2) yaw += Mathf.SmoothStep(0, 360, phrase / 2);
            if (phrase >= 2.5f && phrase < 5.5f) yaw += Mathf.SmoothStep(0, 720, (phrase - 2.5f) / 3);         // two turns sitting
            float pitch = -28 * sitSpin + 32 * freeze;                                                           // sit back / rear up
            float rollAngle = phrase >= 6.5f && phrase < 9.5f ? Mathf.SmoothStep(0, 360, (phrase - 6.5f) / 3) : 0;
            float lower = .45f * sitSpin + .55f * roll * Mathf.Sin(Mathf.Clamp01((phrase - 6) / 4) * Mathf.PI);
            float hop = pulse * .2f * groove + pulse * .06f * freeze;
            var rotation = anchorRotation * Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(pitch, 0, side * 5 * groove + rollAngle);
            Vector3 centre = anchorPosition + Vector3.up * (BodyCentre + hop - lower);
            root.SetPositionAndRotation(centre - rotation * Vector3.up * BodyCentre, rotation);

            // Legs: stomps in the groove, kicks while sitting, tucked in the roll, front legs high in the freeze.
            bool even = Mathf.FloorToInt(beat) % 2 == 0;
            float kick = Mathf.Abs(Mathf.Sin(beat * Mathf.PI * 2)) * 45 * sitSpin;
            Rotate(frontLeft, (even ? pulse * 28 : 0) * groove + (even ? kick : 0) + 40 * roll + 55 * freeze);
            Rotate(frontRight, (even ? 0 : pulse * 28) * groove + (even ? 0 : kick) + 40 * roll + 55 * freeze);
            Rotate(rearLeft, (even ? 0 : pulse * 10) * groove + 40 * roll);
            Rotate(rearRight, (even ? pulse * 10 : 0) * groove + 40 * roll);

            Rotate(head, side * 16 * (1 - roll));
            float flap = Mathf.Sin(beat * Mathf.PI * 2) * 28;
            Rotate(leftEar, flap);
            Rotate(rightEar, flap);
            Rotate(tail, Mathf.Sin(beat * Mathf.PI * 2) * 35);

            // Trunk: swings in the groove, lifts high to trumpet (beats 14–18) and in the freeze.
            PoseTrunk(beat, Mathf.Max(Window(phrase, 14, 18), freeze));
        }

        // Crouch (0–0.2), leap to the dance spot (start or finish centre) with one spin in the air (0.2–0.82), soft landing (0.82–1).
        void Leap(float p)
        {
            p = Mathf.Clamp01(p);
            float crouch = Mathf.SmoothStep(0, 1, p / .16f) * (1 - Mathf.SmoothStep(0, 1, (p - .16f) / .06f));
            float s = Mathf.Clamp01((p - .2f) / .62f);
            float tuck = Mathf.Sin(s * Mathf.PI);
            float land = p > .82f ? Mathf.Sin(Mathf.Clamp01((p - .82f) / .18f) * Mathf.PI) : 0;

            Vector3 ground = Vector3.Lerp(leapFrom, anchorPosition, Mathf.SmoothStep(0, 1, s));
            float height = 4 * leapHeight * s * (1 - s);
            float yaw = Mathf.SmoothStep(0, 360, s);
            float pitch = -10 * crouch + Mathf.Lerp(-18, 14, s) * tuck;   // nose up on take-off, down to land
            var heading = Quaternion.Slerp(leapFromRotation, anchorRotation, Mathf.SmoothStep(0, 1, s));
            var rotation = heading * Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(pitch, 0, 0);
            Vector3 centre = ground + Vector3.up * (BodyCentre + height - .4f * crouch - .3f * land);
            root.SetPositionAndRotation(centre - rotation * Vector3.up * BodyCentre, rotation);

            Rotate(frontLeft, 30 * crouch + 45 * tuck + 20 * land);
            Rotate(frontRight, 30 * crouch + 45 * tuck + 20 * land);
            Rotate(rearLeft, 20 * crouch + 35 * tuck + 15 * land);
            Rotate(rearRight, 20 * crouch + 35 * tuck + 15 * land);
            float flap = Mathf.Sin(Time.time * 24) * 35 * tuck;
            Rotate(leftEar, flap);
            Rotate(rightEar, flap);
            Rotate(tail, Mathf.Sin(Time.time * 18) * 40 * tuck);
            PoseTrunk(0, Mathf.Max(tuck, crouch));
        }

        void PoseTrunk(float beat, float lift)
        {
            if (trunk == null || trunk.bones == null) return;
            Vector3 axis = trunk.localAxis.sqrMagnitude > .0001f ? trunk.localAxis.normalized : Vector3.forward;
            for (int i = 0; i < trunk.bones.Length; i++)
            {
                if (!trunk.bones[i]) continue;
                float swing = Mathf.Sin(beat * Mathf.PI - i * .5f) * 9;
                float raise = i < trunkRaise.Length ? trunkRaise[i] : 0;
                trunk.bones[i].localRotation *= Quaternion.AngleAxis(Mathf.Lerp(swing, raise, lift), axis);
            }
        }

        // 0 outside [from, to], easing to 1 within half a beat of each end.
        static float Window(float phrase, float from, float to)
        {
            if (phrase < from || phrase >= to) return 0;
            return Mathf.SmoothStep(0, 1, Mathf.Min(1, (phrase - from) * 2)) * Mathf.SmoothStep(0, 1, Mathf.Min(1, (to - phrase) * 2));
        }

        static void Rotate(BoneControl control, float degrees)
        {
            if (control == null || control.bones == null) return;
            Vector3 axis = control.localAxis.sqrMagnitude > .0001f ? control.localAxis.normalized : Vector3.forward;
            foreach (var bone in control.bones)
                if (bone) bone.localRotation *= Quaternion.AngleAxis(degrees, axis);
        }
    }
}
