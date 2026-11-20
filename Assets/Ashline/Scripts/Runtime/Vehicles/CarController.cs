using UnityEngine;

namespace Ashline.Vehicles
{
    public struct CarInput
    {
        public float throttle;  // 0..1
        public float brake;     // 0..1, also reverse when nearly stopped
        public float steer;     // -1..1
        public bool handbrake;
        public bool nitro;
    }

    /// <summary>
    /// Raycast-suspension car: spring/damper per wheel, slip-based tire forces clamped to a friction
    /// circle, rear-wheel drive, handbrake drifts, nitro, downforce and anti-roll bars.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CarController : MonoBehaviour
    {
        public class Wheel
        {
            public Transform visual;
            public Transform knuckle;     // brake caliper: steers but does not spin
            public Quaternion baseRotation; // visual rotation relative to the body
            public Vector3 anchor;      // local suspension top
            public bool front, left;
            public float compression, lastCompression;
            public bool grounded;
            public Vector3 contact, contactNormal;
            public float spin, slip;
        }

        [Header("Suspension")]
        public float restLength = 0.32f;
        public float spring = 42000f;
        public float damper = 4200f;
        public float antiRoll = 16000f;
        [Tooltip("Tire forces act this far above the contact patch; higher means less body roll.")]
        public float rollCenter = 0.3f;
        public float wheelRadius = 0.34f;

        [Header("Engine")]
        public float engineForce = 9500f;
        public float topSpeed = 58f;
        public float reverseSpeed = 12f;
        public float brakeForce = 14000f;
        public float nitroBoost = 1.7f;
        public float nitroTopSpeed = 72f;
        public float nitroCapacity = 6f;

        [Header("Tires")]
        public float grip = 1.25f;
        public float lateralStiffness = 11000f;
        public float rearDriftGrip = 0.5f;
        public float downforce = 2.2f;

        [Header("Steering")]
        public float maxSteer = 34f;
        public float highSpeedSteer = 9f;
        public float steerSpeed = 4.5f;

        [System.NonSerialized] public CarInput input;
        [System.NonSerialized] public Wheel[] wheels = new Wheel[0];

        public Rigidbody Body { get; private set; }
        public float SpeedKmh => Body.linearVelocity.magnitude * 3.6f;
        public float ForwardSpeed { get; private set; }
        public float Nitro { get; private set; }
        public float SteerAngle { get; private set; }
        public float Rpm01 { get; private set; }
        public int Gear { get; private set; } = 1;
        public bool Drifting { get; private set; }
        public bool Grounded { get; private set; }

        /// <summary>Every active car, for AI awareness and the enter-vehicle prompt.</summary>
        public static readonly System.Collections.Generic.List<CarController> All = new System.Collections.Generic.List<CarController>();

        /// <summary>Someone (player or AI) is at the wheel; parked cars hold their handbrake.</summary>
        [System.NonSerialized] public bool occupied;

        float steer;
        float flippedTime;

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            Nitro = nitroCapacity;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        /// <summary>Pivots and optional steer-only knuckles in FL, FR, RL, RR order.</summary>
        public void SetupWheels(Transform[] pivots, Transform[] knuckles)
        {
            wheels = new Wheel[4];
            for (int i = 0; i < 4; i++)
            {
                var t = pivots[i];
                var local = transform.InverseTransformPoint(t.position);
                wheels[i] = new Wheel
                {
                    visual = t, knuckle = knuckles != null ? knuckles[i] : null,
                    baseRotation = Quaternion.Inverse(transform.rotation) * t.rotation,
                    anchor = local + Vector3.up * restLength, front = i < 2, left = i % 2 == 0,
                };
            }
        }

        void FixedUpdate()
        {
            if (wheels.Length != 4) return;
            float dt = Time.fixedDeltaTime;
            var up = transform.up;
            var vel = Body.linearVelocity;
            ForwardSpeed = Vector3.Dot(vel, transform.forward);
            float speed = vel.magnitude;

            float steerLimit = Mathf.Lerp(maxSteer, highSpeedSteer, Mathf.Clamp01(speed / 45f));
            steer = Mathf.MoveTowards(steer, input.steer, steerSpeed * dt);
            // Counter-steer assist: when the rear slides, let the fronts follow the travel direction.
            float slideAngle = speed > 3f ? Vector3.SignedAngle(transform.forward, Vector3.ProjectOnPlane(vel, up), up) : 0f;
            SteerAngle = Mathf.Clamp(steer * steerLimit + (Drifting ? slideAngle * 0.35f : 0f), -maxSteer, maxSteer);

            bool boosting = input.nitro && Nitro > 0f && input.throttle > 0.1f;
            Nitro = boosting ? Mathf.Max(0f, Nitro - dt) : Mathf.Min(nitroCapacity, Nitro + dt * 0.25f);

            float drive = 0f, brake = 0f;
            float limit = boosting ? nitroTopSpeed : topSpeed;
            if (input.throttle > 0.01f)
            {
                float k = Mathf.Clamp01(1f - ForwardSpeed / limit);
                drive = input.throttle * engineForce * Mathf.Sqrt(k) * (boosting ? nitroBoost : 1f);
            }
            if (input.brake > 0.01f)
            {
                if (ForwardSpeed > 1.5f) brake = input.brake * brakeForce;
                else drive -= input.brake * engineForce * 0.6f * Mathf.Clamp01(1f + ForwardSpeed / reverseSpeed);
            }

            // Hold the car still when idle so it does not creep on the suspension.
            if (input.throttle < 0.01f && input.brake < 0.01f && Mathf.Abs(ForwardSpeed) < 1.5f) brake = brakeForce * 0.25f;

            Grounded = false;
            int groundedRear = 0;
            float rearSlip = 0f;
            foreach (var w in wheels)
            {
                var origin = transform.TransformPoint(w.anchor);
                float maxDist = restLength + wheelRadius;
                w.lastCompression = w.compression;
                if (Physics.Raycast(origin, -up, out var hit, maxDist, ~0, QueryTriggerInteraction.Ignore) && hit.rigidbody != Body)
                {
                    w.grounded = true;
                    w.compression = maxDist - hit.distance;
                    w.contact = hit.point;
                    w.contactNormal = hit.normal;
                    Grounded = true;
                }
                else
                {
                    w.grounded = false;
                    w.compression = 0f;
                }
            }

            for (int a = 0; a < 2; a++)
            {
                var l = wheels[a * 2];
                var r = wheels[a * 2 + 1];
                float diff = (l.compression - r.compression) * antiRoll;
                if (l.grounded) Body.AddForceAtPosition(up * diff, l.contact);
                if (r.grounded) Body.AddForceAtPosition(-up * diff, r.contact);
            }

            foreach (var w in wheels)
            {
                if (!w.grounded) { w.slip = 0f; continue; }
                float vSusp = (w.compression - w.lastCompression) / dt;
                float load = Mathf.Max(0f, w.compression * spring + vSusp * damper);
                Body.AddForceAtPosition(up * load, w.contact);

                var rot = w.front ? Quaternion.AngleAxis(SteerAngle, up) : Quaternion.identity;
                var fwd = Vector3.ProjectOnPlane(rot * transform.forward, w.contactNormal).normalized;
                var right = Vector3.Cross(w.contactNormal, fwd);
                var pv = Body.GetPointVelocity(w.contact);
                float vLong = Vector3.Dot(pv, fwd);
                float vLat = Vector3.Dot(pv, right);

                bool hand = input.handbrake && !w.front;
                float mu = grip * (hand ? rearDriftGrip : 1f);
                float maxF = mu * load;

                float fLat = -vLat * lateralStiffness;
                // Below walking pace use a stronger static term so the car does not creep sideways on slopes.
                if (speed < 2f) fLat *= 2f;

                float fLong = 0f;
                if (!w.front) fLong += drive * 0.5f;
                float b = brake * (w.front ? 0.32f : 0.18f) + (hand ? brakeForce * 0.35f : 0f);
                fLong -= Mathf.Clamp(vLong * 4000f, -b, b);
                fLong -= vLong * 18f;

                var f = new Vector2(fLong, fLat);
                float mag = f.magnitude;
                w.slip = maxF > 1f ? Mathf.Clamp01(mag / maxF - 0.75f) * 4f : 0f;
                if (mag > maxF) f *= maxF / mag;
                Body.AddForceAtPosition(fwd * f.x + right * f.y, w.contact + up * rollCenter);

                if (!w.front) { groundedRear++; rearSlip += Mathf.Abs(vLat); }
                float wheelSpeed = w.front || drive == 0f ? vLong : vLong + (mag > maxF && !w.front ? Mathf.Sign(drive) * 6f : 0f);
                w.spin += wheelSpeed / wheelRadius * Mathf.Rad2Deg * dt;
            }

            Drifting = groundedRear > 0 && rearSlip / groundedRear > 3.2f && speed > 6f;

            if (Grounded)
                Body.AddForce(-up * downforce * speed * speed);

            UpdateGearbox(speed, limit);
            SelfRight(dt);
        }

        void UpdateGearbox(float speed, float limit)
        {
            float[] shifts = { 0f, 0.16f, 0.32f, 0.5f, 0.7f, 0.88f };
            float s = Mathf.Clamp01(Mathf.Abs(ForwardSpeed) / topSpeed);
            int g = 1;
            for (int i = 1; i < shifts.Length; i++) if (s > shifts[i]) g = i + 1;
            Gear = ForwardSpeed < -0.5f ? 0 : g;
            float lo = shifts[Mathf.Clamp(g - 1, 0, shifts.Length - 1)];
            float hi = g < shifts.Length ? shifts[g] : 1f;
            float target = Mathf.Lerp(0.25f, 1f, Mathf.InverseLerp(lo, hi, s));
            if (!Grounded || Drifting) target = Mathf.Max(target, input.throttle * 0.95f);
            Rpm01 = Mathf.MoveTowards(Rpm01, Mathf.Max(target, input.throttle * 0.3f), Time.fixedDeltaTime * 3f);
        }

        void SelfRight(float dt)
        {
            bool upsideDown = Vector3.Dot(transform.up, Vector3.up) < 0.3f && Body.linearVelocity.sqrMagnitude < 4f;
            flippedTime = upsideDown ? flippedTime + dt : 0f;
            if (flippedTime > 2.5f)
            {
                flippedTime = 0f;
                var fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                Body.position += Vector3.up * 1.5f;
                Body.rotation = Quaternion.LookRotation(fwd, Vector3.up);
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
            }
        }

        void LateUpdate()
        {
            foreach (var w in wheels)
            {
                if (w.visual == null) continue;
                float travel = w.grounded ? restLength - w.compression : restLength;
                w.visual.position = transform.TransformPoint(w.anchor - Vector3.up * travel);
                float yaw = w.front ? SteerAngle : 0f;
                w.visual.rotation = transform.rotation * Quaternion.Euler(w.spin, yaw, 0f) * w.baseRotation;
                if (w.knuckle != null)
                    w.knuckle.SetPositionAndRotation(w.visual.position, transform.rotation * Quaternion.Euler(0f, yaw, 0f));
            }
        }
    }
}
