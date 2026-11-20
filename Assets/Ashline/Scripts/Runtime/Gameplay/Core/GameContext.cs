using System;
using Ashline.Characters;
using Ashline.City;
using Ashline.UI;
using Ashline.Vehicles;
using UnityEngine;

namespace Ashline.Gameplay
{
    /// <summary>
    /// Shared handles for gameplay modules. Filled in by <see cref="GameBootstrap"/> before
    /// <see cref="GameEvents.WorldReady"/> fires; modules subscribe to that event from a
    /// [RuntimeInitializeOnLoadMethod] so they install themselves without edits to the bootstrap.
    /// </summary>
    public static class GameContext
    {
        public static GameBootstrap Bootstrap;
        public static CityGenerator City;
        public static PlayerCharacter Player;
        public static PlayerStatus Status;
        /// <summary>Set by the Inventory module on WorldReady.</summary>
        public static IPlayerInventory Inventory;
        public static Camera Camera;
        public static TouchControls Hud;
        /// <summary>Model and clips used for every human; swap per role once more models exist.</summary>
        public static GameObject HumanModel;
        public static AnimationClip IdleClip, WalkClip, RunClip;

        public static bool Driving => Bootstrap != null && Bootstrap.Driving;
        public static CarController PlayerCar => Bootstrap != null ? Bootstrap.Car : null;
        /// <summary>Player position whether walking or driving.</summary>
        public static Vector3 PlayerPosition => Driving ? PlayerCar.transform.position : Player != null ? Player.transform.position : Vector3.zero;
    }

    public static class GameEvents
    {
        /// <summary>City, player, camera and HUD exist. Install gameplay systems here.</summary>
        public static event Action WorldReady;
        /// <summary>A building's alarm went off (shop may be null for street crimes).</summary>
        public static event Action<ShopLayout, Vector3> AlarmRaised;
        /// <summary>Player's wanted level changed (0..3).</summary>
        public static event Action<int> WantedChanged;
        /// <summary>Something in the world was killed; byPlayer tells whether the player did it.</summary>
        public static event Action<GameObject, bool> Killed;

        /// <summary>Glass (display case, shop window) was smashed at a point; may set off a building alarm.</summary>
        public static event Action<Vector3> GlassBroken;
        /// <summary>Player was arrested ("BUSTED") or killed ("WASTED"); bag/inventory loss is already applied.</summary>
        public static event Action<string> PlayerBusted;

        public static void RaiseWorldReady() => WorldReady?.Invoke();
        public static void RaiseGlassBroken(Vector3 at) => GlassBroken?.Invoke(at);
        public static void RaisePlayerBusted(string reason) => PlayerBusted?.Invoke(reason);
        public static void RaiseAlarm(ShopLayout shop, Vector3 at) => AlarmRaised?.Invoke(shop, at);
        public static void RaiseWantedChanged(int level) => WantedChanged?.Invoke(level);
        public static void RaiseKilled(GameObject victim, bool byPlayer) => Killed?.Invoke(victim, byPlayer);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            WorldReady = null;
            AlarmRaised = null;
            WantedChanged = null;
            Killed = null;
            GlassBroken = null;
            PlayerBusted = null;
            GameContext.Inventory = null;
        }
    }
}
