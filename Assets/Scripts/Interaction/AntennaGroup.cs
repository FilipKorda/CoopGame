using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace CoopGame
{
    /// <summary>
    /// Watches a set of <see cref="AntennaRig"/>s: once every one is aimed, every player gets a message and
    /// <see cref="onAllAligned"/> runs on the server (for whatever that unlocks).
    /// </summary>
    public class AntennaGroup : MonoBehaviour
    {
        [SerializeField] AntennaRig[] antennas;
        [SerializeField] string message = "Wszystkie anteny ustawione — sygnał połączony!";
        [SerializeField] float messageTime = 5f;
        [Header("Server")]
        [SerializeField] UnityEvent onAllAligned;

        bool done;
        float doneAt = -100f;

        public bool AllAligned => done;

        void Update()
        {
            if (done || antennas == null || antennas.Length == 0) return;
            foreach (var antenna in antennas)
                if (antenna == null || !antenna.IsAligned) return;
            done = true;
            doneAt = Time.time;
            var network = NetworkManager.Singleton;
            if (network != null && network.IsServer) onAllAligned?.Invoke();
        }

        void OnGUI()
        {
            if (!done || Time.time - doneAt > messageTime || CutsceneSystem.HidesGameplayUI) return;
            var style = new GUIStyle(GUI.skin.box) { fontSize = 24, alignment = TextAnchor.MiddleCenter };
            GUI.Box(new Rect(Screen.width * 0.5f - 380f, 120f, 760f, 48f), message, style);
        }

        /// <summary>Editor setup.</summary>
        public void SetAntennas(AntennaRig[] value) => antennas = value;
    }
}
