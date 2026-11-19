using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// One part of a <see cref="BossFight"/> boss (an arm, the head): its own NetworkObject with a
    /// <see cref="Damageable"/> holding its share of the health. Hits only get through while it's the boss's open
    /// segment; when it breaks, the boss moves on to the next one.
    /// </summary>
    [RequireComponent(typeof(Damageable))]
    public class BossSegment : NetworkBehaviour, IDamageAbsorber
    {
        [SerializeField] string displayName = "Segment";

        BossFight boss;
        int index;
        Damageable damageable;
        Targetable target;
        int lastKnownHealth = -1;

        public string DisplayName => displayName;
        public Damageable Health => damageable;
        public int MaxHealth => (damageable != null ? damageable : damageable = GetComponent<Damageable>()) != null ? damageable.MaxHealth : 0;
        /// <summary>Its health now (0 once it's broken off and gone).</summary>
        public int CurrentHealth
        {
            get
            {
                if (this == null || damageable == null) return 0;
                if (damageable.IsSpawned) lastKnownHealth = damageable.Health;
                else if (lastKnownHealth < 0) lastKnownHealth = damageable.MaxHealth;
                return Mathf.Max(0, lastKnownHealth);
            }
        }

        void Awake()
        {
            damageable = GetComponent<Damageable>();
            target = GetComponent<Targetable>();
        }

        public void Bind(BossFight fight, int segmentIndex)
        {
            boss = fight;
            index = segmentIndex;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) damageable.Died += OnDied;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) damageable.Died -= OnDied;
            lastKnownHealth = 0;
        }

        void OnDied(Vector3 direction)
        {
            lastKnownHealth = 0;
            if (boss != null) boss.ServerSegmentDown(index);
        }

        /// <summary>Server: hits only count while this is the open segment.</summary>
        public int Absorb(int amount, Vector3 hitDirection, DamageTypes type) =>
            boss != null && boss.ServerCanHurt(index) ? amount : 0;

        /// <summary>Every machine: open = can be hurt (normal colours, aimable); otherwise darkened and not aimable.</summary>
        public void SetOpen(bool open, Color lockedTint, float amount, bool fightGoing)
        {
            if (damageable == null || !damageable.IsSpawned) return;
            damageable.SetStatusTint(lockedTint, open || !fightGoing ? 0f : amount);
            if (target != null && target.enabled != open) target.enabled = open;
        }

        /// <summary>Editor setup.</summary>
        public void Setup(string name) => displayName = name;
    }

}
