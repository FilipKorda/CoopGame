using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace CoopGame
{
    /// <summary>
    /// A lever turned with the wrench beam (a Hinge <see cref="Graspable"/>) that connects something for good once
    /// it's turned all the way: the beam lets go, the lever stays there and can't be grabbed again, its lamp turns
    /// green, the power line lights up segment by segment from the lever to what it powers, anything listed as a
    /// spinner starts turning, and every player gets a message. <see cref="onConnected"/> runs on the server for
    /// whatever else should happen. The server decides; the state is shared, so a player joining later sees it done.
    /// </summary>
    public class PowerLever : NetworkBehaviour
    {
        [SerializeField] Graspable lever;
        [Tooltip("How far it has to be turned to connect (0..1 of its range).")]
        [SerializeField, Range(0.5f, 1f)] float threshold = 0.97f;

        [Header("Visuals")]
        [SerializeField] Renderer lamp;
        [SerializeField] Color lampOff = new Color(1f, 0.35f, 0.15f);
        [SerializeField] Color lampOn = new Color(0.3f, 1f, 0.4f);
        [Tooltip("Lit one after another when connected, in this order (from the lever to what it powers).")]
        [SerializeField] Renderer[] lineSegments;
        [SerializeField] float lineFillTime = 1.6f;
        [SerializeField] Color lineOff = new Color(0.05f, 0.08f, 0.12f);
        [SerializeField] Color lineOn = new Color(0.25f, 0.85f, 1f);
        [SerializeField] float lineGlow = 3f;
        [SerializeField] Vector3 spinAxis = Vector3.forward;
        [SerializeField] float spinSpeed = 120f;
        [SerializeField] float spinUpTime = 4f;

        [Header("Messages")]
        [SerializeField] string hint = "Dźwignia: wyceluj kluczem (PPM), przytrzymaj LPM i przekręć ją klawiszami ruchu do końca";
        [SerializeField] float hintRange = 8f;
        [SerializeField] string connectedMessage = "Turbina połączona z resztą urządzenia!";
        [SerializeField] float messageTime = 5f;

        [Header("Locked")]
        [Tooltip("Can't be grabbed until ServerUnlock is called (e.g. by a puzzle's On Solved).")]
        [SerializeField] bool startLocked;
        [SerializeField] string lockedHint = "";

        [Header("Server")]
        [SerializeField] UnityEvent onConnected;

        readonly NetworkVariable<bool> connected = new();
        readonly NetworkVariable<bool> unlocked = new();
        readonly NetworkVariable<double> connectedAt = new();

        Targetable leverTarget;
        Material lampMaterial;
        MaterialPropertyBlock block;
        Quaternion[] spinnerRest;
        float spinAngle;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        public bool IsConnected => connected.Value;
        public bool IsUsable => !startLocked || unlocked.Value;

        /// <summary>Server: let it be grabbed (it starts locked with Start Locked).</summary>
        public void ServerUnlock()
        {
            if (IsServer) unlocked.Value = true;
        }

        double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : Time.timeAsDouble;

        void Awake()
        {
            if (lever != null) leverTarget = lever.GetComponent<Targetable>();
            if (lamp != null) lampMaterial = lamp.material;
        }

        void Update()
        {
            if (IsServer && IsSpawned && !connected.Value && IsUsable && lever != null && lever.HingeProgress >= threshold) ServerConnect();
            UpdateVisuals();
        }

        /// <summary>Server: connect now (tests, scripted sequences).</summary>
        public void ServerConnect()
        {
            if (!IsServer || connected.Value) return;
            connected.Value = true;
            connectedAt.Value = Now;
            if (lever != null) lever.ServerForceRelease();
            onConnected?.Invoke();
        }

        float SinceConnected => connected.Value ? (float)(Now - connectedAt.Value) : -1f;

        void UpdateVisuals()
        {
            bool done = IsSpawned && connected.Value;
            float since = done ? SinceConnected : -1f;
            bool usable = !IsSpawned || IsUsable;
            if (leverTarget != null) leverTarget.enabled = !done && usable;
            // The beam only grabs an enabled Graspable (checked on the server).
            if (lever != null && lever.enabled != usable) lever.enabled = usable;
            if (lampMaterial != null) lampMaterial.color = done ? lampOn : Color.Lerp(lampOff * 0.6f, lampOff, 0.5f + 0.5f * Mathf.Sin(Time.time * 4f));

            if (lineSegments != null && lineSegments.Length > 0)
            {
                block ??= new MaterialPropertyBlock();
                float fill = done ? since / Mathf.Max(0.01f, lineFillTime) * lineSegments.Length : 0f;
                float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 6f);
                for (int i = 0; i < lineSegments.Length; i++)
                {
                    var segment = lineSegments[i];
                    if (segment == null) continue;
                    float lit = Mathf.Clamp01(fill - i);
                    // A segment flashes as the power reaches it, then glows steadily.
                    float flash = lit > 0f && lit < 1f ? 1.8f : 1f;
                    segment.GetPropertyBlock(block);
                    block.SetColor(BaseColorId, Color.Lerp(lineOff, lineOn, lit));
                    block.SetColor(EmissionId, lineOn * (lineGlow * lit * flash * (lit >= 1f ? pulse : 1f)));
                    segment.SetPropertyBlock(block);
                }
            }
        }

        /// <summary>Editor setup.</summary>
        public void Setup(Graspable leverGraspable, Renderer lampRenderer, Renderer[] segments)
        {
            lever = leverGraspable;
            lamp = lampRenderer;
            lineSegments = segments;
        }

        void OnGUI()
        {
            if (!IsSpawned || CutsceneSystem.HidesGameplayUI) return;
            string text = null;
            if (connected.Value)
            {
                if (SinceConnected < messageTime) text = connectedMessage;
            }
            else
            {
                var player = PlayerHealth.Local;
                var anchor = lever != null ? lever.transform.position : transform.position;
                if (player != null && player.IsAlive && (player.transform.position - anchor).sqrMagnitude <= hintRange * hintRange && (lever == null || !lever.IsHeld))
                    text = IsUsable ? hint : lockedHint;
            }
            if (string.IsNullOrEmpty(text)) return;
            var style = new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            GUI.Box(new Rect(Screen.width * 0.5f - 420f, Screen.height - 240f, 840f, 44f), text, style);
        }

        public override void OnDestroy()
        {
            if (lampMaterial != null) Destroy(lampMaterial);
            base.OnDestroy();
        }
    }
}
