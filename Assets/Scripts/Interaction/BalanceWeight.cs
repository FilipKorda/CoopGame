using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// One of the items for the <see cref="ScaleBalancePuzzle"/>, with its (secret) weight. The players find the weight
    /// out from the sum panel. A light item is carried by one player (a Carry <see cref="Graspable"/> on this object);
    /// a heavy one needs both (a <see cref="HeavyObject"/> with two handles). Not networked itself.
    /// </summary>
    public class BalanceWeight : MonoBehaviour
    {
        [SerializeField, Min(0)] int weight = 1;
        [Tooltip("The box that rests on the scale (defaults to the BoxCollider on this object).")]
        [SerializeField] BoxCollider body;

        Graspable[] grips;
        GUIStyle hintStyle;

        public int Weight => weight;
        public BoxCollider Body => body;

        void Awake()
        {
            if (body == null) body = GetComponent<BoxCollider>();
            grips = GetComponentsInChildren<Graspable>(true);
        }

        /// <summary>Lifted by the beam right now: every grip held (the one grip of a light item, both handles of a heavy one).</summary>
        public bool IsCarried
        {
            get
            {
                if (grips == null || grips.Length == 0) return false;
                foreach (var grip in grips)
                    if (grip == null || !grip.IsHeld) return false;
                return true;
            }
        }

        void OnGUI()
        {
            if (grips == null || grips.Length < 2 || CutsceneSystem.HidesGameplayUI) return;
            var network = NetworkManager.Singleton;
            if (network == null || !network.IsListening) return;

            ulong me = network.LocalClientId;
            bool mine = false;
            int held = 0;
            foreach (var grip in grips)
            {
                if (grip == null || !grip.IsHeld) continue;
                held++;
                if (grip.HolderClientId == me) mine = true;
            }
            if (!mine) return;

            string text = held < grips.Length
                ? "Ten przedmiot jest za ciężki dla jednej osoby — drugi gracz musi złapać drugi uchwyt."
                : "Nieście go razem: pchajcie w tę samą stronę. Puśćcie nad szalką, to na nią opadnie.";
            hintStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 20, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            var content = new GUIContent(text);
            float width = Mathf.Min(900f, Screen.width - 40f);
            float height = Mathf.Max(48f, hintStyle.CalcHeight(content, width) + 10f);
            GUI.Box(new Rect((Screen.width - width) * 0.5f, Screen.height * 0.74f, width, height), content, hintStyle);
        }
    }
}
