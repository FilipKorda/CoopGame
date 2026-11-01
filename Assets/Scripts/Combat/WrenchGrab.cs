using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoopGame
{
    /// <summary>
    /// The wrench's magnetic beam. With the wrench in hand, aim (RMB) at a <see cref="Graspable"/> so it gets locked,
    /// then hold Attack (LMB): the beam latches on and the move keys steer the object while the player stands still.
    /// Letting go of Attack releases it. Getting hit, downed, switching weapons or walking out of range lets go too.
    /// The server decides who holds what (one holder per object) and moves the object; this only sends input.
    /// </summary>
    [RequireComponent(typeof(PlayerTargeting))]
    [RequireComponent(typeof(PlayerMelee))]
    public class WrenchGrab : NetworkBehaviour
    {
        [Tooltip("Max distance from the player to the object's socket when grabbing.")]
        [SerializeField] float grabRange = 16f;
        [Tooltip("How often steering input is sent to the server while holding (seconds).")]
        [SerializeField] float inputSendInterval = 0.05f;
        [Tooltip("Give up if the server hasn't confirmed the grab after this long (someone else got it first).")]
        [SerializeField] float confirmTimeout = 0.5f;

        PlayerTargeting targeting;
        PlayerMelee melee;
        PlayerMovement movement;
        PlayerHealth health;
        PlayerWeapons weapons;
        WrenchThrow thrower;
        InputAction attackAction, moveAction;

        Graspable requested;
        int lastHealth;
        float requestTime;
        bool confirmed;
        Vector2 lastSentMove;
        float lastSentYaw;
        float lastSendTime;
        bool latched;
        Vector2 latchedMove;
        float latchedYaw;

        /// <summary>Owner: true from the moment a grab is requested until it ends.</summary>
        public bool IsGrabbing => requested != null;

        /// <summary>Owner, test builds: the grab is frozen and keeps holding/steering without input.</summary>
        public bool IsLatched => latched;

        /// <summary>
        /// Test helper (one person testing co-op in two windows): freezes the current grab with the steering held
        /// right now, so it keeps going without LMB, even after this window loses focus. Call again to unfreeze;
        /// it then lets go unless LMB is held. Returns false if there's nothing to freeze.
        /// </summary>
        public bool ToggleLatch()
        {
            if (latched)
            {
                latched = false;
                return true;
            }
            if (requested == null || !confirmed) return false;
            latched = true;
            latchedMove = CurrentMove();
            latchedYaw = CurrentYaw();
            return true;
        }

        void Awake()
        {
            targeting = GetComponent<PlayerTargeting>();
            melee = GetComponent<PlayerMelee>();
            movement = GetComponent<PlayerMovement>();
            health = GetComponent<PlayerHealth>();
            weapons = GetComponent<PlayerWeapons>();
            thrower = GetComponent<WrenchThrow>();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner) return;
            attackAction = InputSystem.actions.FindAction("Player/Attack", true);
            moveAction = InputSystem.actions.FindAction("Player/Move", true);
            if (health != null)
            {
                lastHealth = health.Current;
                health.HealthChanged += OnHealthChanged;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner && health != null) health.HealthChanged -= OnHealthChanged;
            if (IsOwner) EndGrab(notifyServer: false);
        }

        void OnHealthChanged(int current, int max)
        {
            // Taking a hit breaks the beam (HealthChanged also fires on heals; only damage counts).
            if (current < lastHealth) EndGrab(notifyServer: true);
            lastHealth = current;
        }

        void Update()
        {
            if (!IsSpawned || !IsOwner || PlayerSpawnPoints.Instance == null) return;

            bool wrenchReady = (weapons == null || weapons.MainActive) && (thrower == null || thrower.HasWrench);
            bool canHold = wrenchReady && (health == null || health.IsAlive) && !movement.IsSwimming && !movement.IsHanging
                && (latched || (!GameplayUI.IsMenuOpen && attackAction.IsPressed()));

            if (requested == null)
            {
                TryStartGrab(wrenchReady);
            }
            else
            {
                bool holderIsMe = requested.HolderClientId == OwnerClientId;
                if (holderIsMe) confirmed = true;
                // The server let go (range, downed...), or the grab point was switched off (torn-down barrier).
                bool lostIt = (confirmed && !holderIsMe) || !requested.isActiveAndEnabled;
                bool refused = !confirmed && (requested.IsHeld || Time.time - requestTime > confirmTimeout);

                if (!canHold || lostIt || refused) EndGrab(notifyServer: !lostIt);
                else SendSteering();
            }

            movement.SetInputBlock(this, requested != null);
            movement.GrabFacing = requested != null
                ? Vector3.ProjectOnPlane(requested.SocketPosition - transform.position, Vector3.up)
                : Vector3.zero;
        }

        void TryStartGrab(bool wrenchReady)
        {
            if (!wrenchReady || !targeting.IsAiming || !attackAction.WasPressedThisFrame()) return;
            var target = targeting.CurrentTarget;
            var graspable = target != null ? target.Graspable : null;
            if (graspable == null || graspable.IsHeld) return;
            if ((graspable.NearestSocketPosition(transform.position) - transform.position).sqrMagnitude > grabRange * grabRange) return;

            requested = graspable;
            requestTime = Time.time;
            confirmed = false;
            lastSentMove = Vector2.zero;
            // A behaviour reference, not the object: one object can carry several handles (co-op objects).
            GrabRpc(graspable);
        }

        Vector2 CurrentMove() => Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);

        float CurrentYaw()
        {
            var rig = ThirdPersonCameraRig.Instance;
            return rig != null ? Quaternion.LookRotation(rig.PlanarForward).eulerAngles.y : transform.eulerAngles.y;
        }

        void SendSteering()
        {
            Vector2 move = latched ? latchedMove : CurrentMove();
            float yaw = latched ? latchedYaw : CurrentYaw();

            bool changed = (move - lastSentMove).sqrMagnitude > 0.0025f || Mathf.Abs(Mathf.DeltaAngle(yaw, lastSentYaw)) > 2f;
            if (!changed && Time.time - lastSendTime < inputSendInterval * 4f) return;
            if (Time.time - lastSendTime < inputSendInterval) return;

            lastSentMove = move;
            lastSentYaw = yaw;
            lastSendTime = Time.time;
            SteerRpc(move, yaw);
        }

        void EndGrab(bool notifyServer)
        {
            if (requested == null) return;
            if (notifyServer && requested.IsSpawned) ReleaseRpc(requested);
            requested = null;
            confirmed = false;
            latched = false;
            movement.SetInputBlock(this, false);
            movement.GrabFacing = Vector3.zero;
        }

        // ---------- Server ----------

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void GrabRpc(NetworkBehaviourReference targetRef)
        {
            if (!targetRef.TryGet(out Graspable graspable) || !graspable.isActiveAndEnabled) return;
            if (health != null && !health.IsAlive) return;
            // Generous range check: the server sees the owner's position slightly late.
            float range = grabRange + 3f;
            if ((graspable.NearestSocketPosition(transform.position) - transform.position).sqrMagnitude > range * range) return;
            graspable.ServerTryGrab(OwnerClientId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void ReleaseRpc(NetworkBehaviourReference targetRef)
        {
            if (targetRef.TryGet(out Graspable graspable)) graspable.ServerRelease(OwnerClientId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner, Delivery = RpcDelivery.Unreliable)]
        void SteerRpc(Vector2 move, float cameraYaw)
        {
            foreach (var graspable in Graspable.All)
                if (graspable.HolderClientId == OwnerClientId) graspable.ServerSetInput(OwnerClientId, move, cameraYaw);
        }

        // ---------- Visual (every client): point the wrench at what it holds ----------

        void LateUpdate()
        {
            if (melee == null || melee.WeaponPivot == null) return;
            foreach (var graspable in Graspable.All)
            {
                if (graspable.HolderClientId != OwnerClientId) continue;
                Vector3 toSocket = graspable.SocketPosition - melee.WeaponPivot.position;
                if (toSocket.sqrMagnitude > 0.0001f) melee.WeaponPivot.rotation = Quaternion.LookRotation(toSocket);
                return;
            }
        }
    }
}
