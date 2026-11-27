using System;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace CoopGame
{
    /// <summary>
    /// A2 underwater airlock riddle: three word riddles in a row on the airlock screen, answered by "touching" one of
    /// three symbols carved into the mountain with the wrench beam (each symbol is a Fixed <see cref="Graspable"/>).
    /// A good answer swaps the symbols for the next set and shows the next riddle; a wrong one starts over from
    /// riddle 1. After the third good answer the symbols sink into the rock and the outer bulkhead hatch swings open.
    /// The server decides everything; clients draw the same state from the network variables (shared server time).
    /// The riddle is shown while any player is near the screen (and as an overlay for the local player near it).
    /// </summary>
    public class AirlockRiddlePuzzle : NetworkBehaviour
    {
        [Serializable]
        public class Riddle
        {
            [TextArea(3, 6)] public string text;
            [Tooltip("Names of the three symbols shown for this riddle (symbol slot 1..3), for the screen hint and logs.")]
            public string[] symbolNames = new string[3];
            [Tooltip("Correct symbol slot, 0-based (0 = Symbol 1).")]
            public int correct;
        }

        [SerializeField] Riddle[] riddles;

        [Header("Symbols (slot N = element N-1)")]
        [Tooltip("Beam targets on the rock, one per slot.")]
        [SerializeField] Graspable[] symbols;
        [Tooltip("The part of each symbol that sinks into the rock when the puzzle is solved.")]
        [SerializeField] Transform[] symbolModels;
        [Tooltip("Glyph per riddle and slot: element riddle*3 + slot.")]
        [SerializeField] Renderer[] glyphs;
        [Tooltip("How far the symbols sink into the rock (along -X of the symbol root) when solved.")]
        [SerializeField] float sinkDepth = 0.45f;
        [SerializeField] float sinkTime = 2.5f;

        [Header("Screen")]
        [SerializeField] Transform screen;
        [SerializeField] TMP_Text[] screenTexts;
        [Tooltip("The riddle shows while a player is this close to the screen.")]
        [SerializeField] float showRange = 14f;

        [Header("Airlock hatch")]
        [Tooltip("Hinge pivots of the hatch leaves (closed at their scene rotation).")]
        [SerializeField] Transform[] hatchHinges;
        [Tooltip("Opening angle per hinge, degrees around its local Y.")]
        [SerializeField] float[] hatchOpenAngles = { -85f, 85f };
        [SerializeField] float hatchDelay = 1.5f;
        [SerializeField] float hatchTime = 4f;

        [Header("Timing")]
        [Tooltip("The beam stays latched this long before the choice counts and it lets go (so the beam is seen).")]
        [SerializeField] float touchTime = 0.35f;
        [Tooltip("Swap animation of the symbols after a choice; new touches are ignored meanwhile.")]
        [SerializeField] float swapTime = 1.6f;
        [Tooltip("How long the 'wrong' / 'good' message stays on the screen.")]
        [SerializeField] float messageTime = 3f;

        [Header("Colours")]
        [SerializeField] Color textColor = new Color(0.55f, 0.95f, 1f);
        [SerializeField] Color goodColor = new Color(0.35f, 1f, 0.45f);
        [SerializeField] Color wrongColor = new Color(1f, 0.3f, 0.22f);
        [SerializeField] Color idleColor = new Color(0.3f, 0.45f, 0.55f);
        [SerializeField] float glyphGlow = 1f;
        [SerializeField] float glyphFlash = 2.2f;

        [Tooltip("Server only, once: all three riddles solved.")]
        public UnityEvent onSolvedServer;

        enum Result : byte { None, Good, Wrong }

        // 0..riddles-1 = current riddle; riddles.Length = solved.
        readonly NetworkVariable<byte> stage = new(0);
        readonly NetworkVariable<byte> previousStage = new(0);
        readonly NetworkVariable<byte> lastResult = new((byte)Result.None);
        readonly NetworkVariable<byte> lastSlot = new(0);
        readonly NetworkVariable<double> lastChoiceAt = new(-100.0);
        readonly NetworkVariable<double> solvedAt = new(-1.0);

        // server
        float[] heldSince;
        // visuals
        Vector3[] modelRest;
        Quaternion[] hingeClosed;
        Material[] glyphMaterials;
        Color[] glyphBase;
        Vector3[] glyphScale;
        string overlayText;
        Color overlayColor;
        GUIStyle overlayStyle;
        bool symbolsLocked;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public bool IsSolved => riddles != null && stage.Value >= riddles.Length;
        public int Stage => stage.Value;

        double Now => NetworkManager != null && NetworkManager.IsListening ? NetworkManager.ServerTime.Time : Time.timeAsDouble;

        void Awake()
        {
            int n = symbols != null ? symbols.Length : 0;
            heldSince = new float[n];
            modelRest = new Vector3[symbolModels != null ? symbolModels.Length : 0];
            for (int i = 0; i < modelRest.Length; i++)
                if (symbolModels[i] != null) modelRest[i] = symbolModels[i].localPosition;
            hingeClosed = new Quaternion[hatchHinges != null ? hatchHinges.Length : 0];
            for (int i = 0; i < hingeClosed.Length; i++)
                if (hatchHinges[i] != null) hingeClosed[i] = hatchHinges[i].localRotation;
            int g = glyphs != null ? glyphs.Length : 0;
            glyphMaterials = new Material[g];
            glyphBase = new Color[g];
            glyphScale = new Vector3[g];
            for (int i = 0; i < g; i++)
            {
                if (glyphs[i] == null) continue;
                glyphMaterials[i] = glyphs[i].material;
                glyphBase[i] = glyphMaterials[i].HasProperty(BaseColorId) ? glyphMaterials[i].GetColor(BaseColorId) : Color.white;
                glyphScale[i] = glyphs[i].transform.localScale;
            }
        }

        public override void OnDestroy()
        {
            if (glyphMaterials != null)
                foreach (var m in glyphMaterials) if (m != null) Destroy(m);
            base.OnDestroy();
        }

        void Update()
        {
            if (IsServer && IsSpawned) ServerTick();
            UpdateVisuals();
        }

        // ---------- Server ----------

        void ServerTick()
        {
            if (symbols == null || riddles == null) return;
            bool busy = IsSolved || Now - lastChoiceAt.Value < swapTime;
            for (int i = 0; i < symbols.Length; i++)
            {
                var s = symbols[i];
                if (s == null) continue;
                if (!s.IsHeld) { heldSince[i] = -1f; continue; }
                if (heldSince[i] < 0f) heldSince[i] = Time.time;
                if (Time.time - heldSince[i] < touchTime) continue;

                ulong holder = s.HolderClientId;
                s.ServerRelease(holder);
                heldSince[i] = -1f;
                if (busy) continue;
                Choose(i);
                busy = true;
            }
        }

        void Choose(int slot)
        {
            int current = stage.Value;
            var riddle = riddles[current];
            previousStage.Value = (byte)current;
            lastSlot.Value = (byte)slot;
            lastChoiceAt.Value = Now;
            if (slot == riddle.correct)
            {
                lastResult.Value = (byte)Result.Good;
                stage.Value = (byte)(current + 1);
                if (IsSolved)
                {
                    solvedAt.Value = Now;
                    onSolvedServer?.Invoke();
                }
            }
            else
            {
                lastResult.Value = (byte)Result.Wrong;
                stage.Value = 0;
            }
        }

        // ---------- Visuals (every machine) ----------

        void UpdateVisuals()
        {
            if (riddles == null || riddles.Length == 0) return;
            double now = Now;
            double sinceChoice = now - lastChoiceAt.Value;
            var result = (Result)lastResult.Value;
            bool solved = IsSolved;

            // Symbols: during a swap the old set shrinks away (the touched glyph flashes green/red), then the new
            // set grows in. After the solve the last set stays and everything sinks into the rock and goes dark.
            float swapT = result == Result.None ? 1f : Mathf.Clamp01((float)(sinceChoice / swapTime));
            int shownStage = swapT < 0.5f ? previousStage.Value : Mathf.Min(stage.Value, riddles.Length - 1);
            if (solved) shownStage = riddles.Length - 1;
            float grow = solved ? 1f : swapT < 0.5f ? 1f - Smooth(Mathf.InverseLerp(0.3f, 0.5f, swapT)) : Smooth(Mathf.InverseLerp(0.5f, 0.75f, swapT));
            float sink = solved ? Smooth(Mathf.Clamp01((float)((now - solvedAt.Value) / sinkTime))) : 0f;

            for (int i = 0; glyphs != null && i < glyphs.Length; i++)
            {
                if (glyphs[i] == null) continue;
                int riddle = i / 3, slot = i % 3;
                bool show = riddle == shownStage && grow > 0.001f;
                if (glyphs[i].gameObject.activeSelf != show) glyphs[i].gameObject.SetActive(show);
                if (!show) continue;
                glyphs[i].transform.localScale = glyphScale[i] * Mathf.Max(0.001f, grow);
                float k = glyphGlow * (1f - sink);
                Color c = glyphBase[i] * k;
                bool flashing = swapT < 0.5f && slot == lastSlot.Value && result != Result.None && riddle == previousStage.Value;
                if (flashing)
                {
                    float pulse = 0.5f + 0.5f * Mathf.Cos((float)sinceChoice * 18f);
                    c = Color.Lerp(glyphBase[i], result == Result.Good ? goodColor : wrongColor, 0.75f) * Mathf.Lerp(glyphGlow, glyphFlash, pulse);
                }
                c.a = 1f;
                if (glyphMaterials[i] != null) glyphMaterials[i].SetColor(BaseColorId, c);
            }

            for (int i = 0; symbolModels != null && i < symbolModels.Length; i++)
                if (symbolModels[i] != null)
                    symbolModels[i].localPosition = modelRest[i] + symbolModels[i].parent.InverseTransformVector(-symbolModels[i].parent.right * (sinkDepth * sink));

            if (solved && !symbolsLocked)
            {
                symbolsLocked = true;
                foreach (var s in symbols)
                {
                    if (s == null) continue;
                    var t = s.GetComponent<Targetable>();
                    if (t != null) t.enabled = false;
                }
            }

            // Hatch
            float open = solved ? Smooth(Mathf.Clamp01((float)((now - solvedAt.Value - hatchDelay) / hatchTime))) : 0f;
            for (int i = 0; hatchHinges != null && i < hatchHinges.Length; i++)
            {
                if (hatchHinges[i] == null) continue;
                float angle = i < hatchOpenAngles.Length ? hatchOpenAngles[i] : 85f;
                hatchHinges[i].localRotation = hingeClosed[i] * Quaternion.AngleAxis(angle * open, Vector3.up);
            }

            // Screen
            bool near = AnyPlayerNear();
            string text;
            Color color;
            if (solved)
            {
                text = "<size=130%>ŚLUZA OTWARTA</size>\n\nWszystkie trzy zagadki rozwiązane.";
                color = goodColor;
            }
            else if (result == Result.Wrong && sinceChoice < messageTime)
            {
                text = "<size=130%>ZŁY SYMBOL!</size>\n\nZagadki zaczynają się od początku.";
                color = wrongColor;
            }
            else if (result == Result.Good && sinceChoice < swapTime)
            {
                text = $"<size=130%>DOBRZE!</size>\n\nZagadka {stage.Value + 1} z {riddles.Length}...";
                color = goodColor;
            }
            else if (near)
            {
                var r = riddles[stage.Value];
                text = $"<size=80%>ŚLUZA — ZAGADKA {stage.Value + 1} / {riddles.Length}</size>\n\n{r.text}\n\n<size=75%>Wskaż kluczem właściwy symbol na skale.</size>";
                color = textColor;
            }
            else
            {
                text = "ŚLUZA ZAMKNIĘTA\n\n<size=75%>podejdź bliżej</size>";
                color = idleColor;
            }

            if (screenTexts != null)
                foreach (var st in screenTexts)
                {
                    if (st == null) continue;
                    if (st.text != text) st.text = text;
                    st.color = color;
                }

            var local = PlayerHealth.Local;
            bool localNear = local != null && screen != null && (local.transform.position - screen.position).sqrMagnitude <= showRange * showRange;
            overlayText = localNear ? System.Text.RegularExpressions.Regex.Replace(text, "<[^>]*>", "") : null;
            overlayColor = color;
        }

        bool AnyPlayerNear()
        {
            if (screen == null) return false;
            foreach (var p in PlayerHealth.All)
                if (p != null && (p.transform.position - screen.position).sqrMagnitude <= showRange * showRange) return true;
            return false;
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);

        void OnGUI()
        {
            if (string.IsNullOrEmpty(overlayText) || CutsceneSystem.HidesGameplayUI) return;
            overlayStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.MiddleCenter, wordWrap = true, richText = true };
            overlayStyle.normal.textColor = overlayColor;
            const float w = 560f, h = 250f;
            GUI.Box(new Rect(Screen.width * 0.5f - w * 0.5f, 40f, w, h), overlayText, overlayStyle);
        }

        /// <summary>Editor setup (AirlockRiddlePuzzleBuilder).</summary>
        public void Setup(Riddle[] riddleList, Graspable[] symbolTargets, Transform[] models, Renderer[] glyphRenderers,
                          Transform screenRoot, TMP_Text[] texts, Transform[] hinges, float[] openAngles)
        {
            riddles = riddleList;
            symbols = symbolTargets;
            symbolModels = models;
            glyphs = glyphRenderers;
            screen = screenRoot;
            screenTexts = texts;
            hatchHinges = hinges;
            hatchOpenAngles = openAngles;
        }
    }
}
