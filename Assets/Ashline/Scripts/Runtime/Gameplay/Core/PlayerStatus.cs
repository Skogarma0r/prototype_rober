using System;
using UnityEngine;

namespace Ashline.Gameplay
{
    public enum PlayerRole { Citizen, Criminal }

    /// <summary>
    /// Role, wanted level and wallet. Putting the mask on makes the player a Criminal; as in ROB RUN
    /// the status only clears when the player leaves the city (or is arrested).
    /// </summary>
    public class PlayerStatus : MonoBehaviour
    {
        public const int MaxWanted = 3;

        public PlayerRole Role { get; private set; } = PlayerRole.Citizen;
        public bool MaskOn { get; private set; }
        public int Wanted { get; private set; }
        public int Money { get; private set; } = 500;

        public event Action Changed;

        public bool IsCriminal => Role == PlayerRole.Criminal;

        public void SetMask(bool on)
        {
            if (MaskOn == on) return;
            MaskOn = on;
            if (on) Role = PlayerRole.Criminal;
            Changed?.Invoke();
        }

        public void AddWanted(int points)
        {
            int w = Mathf.Clamp(Wanted + points, 0, MaxWanted);
            if (w == Wanted) return;
            Wanted = w;
            if (w > 0) Role = PlayerRole.Criminal;
            GameEvents.RaiseWantedChanged(w);
            Changed?.Invoke();
        }

        /// <summary>Arrest, death or leaving the city: back to a clean citizen.</summary>
        public void ClearRecord()
        {
            Wanted = 0;
            MaskOn = false;
            Role = PlayerRole.Citizen;
            GameEvents.RaiseWantedChanged(0);
            Changed?.Invoke();
        }

        public void AddMoney(int amount)
        {
            Money = Mathf.Max(0, Money + amount);
            Changed?.Invoke();
        }

        public bool TrySpend(int amount)
        {
            if (amount > Money) return false;
            Money -= amount;
            Changed?.Invoke();
            return true;
        }
    }
}
