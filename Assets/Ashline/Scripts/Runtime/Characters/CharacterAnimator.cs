using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Ashline.Characters
{
    /// <summary>
    /// Idle / walk / run blend driven by ground speed. Walk and run share one gait phase so the feet
    /// stay in step while blending, and playback rate follows the actual speed to avoid foot sliding.
    /// </summary>
    public class CharacterAnimator : MonoBehaviour
    {
        public float walkSpeed = 1.45f;     // ground speed the walk clip was authored at
        public float runSpeed = 4.6f;

        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationClipPlayable idle, walk, run;
        float phase, idleTime, speed, blendSpeed;
        bool ready;
        Renderer body;

        public void Init(Animator animator, AnimationClip idleClip, AnimationClip walkClip, AnimationClip runClip)
        {
            if (animator == null || idleClip == null || walkClip == null || runClip == null) return;
            animator.applyRootMotion = false;
            graph = PlayableGraph.Create(name + "_Anim");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, 3);
            idle = AnimationClipPlayable.Create(graph, idleClip);
            walk = AnimationClipPlayable.Create(graph, walkClip);
            run = AnimationClipPlayable.Create(graph, runClip);
            foreach (var p in new[] { idle, walk, run }) p.SetApplyFootIK(false);
            graph.Connect(idle, 0, mixer, 0);
            graph.Connect(walk, 0, mixer, 1);
            graph.Connect(run, 0, mixer, 2);
            var output = AnimationPlayableOutput.Create(graph, "Out", animator);
            output.SetSourcePlayable(mixer);
            phase = Random.value;
            idleTime = Random.value * idleClip.length;
            body = animator.GetComponentInChildren<Renderer>();
            ready = true;
            Evaluate(0f, true);
        }

        public void SetSpeed(float metersPerSecond) => speed = metersPerSecond;

        void LateUpdate()
        {
            if (!ready) return;
            Evaluate(Time.deltaTime, false);
        }

        void Evaluate(float dt, bool force)
        {
            blendSpeed = Mathf.MoveTowards(blendSpeed, speed, dt * 9f);
            float v = blendSpeed;
            float wIdle, wWalk, wRun;
            if (v < walkSpeed)
            {
                float k = Mathf.SmoothStep(0, 1, v / walkSpeed);
                wIdle = 1 - k; wWalk = k; wRun = 0;
            }
            else
            {
                float k = Mathf.SmoothStep(0, 1, Mathf.Clamp01((v - walkSpeed) / (runSpeed - walkSpeed)));
                wIdle = 0; wWalk = 1 - k; wRun = k;
            }
            mixer.SetInputWeight(0, wIdle);
            mixer.SetInputWeight(1, wWalk);
            mixer.SetInputWeight(2, wRun);

            // Gait cycles per second at this speed, interpolated between the two clips.
            float walkLen = (float)walk.GetAnimationClip().length, runLen = (float)run.GetAnimationClip().length;
            float walkRate = Mathf.Max(v, 0.5f) / walkSpeed / walkLen;
            float runRate = Mathf.Max(v, 0.5f) / runSpeed / runLen;
            float rate = (wWalk * walkRate + wRun * runRate) / Mathf.Max(wWalk + wRun, 1e-3f);
            phase = Mathf.Repeat(phase + rate * dt, 1f);
            idleTime += dt;
            // Off-screen (and LOD-culled) characters keep their gait phase but skip the pose evaluation.
            if (!force && body && !body.isVisible) return;
            idle.SetTime(Mathf.Repeat(idleTime, (float)idle.GetAnimationClip().length));
            walk.SetTime(phase * walkLen);
            run.SetTime(phase * runLen);
            graph.Evaluate(0f);
        }

        void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }
    }
}
