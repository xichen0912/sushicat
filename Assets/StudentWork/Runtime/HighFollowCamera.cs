using DigiPhant;
using UnityEngine;

namespace StudentWork
{
    // Play-only view above the acacia canopies (about 5 units tall), so trees between the
    // camera and the elephant no longer hide it. Press V (or the on-screen button) to cycle
    // High follow -> Top-down -> Original. Original leaves the supplied fixed-offset follow
    // camera in DigiPhantLocomotion untouched. Runs after the controller has moved the
    // elephant and the supplied camera for this frame.
    [DefaultExecutionOrder(600)]
    public sealed class HighFollowCamera : MonoBehaviour
    {
        public enum View { HighFollow, TopDown, Original }

        public View view = View.HighFollow;
        // Behind and above the elephant: 13 back and 8 up (above the 3–6 m roofs), looking about 23° down.
        [Min(1)] public float highHeight = 8;
        [Min(0)] public float highDistance = 13;
        [Min(1)] public float topDownHeight = 24;
        [Tooltip("Height on the elephant the camera looks at.")]
        [Min(0)] public float lookHeight = 2.5f;
        [Tooltip("How quickly the high view turns to stay behind the elephant; 0 keeps a fixed world direction.")]
        [Range(0, 10)] public float headingFollow = 2.5f;
        [Range(1, 30)] public float positionFollow = 8;

        DigiPhantLocomotion locomotion;
        float yaw;
        bool placed;
        // Our own smoothed pose. DigiPhantLocomotion moves the camera back to its fixed offset
        // every travelled frame, so smoothing must never start from the camera's current pose.
        Vector3 viewPosition;
        Quaternion viewRotation;
        // Set by the city's finish celebration: a slow orbit keeping the dancing elephant, the skyline and the
        // fireworks in frame. The normal view resumes when it is cleared.
        public static bool Orbit;
        bool orbiting;
        float orbitYaw, savedFov;
        // Orbit framing: 10 m out and 6.5 m up (above the 3–6 m row-house roofs, so they do not block the view),
        // always facing the elephant and tilted only 3° down — the elephant sits in the lower middle of the frame
        // and the sky above the skyline, where the fireworks burst, fills the top.
        const float OrbitRadius = 10, OrbitHeight = 6.5f, OrbitPitch = 3, OrbitDegreesPerSecond = 12, OrbitFov = 66;
        // The supplied view: a fixed offset from the elephant and a fixed rotation.
        Vector3 originalOffset;
        Quaternion originalRotation;
        bool hasOriginal;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AttachToSceneLocomotion()
        {
            foreach (var found in FindObjectsByType<DigiPhantLocomotion>(FindObjectsSortMode.None))
                if (found.followCamera && !found.GetComponent<HighFollowCamera>())
                    found.gameObject.AddComponent<HighFollowCamera>();
        }

        void OnEnable()
        {
            locomotion = GetComponent<DigiPhantLocomotion>();
            placed = false;
            var camera = locomotion ? locomotion.followCamera : null;
            if (camera && locomotion.travelRoot && !hasOriginal)
            {
                originalOffset = camera.transform.position - locomotion.travelRoot.position;
                originalRotation = camera.transform.rotation;
                hasOriginal = true;
            }
        }

        void LateUpdate()
        {
            if (Input.GetKeyDown(KeyCode.V)) NextView();
            var camera = locomotion ? locomotion.followCamera : null;
            var target = locomotion ? locomotion.travelRoot : null;
            if (!camera || !target) { placed = false; return; }
            if (Orbit)
            {
                if (!orbiting)
                {
                    orbiting = true;
                    orbitYaw = placed ? viewRotation.eulerAngles.y : target.eulerAngles.y;
                    if (!placed) { viewPosition = camera.transform.position; viewRotation = camera.transform.rotation; }
                    savedFov = camera.fieldOfView;
                }
                camera.fieldOfView = Mathf.Lerp(camera.fieldOfView, OrbitFov, 1 - Mathf.Exp(-2f * Time.unscaledDeltaTime));
                Vector3 body = target.position + Vector3.up * 2f;
                orbitYaw += OrbitDegreesPerSecond * Time.unscaledDeltaTime;
                Vector3 want = new Vector3(body.x, OrbitHeight, body.z) + Quaternion.Euler(0, orbitYaw, 0) * new Vector3(0, 0, -OrbitRadius);
                Vector3 toElephant = body - want; toElephant.y = 0;
                Quaternion look = Quaternion.LookRotation(toElephant, Vector3.up) * Quaternion.Euler(OrbitPitch, 0, 0);
                float k = 1 - Mathf.Exp(-2f * Time.unscaledDeltaTime);
                viewPosition = Vector3.Lerp(viewPosition, want, k);
                viewRotation = Quaternion.Slerp(viewRotation, look, k);
                camera.transform.SetPositionAndRotation(viewPosition, viewRotation);
                placed = true;
                return;
            }
            if (orbiting) { orbiting = false; camera.fieldOfView = savedFov; }
            if (view == View.Original)
            {
                // Hand the camera back to the supplied fixed-offset follow exactly as it was.
                if (placed && hasOriginal)
                    camera.transform.SetPositionAndRotation(target.position + originalOffset, originalRotation);
                placed = false;
                return;
            }

            Vector3 focus = target.position + Vector3.up * lookHeight;
            float targetYaw = target.eulerAngles.y;
            float dt = Time.unscaledDeltaTime;
            // Snap on the first frame and after a reset/teleport instead of sweeping across the course.
            bool snap = !placed || (viewPosition - focus).sqrMagnitude > 60 * 60;
            yaw = snap || headingFollow <= 0 ? targetYaw
                : Mathf.LerpAngle(yaw, targetYaw, 1 - Mathf.Exp(-headingFollow * dt));

            Vector3 desired;
            Quaternion rotation;
            if (view == View.TopDown)
            {
                desired = focus + Vector3.up * topDownHeight;
                rotation = Quaternion.Euler(90, yaw, 0); // screen-up is the elephant's forward
            }
            else
            {
                desired = focus + Quaternion.Euler(0, yaw, 0) * new Vector3(0, highHeight, -highDistance);
                rotation = Quaternion.LookRotation(focus - desired, Vector3.up);
            }
            float blend = snap ? 1 : 1 - Mathf.Exp(-positionFollow * dt);
            viewPosition = snap ? desired : Vector3.Lerp(viewPosition, desired, blend);
            viewRotation = snap ? rotation : Quaternion.Slerp(viewRotation, rotation, blend);
            camera.transform.SetPositionAndRotation(viewPosition, viewRotation);
            placed = true;
        }

        void NextView()
        {
            view = (View)(((int)view + 1) % 3);
            // Keep "placed" so leaving a custom view restores the original camera once.
        }

        void OnGUI()
        {
            string label = view == View.HighFollow ? "High follow" : view == View.TopDown ? "Top down" : "Original";
            if (GUI.Button(new Rect(Screen.width * .5f - 110, 10, 220, 28), "View: " + label + " (V to switch)")) NextView();
        }
    }
}
