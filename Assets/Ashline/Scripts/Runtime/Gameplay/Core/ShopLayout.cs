using System.Collections.Generic;
using UnityEngine;

namespace Ashline.Gameplay
{
    public enum ShopKind { Convenience, Pharmacy, Jewelry, Electronics, Cafe, Bank }

    /// <summary>What a loot spot is; decides its loot table, size of its grid and how it opens.</summary>
    public enum ContainerKind { Register, Shelf, Cabinet, Display, Safe, Vault }

    public enum LockKind { None, Lockpick, Key, Drill }

    public struct ContainerSpot
    {
        public ContainerKind kind;
        public LockKind lockKind;
        /// <summary>World pose: position on the floor/counter, forward faces the player who loots it.</summary>
        public Pose pose;
        /// <summary>Approximate size of the prop (for colliders and interaction range).</summary>
        public Vector3 size;
    }

    public struct DoorSpot
    {
        public Pose pose;          // center of the opening at floor level, forward points out of the shop
        public float width, height;
        public bool back;          // staff/back door: quiet entry with a lockpick
        public LockKind lockKind;
    }

    /// <summary>
    /// Interior description produced by the city generator for every enterable shop. Gameplay
    /// spawns people, cameras, alarm panels and loot containers from these anchors. All poses are
    /// in world space.
    /// </summary>
    public class ShopLayout
    {
        public ShopKind kind;
        public string name;
        /// <summary>Box of the sales floor and back rooms in <see cref="frame"/>-local coordinates.</summary>
        public Bounds localBounds;
        /// <summary>Shop frame: position = front door center at floor level, rotation forward = out to the street.</summary>
        public Pose frame;
        public float floorY;

        public readonly List<DoorSpot> doors = new List<DoorSpot>();
        /// <summary>Where clerks stand (behind the counter, facing the customer side).</summary>
        public readonly List<Pose> clerks = new List<Pose>();
        /// <summary>Where a security guard idles (banks, jewellers, big stores).</summary>
        public readonly List<Pose> guards = new List<Pose>();
        public readonly List<ContainerSpot> containers = new List<ContainerSpot>();
        /// <summary>Ceiling-mounted cameras: position at the lens, forward = initial look direction.</summary>
        public readonly List<Pose> cameras = new List<Pose>();
        /// <summary>Electrical/alarm panel; null when the building has none (bank, gallery).</summary>
        public Pose? alarmPanel;
        /// <summary>Metal detectors at the entrance (jewellers, bank).</summary>
        public readonly List<Pose> metalDetectors = new List<Pose>();

        /// <summary>World point → shop-local (frame) coordinates.</summary>
        public Vector3 ToLocal(Vector3 world) => Quaternion.Inverse(frame.rotation) * (world - frame.position);
        public bool Contains(Vector3 world) => localBounds.Contains(ToLocal(world));
    }

    /// <summary>All enterable shops of the current city, filled during generation.</summary>
    public static class ShopRegistry
    {
        public static readonly List<ShopLayout> All = new List<ShopLayout>();

        public static ShopLayout At(Vector3 world)
        {
            foreach (var s in All)
                if (s.Contains(world)) return s;
            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();
    }
}
