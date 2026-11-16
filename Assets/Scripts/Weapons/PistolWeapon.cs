using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Simple pistol: fires glowing bullets straight at whatever is under the crosshair (no lock-on), 25 per
    /// magazine, unlimited ammo, reloads by itself when empty. Bullets are real projectiles — fast, but you can see
    /// them fly — and hurt what they reach. The shooter's copy of each bullet decides what it hit and reports it;
    /// the server validates. Everyone else flies their own copy and stops it where the shooter says.
    /// </summary>
    public class PistolWeapon : PlayerWeapon
    {
        [SerializeField] int damage = 1;
        [SerializeField] int magazineSize = 25;
        [SerializeField] float fireInterval = 0.22f;
        [SerializeField] float reloadTime = 1.2f;
        [SerializeField] float range = 40f;
        [SerializeField] float bulletSpeed = 45f;
        [SerializeField] float bulletRadius = 0.12f;
        [Tooltip("Extra distance the server accepts between the shooter and what got hit.")]
        [SerializeField] float serverRangeTolerance = 5f;
        [SerializeField] Transform muzzle;
        [Tooltip("Muzzle flash.")]
        [SerializeField] Material tracerMaterial;
        [SerializeField] Material bulletMaterial;

        PlayerTargeting targeting;
        int ammo;
        float nextShotTime;
        float reloadDoneTime = float.NegativeInfinity;
        bool reloading;
        int shotCount;
        readonly Dictionary<int, PistolBullet> inFlight = new();

        public override WeaponId Id => WeaponId.Pistol;
        public override string StatusText => reloading ? "przeładowanie..." : $"{ammo}/{magazineSize}";

        void Awake()
        {
            targeting = GetComponent<PlayerTargeting>();
            ammo = magazineSize;
        }

        public override void OnNetworkDespawn()
        {
            foreach (var bullet in inFlight.Values)
                if (bullet != null) Destroy(bullet.gameObject);
            inFlight.Clear();
        }

        public override void TickEquipped()
        {
            if (reloading && Time.time >= reloadDoneTime)
            {
                reloading = false;
                ammo = magazineSize;
            }
        }

        public override void TickAiming(bool firePressed, bool fireHeld)
        {
            if (!firePressed || reloading || Time.time < nextShotTime) return;
            Shoot();
        }

        void Shoot()
        {
            nextShotTime = Time.time + fireInterval;
            ammo--;
            if (ammo <= 0)
            {
                reloading = true;
                reloadDoneTime = Time.time + reloadTime;
            }

            // The crosshair picks the point; the bullet still has to get there from the muzzle.
            Vector3 from = muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.2f;
            Vector3 direction = (targeting.GetAimPoint() - from).normalized;
            SpawnBullet(from, direction, ++shotCount);
            FireRpc(from, direction, shotCount);
        }

        void SpawnBullet(Vector3 from, Vector3 direction, int id)
        {
            ShotEffects.Spawn(from, from + direction * 0.3f, tracerMaterial); // muzzle flash
            inFlight[id] = PistolBullet.Spawn(this, id, from, direction, bulletSpeed, bulletRadius, range, bulletMaterial);
        }

        [Rpc(SendTo.NotMe, InvokePermission = RpcInvokePermission.Owner)]
        void FireRpc(Vector3 from, Vector3 direction, int id) => SpawnBullet(from, direction, id);

        /// <summary>This client's copy of a bullet stopped (hit something or flew its full range).</summary>
        public void OnBulletEnd(int id, Vector3 point, Collider hitCollider, Vector3 direction)
        {
            inFlight.Remove(id);
            if (hitCollider != null) ShotEffects.Sparks(point, bulletMaterial);
            if (!IsOwner) return;

            // The shooter's copy decides: report the hit and stop everyone else's copy at the same spot.
            var damageable = hitCollider != null ? hitCollider.GetComponentInParent<Damageable>() : null;
            if (damageable != null && damageable.IsSpawned && !damageable.IsDead)
                HitRpc(damageable.NetworkObject, Vector3.ProjectOnPlane(direction, Vector3.up).normalized);
            EndRpc(id, point, hitCollider != null);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void HitRpc(NetworkObjectReference targetRef, Vector3 direction)
        {
            if (!targetRef.TryGet(out NetworkObject targetObject) || !targetObject.TryGetComponent(out Damageable target)) return;
            if (Vector3.Distance(targetObject.transform.position, transform.position) > range + serverRangeTolerance) return;
            target.ApplyDamage(damage, direction);
        }

        [Rpc(SendTo.NotMe, InvokePermission = RpcInvokePermission.Owner)]
        void EndRpc(int id, Vector3 point, bool hitSomething)
        {
            if (!inFlight.TryGetValue(id, out var bullet)) return; // this copy already stopped by itself
            inFlight.Remove(id);
            if (bullet != null) Destroy(bullet.gameObject);
            if (hitSomething) ShotEffects.Sparks(point, bulletMaterial);
        }
    }
}
