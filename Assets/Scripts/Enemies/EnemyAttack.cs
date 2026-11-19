using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Base for one enemy attack. Add any number of these next to an <see cref="EnemyController"/>;
    /// the controller starts the first one whose <see cref="CanStart"/> passes and then lets it drive
    /// the enemy until it finishes. Everything here runs on the server; use NetworkVariables/RPCs in
    /// subclasses for anything clients need to see (telegraphs, effects).
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public abstract class EnemyAttack : NetworkBehaviour
    {
        [SerializeField] protected float minRange = 0f;
        [SerializeField] protected float maxRange = 2f;
        [SerializeField] protected float cooldown = 2f;

        float readyTime;

        protected EnemyController Enemy { get; private set; }
        /// <summary>Who this attack goes for: a player, or the ally robot that charmed the enemy.</summary>
        protected IEnemyTarget Target { get; private set; }
        /// <summary>The target still exists and can be hurt (a robot may have been destroyed mid-attack).</summary>
        protected bool TargetAlive => Target != null && Target.IsAlive;
        public bool IsRunning { get; private set; }

        protected virtual void Awake() => Enemy = GetComponent<EnemyController>();

        public virtual bool CanStart(IEnemyTarget target, float distance) =>
            Time.time >= readyTime && distance >= minRange && distance <= maxRange;

        public void Begin(IEnemyTarget target)
        {
            Target = target;
            IsRunning = true;
            OnBegin();
        }

        /// <summary>Advance the attack; it finishes itself when <see cref="OnTick"/> returns false.</summary>
        public void Tick(float dt)
        {
            if (IsRunning && !OnTick(dt)) Finish();
        }

        /// <summary>Interrupt (e.g. the enemy got hit).</summary>
        public void Cancel()
        {
            if (!IsRunning) return;
            OnCancel();
            Finish();
        }

        void Finish()
        {
            IsRunning = false;
            Target = null;
            readyTime = Time.time + cooldown;
        }

        protected abstract void OnBegin();

        /// <summary>Return true while the attack is still running.</summary>
        protected abstract bool OnTick(float dt);

        protected virtual void OnCancel() { }
    }
}
