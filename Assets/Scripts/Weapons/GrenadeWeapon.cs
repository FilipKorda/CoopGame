using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Impact grenade. Hold Aim to see the throw arc and the blast circle where it lands; lock onto a target with
    /// the crosshair like any aimed weapon, or aim at the ground/anything in reach (nothing in reach: the furthest
    /// spot straight ahead). Attack throws it at that spot as it is at that moment — a locked target that moves
    /// away is not followed, which is why the blast is big. It goes off on the first thing it touches and hurts
    /// enemies and breakables in range (not players; metal crates shrug it off).
    /// Every client flies its own copy of the grenade; the server's copy deals the damage. The thrower sees the
    /// blast at once, everyone else when the server reports it.
    /// The player carries at most 5 and gets one back every 5 seconds while below that (<see cref="RegeneratingAmmo"/>,
    /// also while another weapon is in hand). The count is kept by the owner, like the pistol's ammo;
    /// <see cref="AddGrenades"/> / <see cref="GiveGrenadesRpc"/> can top it up from elsewhere.
    /// </summary>
    public class GrenadeWeapon : PlayerWeapon, IGrenadeThrower
    {
        [SerializeField] RegeneratingAmmo grenades = new RegeneratingAmmo(5, 5f);
        [SerializeField] int damage = 1;
        [SerializeField] float blastRadius = 4f;
        [Tooltip("Furthest landing spot, horizontal distance from the player; also how far lock-on reaches.")]
        [SerializeField] float throwRange = 18f;
        [SerializeField] float throwInterval = 0.9f;
        [SerializeField] float gravity = 25f;
        [Tooltip("Flight time for the nearest / furthest throw: far throws lob higher.")]
        [SerializeField] Vector2 flightTime = new Vector2(0.45f, 1.1f);
        [SerializeField] float grenadeRadius = 0.15f;
        [Tooltip("Where the grenade leaves the hand.")]
        [SerializeField] Transform throwPoint;

        [Header("Visuals")]
        [SerializeField] Material grenadeMaterial;
        [SerializeField] Material bandMaterial;
        [SerializeField] Material arcMaterial;
        [SerializeField] Material fireMaterial;
        [SerializeField] Material coreMaterial;
        [SerializeField] float cameraShake = 0.35f;

        PlayerTargeting targeting;
        float nextThrowTime;
        int throwCount;
        bool aimedThisFrame;
        readonly Dictionary<int, GrenadeProjectile> inFlight = new();
        ThrowAim aim; // owner, made on first use

        public override WeaponId Id => WeaponId.Grenade;
        public override bool UsesLockOn => true;
        public override float LockRange => throwRange;
        public override string StatusText => grenades.StatusText;

        /// <summary>Owner: grenades left.</summary>
        public int Grenades => grenades.Count;
        public int MaxGrenades => grenades.Max;

        void Awake() => targeting = GetComponent<PlayerTargeting>();

        /// <summary>Owner: refill (up to the maximum), e.g. from a pickup.</summary>
        public void AddGrenades(int count) => grenades.Add(count);

        void Update()
        {
            if (IsSpawned && IsOwner) grenades.Tick(Time.deltaTime);
        }

        /// <summary>Server → owner: hand this player some grenades (pickups and the like are decided on the server).</summary>
        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        public void GiveGrenadesRpc(int count) => AddGrenades(count);

        public override void OnNetworkDespawn()
        {
            foreach (var grenade in inFlight.Values)
                if (grenade != null) Destroy(grenade.gameObject);
            inFlight.Clear();
            aim?.Destroy();
        }

        // ---------- Owner: aiming and throwing ----------

        public override void TickAiming(bool firePressed, bool fireHeld)
        {
            if (grenades.IsEmpty) return; // nothing to throw: no arc either (the HUD says so)

            aimedThisFrame = true;
            aim ??= new ThrowAim(transform, targeting, throwRange, gravity, flightTime, grenadeRadius, blastRadius, arcMaterial, "GrenadeAimPreview");
            Vector3 start = throwPoint != null ? throwPoint.position : transform.position + Vector3.up * 1.5f;
            Vector3 velocity = aim.LaunchVelocity(start, aim.LandingSpot(), out float flight);
            aim.ShowPreview(start, velocity, flight);

            if (firePressed && Time.time >= nextThrowTime && grenades.TryUse())
            {
                nextThrowTime = Time.time + throwInterval;
                ThrowRpc(start, velocity, ++throwCount);
            }
        }

        void LateUpdate()
        {
            if (!IsOwner) return;
            if (!aimedThisFrame) aim?.HidePreview();
            aimedThisFrame = false;
        }

        // ---------- Flight and blast (every client) ----------

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
        void ThrowRpc(Vector3 start, Vector3 velocity, int id)
        {
            // The server only lets throws from the player's own hand through (slack for latency).
            if (IsServer && !IsOwner && ((start - transform.position).sqrMagnitude > 9f || velocity.sqrMagnitude > 60f * 60f)) return;
            inFlight[id] = GrenadeProjectile.Spawn(this, id, start, velocity, gravity, grenadeRadius, grenadeMaterial, bandMaterial);
        }

        /// <summary>Called by this weapon's grenade copy on this client when it touches something.</summary>
        public void OnGrenadeImpact(int id, Vector3 point)
        {
            inFlight.Remove(id);
            if (IsServer)
            {
                ServerBlast(point);
                PlayBlast(point);
                BlastRpc(id, point);
            }
            else if (IsOwner)
            {
                PlayBlast(point); // the thrower sees it at once; the damage follows from the server
            }
            // Other clients wait for the server's blast.
        }

        void ServerBlast(Vector3 point)
        {
            var hit = new HashSet<Damageable>();
            foreach (var collider in Physics.OverlapSphere(point, blastRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                var target = collider.GetComponentInParent<Damageable>();
                if (target == null || target.IsDead || !hit.Add(target)) continue;
                Vector3 push = Vector3.ProjectOnPlane(target.transform.position - point, Vector3.up).normalized;
                target.ApplyDamage(damage, push, DamageTypes.Grenade);
            }
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        void BlastRpc(int id, Vector3 point)
        {
            if (inFlight.TryGetValue(id, out var grenade))
            {
                inFlight.Remove(id);
                if (grenade != null) Destroy(grenade.gameObject);
            }
            if (!IsOwner) PlayBlast(point);
        }

        void PlayBlast(Vector3 point) => ExplosionEffect.Spawn(point, blastRadius, fireMaterial, coreMaterial, cameraShake);
    }
}
