using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// A crate full of explosives. Two ways to set it off:
    /// - Hit it with anything (wrench, thrown wrench, shot, slam...): it blows up on the spot.
    /// - Walk up close: the fuse lights and it blows up a few seconds later, whether you stay or run.
    /// The blast hurts everything in range: players, enemies and breakable crates (which drop their loot as usual).
    /// Other explosive crates caught in it go off a moment later, so rows of them chain.
    /// The server decides everything; the fuse end time is replicated so every client blinks in step.
    /// </summary>
    [RequireComponent(typeof(Damageable))]
    public class ExplosiveCrate : NetworkBehaviour
    {
        [SerializeField] int damage = 1;
        [SerializeField] float blastRadius = 3.5f;
        [Tooltip("A living player this close lights the fuse.")]
        [SerializeField] float triggerRadius = 2.5f;
        [SerializeField] float fuseTime = 3f;
        [Tooltip("Delay before an explosive crate caught in another blast goes off.")]
        [SerializeField] float chainDelay = 0.15f;
        [SerializeField] LayerMask blastMask = ~0;

        [Header("Visuals")]
        [SerializeField] Renderer lamp;
        [SerializeField] Transform pulseVisual;
        [SerializeField] Color lampIdleColor = new Color(0.3f, 0.05f, 0.05f);
        [SerializeField] Color lampOnColor = new Color(1f, 0.15f, 0.1f);
        [SerializeField] Color lampFlashColor = new Color(1f, 0.95f, 0.4f);
        [SerializeField] Material fireMaterial;
        [SerializeField] Material coreMaterial;
        [SerializeField] float cameraShake = 0.5f;

        const double NotLit = -1d;

        // Server time when it goes off; NotLit while the fuse is unlit.
        readonly NetworkVariable<double> detonateAt = new(NotLit);

        Damageable damageable;
        bool exploded;
        Material lampMaterial;
        Vector3 pulseRestScale;

        public bool IsLit => detonateAt.Value >= 0d;
        Vector3 Center => transform.position;
        double Now => NetworkManager.ServerTime.Time;

        void Awake()
        {
            damageable = GetComponent<Damageable>();
            if (lamp != null) lampMaterial = lamp.material;
            if (pulseVisual != null) pulseRestScale = pulseVisual.localScale;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) damageable.Died += OnDied;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) damageable.Died -= OnDied;
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (IsServer) ServerTick();
            UpdateVisuals();
        }

        // ---------- Server ----------

        void ServerTick()
        {
            if (exploded) return;
            if (IsLit)
            {
                if (Now >= detonateAt.Value) Detonate();
                return;
            }

            foreach (var player in PlayerHealth.All)
            {
                if (!player.IsAlive) continue;
                Vector3 body = player.transform.position + Vector3.up;
                if ((body - Center).sqrMagnitude <= triggerRadius * triggerRadius)
                {
                    Light(fuseTime);
                    return;
                }
            }
        }

        /// <summary>Server: start (or shorten) the fuse so it goes off in <paramref name="delay"/> seconds.</summary>
        public void Light(float delay)
        {
            if (!IsServer || exploded) return;
            double at = Now + delay;
            if (!IsLit || at < detonateAt.Value) detonateAt.Value = at;
        }

        // Hit by a player (or anything else that breaks it): blow up right now.
        void OnDied(Vector3 hitDirection) => Detonate();

        void Detonate()
        {
            if (exploded) return;
            exploded = true;
            Vector3 center = Center;
            ExplodeRpc(center);

            var hurtPlayers = new HashSet<PlayerHealth>();
            var hurtObjects = new HashSet<Damageable>();
            foreach (var hit in Physics.OverlapSphere(center, blastRadius, blastMask, QueryTriggerInteraction.Ignore))
            {
                var player = hit.GetComponentInParent<PlayerHealth>();
                if (player != null)
                {
                    if (hurtPlayers.Add(player))
                        player.ApplyDamage(new DamageInfo(damage, player.transform.position - center, gameObject));
                    continue;
                }

                var target = hit.GetComponentInParent<Damageable>();
                if (target == null || target == damageable || target.IsDead || !hurtObjects.Add(target)) continue;
                if (target.TryGetComponent(out ExplosiveCrate other)) other.Light(chainDelay); // chain reaction
                else target.ApplyDamage(damage, Vector3.ProjectOnPlane(target.transform.position - center, Vector3.up).normalized, DamageTypes.Explosive);
            }

            // Set off by the fuse: break it (despawns; OnDied comes back here and stops at the flag).
            if (!damageable.IsDead) damageable.ApplyDamage(1000, Vector3.zero, DamageTypes.Explosive);
        }

        // ---------- Visuals (every client) ----------

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void ExplodeRpc(Vector3 center)
        {
            ExplosionEffect.Spawn(center, blastRadius, fireMaterial, coreMaterial, cameraShake);
        }

        void UpdateVisuals()
        {
            if (!IsLit)
            {
                if (lampMaterial != null) lampMaterial.color = lampIdleColor;
                if (pulseVisual != null) pulseVisual.localScale = pulseRestScale;
                return;
            }

            // Blink faster and swell as the fuse burns down.
            float remaining = Mathf.Max(0f, (float)(detonateAt.Value - Now));
            float burnt = 1f - Mathf.Clamp01(remaining / fuseTime);
            float rate = Mathf.Lerp(3f, 14f, burnt);
            bool flash = Mathf.Repeat(Time.time * rate, 1f) < 0.5f;
            if (lampMaterial != null) lampMaterial.color = flash ? lampFlashColor : lampOnColor;
            if (pulseVisual != null)
            {
                float pulse = 1f + (flash ? 0.04f : 0f) + 0.08f * burnt;
                pulseVisual.localScale = pulseRestScale * pulse;
            }
        }

        public override void OnDestroy()
        {
            if (lampMaterial != null) Destroy(lampMaterial);
            base.OnDestroy();
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.1f);
            Gizmos.DrawWireSphere(transform.position, triggerRadius);
            Gizmos.color = new Color(1f, 0.15f, 0.1f);
            Gizmos.DrawWireSphere(transform.position, blastRadius);
        }
    }
}
