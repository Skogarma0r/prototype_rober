using UnityEngine;

namespace Ashline.Characters
{
    /// <summary>Third-person orbit camera over the shoulder; swings back behind the player while walking.</summary>
    [RequireComponent(typeof(Camera))]
    public class FootCamera : MonoBehaviour
    {
        public PlayerCharacter target;
        public float distance = 4.2f;
        public float pivotHeight = 1.55f;
        public float shoulder = 0.45f;
        public float fov = 60f;
        public LayerMask obstacles = ~0;

        /// <summary>Look input in degrees for this frame (x = yaw, y = pitch).</summary>
        [System.NonSerialized] public Vector2 look;

        float yaw, pitch = 12f, idleLook, currentDistance;
        Vector3 pivotVel;
        Vector3 pivot;
        Camera cam;

        void Awake() => cam = GetComponent<Camera>();

        public void Snap(float heading)
        {
            yaw = heading;
            pitch = 12f;
            if (target != null) pivot = target.transform.position + Vector3.up * pivotHeight;
            currentDistance = distance;
            Apply();
        }

        void LateUpdate()
        {
            if (target == null) return;
            float dt = Time.deltaTime;
            yaw += look.x;
            pitch = Mathf.Clamp(pitch - look.y, -25f, 65f);
            idleLook = look.sqrMagnitude > 0.01f ? 0f : idleLook + dt;
            look = Vector2.zero;

            // Recenter behind the player when walking and the player isn't steering the camera.
            var v = target.Velocity;
            v.y = 0;
            if (idleLook > 1.2f && v.magnitude > 1.2f)
            {
                float heading = Quaternion.LookRotation(v).eulerAngles.y;
                yaw = Mathf.LerpAngle(yaw, heading, 1f - Mathf.Exp(-1.6f * dt));
                pitch = Mathf.Lerp(pitch, 10f, 1f - Mathf.Exp(-1.2f * dt));
            }
            var goal = target.transform.position + Vector3.up * pivotHeight;
            pivot = Vector3.SmoothDamp(pivot, goal, ref pivotVel, 0.06f);
            Apply();
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov + Mathf.Clamp01(v.magnitude / 6f) * 6f, dt * 4f);
        }

        void Apply()
        {
            var rot = Quaternion.Euler(pitch, yaw, 0);
            var side = rot * Vector3.right * shoulder;
            var origin = pivot + side;
            var back = rot * Vector3.back;
            float want = distance;
            if (Physics.SphereCast(pivot, 0.25f, (origin + back * distance - pivot).normalized, out var hit, (origin + back * distance - pivot).magnitude, obstacles, QueryTriggerInteraction.Ignore)
                && hit.collider.gameObject != target.gameObject)
                want = Mathf.Max(0.8f, hit.distance - 0.2f);
            currentDistance = want < currentDistance ? want : Mathf.Lerp(currentDistance, want, 1f - Mathf.Exp(-4f * Time.deltaTime));
            transform.position = origin + back * currentDistance;
            transform.rotation = rot;
        }
    }
}
