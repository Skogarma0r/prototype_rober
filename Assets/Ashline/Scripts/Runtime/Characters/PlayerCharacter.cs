using UnityEngine;

namespace Ashline.Characters
{
    /// <summary>On-foot player: camera-relative movement with acceleration, turning, sprint, jump and gravity.</summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerCharacter : MonoBehaviour
    {
        public float walkSpeed = 2.2f;
        public float runSpeed = 6.2f;
        public float acceleration = 14f;
        public float turnSpeed = 720f;
        public float jumpHeight = 1.0f;
        public float gravity = 22f;

        /// <summary>Set by the game every frame: x = strafe, y = forward, magnitude 0..1.</summary>
        [System.NonSerialized] public Vector2 move;
        [System.NonSerialized] public bool sprint;
        [System.NonSerialized] public bool jump;
        [System.NonSerialized] public Transform view;

        public CharacterController Controller { get; private set; }
        public CharacterAnimator Anim { get; private set; }
        public Vector3 Velocity => velocity;
        public bool Grounded => Controller.isGrounded;
        public Vector3 LastGroundPosition { get; private set; }

        Vector3 velocity;
        float groundedTimer;

        public static PlayerCharacter Create(GameObject model, AnimationClip idle, AnimationClip walk, AnimationClip run, Vector3 pos, Quaternion rot)
        {
            var go = new GameObject("Player");
            go.transform.SetPositionAndRotation(pos, rot);
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.32f;
            cc.center = Vector3.up * 0.92f;
            cc.stepOffset = 0.4f;
            cc.slopeLimit = 50f;
            cc.skinWidth = 0.04f;
            var p = go.AddComponent<PlayerCharacter>();
            p.Controller = cc;
            if (model != null)
            {
                var view = Instantiate(model, go.transform);
                view.name = "Model";
                view.transform.localPosition = Vector3.zero;
                view.transform.localRotation = Quaternion.Euler(0, 180f, 0);
                p.Anim = go.AddComponent<CharacterAnimator>();
                p.Anim.Init(view.GetComponentInChildren<Animator>(), idle, walk, run);
                foreach (var r in view.GetComponentsInChildren<SkinnedMeshRenderer>()) r.updateWhenOffscreen = false;
            }
            p.LastGroundPosition = pos;
            return p;
        }

        void Awake() => Controller = GetComponent<CharacterController>();

        void Update()
        {
            float dt = Time.deltaTime;
            var input = Vector2.ClampMagnitude(move, 1f);
            Vector3 fwd = Vector3.forward, right = Vector3.right;
            if (view != null)
            {
                fwd = Vector3.ProjectOnPlane(view.forward, Vector3.up).normalized;
                right = Vector3.Cross(Vector3.up, fwd);
            }
            var wish = (fwd * input.y + right * input.x);
            float target = Mathf.Lerp(walkSpeed, runSpeed, sprint ? 1f : 0f) * wish.magnitude;
            if (input.magnitude > 0.85f && !sprint) target = Mathf.Max(target, walkSpeed * 1.15f);
            var horiz = new Vector3(velocity.x, 0, velocity.z);
            var desired = wish.sqrMagnitude > 1e-4f ? wish.normalized * target : Vector3.zero;
            float accel = Controller.isGrounded ? acceleration : acceleration * 0.3f;
            horiz = Vector3.MoveTowards(horiz, desired, accel * dt);

            if (Controller.isGrounded)
            {
                groundedTimer = 0.15f;
                if (velocity.y < 0) velocity.y = -2f;
                LastGroundPosition = transform.position;
            }
            else groundedTimer -= dt;
            if (jump && groundedTimer > 0f)
            {
                velocity.y = Mathf.Sqrt(2f * gravity * jumpHeight);
                groundedTimer = 0;
            }
            jump = false;
            velocity.y -= gravity * dt;
            velocity.x = horiz.x;
            velocity.z = horiz.z;
            Controller.Move(velocity * dt);

            // Turn toward travel; quicker at low speed so starts feel responsive.
            if (horiz.sqrMagnitude > 0.05f)
            {
                var face = Quaternion.LookRotation(horiz);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, face, turnSpeed * dt);
            }
            if (Anim != null) Anim.SetSpeed(horiz.magnitude);
        }

        public void Teleport(Vector3 pos, Quaternion rot)
        {
            Controller.enabled = false;
            transform.SetPositionAndRotation(pos, rot);
            Controller.enabled = true;
            velocity = Vector3.zero;
            LastGroundPosition = pos;
        }
    }
}
