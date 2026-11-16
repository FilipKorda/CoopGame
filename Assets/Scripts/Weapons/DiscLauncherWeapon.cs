using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Disc launcher. Aim and lock onto one target (or just aim: the disc flies at the crosshair); Attack sends a
    /// spinning disc at it. After it hits something, it bounces on to the nearest other enemy close by and hits that
    /// one too — two different targets at most. With no enemy close enough to bounce to, it just stops there.
    /// Every target it cuts bleeds: a moment later it takes 1 more damage (<see cref="Damageable.ApplyBleed"/>).
    /// 5 discs, one comes back every couple of seconds (<see cref="RegeneratingAmmo"/>).
    /// Every client flies its own copy of each disc; the server's copy deals the damage and picks the bounce, and
    /// tells the others where to go next.
    /// </summary>
    public class DiscLauncherWeapon : PlayerWeapon
    {
        [SerializeField] RegeneratingAmmo discs = new RegeneratingAmmo(5, 2f);
        [Tooltip("Damage per target hit.")]
        [SerializeField] int damage = 1;
        [Tooltip("Extra damage the bleeding deals, once it runs its course.")]
        [SerializeField] int bleedDamage = 1;
        [Tooltip("Seconds a cut target bleeds before the extra damage lands.")]
        [SerializeField] float bleedDuration = 2f;
        [Tooltip("Different targets a disc hits at most: the first one plus bounces.")]
        [SerializeField] int maxHits = 2;
        [Tooltip("How close another enemy has to be to the hit for the disc to bounce on to it.")]
        [SerializeField] float bounceRange = 8f;
        [SerializeField] float fireInterval = 0.45f;
        [SerializeField] float discSpeed = 24f;
        [SerializeField] float discRadius = 0.2f;
        [Tooltip("How far a disc flies with no target before it gives up.")]
        [SerializeField] float reach = 30f;
        [SerializeField] Transform muzzle;

        [Header("Visuals")]
        [SerializeField] Material discMaterial;
        [SerializeField] Material hubMaterial;

        PlayerTargeting targeting;
        float nextFireTime;
        int discCount;
        readonly Dictionary<int, DiscProjectile> inFlight = new();

        public override WeaponId Id => WeaponId.DiscLauncher;
        public override bool UsesLockOn => true;
        public override string StatusText => discs.StatusText;

        void Awake() => targeting = GetComponent<PlayerTargeting>();

        void Update()
        {
            if (IsSpawned && IsOwner) discs.Tick(Time.deltaTime);
        }

        public override void OnNetworkDespawn()
        {
            foreach (var disc in inFlight.Values)
                if (disc != null) Destroy(disc.gameObject);
            inFlight.Clear();
        }

        // ---------- Owner: firing ----------

        public override void TickAiming(bool firePressed, bool fireHeld)
        {
            if (!firePressed || Time.time < nextFireTime || !discs.TryUse()) return;
            nextFireTime = Time.time + fireInterval;

            Vector3 start = muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.3f;
            var target = targeting.CurrentTarget;
            var targetObject = target != null ? target.GetComponentInParent<NetworkObject>() : null;
            bool hasTarget = targetObject != null && targetObject.IsSpawned;
            NetworkObjectReference targetRef = default;
            if (hasTarget) targetRef = targetObject;
            FireRpc(start, targeting.GetAimPoint(), hasTarget, targetRef, ++discCount);
        }

        // ---------- Flight (every client), hits and bounces (server) ----------

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
        void FireRpc(Vector3 start, Vector3 aimPoint, bool hasTarget, NetworkObjectReference targetRef, int id)
        {
            // The server only lets discs from the player's own hand through (slack for latency).
            if (IsServer && !IsOwner && (start - transform.position).sqrMagnitude > 9f) return;

            Targetable target = null;
            if (hasTarget && targetRef.TryGet(out NetworkObject targetObject)) target = targetObject.GetComponentInChildren<Targetable>();
            Vector3 direction = aimPoint - start;
            if (direction.sqrMagnitude < 0.01f) direction = transform.forward;
            inFlight[id] = DiscProjectile.Spawn(this, id, start, direction.normalized, target, discSpeed, discRadius, reach, discMaterial, hubMaterial);
        }

        /// <summary>This client's copy of a disc finished a leg (hit something, or reached its spot / its reach).</summary>
        public void OnDiscLegEnd(DiscProjectile disc, Collider hitCollider, Vector3 point)
        {
            if (!IsServer)
            {
                disc.Wait(); // the server says whether it bounces
                return;
            }

            Damageable victim = hitCollider != null ? hitCollider.GetComponentInParent<Damageable>() : null;
            if (victim == null && hitCollider == null && disc.Target != null) victim = disc.Target.Damageable; // reached its target's spot
            bool hitSomething = victim != null && !victim.IsDead && victim != disc.LastHit;

            if (hitSomething)
            {
                victim.ApplyDamage(damage, Vector3.ProjectOnPlane(disc.Direction, Vector3.up).normalized);
                if (!victim.IsDead) victim.ApplyBleed(bleedDamage, bleedDuration);
                disc.Hits++;
                disc.LastHit = victim;
            }

            var next = hitSomething && disc.Hits < maxHits ? BounceTarget(point, victim) : null;
            if (next != null)
            {
                var nextTarget = next.GetComponentInChildren<Targetable>();
                // It leaves from the surface of what it just hit: don't let it hit that again on the way out.
                disc.StartLeg(point, next.transform.position - point, nextTarget, reach, victim.transform);
                DiscProjectile.Sparks(point, discMaterial);
                // A target the hit just destroyed is despawned already (and gone on the clients by the time this lands).
                NetworkObjectReference hitRef = default;
                if (victim.IsSpawned) hitRef = victim.NetworkObject;
                BounceRpc(disc.Id, point, hitRef, next.NetworkObject);
                return;
            }

            EndDisc(disc.Id, point, hitSomething);
            EndRpc(disc.Id, point, hitSomething);
        }

        /// <summary>The nearest other living enemy close to <paramref name="point"/>, or null.</summary>
        EnemyController BounceTarget(Vector3 point, Damageable justHit)
        {
            EnemyController best = null;
            float bestSqr = bounceRange * bounceRange;
            foreach (var enemy in EnemyController.All)
            {
                if (!enemy.IsAlive || enemy.Damageable == justHit) continue;
                float sqr = (enemy.transform.position - point).sqrMagnitude;
                if (sqr > bestSqr) continue;
                bestSqr = sqr;
                best = enemy;
            }
            return best;
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        void BounceRpc(int id, Vector3 from, NetworkObjectReference hitRef, NetworkObjectReference nextRef)
        {
            if (!nextRef.TryGet(out NetworkObject next)) return;
            var nextTarget = next.GetComponentInChildren<Targetable>();
            Transform ignore = hitRef.TryGet(out NetworkObject hit) ? hit.transform : null;
            DiscProjectile.Sparks(from, discMaterial);
            if (!inFlight.TryGetValue(id, out var disc) || disc == null)
                inFlight[id] = disc = DiscProjectile.Spawn(this, id, from, next.transform.position - from, nextTarget, discSpeed, discRadius, reach, discMaterial, hubMaterial);
            disc.StartLeg(from, next.transform.position - from, nextTarget, reach, ignore);
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        void EndRpc(int id, Vector3 point, bool hitSomething) => EndDisc(id, point, hitSomething);

        void EndDisc(int id, Vector3 point, bool hitSomething)
        {
            if (inFlight.TryGetValue(id, out var disc))
            {
                inFlight.Remove(id);
                if (disc != null) Destroy(disc.gameObject);
            }
            if (hitSomething) DiscProjectile.Sparks(point, discMaterial);
        }
    }
}
