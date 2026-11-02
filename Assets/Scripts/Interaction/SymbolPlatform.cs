using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// A platform that slides out of a wall when its symbol is lit on a <see cref="SymbolPanel"/> and back in when it
    /// isn't. Where it's placed in the scene is where it sits when out; hidden it sits <see cref="retractOffset"/> away
    /// (inside the wall), which is how every level starts. It isn't networked itself: every client moves it from the
    /// panel's shared state, and players standing on it ride along.
    /// </summary>
    public class SymbolPlatform : MonoBehaviour
    {
        [SerializeField] PlatformSymbol symbol;
        [Tooltip("From the placed (out) position to the hidden one, in world space. 'Fit Into Housing' works it out.")]
        [SerializeField] Vector3 retractOffset;
        [Tooltip("What it hides in (the tower), for 'Fit Into Housing'.")]
        [SerializeField] Collider housing;
        [Tooltip("When hidden its outer end stays this far out of the wall, so the symbol on it can still be seen.")]
        [SerializeField] float showOutside = 0.02f;
        [Tooltip("Seconds to slide all the way out or in.")]
        [SerializeField] float moveTime = 0.6f;
        [SerializeField] bool startExtended;
        [Tooltip("Symbol marks on the platform: they glow while it's out.")]
        [SerializeField] Renderer[] marks;
        [SerializeField] float markGlowOut = 1.6f;
        [SerializeField] float markGlowIn = 0.35f;

        Vector3 extendedPosition;
        float progress;
        bool extended;
        MaterialPropertyBlock block;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        public PlatformSymbol Symbol => symbol;
        public bool IsExtended => extended;
        /// <summary>0 = hidden, 1 = all the way out.</summary>
        public float Progress => progress;

        void Awake()
        {
            extendedPosition = transform.position;
            extended = startExtended;
            progress = extended ? 1f : 0f;
            Place();
        }

        /// <summary>Slide out (true) or in (false); instant jumps straight there (joining a game in progress).</summary>
        public void SetExtended(bool value, bool instant = false)
        {
            extended = value;
            if (instant)
            {
                progress = value ? 1f : 0f;
                Place();
            }
        }

        void Update()
        {
            float goal = extended ? 1f : 0f;
            if (Mathf.Approximately(progress, goal)) return;
            progress = Mathf.MoveTowards(progress, goal, Time.deltaTime / Mathf.Max(0.01f, moveTime));
            Place();
        }

        void Place()
        {
            transform.position = extendedPosition + retractOffset * (1f - Mathf.SmoothStep(0f, 1f, progress));
            if (marks == null || marks.Length == 0) return;
            block ??= new MaterialPropertyBlock();
            Color glow = PlatformSymbols.Tint(symbol) * Mathf.Lerp(markGlowIn, markGlowOut, progress);
            foreach (var mark in marks)
            {
                if (mark == null) continue;
                mark.GetPropertyBlock(block);
                block.SetColor(EmissionId, glow);
                mark.SetPropertyBlock(block);
            }
        }

        /// <summary>Editor: point the retract offset straight into the housing, through the wall it touches.</summary>
        [ContextMenu("Fit Into Housing")]
        public void FitIntoHousing()
        {
            if (housing == null) return;
            var box = GetComponent<Collider>();
            Bounds own = box != null ? box.bounds : new Bounds(transform.position, Vector3.one);
            Transform frame = housing.transform;
            Bounds wall = housing.bounds;
            // Which side of the housing it's on: the horizontal axis of the housing it sticks out furthest along.
            Vector3 local = frame.InverseTransformPoint(own.center);
            Vector3 half = frame.InverseTransformVector(wall.extents);
            half = new Vector3(Mathf.Abs(half.x), Mathf.Abs(half.y), Mathf.Abs(half.z));
            bool alongX = Mathf.Abs(local.x) / Mathf.Max(0.01f, half.x) >= Mathf.Abs(local.z) / Mathf.Max(0.01f, half.z);
            Vector3 outward = (alongX ? frame.right * Mathf.Sign(local.x) : frame.forward * Mathf.Sign(local.z)).normalized;
            // Slide in until the outer end is just outside the wall.
            float outerEnd = Vector3.Dot(own.center, outward) + Mathf.Abs(Vector3.Dot(own.extents, new Vector3(Mathf.Abs(outward.x), Mathf.Abs(outward.y), Mathf.Abs(outward.z))));
            float wallFace = Vector3.Dot(wall.center, outward) + Mathf.Abs(Vector3.Dot(wall.extents, new Vector3(Mathf.Abs(outward.x), Mathf.Abs(outward.y), Mathf.Abs(outward.z))));
            retractOffset = -outward * Mathf.Max(0f, outerEnd - wallFace - showOutside);
        }

        /// <summary>Editor setup: symbol, housing and marks in one go.</summary>
        public void Setup(PlatformSymbol value, Collider housingCollider, Renderer[] markRenderers)
        {
            symbol = value;
            housing = housingCollider;
            marks = markRenderers;
            FitIntoHousing();
        }

        void OnDrawGizmosSelected()
        {
            Vector3 from = Application.isPlaying ? extendedPosition : transform.position;
            Gizmos.color = new Color(1f, 0.8f, 0.2f);
            Gizmos.DrawLine(from, from + retractOffset);
            var box = GetComponent<BoxCollider>();
            if (box != null)
            {
                Gizmos.matrix = Matrix4x4.TRS(from + retractOffset, transform.rotation, transform.lossyScale);
                Gizmos.DrawWireCube(box.center, box.size);
                Gizmos.matrix = Matrix4x4.identity;
            }
        }
    }
}
