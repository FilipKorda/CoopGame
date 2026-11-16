using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Throws an <see cref="AllyRobot"/>, folded up, like a grenade but further. Hold Aim to see the arc and, where it
    /// lands, the circle its charm spell covers. No lock-on: it goes where the crosshair points, within reach
    /// (nothing in reach: the furthest spot straight ahead; see <see cref="ThrowAim"/>). Attack throws it; the robot
    /// unfolds where it comes down (on the ground in front if it hits a wall or an enemy). One charge, which comes back with time (<see cref="RegeneratingAmmo"/>). Each player
    /// has one robot out at most: a new one sends the old one away. Every client flies its own copy of the throw; the
    /// server's copy decides where the robot is spawned.
    /// </summary>
    public class RobotWeapon : PlayerWeapon, IGrenadeThrower
    {
        [SerializeField] RegeneratingAmmo charges = new RegeneratingAmmo(1, 20f);
        [Tooltip("Furthest landing spot, horizontal distance from the player (further than a grenade).")]
        [SerializeField] float throwRange = 28f;
        [SerializeField] float gravity = 25f;
        [Tooltip("Flight time for the nearest / furthest throw: far throws lob higher.")]
        [SerializeField] Vector2 flightTime = new Vector2(0.6f, 1.5f);
        [SerializeField] float capsuleRadius = 0.25f;
        [Tooltip("Where the robot leaves the hand.")]
        [SerializeField] Transform throwPoint;
        [SerializeField] GameObject robotPrefab;

        [Header("Visuals")]
        [SerializeField] Material previewMaterial;
        [SerializeField] Material capsuleMaterial;
        [SerializeField] Material capsuleBandMaterial;

        PlayerTargeting targeting;
        bool aimedThisFrame;
        int throwCount;
        ThrowAim aim; // owner, made on first use
        readonly Dictionary<int, GrenadeProjectile> inFlight = new();
        readonly Dictionary<int, Vector3> throwDirections = new();
        NetworkObject activeRobot; // server

        public override WeaponId Id => WeaponId.CharmRobot;
        public override string StatusText => charges.StatusText; // no lock-on: thrown where the crosshair points

        float CharmRange
        {
            get
            {
                var robot = robotPrefab != null ? robotPrefab.GetComponent<AllyRobot>() : null;
                return robot != null ? robot.CharmRange : 10f;
            }
        }

        void Awake() => targeting = GetComponent<PlayerTargeting>();

        void Update()
        {
            if (IsSpawned && IsOwner) charges.Tick(Time.deltaTime);
        }

        public override void OnNetworkDespawn()
        {
            aim?.Destroy();
            foreach (var capsule in inFlight.Values)
                if (capsule != null) Destroy(capsule.gameObject);
            inFlight.Clear();
            // The player left: take their robot with them (not while the whole session shuts down).
            if (IsServer && !NetworkManager.ShutdownInProgress && activeRobot != null && activeRobot.IsSpawned) activeRobot.Despawn(true);
        }

        // ---------- Owner: aiming and throwing ----------

        public override void TickAiming(bool firePressed, bool fireHeld)
        {
            if (charges.IsEmpty) return; // nothing to throw: no arc either (the HUD says so)

            aimedThisFrame = true;
            aim ??= new ThrowAim(transform, targeting, throwRange, gravity, flightTime, capsuleRadius, CharmRange, previewMaterial, "RobotAimPreview");
            Vector3 start = throwPoint != null ? throwPoint.position : transform.position + Vector3.up * 1.5f;
            Vector3 velocity = aim.LaunchVelocity(start, aim.LandingSpot(), out float flight);
            aim.ShowPreview(start, velocity, flight);

            if (firePressed && charges.TryUse()) ThrowRpc(start, velocity, ++throwCount);
        }

        void LateUpdate()
        {
            if (!IsOwner) return;
            if (!aimedThisFrame) aim?.HidePreview();
            aimedThisFrame = false;
        }

        // ---------- Flight (every client) and landing (server) ----------

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
        void ThrowRpc(Vector3 start, Vector3 velocity, int id)
        {
            // The server only lets throws from the player's own hand through (slack for latency).
            if (IsServer && !IsOwner && ((start - transform.position).sqrMagnitude > 9f || velocity.sqrMagnitude > 60f * 60f)) return;
            throwDirections[id] = Vector3.ProjectOnPlane(velocity, Vector3.up);
            inFlight[id] = GrenadeProjectile.Spawn(this, id, start, velocity, gravity, capsuleRadius, capsuleMaterial, capsuleBandMaterial, size: 1.5f);
        }

        /// <summary>This client's copy of the thrown robot touched down; the server's copy unfolds the robot there.</summary>
        public void OnGrenadeImpact(int id, Vector3 point)
        {
            inFlight.Remove(id);
            throwDirections.Remove(id, out Vector3 direction);
            if (IsServer) SpawnRobot(LandingGround(point, direction), direction);
        }

        /// <summary>
        /// Ground to stand on where it came down: stepped back a little along the throw (so a wall hit lands it in
        /// front of the wall) and dropped down past crates and enemies onto the floor.
        /// </summary>
        static Vector3 LandingGround(Vector3 point, Vector3 direction)
        {
            Vector3 back = direction.sqrMagnitude > 0.0001f ? -direction.normalized * 0.6f : Vector3.zero;
            Vector3 from = point + back + Vector3.up * 2f;
            Vector3 ground = point;
            float best = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(from, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.distance >= best || hit.collider is CharacterController) continue;
                if (hit.collider.GetComponentInParent<PlayerMovement>() != null || hit.collider.GetComponentInParent<Damageable>() != null) continue;
                best = hit.distance;
                ground = hit.point;
            }
            return ground;
        }

        void SpawnRobot(Vector3 spot, Vector3 direction)
        {
            if (robotPrefab == null) return;
            if (activeRobot != null && activeRobot.IsSpawned) activeRobot.Despawn(true); // one robot per player

            float yaw = direction.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(direction).eulerAngles.y : transform.eulerAngles.y;
            var robot = Instantiate(robotPrefab, spot, Quaternion.Euler(0f, yaw, 0f));
            activeRobot = robot.GetComponent<NetworkObject>();
            activeRobot.Spawn(destroyWithScene: true);
        }
    }
}
