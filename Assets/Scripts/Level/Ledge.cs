using System.Collections.Generic;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// A grabbable edge, like the marked ledges in Ratchet &amp; Clank. Only edges with this component can be grabbed.
    /// The transform sits on the middle of the edge line: local X runs along the edge, local Z (forward) points
    /// away from the wall towards where the player hangs.
    /// </summary>
    public class Ledge : MonoBehaviour
    {
        static readonly List<Ledge> s_All = new();
        public static IReadOnlyList<Ledge> All => s_All;

        [SerializeField] float length = 4f;

        public float HalfLength => length * 0.5f;
        public Vector3 Center => transform.position;
        public Vector3 Along => transform.right;
        /// <summary>Points out of the wall, towards the hanging player.</summary>
        public Vector3 Outward => transform.forward;

        void OnEnable() => s_All.Add(this);
        void OnDisable() => s_All.Remove(this);

        /// <summary>Signed distance along the edge from its center to the projection of <paramref name="point"/>.</summary>
        public float Project(Vector3 point) => Vector3.Dot(point - Center, Along);

        public Vector3 PointAt(float offset) => Center + Along * Mathf.Clamp(offset, -HalfLength, HalfLength);

#if UNITY_EDITOR
        // Keep the marker strip (a child named "Strip") as long as the edge when the length changes in the Inspector.
        void OnValidate()
        {
            var strip = transform.Find("Strip");
            if (strip == null) return;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null || strip == null || Mathf.Approximately(strip.localScale.x, length)) return;
                var scale = strip.localScale;
                scale.x = length;
                strip.localScale = scale;
            };
        }
#endif

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.1f);
            Vector3 a = PointAt(-HalfLength), b = PointAt(HalfLength);
            Gizmos.DrawLine(a, b);
            Gizmos.DrawRay(Center, Outward * 0.5f);
        }
    }
}
