using UnityEngine;

namespace CoopGame
{
    /// <summary>On the final boss's body: hits only get through when the fight allows it (server).</summary>
    public class FinalBossPart : MonoBehaviour, IDamageAbsorber
    {
        [SerializeField] FinalBoss fight;

        public int Absorb(int amount, Vector3 hitDirection, DamageTypes type) => fight != null ? fight.ServerAbsorb(amount) : 0;
    }
}
