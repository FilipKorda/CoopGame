using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Shotgun: each shot is 6 pellets spread in a cone towards the crosshair (no lock-on), very short reach.
    /// Every pellet that lands deals its damage, so point blank all 6 hit and up close it hits hard; further out
    /// the spread lets most of them miss. Whatever gets hit is blown back much harder than by other weapons.
    /// 10 shells, one comes back every few seconds (<see cref="RegeneratingAmmo"/>). The shooter traces the
    /// pellets and reports one hit per target with its pellet count; the server validates it. Everyone sees the
    /// tracers.
    /// </summary>
    public class ShotgunWeapon : PlayerWeapon
    {
        const int MaxPellets = 16;

        [SerializeField] RegeneratingAmmo shells = new RegeneratingAmmo(10, 2f);
        [Tooltip("Damage per pellet that lands.")]
        [SerializeField] int damage = 1;
        [SerializeField, Range(1, MaxPellets)] int pellets = 6;
        [Tooltip("Half-angle of the spread cone, degrees.")]
        [SerializeField] float spreadAngle = 7f;
        [SerializeField] float range = 9f;
        [Tooltip("Knockback compared to a normal hit (1 = like the wrench).")]
        [SerializeField] float knockback = 1.8f;
        [SerializeField] float fireInterval = 0.75f;
        [Tooltip("Extra distance the server accepts between the shooter and what got hit.")]
        [SerializeField] float serverRangeTolerance = 4f;
        [SerializeField] Transform muzzle;
        [SerializeField] Material tracerMaterial;

        PlayerTargeting targeting;
        float nextShotTime;
        readonly Dictionary<Damageable, int> pelletHits = new();

        public override WeaponId Id => WeaponId.Shotgun;
        public override string StatusText => shells.StatusText;

        void Awake() => targeting = GetComponent<PlayerTargeting>();

        void Update()
        {
            if (IsSpawned && IsOwner) shells.Tick(Time.deltaTime);
        }

        public override void TickAiming(bool firePressed, bool fireHeld)
        {
            if (!firePressed || Time.time < nextShotTime || !shells.TryUse()) return;
            nextShotTime = Time.time + fireInterval;
            Shoot();
        }

        void Shoot()
        {
            Vector3 from = muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.2f;
            // Pellets are traced from the player's own axis at muzzle height: point blank, the muzzle can already be
            // inside the enemy, and a ray never hits the collider it starts in. (Our own capsule is skipped.)
            Vector3 traceFrom = new Vector3(transform.position.x, from.y, transform.position.z);
            Vector3 aim = (targeting.GetAimPoint() - traceFrom).normalized;
            Quaternion aimRotation = Quaternion.LookRotation(aim);

            var ends = new Vector3[pellets];
            pelletHits.Clear();
            for (int i = 0; i < pellets; i++)
            {
                // Uniform over the cone's disc, so the pattern isn't bunched up in the middle.
                Vector2 offset = Random.insideUnitCircle * Mathf.Tan(spreadAngle * Mathf.Deg2Rad);
                Vector3 direction = (aimRotation * new Vector3(offset.x, offset.y, 1f)).normalized;
                ends[i] = traceFrom + direction * range;

                if (!RaycastIgnoringPlayers(traceFrom, direction, range, out RaycastHit hit)) continue;
                ends[i] = hit.point;
                var damageable = hit.collider.GetComponentInParent<Damageable>();
                if (damageable == null || !damageable.IsSpawned || damageable.IsDead) continue;
                pelletHits.TryGetValue(damageable, out int count);
                pelletHits[damageable] = count + 1;
            }

            // One report per target: all its pellets' damage and a single big shove.
            Vector3 push = Vector3.ProjectOnPlane(aim, Vector3.up).normalized;
            foreach (var pair in pelletHits)
                HitRpc(pair.Key.NetworkObject, pair.Value, push);

            SpawnTracers(from, ends);
            ShotRpc(from, ends);
        }

        static bool RaycastIgnoringPlayers(Vector3 from, Vector3 direction, float distance, out RaycastHit nearest)
        {
            nearest = default;
            float best = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(from, direction, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<PlayerMovement>() != null || hit.distance >= best) continue;
                best = hit.distance;
                nearest = hit;
            }
            return best < float.MaxValue;
        }

        void SpawnTracers(Vector3 from, Vector3[] ends)
        {
            foreach (var end in ends) ShotEffects.Spawn(from, end, tracerMaterial);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void HitRpc(NetworkObjectReference targetRef, int pelletCount, Vector3 direction)
        {
            if (!targetRef.TryGet(out NetworkObject targetObject) || !targetObject.TryGetComponent(out Damageable target)) return;
            if (Vector3.Distance(targetObject.transform.position, transform.position) > range + serverRangeTolerance) return;
            int landed = Mathf.Clamp(pelletCount, 1, pellets);
            target.ApplyDamage(landed * damage, direction.normalized * knockback);
        }

        [Rpc(SendTo.NotMe, InvokePermission = RpcInvokePermission.Owner)]
        void ShotRpc(Vector3 from, Vector3[] ends) => SpawnTracers(from, ends);
    }
}
