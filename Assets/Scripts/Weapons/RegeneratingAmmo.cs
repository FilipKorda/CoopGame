using System;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Ammo that comes back by itself: at most <see cref="Max"/>, one more every <c>regenInterval</c> seconds while
    /// below that (the countdown starts with the first shot from full). Plain owner-side state for a weapon:
    /// call <see cref="Tick"/> every frame, whether or not the weapon is in hand.
    /// </summary>
    [Serializable]
    public class RegeneratingAmmo
    {
        [SerializeField] int max = 5;
        [Tooltip("Seconds to get one back while below the maximum.")]
        [SerializeField] float regenInterval = 5f;

        int count = -1; // starts full
        float timer;

        public RegeneratingAmmo() { }

        public RegeneratingAmmo(int max, float regenInterval)
        {
            this.max = max;
            this.regenInterval = regenInterval;
        }

        public int Count => count < 0 ? max : count;
        public int Max => max;
        public bool IsEmpty => Count <= 0;

        /// <summary>Spends one if there's any left.</summary>
        public bool TryUse()
        {
            if (IsEmpty) return false;
            count = Count - 1;
            return true;
        }

        /// <summary>Tops up (pickups and the like), never past the maximum.</summary>
        public void Add(int amount) => count = Mathf.Clamp(Count + amount, 0, max);

        public void Tick(float deltaTime)
        {
            if (Count >= max)
            {
                timer = 0f;
                return;
            }
            timer += deltaTime;
            if (timer >= regenInterval)
            {
                timer -= regenInterval;
                Add(1);
            }
        }

        /// <summary>HUD text: "7/10", or with the countdown while refilling: "7/10  (+1 za 2 s)", "0/10  brak, +1 za 2 s".</summary>
        public string StatusText
        {
            get
            {
                int left = Count;
                if (left >= max) return $"{left}/{max}";
                int seconds = Mathf.CeilToInt(regenInterval - timer);
                return left > 0 ? $"{left}/{max}  (+1 za {seconds} s)" : $"0/{max}  brak, +1 za {seconds} s";
            }
        }
    }
}
