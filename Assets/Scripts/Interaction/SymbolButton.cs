using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// One button on a <see cref="SymbolPanel"/>: shows its symbol, lights up while its platforms are out, brightens
    /// a little when the local player can press it, and sinks in briefly when pressed. The panel does the logic.
    /// </summary>
    public class SymbolButton : MonoBehaviour
    {
        [SerializeField] PlatformSymbol symbol;
        [Tooltip("The part that sinks in when pressed (moves along its local -Z).")]
        [SerializeField] Transform cap;
        [SerializeField] Renderer mark;
        [SerializeField] float pressDepth = 0.06f;
        [SerializeField] float pressTime = 0.18f;
        [SerializeField] float glowLit = 2.2f;
        [SerializeField] float glowNear = 0.7f;
        [SerializeField] float glowOff = 0.12f;

        Vector3 capRest;
        float pressedAt = -10f;
        MaterialPropertyBlock block;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public PlatformSymbol Symbol => symbol;

        void Awake()
        {
            if (cap != null) capRest = cap.localPosition;
        }

        public void Setup(PlatformSymbol value, Transform capTransform, Renderer markRenderer)
        {
            symbol = value;
            cap = capTransform;
            mark = markRenderer;
        }

        public void Press() => pressedAt = Time.time;

        /// <summary>lit: its platforms are out; near: the local player can press it; off: the panel is switched off (dark).</summary>
        public void Show(bool lit, bool near, bool off = false)
        {
            if (cap != null)
            {
                float t = (Time.time - pressedAt) / pressTime;
                float sink = t < 0f || t > 1f ? 0f : Mathf.Sin(t * Mathf.PI);
                cap.localPosition = capRest + Vector3.back * (pressDepth * sink);
            }
            if (mark == null) return;
            block ??= new MaterialPropertyBlock();
            float glow = off ? 0f : lit ? glowLit : near ? glowNear : glowOff;
            if (lit && near) glow *= 1.2f;
            Color tint = PlatformSymbols.Tint(symbol);
            mark.GetPropertyBlock(block);
            block.SetColor(BaseColorId, off ? tint * 0.3f : tint);
            block.SetColor(EmissionId, tint * glow);
            mark.SetPropertyBlock(block);
        }
    }
}
