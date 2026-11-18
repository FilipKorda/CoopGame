using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Moves a plain scene object (no network components) out of the way when <see cref="Open"/> is called, e.g. from
    /// a <see cref="CombatArena"/>'s On Completed event, which runs on every machine (also for a player who joins
    /// later). By default it sinks into the floor by its own height.
    /// </summary>
    public class SceneObjectMover : MonoBehaviour
    {
        [Tooltip("Sink into the floor by the object's own height (plus Extra). Off: move by Offset instead.")]
        [SerializeField] bool sinkIntoFloor = true;
        [SerializeField] float extra = 0.1f;
        [Tooltip("World-space move when Sink Into Floor is off.")]
        [SerializeField] Vector3 offset = new Vector3(0f, -6f, 0f);
        [SerializeField] float moveTime = 1.5f;

        Vector3 start, end;
        float progress = -1f;

        void Awake()
        {
            start = transform.position;
            float height = TryGetComponent(out Renderer r) ? r.bounds.size.y : transform.lossyScale.y;
            end = start + (sinkIntoFloor ? Vector3.down * (height + extra) : offset);
        }

        /// <summary>Start moving (once; calling it again does nothing).</summary>
        public void Open()
        {
            if (progress < 0f) progress = 0f;
        }

        void Update()
        {
            if (progress < 0f || progress >= 1f) return;
            progress = Mathf.Min(1f, progress + Time.deltaTime / Mathf.Max(0.01f, moveTime));
            transform.position = Vector3.Lerp(start, end, Mathf.SmoothStep(0f, 1f, progress));
        }
    }
}
