using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoopGame
{
    /// <summary>
    /// Boomerang throw: while aiming (<see cref="PlayerTargeting"/>), Attack throws the wrench at the locked target,
    /// or straight down the crosshair if nothing is locked. It comes back on its own; until it's caught the player
    /// has no wrench and can't attack at all. The thrower simulates the flight and reports hits; the server
    /// validates them. Other clients replay the same flight from the throw RPC.
    /// </summary>
    [RequireComponent(typeof(PlayerTargeting))]
    [RequireComponent(typeof(PlayerMelee))]
    public class WrenchThrow : NetworkBehaviour
    {
        [Serializable]
        public struct FlightSettings
        {
            public float ThrowSpeed;
            public float ReturnSpeed;
            public float ReturnAcceleration;
            [Tooltip("How far a throw without a locked target flies before turning back.")]
            public float MaxDistance;
            [Tooltip("Safety: give up on a target after this long and come back.")]
            public float MaxOutboundTime;
            public float HitRadius;
            public float CatchRadius;
            public float SpinSpeed;
        }

        [SerializeField] int damage = 1;
        [Tooltip("Extra distance the server accepts between the thrower and what the wrench hit.")]
        [SerializeField] float serverRangeTolerance = 5f;
        [SerializeField] FlightSettings flight = new()
        {
            ThrowSpeed = 22f,
            ReturnSpeed = 16f,
            ReturnAcceleration = 40f,
            MaxDistance = 18f,
            MaxOutboundTime = 2f,
            HitRadius = 0.35f,
            CatchRadius = 0.6f,
            SpinSpeed = 1080f,
        };

        PlayerTargeting targeting;
        PlayerMelee melee;
        PlayerMovement movement;
        PlayerHealth health;
        PlayerWeapons weapons;
        WrenchGrab grab;
        InputAction attackAction;
        ThrownWrench inFlight;

        /// <summary>False while the wrench is flying; no attacks of any kind until it's back.</summary>
        public bool HasWrench => inFlight == null;

        void Awake()
        {
            targeting = GetComponent<PlayerTargeting>();
            melee = GetComponent<PlayerMelee>();
            movement = GetComponent<PlayerMovement>();
            health = GetComponent<PlayerHealth>();
            weapons = GetComponent<PlayerWeapons>();
            grab = GetComponent<WrenchGrab>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner) attackAction = InputSystem.actions.FindAction("Player/Attack", true);
        }

        public override void OnNetworkDespawn()
        {
            if (inFlight != null) Destroy(inFlight.gameObject);
            inFlight = null;
        }

        void Update()
        {
            if (!IsSpawned || !IsOwner || !HasWrench || !targeting.IsAiming) return;
            // Aim + Attack throws only while the wrench is the active weapon; otherwise the secondary weapon fires.
            if (weapons != null && !weapons.MainActive) return;
            // A locked grabbable object gets the magnetic beam (WrenchGrab) instead of a throw.
            if (targeting.CurrentTarget != null && targeting.CurrentTarget.Graspable != null) return;
            if (grab != null && grab.IsGrabbing) return;

            bool canThrow = (health == null || health.IsAlive) && !GameplayUI.IsMenuOpen
                && !movement.IsSwimming && !movement.IsHanging && !movement.IsSlamming;
            if (canThrow && attackAction.WasPressedThisFrame()) Throw();
        }

        void Throw()
        {
            Vector3 origin = melee.WeaponPivot.position;
            Targetable target = targeting.CurrentTarget;
            Vector3 aimPoint = target != null ? target.AimPoint : targeting.GetAimPoint();
            Vector3 direction = aimPoint - origin;
            if (direction.sqrMagnitude < 0.01f) direction = transform.forward;
            direction.Normalize();

            StartFlight(origin, target, direction, reportsHits: true);

            // Only networked targets can be named to other clients; anything else is replayed as a straight throw.
            NetworkObject targetObject = target != null ? target.GetComponent<NetworkObject>() : null;
            if (targetObject != null && targetObject.IsSpawned) ThrowRpc(origin, true, targetObject, direction);
            else ThrowRpc(origin, false, default, direction);
        }

        void StartFlight(Vector3 origin, Targetable target, Vector3 direction, bool reportsHits)
        {
            if (inFlight != null) Destroy(inFlight.gameObject);
            // PlayerWeapons hides the held wrench while HasWrench is false.
            inFlight = ThrownWrench.Launch(this, melee.WeaponPivot, origin, target, direction, reportsHits, flight);
        }

        [Rpc(SendTo.NotMe, InvokePermission = RpcInvokePermission.Owner)]
        void ThrowRpc(Vector3 origin, bool hasTarget, NetworkObjectReference targetRef, Vector3 direction)
        {
            Targetable target = null;
            if (hasTarget && targetRef.TryGet(out NetworkObject targetObject)) target = targetObject.GetComponent<Targetable>();
            StartFlight(origin, target, direction, reportsHits: false);
        }

        /// <summary>Called by the thrower's own flying wrench.</summary>
        internal void ReportHit(Damageable damageable, Vector3 direction)
        {
            if (!IsOwner || damageable == null || !damageable.IsSpawned) return;
            ThrowHitRpc(damageable.NetworkObject, Vector3.ProjectOnPlane(direction, Vector3.up).normalized);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void ThrowHitRpc(NetworkObjectReference targetRef, Vector3 direction)
        {
            if (!targetRef.TryGet(out NetworkObject targetObject) || !targetObject.TryGetComponent(out Damageable target)) return;

            float maxDistance = Mathf.Max(flight.MaxDistance, targeting.MaxRange) + serverRangeTolerance;
            if (Vector3.Distance(targetObject.transform.position, transform.position) > maxDistance) return;

            target.ApplyDamage(damage, direction);
        }

        internal void OnWrenchCaught(ThrownWrench wrench)
        {
            if (wrench == inFlight) inFlight = null;
        }
    }
}
