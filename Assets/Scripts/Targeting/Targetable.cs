using System.Collections.Generic;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Marks something the player can lock onto while aiming (enemies, crates, later switches, turrets...).
    /// Any aimed weapon — the thrown wrench now, blasters and grenades later — asks <see cref="PlayerTargeting"/>
    /// for the locked Targetable and uses <see cref="AimPoint"/> as where to hit.
    /// </summary>
    public class Targetable : MonoBehaviour
    {
        static readonly List<Targetable> s_All = new();
        public static IReadOnlyList<Targetable> All => s_All;

        [Tooltip("Height above the pivot that weapons aim at and the lock marker sits on.")]
        [SerializeField] float aimHeight;
        [Tooltip("Optional: aim at this transform instead (e.g. the beam socket on a moving bridge).")]
        [SerializeField] Transform aimOverride;
        [Tooltip("Rough size; makes big or close targets easier to put the crosshair on.")]
        [SerializeField] float radius = 0.6f;

        Damageable damageable;
        Graspable graspable;

        public Vector3 AimPoint => aimOverride != null ? aimOverride.position : transform.position + Vector3.up * aimHeight;
        public float Radius => radius;
        /// <summary>The health of this target, if it has any (null for e.g. a switch).</summary>
        public Damageable Damageable => damageable;
        /// <summary>Set if the wrench beam can grab this target.</summary>
        public Graspable Graspable => graspable;

        /// <summary>False once it's destroyed/dead or disabled, so weapons and the lock let go of it.</summary>
        public bool IsTargetable => isActiveAndEnabled && (damageable == null || (damageable.IsSpawned && !damageable.IsDead));

        void Awake()
        {
            damageable = GetComponent<Damageable>();
            graspable = GetComponent<Graspable>();
        }
        void OnEnable() => s_All.Add(this);
        void OnDisable() => s_All.Remove(this);

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(AimPoint, radius);
        }
    }
}
