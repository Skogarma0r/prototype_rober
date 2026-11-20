using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ashline.Gameplay
{
    /// <summary>
    /// Something the on-foot player can use: loot a container, pick a lock, breach a panel, talk to a
    /// fence. The best candidate in front of the player is shown on the HUD USE button; tap or hold
    /// (when <see cref="HoldTime"/> &gt; 0) to trigger it.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>Short caption for the button, e.g. "LOOT", "PICK LOCK", "BREACH".</summary>
        string Prompt { get; }
        /// <summary>Seconds to hold; 0 = instant tap. May depend on the player's tools.</summary>
        float HoldTime { get; }
        Vector3 Position { get; }
        float Range { get; }
        bool CanInteract { get; }
        void Interact();
    }

    public static class Interactables
    {
        static readonly List<IInteractable> all = new List<IInteractable>();

        public static void Register(IInteractable i) { if (!all.Contains(i)) all.Add(i); }
        public static void Unregister(IInteractable i) => all.Remove(i);

        /// <summary>Closest usable candidate in reach, favouring what the player faces.</summary>
        public static IInteractable Best(Vector3 pos, Vector3 forward)
        {
            IInteractable best = null;
            float bestScore = float.MaxValue;
            foreach (var i in all)
            {
                if (i == null || !i.CanInteract) continue;
                var d = i.Position - pos;
                d.y = Mathf.Clamp(d.y, -0.5f, 0.5f) * 0.3f;
                float dist = d.magnitude;
                if (dist > i.Range) continue;
                float facing = dist > 0.01f ? Vector3.Dot(forward, d / dist) : 1f;
                if (facing < -0.2f) continue;
                float score = dist * (1.6f - facing * 0.6f);
                if (score < bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => all.Clear();
    }

    /// <summary>Drives the HUD USE button from the nearest interactable (on foot only). E key on desktop.</summary>
    public class InteractionDriver : MonoBehaviour
    {
        public IInteractable Current { get; private set; }
        /// <summary>Set by modules that take over the screen (inventory open, aiming) to pause prompts.</summary>
        public static bool Blocked;
        public static event Action<IInteractable> Used;

        float held;

        void Update()
        {
            var hud = GameContext.Hud;
            var player = GameContext.Player;
            if (hud == null) return;
            if (Blocked || player == null || !player.gameObject.activeInHierarchy)
            {
                Current = null;
                held = 0;
                hud.SetUse(null, 0f);
                return;
            }
            var next = Interactables.Best(player.transform.position + Vector3.up * 0.9f, player.transform.forward);
            if (next != Current) held = 0;
            Current = next;
            if (Current == null)
            {
                hud.SetUse(null, 0f);
                return;
            }

            var kb = Keyboard.current;
            bool pressing = hud.use.Held || (kb != null && kb.eKey.isPressed);
            bool tapped = hud.use.Consume() | (kb != null && kb.eKey.wasPressedThisFrame);
            float need = Current.HoldTime;
            if (need <= 0f)
            {
                if (tapped) Fire();
            }
            else if (pressing)
            {
                held += Time.deltaTime;
                if (held >= need) Fire();
            }
            else held = 0;
            hud.SetUse(Current?.Prompt, need > 0f ? held / need : 0f);
        }

        void Fire()
        {
            var i = Current;
            held = 0;
            i.Interact();
            Used?.Invoke(i);
        }
    }
}
