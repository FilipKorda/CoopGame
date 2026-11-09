using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoopGame
{
    /// <summary>
    /// Aim mode shared by every aimed weapon. Hold Aim (RMB / gamepad LT) to show the crosshair and strafe;
    /// putting the crosshair on a <see cref="Targetable"/> locks it (one target at a time, it stays locked
    /// until another one is hovered, it dies, goes out of range, or Aim is released). Weapons that allow more
    /// (<see cref="MaxLocks"/>, e.g. the rocket launcher's 3) keep adding hovered targets until the limit.
    /// Weapons read <see cref="IsAiming"/>, <see cref="CurrentTarget"/> and <see cref="GetAimPoint"/>;
    /// this class never fires anything itself. Owner-side only (local input and camera).
    /// </summary>
    [RequireComponent(typeof(PlayerMovement))]
    public class PlayerTargeting : NetworkBehaviour
    {
        public static PlayerTargeting Local { get; private set; }

        [SerializeField] float maxRange = 25f;
        [Tooltip("How far off the crosshair (degrees, plus the target's own size) a target can be and still get locked.")]
        [SerializeField] float lockAngle = 3f;
        [Tooltip("A locked target is dropped beyond maxRange times this.")]
        [SerializeField] float keepLockRangeFactor = 1.2f;
        [SerializeField] LayerMask lineOfSightMask = ~0;

        PlayerMovement movement;
        PlayerHealth health;
        InputAction aimAction;
        readonly List<Targetable> locks = new();

        public bool IsAiming { get; private set; }
        /// <summary>The most recently locked target (the only one, for single-lock weapons).</summary>
        public Targetable CurrentTarget { get; private set; }
        /// <summary>Every locked target, oldest first.</summary>
        public IReadOnlyList<Targetable> LockedTargets => locks;

        /// <summary>Set by the active weapon: how many targets it locks at once (1 for most).</summary>
        public int MaxLocks { get; set; } = 1;
        /// <summary>Lock-on reach right now: the active weapon's own, or the default.</summary>
        public float MaxRange => LockRangeOverride > 0f ? LockRangeOverride : maxRange;

        /// <summary>Set by the active weapon: homing weapons lock targets, point-and-shoot weapons don't.</summary>
        public bool LockOnEnabled { get; set; } = true;

        /// <summary>Set by the active weapon: its own lock-on reach (e.g. how far a grenade flies); 0 = default.</summary>
        public float LockRangeOverride { get; set; }

        /// <summary>Set by the active weapon: only the wrench locks onto beam sockets (it's the one that grabs them).</summary>
        public bool GraspablesLockable { get; set; } = true;

        /// <summary>Set by the active weapon: melee weapons don't aim at all.</summary>
        public bool AimAllowed { get; set; } = true;

        /// <summary>Raised on the owner when the locked target changes (null = no lock).</summary>
        public event Action<Targetable> TargetChanged;

        /// <summary>Ray through the middle of the screen, where the crosshair is.</summary>
        public Ray AimRay
        {
            get
            {
                var cam = Camera.main;
                return cam != null ? cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)) : new Ray(transform.position + Vector3.up * 1.5f, transform.forward);
            }
        }

        void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            health = GetComponent<PlayerHealth>();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner) return;
            Local = this;
            aimAction = InputSystem.actions.FindAction("Player/Aim", true);
        }

        public override void OnNetworkDespawn()
        {
            if (Local == this) Local = null;
        }

        void Update()
        {
            if (!IsSpawned || !IsOwner || PlayerSpawnPoints.Instance == null) return;

            bool canAim = AimAllowed && (health == null || health.IsAlive) && !movement.IsSwimming && !movement.IsHanging && !GameplayUI.IsMenuOpen;
            IsAiming = canAim && aimAction.IsPressed();

            // Strafe: keep facing where the camera looks while aiming.
            movement.FacingOverride = IsAiming ? Vector3.ProjectOnPlane(AimRay.direction, Vector3.up) : Vector3.zero;

            if (!IsAiming || !LockOnEnabled)
            {
                ClearLocks();
                return;
            }

            locks.RemoveAll(target => target == null || !CanKeepLock(target));

            int maxLocks = Mathf.Max(1, MaxLocks);
            var hovered = FindTargetUnderCrosshair();
            if (hovered != null && !locks.Contains(hovered))
            {
                if (maxLocks == 1) locks.Clear(); // single lock: the new target replaces the old one
                if (locks.Count < maxLocks) locks.Add(hovered);
            }
            while (locks.Count > maxLocks) locks.RemoveAt(0); // the weapon allows fewer now (switched)

            SetTarget(locks.Count > 0 ? locks[locks.Count - 1] : null);
        }

        /// <summary>Owner: drop every lock, e.g. after a multi-lock volley so the next one is painted afresh.</summary>
        public void ClearLocks()
        {
            locks.Clear();
            SetTarget(null);
        }

        /// <summary>
        /// Where an aimed weapon should go: the locked target, otherwise whatever is under the crosshair,
        /// otherwise a point far down the crosshair ray.
        /// </summary>
        public Vector3 GetAimPoint()
        {
            if (CurrentTarget != null) return CurrentTarget.AimPoint;

            Ray ray = AimRay;
            float far = maxRange + Vector3.Distance(ray.origin, transform.position);
            return RaycastIgnoringPlayers(ray, far, out RaycastHit hit) ? hit.point : ray.GetPoint(far);
        }

        bool Lockable(Targetable target) => target.IsTargetable && (GraspablesLockable || target.Graspable == null);

        Targetable FindTargetUnderCrosshair()
        {
            Ray ray = AimRay;
            Targetable best = null;
            float bestScore = lockAngle;
            float range = MaxRange;

            foreach (var target in Targetable.All)
            {
                if (!Lockable(target)) continue;
                if ((target.AimPoint - transform.position).sqrMagnitude > range * range) continue;

                Vector3 toTarget = target.AimPoint - ray.origin;
                float distance = toTarget.magnitude;
                if (distance < 0.01f) continue;

                // Angle off the crosshair, minus how big the target looks from here.
                float angularSize = Mathf.Atan2(target.Radius, distance) * Mathf.Rad2Deg;
                float score = Vector3.Angle(ray.direction, toTarget) - angularSize;
                if (score >= bestScore || !HasLineOfSight(ray.origin, target)) continue;

                bestScore = score;
                best = target;
            }
            return best;
        }

        bool CanKeepLock(Targetable target)
        {
            float keepRange = MaxRange * keepLockRangeFactor;
            return Lockable(target) && (target.AimPoint - transform.position).sqrMagnitude <= keepRange * keepRange;
        }

        bool HasLineOfSight(Vector3 from, Targetable target)
        {
            Vector3 to = target.AimPoint;
            var ray = new Ray(from, to - from);
            if (!RaycastIgnoringPlayers(ray, Vector3.Distance(from, to), out RaycastHit hit)) return true;
            // Hitting any part of the same object counts (e.g. the body of a heavy block around its handle).
            return hit.collider.GetComponentInParent<Targetable>() == target || hit.collider.transform.root == target.transform.root;
        }

        /// <summary>Nearest hit along the ray that isn't a player (the camera looks through our own capsule).</summary>
        bool RaycastIgnoringPlayers(Ray ray, float distance, out RaycastHit nearest)
        {
            nearest = default;
            float best = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(ray, distance, lineOfSightMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<PlayerMovement>() != null || hit.distance >= best) continue;
                best = hit.distance;
                nearest = hit;
            }
            return best < float.MaxValue;
        }

        void SetTarget(Targetable target)
        {
            if (CurrentTarget == target) return;
            CurrentTarget = target;
            TargetChanged?.Invoke(target);
        }
    }
}
