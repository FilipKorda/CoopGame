using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoopGame
{
    /// <summary>
    /// Lets a standing player revive a downed teammate by holding Interact (F / gamepad North) nearby.
    /// The owner only reports whom they are holding Interact on; the server checks distance every frame
    /// and advances the downed player's revive progress (see <see cref="PlayerHealth"/>).
    /// </summary>
    [RequireComponent(typeof(PlayerHealth))]
    public class PlayerRevive : NetworkBehaviour
    {
        public static PlayerRevive Local { get; private set; }

        [SerializeField] float reviveRange = 2f;
        [Tooltip("Extra distance the server accepts because it sees the owner's position slightly late.")]
        [SerializeField] float serverRangeTolerance = 1f;

        PlayerHealth health;
        PlayerMovement movement;
        InputAction interactAction;
        PlayerHealth reportedTarget;
        PlayerHealth serverTarget;

        /// <summary>Owner: closest downed teammate within range, if any.</summary>
        public PlayerHealth TargetInRange { get; private set; }
        /// <summary>Owner: currently holding Interact on <see cref="TargetInRange"/>.</summary>
        public bool IsReviving { get; private set; }

        /// <summary>Server: the downed player this player is holding Interact on.</summary>
        public PlayerHealth ServerReviveTarget => serverTarget;
        public float ServerMaxRange => reviveRange + serverRangeTolerance;

        void Awake()
        {
            health = GetComponent<PlayerHealth>();
            movement = GetComponent<PlayerMovement>();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner) return;
            Local = this;
            interactAction = InputSystem.actions.FindAction("Player/Interact", true);
        }

        public override void OnNetworkDespawn()
        {
            if (Local == this) Local = null;
            if (movement != null) movement.SetInputBlock(this, false);
        }

        void Update()
        {
            if (!IsSpawned || !IsOwner) return;

            TargetInRange = health.IsAlive ? FindDownedTeammate() : null;
            IsReviving = TargetInRange != null && interactAction.IsPressed();

            // Stand still while reviving.
            movement.SetInputBlock(this, IsReviving);

            PlayerHealth target = IsReviving ? TargetInRange : null;
            if (target != reportedTarget)
            {
                reportedTarget = target;
                if (target != null) SetReviveTargetRpc(true, target.NetworkObject);
                else SetReviveTargetRpc(false, default);
            }
        }

        PlayerHealth FindDownedTeammate()
        {
            PlayerHealth best = null;
            float bestSqr = reviveRange * reviveRange;
            foreach (var other in PlayerHealth.All)
            {
                if (other == health || !other.IsDowned) continue;
                float sqr = (other.transform.position - transform.position).sqrMagnitude;
                if (sqr <= bestSqr)
                {
                    bestSqr = sqr;
                    best = other;
                }
            }
            return best;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void SetReviveTargetRpc(bool hasTarget, NetworkObjectReference target)
        {
            serverTarget = hasTarget && target.TryGet(out NetworkObject targetObject)
                ? targetObject.GetComponent<PlayerHealth>()
                : null;
        }
    }
}
