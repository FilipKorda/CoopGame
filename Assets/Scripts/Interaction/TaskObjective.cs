using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace CoopGame
{
    /// <summary>
    /// A level task: done once every listed obstacle is destroyed and every listed beam lever (or wheel) is turned.
    /// Then it switches on its <see cref="showWhenDone"/> objects (e.g. water pouring from pipes), every player gets
    /// a message, and <see cref="onDone"/> runs on the server. Not networked itself: it reads the obstacles' and
    /// levers' shared state, so every client gets there at the same moment, a player joining later included.
    /// </summary>
    public class TaskObjective : MonoBehaviour
    {
        [Tooltip("Must all be destroyed.")]
        [SerializeField] Damageable[] destroy;
        [Tooltip("Must all be turned all the way.")]
        [SerializeField] PowerLever[] turn;
        [Tooltip("Switched on when the task is done (kept off until then).")]
        [SerializeField] GameObject[] showWhenDone;
        [SerializeField] string doneMessage = "Zadanie wykonane — woda płynie!";
        [SerializeField] float messageTime = 6f;
        [Header("Server")]
        [SerializeField] UnityEvent onDone;

        bool done;
        float doneAt = -100f;

        public bool IsDone => done;

        /// <summary>Obstacles still standing (not yet spawned counts as standing).</summary>
        public int ObstaclesLeft
        {
            get
            {
                int left = 0;
                if (destroy != null)
                    foreach (var obstacle in destroy)
                        if (obstacle != null && (!obstacle.IsSpawned || !obstacle.IsDead)) left++;
                return left;
            }
        }

        /// <summary>Levers or wheels not turned yet.</summary>
        public int LeversLeft
        {
            get
            {
                int left = 0;
                if (turn != null)
                    foreach (var lever in turn)
                        if (lever != null && !(lever.IsSpawned && lever.IsConnected)) left++;
                return left;
            }
        }

        void Awake()
        {
            Show(false);
        }

        void Update()
        {
            if (done) return;
            var network = NetworkManager.Singleton;
            if (network == null || !network.IsListening) return;
            if (ObstaclesLeft > 0 || LeversLeft > 0) return;
            done = true;
            doneAt = Time.time;
            Show(true);
            if (network.IsServer) onDone?.Invoke();
        }

        void Show(bool on)
        {
            if (showWhenDone == null) return;
            foreach (var go in showWhenDone)
                if (go != null && go.activeSelf != on) go.SetActive(on);
        }

        void OnGUI()
        {
            if (!done || Time.time - doneAt > messageTime || CutsceneSystem.HidesGameplayUI) return;
            var style = new GUIStyle(GUI.skin.box) { fontSize = 24, alignment = TextAnchor.MiddleCenter };
            GUI.Box(new Rect(Screen.width * 0.5f - 380f, 120f, 760f, 48f), doneMessage, style);
        }

        /// <summary>Editor setup.</summary>
        public void Setup(Damageable[] obstacles, PowerLever[] levers, GameObject[] show)
        {
            destroy = obstacles;
            turn = levers;
            showWhenDone = show;
        }
    }
}
