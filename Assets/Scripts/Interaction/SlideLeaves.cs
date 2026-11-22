using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Leaves that slide open: a double sliding door, a hidden wall that parts, a cover that opens only while something
    /// holds it open. Not networked itself; every machine slides its own copy when told to (<see cref="Open"/> /
    /// <see cref="SetOpen"/>, e.g. from a synced puzzle state) or once <see cref="openWith"/> has fired, so a player
    /// joining later sees it the same.
    /// </summary>
    public class SlideLeaves : MonoBehaviour
    {
        [SerializeField] Transform[] leaves;
        [Tooltip("Per leaf: how far it slides when open, in its parent's local axes.")]
        [SerializeField] Vector3[] openOffsets;
        [Tooltip("Seconds from closed to open.")]
        [SerializeField] float openTime = 1.8f;
        [Tooltip("Optional: opens by itself once this co-op mechanism (e.g. two hidden buttons pressed together) has fired.")]
        [SerializeField] CoopMechanism openWith;

        Vector3[] closed;
        float progress;
        bool open;

        public bool IsOpen => open;
        /// <summary>0 closed .. 1 fully open.</summary>
        public float Progress => progress;

        void Awake()
        {
            closed = new Vector3[leaves.Length];
            for (int i = 0; i < leaves.Length; i++)
                if (leaves[i] != null) closed[i] = leaves[i].localPosition;
        }

        public void Open() => open = true;

        /// <summary>Open or close (slides back the same way it opened).</summary>
        public void SetOpen(bool value) => open = value;

        void Update()
        {
            if (!open && openWith != null && openWith.IsActivated) open = true;
            float target = open ? 1f : 0f;
            if (progress == target) return;

            progress = Mathf.MoveTowards(progress, target, Time.deltaTime / Mathf.Max(0.01f, openTime));
            float eased = Mathf.SmoothStep(0f, 1f, progress);
            for (int i = 0; i < leaves.Length; i++)
                if (leaves[i] != null && openOffsets != null && i < openOffsets.Length)
                    leaves[i].localPosition = closed[i] + openOffsets[i] * eased;
        }

        void OnDrawGizmosSelected()
        {
            if (leaves == null || openOffsets == null) return;
            Gizmos.color = Color.cyan;
            for (int i = 0; i < leaves.Length && i < openOffsets.Length; i++)
            {
                if (leaves[i] == null) continue;
                Transform parent = leaves[i].parent;
                Vector3 start = Application.isPlaying && closed != null ? closed[i] : leaves[i].localPosition;
                Vector3 a = parent != null ? parent.TransformPoint(start) : start;
                Vector3 b = parent != null ? parent.TransformPoint(start + openOffsets[i]) : start + openOffsets[i];
                Gizmos.DrawLine(a, b);
                Gizmos.DrawWireSphere(b, 0.15f);
            }
        }
    }
}
