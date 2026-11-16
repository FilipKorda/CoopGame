using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoopGame
{
    /// <summary>
    /// The player's loadout: the wrench (always the main weapon) plus one secondary weapon picked in the
    /// weapon select panel. Q / E choose which one is active. The rules:
    /// - Attack without aiming is always a wrench melee hit; if the secondary was active, the wrench becomes active.
    /// - Attack while aiming uses the active weapon: wrench → throw, secondary → that weapon.
    /// The inactive weapon is shown on the player's back. Loadout and active slot are replicated so everyone sees it.
    /// </summary>
    [RequireComponent(typeof(PlayerMelee))]
    public class PlayerWeapons : NetworkBehaviour
    {
        public static PlayerWeapons Local { get; private set; }

        [Tooltip("Copy of the wrench shown on the back while a secondary weapon is in hand.")]
        [SerializeField] GameObject wrenchBackModel;

        readonly NetworkVariable<byte> secondaryId = new((byte)WeaponId.None,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<bool> secondaryActive = new(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        PlayerMelee melee;
        WrenchThrow wrenchThrow;
        PlayerTargeting targeting;
        PlayerHealth health;
        PlayerMovement movement;
        PlayerWeapon[] weapons;
        InputAction selectMainAction, selectSecondaryAction, attackAction;

        public WeaponId SecondaryId => (WeaponId)secondaryId.Value;
        public PlayerWeapon Secondary => Find(SecondaryId);
        public bool MainActive => !secondaryActive.Value || Secondary == null;
        public PlayerWeapon ActiveSecondary => MainActive ? null : Secondary;

        void Awake()
        {
            melee = GetComponent<PlayerMelee>();
            wrenchThrow = GetComponent<WrenchThrow>();
            targeting = GetComponent<PlayerTargeting>();
            health = GetComponent<PlayerHealth>();
            movement = GetComponent<PlayerMovement>();
            weapons = GetComponents<PlayerWeapon>();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner) return;
            Local = this;
            selectMainAction = InputSystem.actions.FindAction("Player/SelectMainWeapon", true);
            selectSecondaryAction = InputSystem.actions.FindAction("Player/SelectSecondaryWeapon", true);
            attackAction = InputSystem.actions.FindAction("Player/Attack", true);
        }

        public override void OnNetworkDespawn()
        {
            if (Local == this) Local = null;
        }

        // ---------- Owner API ----------

        /// <summary>Owner: put a weapon in the secondary slot (replacing the old one) and switch to it.</summary>
        public void EquipSecondary(WeaponId id)
        {
            if (!IsOwner || Find(id) == null) return;
            secondaryId.Value = (byte)id;
            secondaryActive.Value = true;
        }

        public void SelectMain()
        {
            if (IsOwner && secondaryActive.Value) secondaryActive.Value = false;
        }

        public void SelectSecondary()
        {
            if (IsOwner && Secondary != null && !secondaryActive.Value) secondaryActive.Value = true;
        }

        void Update()
        {
            if (IsSpawned && IsOwner && PlayerSpawnPoints.Instance != null) UpdateOwner();
        }

        void UpdateOwner()
        {
            if (selectMainAction.WasPressedThisFrame()) SelectMain();
            if (selectSecondaryAction.WasPressedThisFrame()) SelectSecondary();

            var active = ActiveSecondary;
            bool meleeActive = active != null && active.IsMelee;

            // Lock-on only for weapons that use it (wrench throw, grenade); the pistol fires where you point.
            // Only the wrench locks beam sockets (it grabs them). Melee weapons don't aim at all.
            targeting.LockOnEnabled = MainActive || (active != null && active.UsesLockOn);
            targeting.GraspablesLockable = MainActive;
            targeting.LockRangeOverride = active != null ? active.LockRange : 0f;
            targeting.MaxLocks = active != null ? active.MaxLocks : 1;
            targeting.AimAllowed = !meleeActive;

            foreach (var weapon in weapons)
                if (weapon.Id == SecondaryId) weapon.TickEquipped();

            if (active == null) return;

            bool canAttack = !GameplayUI.IsMenuOpen && (health == null || health.IsAlive)
                && !movement.IsSwimming && !movement.IsHanging && !movement.IsSlamming;
            if (meleeActive)
            {
                // Melee secondary: plain Attack swings it; the game stays on this weapon (no switch back to the wrench).
                if (canAttack && attackAction.WasPressedThisFrame()) active.MeleeAttack();
            }
            else if (targeting.IsAiming)
            {
                active.TickAiming(canAttack && attackAction.WasPressedThisFrame(), canAttack && attackAction.IsPressed());
            }
        }

        // ---------- Visuals (every client) ----------

        void LateUpdate()
        {
            bool wrenchHome = wrenchThrow == null || wrenchThrow.HasWrench;
            SetActive(melee.WeaponPivot.gameObject, MainActive && wrenchHome);
            if (wrenchBackModel != null) SetActive(wrenchBackModel, !MainActive && wrenchHome);

            foreach (var weapon in weapons)
            {
                bool equipped = weapon.Id == SecondaryId;
                weapon.SetModels(inHand: equipped && !MainActive, onBack: equipped && MainActive);
            }
        }

        static void SetActive(GameObject go, bool active)
        {
            if (go.activeSelf != active) go.SetActive(active);
        }

        PlayerWeapon Find(WeaponId id)
        {
            if (id == WeaponId.None || weapons == null) return null;
            foreach (var weapon in weapons)
                if (weapon.Id == id) return weapon;
            return null;
        }
    }
}
