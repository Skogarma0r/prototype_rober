using UnityEngine;

namespace Ashline.Gameplay
{
    /// <summary>Item ids other modules refer to. The inventory module defines the full item database.</summary>
    public static class ItemIds
    {
        public const string Pistol = "pistol";
        public const string Ammo9mm = "ammo_9mm";
        public const string Mask = "mask";
        public const string Lockpick = "lockpick";
        public const string Drill = "drill";
        public const string Keycard = "keycard";
        public const string RoomKey = "room_key";
        public const string Cash = "cash";
    }

    public enum EquipSlot { Mask, Armor, Bag, Weapon }

    /// <summary>What gameplay modules may ask of the player's inventory (implemented by the Inventory module).</summary>
    public interface IPlayerInventory
    {
        /// <summary>Total count across pockets, bag and equipment.</summary>
        int Count(string itemId);
        bool Has(string itemId);
        /// <summary>Removes up to n; returns how many were removed.</summary>
        int Consume(string itemId, int n = 1);
        /// <summary>Adds into pockets/bag; returns how many did not fit.</summary>
        int TryAdd(string itemId, int n = 1);
        bool IsEquipped(EquipSlot slot);
        /// <summary>Police arrest / surrender: bag contents are confiscated, equipment kept.</summary>
        void ConfiscateBag();
        /// <summary>Criminal death: everything is lost.</summary>
        void LoseEverything();
        /// <summary>Sum of fence value of everything carried (for HUD/score).</summary>
        int CarriedValue { get; }
    }

    /// <summary>Player aiming state, published by the Combat module every frame.</summary>
    public static class PlayerAim
    {
        public static bool IsAiming;
        /// <summary>World ray from the crosshair.</summary>
        public static Ray Ray;
        /// <summary>Collider under the crosshair within weapon range, or null.</summary>
        public static Collider Target;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            IsAiming = false;
            Target = null;
        }
    }
}
