using System;
using UnityEngine;

namespace Ashline.Gameplay
{
    public struct DamageInfo
    {
        public float amount;
        public Vector3 point, direction;
        public GameObject source;
        public bool byPlayer;
    }

    /// <summary>Anything a bullet can hurt: people, cameras, glass, car tyres.</summary>
    public interface IDamageable
    {
        void Damage(DamageInfo info);
    }

    /// <summary>Hit points with a death event. Raises <see cref="GameEvents.Killed"/> once.</summary>
    public class Health : MonoBehaviour, IDamageable
    {
        public float max = 100f;
        public float Current { get; private set; }
        public bool Dead { get; private set; }
        public event Action<DamageInfo> Hurt;
        public event Action<DamageInfo> Died;

        void Awake() => Current = max;

        public void Damage(DamageInfo info)
        {
            if (Dead) return;
            Current = Mathf.Max(0f, Current - info.amount);
            Hurt?.Invoke(info);
            if (Current > 0f) return;
            Dead = true;
            Died?.Invoke(info);
            GameEvents.RaiseKilled(gameObject, info.byPlayer);
        }

        public void Heal(float amount)
        {
            if (Dead) return;
            Current = Mathf.Min(max, Current + amount);
        }

        public void Revive()
        {
            Dead = false;
            Current = max;
        }
    }
}
