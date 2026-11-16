using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Base for a secondary weapon living on the Player prefab. <see cref="PlayerWeapons"/> decides which one is
    /// equipped and active. Ranged weapons fire while the player aims (<see cref="PlayerTargeting"/>) and presses
    /// Attack; melee weapons (<see cref="IsMelee"/>) don't aim at all and attack on a plain Attack press instead.
    /// Each weapon has a model in the hand and a copy on the back for when it's put away.
    /// </summary>
    public abstract class PlayerWeapon : NetworkBehaviour
    {
        [SerializeField] GameObject handModel;
        [SerializeField] GameObject backModel;

        public abstract WeaponId Id { get; }

        /// <summary>Whether aiming with this weapon locks onto targets (the wrench throw does, the pistol doesn't).</summary>
        public virtual bool UsesLockOn => false;

        /// <summary>How far lock-on reaches with this weapon; 0 = the targeting default.</summary>
        public virtual float LockRange => 0f;

        /// <summary>How many targets this weapon locks at once while aiming (the rocket launcher paints several).</summary>
        public virtual int MaxLocks => 1;

        /// <summary>Short extra text for the loadout HUD, e.g. ammo.</summary>
        public virtual string StatusText => "";

        /// <summary>
        /// Melee weapons replace the wrench's close-range attack while active: Attack uses them (the game doesn't
        /// switch back to the wrench), and there's no aiming, throwing or slam.
        /// </summary>
        public virtual bool IsMelee => false;

        /// <summary>Owner, ranged weapons: called every frame while active and the player is aiming with it.</summary>
        public virtual void TickAiming(bool firePressed, bool fireHeld) { }

        /// <summary>Owner, melee weapons: Attack was pressed (not aiming) while this weapon is active.</summary>
        public virtual void MeleeAttack() { }

        /// <summary>Owner: called every frame while this weapon is equipped (active or not), e.g. for reloading.</summary>
        public virtual void TickEquipped() { }

        public void SetModels(bool inHand, bool onBack)
        {
            if (handModel != null && handModel.activeSelf != inHand) handModel.SetActive(inHand);
            if (backModel != null && backModel.activeSelf != onBack) backModel.SetActive(onBack);
        }
    }
}
