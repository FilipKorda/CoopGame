using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Something enemies chase and attack: the players, and allies on their side such as the charm robot
    /// (<see cref="AllyRobot"/>). Server-side.
    /// </summary>
    public interface IEnemyTarget
    {
        Transform transform { get; }
        bool IsAlive { get; }

        /// <summary>Server only. Returns true if the hit landed.</summary>
        bool ApplyDamage(in DamageInfo damage);
    }
}
