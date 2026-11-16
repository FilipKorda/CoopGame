using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Multi-lock rocket launcher. While aiming, sweeping the crosshair over targets locks up to 3 of them; Attack
    /// fires one rocket at each locked target at once (they fan out of the tube and home in). One volley costs one
    /// round of ammo however many rockets it holds: 3 targets → 3 rockets, 1 target → 1 rocket, same price. With
    /// nothing locked a single rocket flies at the crosshair. Locks clear after each volley, ready to paint the next.
    /// 8 rounds, one comes back every few seconds (<see cref="RegeneratingAmmo"/>).
    /// Each rocket deals its damage in a small blast where it hits (enemies and breakables, not players; metal
    /// crates shrug it off). Every client flies its own copy of the rockets; the server's copies deal the damage.
    /// </summary>
    public class RocketLauncherWeapon : PlayerWeapon
    {
        const int RocketsPerVolley = 3;

        [SerializeField] RegeneratingAmmo rounds = new RegeneratingAmmo(8, 4f);
        [SerializeField] int damage = 1;
        [SerializeField] float blastRadius = 1.5f;
        [SerializeField] float fireInterval = 1f;
        [Tooltip("How far targets can be locked.")]
        [SerializeField] float lockRange = 25f;
        [Tooltip("Angle between the rockets of a volley as they leave the tube, degrees.")]
        [SerializeField] float fanAngle = 22f;
        [SerializeField] RocketFlight flight = RocketFlight.Default;
        [SerializeField] Transform muzzle;

        [Header("Visuals")]
        [SerializeField] Material bodyMaterial;
        [SerializeField] Material noseMaterial;
        [SerializeField] Material flameMaterial;
        [SerializeField] Material smokeMaterial;
        [SerializeField] Material fireMaterial;
        [SerializeField] Material coreMaterial;
        [SerializeField] float cameraShake = 0.15f;

        PlayerTargeting targeting;
        float nextFireTime;
        int volleyCount;
        readonly Dictionary<int, RocketProjectile> inFlight = new();

        public override WeaponId Id => WeaponId.RocketLauncher;
        public override bool UsesLockOn => true;
        public override int MaxLocks => RocketsPerVolley;
        public override float LockRange => lockRange;
        public override string StatusText => targeting != null && targeting.IsAiming
            ? $"{rounds.StatusText}  cele {targeting.LockedTargets.Count}/{RocketsPerVolley}"
            : rounds.StatusText;

        void Awake() => targeting = GetComponent<PlayerTargeting>();

        void Update()
        {
            if (IsSpawned && IsOwner) rounds.Tick(Time.deltaTime);
        }

        public override void OnNetworkDespawn()
        {
            foreach (var rocket in inFlight.Values)
                if (rocket != null) Destroy(rocket.gameObject);
            inFlight.Clear();
        }

        // ---------- Owner: firing ----------

        public override void TickAiming(bool firePressed, bool fireHeld)
        {
            if (!firePressed || Time.time < nextFireTime || !rounds.TryUse()) return;
            nextFireTime = Time.time + fireInterval;

            var targets = new NetworkObjectReference[RocketsPerVolley];
            int count = 0;
            foreach (var target in targeting.LockedTargets)
            {
                if (count == RocketsPerVolley) break;
                var networkObject = target != null ? target.GetComponentInParent<NetworkObject>() : null;
                if (networkObject != null && networkObject.IsSpawned) targets[count++] = networkObject;
            }

            Vector3 start = muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.5f;
            FireRpc(start, targeting.GetAimPoint(), count, targets[0], targets[1], targets[2], ++volleyCount);
            targeting.ClearLocks();
        }

        // ---------- Flight and blasts (every client) ----------

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
        void FireRpc(Vector3 start, Vector3 aimPoint, int count, NetworkObjectReference first, NetworkObjectReference second,
            NetworkObjectReference third, int volley)
        {
            // The server only lets volleys from the player's own shoulder through (slack for latency).
            if (IsServer && !IsOwner && (start - transform.position).sqrMagnitude > 9f) return;

            count = Mathf.Clamp(count, 0, RocketsPerVolley);
            var references = new[] { first, second, third };
            Vector3 forward = aimPoint - start;
            if (forward.sqrMagnitude < 0.01f) forward = transform.forward;
            Quaternion aim = Quaternion.LookRotation(forward.normalized);

            int rockets = Mathf.Max(1, count);
            for (int i = 0; i < rockets; i++)
            {
                Targetable target = null;
                if (i < count && references[i].TryGet(out NetworkObject targetObject))
                    target = targetObject.GetComponentInChildren<Targetable>();
                Vector3 spot = target != null ? target.AimPoint : aimPoint;

                // Fan the volley out of the tube (a single rocket goes straight), slightly upwards, then they curve in.
                float yaw = rockets == 1 ? 0f : Mathf.Lerp(-fanAngle, fanAngle, i / (float)(rockets - 1));
                Vector3 launch = aim * Quaternion.Euler(-10f, yaw, 0f) * Vector3.forward;

                int id = volley * RocketsPerVolley + i;
                inFlight[id] = RocketProjectile.Spawn(this, id, start, launch, target, spot, flight,
                    bodyMaterial, noseMaterial, flameMaterial, smokeMaterial);
            }
        }

        /// <summary>Called by this launcher's rocket copy on this client when it goes off.</summary>
        public void OnRocketImpact(int id, Vector3 point)
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
                PlayBlast(point); // the shooter sees it at once; the damage follows from the server
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
                target.ApplyDamage(damage, push, DamageTypes.Rocket);
            }
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        void BlastRpc(int id, Vector3 point)
        {
            if (inFlight.TryGetValue(id, out var rocket))
            {
                inFlight.Remove(id);
                if (rocket != null) Destroy(rocket.gameObject);
            }
            if (!IsOwner) PlayBlast(point);
        }

        void PlayBlast(Vector3 point) => ExplosionEffect.Spawn(point, blastRadius, fireMaterial, coreMaterial, cameraShake);
    }
}
