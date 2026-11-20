using UnityEngine;

namespace Ashline.Vehicles
{
    /// <summary>Low racing chase camera: yaw lags behind travel direction, FOV opens up with speed.</summary>
    [RequireComponent(typeof(Camera))]
    public class ChaseCamera : MonoBehaviour
    {
        public CarController target;
        public float distance = 6.4f;
        public float height = 2.1f;
        public float lookHeight = 1.1f;
        public float yawLag = 3.2f;
        public float baseFov = 58f;
        public float speedFov = 10f;
        public LayerMask obstacles = ~0;

        Camera cam;
        float yaw;
        Vector3 velocity;
        float shake;

        void Awake() => cam = GetComponent<Camera>();

        public void Snap()
        {
            if (target == null) return;
            yaw = target.transform.eulerAngles.y;
            transform.position = Desired(out var look);
            transform.LookAt(look);
        }

        void LateUpdate()
        {
            if (target == null) return;
            float dt = Time.deltaTime;
            var rb = target.Body;
            var flatVel = Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up);

            // Follow the car's heading, but drift toward the velocity so slides are visible from behind.
            float heading = target.transform.eulerAngles.y;
            if (flatVel.magnitude > 4f && target.ForwardSpeed > 0f)
            {
                float velYaw = Quaternion.LookRotation(flatVel).eulerAngles.y;
                heading = Mathf.LerpAngle(heading, velYaw, 0.45f);
            }
            if (target.ForwardSpeed < -2f) heading = target.transform.eulerAngles.y;
            yaw = Mathf.LerpAngle(yaw, heading, 1f - Mathf.Exp(-yawLag * dt));

            var desired = Desired(out var look);
            // Track the car rigidly along its travel, smoothing only the sideways/vertical offset.
            var carVel = target.Body.linearVelocity;
            transform.position += carVel * dt;
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref velocity, 0.08f);

            float k = Mathf.Clamp01(rb.linearVelocity.magnitude / 55f);
            shake = Mathf.Lerp(shake, k * k * (target.input.nitro ? 0.06f : 0.025f), dt * 4f);
            var jitter = new Vector3(Mathf.PerlinNoise(Time.time * 23f, 0f) - 0.5f, Mathf.PerlinNoise(0f, Time.time * 23f) - 0.5f, 0f) * shake;
            transform.rotation = Quaternion.LookRotation(look - transform.position) * Quaternion.Euler(jitter.y * 20f, jitter.x * 20f, 0f);

            float fov = baseFov + speedFov * k + (target.input.nitro && target.Nitro > 0f ? 8f : 0f);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov, dt * 3f);
        }

        Vector3 Desired(out Vector3 look)
        {
            var pivot = target.transform.position + Vector3.up * lookHeight;
            var back = Quaternion.Euler(0f, yaw, 0f) * Vector3.back;
            float k = Mathf.Clamp01(target.Body.linearVelocity.magnitude / 55f);
            float dist = distance + k * 0.6f;
            var desired = pivot + back * dist + Vector3.up * (height - lookHeight);
            var dir = desired - pivot;
            if (Physics.SphereCast(pivot, 0.3f, dir.normalized, out var hit, dir.magnitude, obstacles, QueryTriggerInteraction.Ignore)
                && hit.rigidbody != target.Body)
                desired = pivot + dir.normalized * Mathf.Max(1.5f, hit.distance - 0.1f);
            look = pivot + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 3f;
            return desired;
        }
    }
}
