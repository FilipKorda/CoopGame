using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoopGame
{
    /// <summary>
    /// Wrench attacks on the Attack action (LMB / gamepad West): a horizontal swing, or — in the air after a
    /// double jump — a slam that drives the player into the ground and hits everything around the impact.
    /// The owner attacks instantly and detects hits locally for responsiveness; the server validates each
    /// hit and applies the damage. Other clients just replay the animations.
    /// </summary>
    [RequireComponent(typeof(PlayerMovement))]
    public class PlayerMelee : NetworkBehaviour
    {
        [Header("Attack")]
        [SerializeField] int damage = 1;
        [SerializeField] float cooldown = 0.32f;
        [Tooltip("Seconds into the swing when the hit is checked.")]
        [SerializeField] float hitTime = 0.07f;
        [SerializeField] float hitRadius = 1.2f;
        [Tooltip("How far in front of the player the hit sphere sits.")]
        [SerializeField] float hitReach = 1.1f;
        [SerializeField] float hitHeight = 1f;
        [SerializeField] LayerMask hitMask = ~0;
        [Tooltip("Extra distance the server accepts to cover latency between owner and server positions.")]
        [SerializeField] float serverReachTolerance = 2.5f;
        [SerializeField, Range(0f, 1f)] float moveSpeedWhileSwinging = 0.35f;

        [Header("Swing Visual")]
        [SerializeField] Transform weaponPivot;
        [SerializeField] float swingDuration = 0.2f;
        [SerializeField] float swingStartYaw = 110f;
        [SerializeField] float swingEndYaw = -110f;
        [SerializeField] float swingPitch = 10f;
        [SerializeField] Vector3 idleEuler = new Vector3(70f, 20f, 0f);
        [SerializeField] float returnSharpness = 15f;

        [Header("Slam (Attack after a double jump)")]
        [SerializeField] int slamDamage = 2;
        [SerializeField] float slamRadius = 2.5f;
        [Tooltip("Extra distance the server accepts between the attacker and a slam target.")]
        [SerializeField] float slamServerTolerance = 2f;
        [Tooltip("Wrench raised over the head while hanging in the air.")]
        [SerializeField] Vector3 slamRaisedEuler = new Vector3(-120f, 0f, 0f);
        [Tooltip("Wrench driven straight down into the ground.")]
        [SerializeField] Vector3 slamStrikeEuler = new Vector3(85f, 0f, 0f);
        [SerializeField] float slamStrikeSwingTime = 0.08f;
        [SerializeField] float slamImpactHold = 0.3f;
        [SerializeField] float slamCameraShake = 0.35f;

        PlayerMovement movement;
        PlayerHealth health;
        PlayerTargeting targeting;
        WrenchThrow wrenchThrow;
        PlayerWeapons weapons;
        WrenchGrab grab;
        InputAction attackAction;
        float swingStartTime = float.NegativeInfinity;
        bool hitChecked = true;
        readonly HashSet<Damageable> hitThisSwing = new();

        // Slam visual state, on every instance.
        enum SlamVisual { None, Raised, Strike, Impact }
        SlamVisual slamVisual;
        float slamVisualStart;
        const float SlamRaiseTime = 0.12f;

        bool IsSwinging => Time.time - swingStartTime < swingDuration;

        /// <summary>The held wrench (its meshes are children); thrown copies are made from it.</summary>
        public Transform WeaponPivot => weaponPivot;

        void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            health = GetComponent<PlayerHealth>();
            targeting = GetComponent<PlayerTargeting>();
            wrenchThrow = GetComponent<WrenchThrow>();
            weapons = GetComponent<PlayerWeapons>();
            grab = GetComponent<WrenchGrab>();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner) return;
            attackAction = InputSystem.actions.FindAction("Player/Attack", true);
            movement.SlamLanded += OnSlamLanded;
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner) movement.SlamLanded -= OnSlamLanded;
        }

        void Update()
        {
            UpdateWeaponVisual();
            if (!IsSpawned || !IsOwner || PlayerSpawnPoints.Instance == null) return;

            // No wrench in hand (it's been thrown) means no attacks; while aiming, Attack throws instead.
            // A melee secondary weapon in hand takes over close-range attacks (PlayerWeapons drives it).
            bool hasWrench = wrenchThrow == null || wrenchThrow.HasWrench;
            bool aiming = targeting != null && targeting.IsAiming;
            bool meleeSecondaryActive = weapons != null && weapons.ActiveSecondary != null && weapons.ActiveSecondary.IsMelee;
            bool grabbing = grab != null && grab.IsGrabbing;
            bool canAttack = (health == null || health.IsAlive) && hasWrench && !aiming && !meleeSecondaryActive && !grabbing && !GameplayUI.IsMenuOpen
                && !movement.IsHanging && !movement.IsSlamming && !movement.IsSwimming;
            if (canAttack && attackAction.WasPressedThisFrame())
            {
                // Attack without aiming is the wrench: pull it out if a ranged secondary weapon was in hand.
                if (weapons != null) weapons.SelectMain();

                if (movement.CanSlam)
                {
                    movement.StartSlam();
                    BeginSlamVisual();
                    PlaySlamRpc();
                }
                else if (Time.time - swingStartTime >= cooldown)
                {
                    BeginSwing();
                    hitChecked = false;
                    if (movement.IsGrounded) movement.SlowDown(moveSpeedWhileSwinging, swingDuration);
                    PlaySwingRpc();
                }
            }

            if (!hitChecked && Time.time - swingStartTime >= hitTime)
            {
                hitChecked = true;
                CheckHits();
            }
        }

        void BeginSwing() => swingStartTime = Time.time;

        void CheckHits()
        {
            Vector3 center = HitCenter();
            hitThisSwing.Clear();

            foreach (var collider in Physics.OverlapSphere(center, hitRadius, hitMask, QueryTriggerInteraction.Ignore))
            {
                var target = collider.GetComponentInParent<Damageable>();
                if (target == null || !target.IsSpawned || target.IsDead || !hitThisSwing.Add(target)) continue;

                Vector3 direction = Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up);
                if (direction.sqrMagnitude < 0.0001f) direction = transform.forward;
                HitRpc(target.NetworkObject, direction.normalized);
            }
        }

        Vector3 HitCenter() => transform.position + Vector3.up * hitHeight + transform.forward * hitReach;

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void HitRpc(NetworkObjectReference targetRef, Vector3 direction)
        {
            if (!targetRef.TryGet(out NetworkObject targetObject)) return;
            if (!targetObject.TryGetComponent(out Damageable target)) return;

            float maxDistance = hitReach + hitRadius + serverReachTolerance;
            if (Vector3.Distance(targetObject.transform.position, transform.position) > maxDistance) return;

            target.ApplyDamage(damage, direction);
        }

        [Rpc(SendTo.NotMe, InvokePermission = RpcInvokePermission.Owner)]
        void PlaySwingRpc() => BeginSwing();

        // ---------- Slam ----------

        void OnSlamLanded(Vector3 impact)
        {
            SlamImpactRpc(impact);

            hitThisSwing.Clear();
            foreach (var collider in Physics.OverlapSphere(impact + Vector3.up * 0.5f, slamRadius, hitMask, QueryTriggerInteraction.Ignore))
            {
                var target = collider.GetComponentInParent<Damageable>();
                if (target == null || !target.IsSpawned || target.IsDead || !hitThisSwing.Add(target)) continue;

                Vector3 direction = Vector3.ProjectOnPlane(target.transform.position - impact, Vector3.up);
                if (direction.sqrMagnitude < 0.0001f) direction = transform.forward;
                SlamHitRpc(target.NetworkObject, direction.normalized);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void SlamHitRpc(NetworkObjectReference targetRef, Vector3 direction)
        {
            if (!targetRef.TryGet(out NetworkObject targetObject)) return;
            if (!targetObject.TryGetComponent(out Damageable target)) return;

            // Planar check: the server may still see the attacker partway up the fall, but the slam is straight down.
            Vector3 offset = Vector3.ProjectOnPlane(targetObject.transform.position - transform.position, Vector3.up);
            if (offset.magnitude > slamRadius + slamServerTolerance) return;

            target.ApplyDamage(slamDamage, direction);
        }

        [Rpc(SendTo.NotMe, InvokePermission = RpcInvokePermission.Owner)]
        void PlaySlamRpc() => BeginSlamVisual();

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
        void SlamImpactRpc(Vector3 impact)
        {
            slamVisual = SlamVisual.Impact;
            slamVisualStart = Time.time;
            SlamShockwave.Spawn(impact, slamRadius, WeaponMaterial());

            var rig = ThirdPersonCameraRig.Instance;
            if (rig != null && rig.Target != null)
            {
                // Full shake for the slammer, fading out for players further away.
                float distance = Vector3.Distance(rig.Target.transform.position, impact);
                float falloff = Mathf.Clamp01(1f - distance / (slamRadius * 4f));
                if (falloff > 0f) rig.AddShake(slamCameraShake * falloff, 0.25f);
            }
        }

        void BeginSlamVisual()
        {
            slamVisual = SlamVisual.Raised;
            slamVisualStart = Time.time;
        }

        Material WeaponMaterial()
        {
            var weaponRenderer = weaponPivot != null ? weaponPivot.GetComponentInChildren<Renderer>() : null;
            return weaponRenderer != null ? weaponRenderer.sharedMaterial : null;
        }

        // ---------- Visuals ----------

        void UpdateWeaponVisual()
        {
            if (weaponPivot == null) return;

            if (UpdateSlamVisual()) return;

            float t = (Time.time - swingStartTime) / swingDuration;
            if (t < 1f)
            {
                float eased = 1f - (1f - t) * (1f - t);
                float yaw = Mathf.Lerp(swingStartYaw, swingEndYaw, eased);
                weaponPivot.localRotation = Quaternion.Euler(swingPitch, yaw, 0f);
            }
            else
            {
                Quaternion idle = Quaternion.Euler(idleEuler);
                weaponPivot.localRotation = Quaternion.Slerp(weaponPivot.localRotation, idle, 1f - Mathf.Exp(-returnSharpness * Time.deltaTime));
            }
        }

        /// <summary>Raised overhead → swung straight down while falling → held in the ground after impact.</summary>
        bool UpdateSlamVisual()
        {
            float elapsed = Time.time - slamVisualStart;
            switch (slamVisual)
            {
                case SlamVisual.Raised:
                    weaponPivot.localRotation = Quaternion.Slerp(weaponPivot.localRotation, Quaternion.Euler(slamRaisedEuler), 1f - Mathf.Exp(-30f * Time.deltaTime));
                    if (elapsed >= SlamRaiseTime)
                    {
                        slamVisual = SlamVisual.Strike;
                        slamVisualStart = Time.time;
                    }
                    return true;

                case SlamVisual.Strike:
                    float t = Mathf.Clamp01(elapsed / slamStrikeSwingTime);
                    weaponPivot.localRotation = Quaternion.Slerp(Quaternion.Euler(slamRaisedEuler), Quaternion.Euler(slamStrikeEuler), t);
                    // Safety net if the impact never arrives (e.g. fell off the map).
                    if (elapsed > 3f) slamVisual = SlamVisual.None;
                    return true;

                case SlamVisual.Impact:
                    weaponPivot.localRotation = Quaternion.Euler(slamStrikeEuler);
                    if (elapsed >= slamImpactHold) slamVisual = SlamVisual.None;
                    return true;
            }
            return false;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(HitCenter(), hitRadius);
        }
    }
}
