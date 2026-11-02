using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoopGame
{
    /// <summary>
    /// A conversation with an NPC that both players have to join: one player presses Interact (F) next to the NPC and
    /// waits; the other comes over and presses F too. Only when both stand by the NPC does the server start the
    /// <see cref="cutscene"/> (the talk itself) for everyone. The waiting player can call it off with F again, and it's
    /// called off by itself if they walk away. A game with a single player starts it at once.
    /// Needs a NetworkObject on this object (the waiting state is shared).
    /// </summary>
    public class NpcTalk : NetworkBehaviour
    {
        const ulong NoOne = ulong.MaxValue;

        [Tooltip("Who you talk to; distances are measured from here. Empty = this object.")]
        [SerializeField] Transform npc;
        [SerializeField] string npcName = "Prezydent wyspy";
        [Tooltip("The conversation: started for both players once both have joined.")]
        [SerializeField] Cutscene cutscene;
        [Tooltip("How close (metres, flat) a player has to be to the NPC to start or join the talk.")]
        [SerializeField] float talkRange = 3.5f;
        [Tooltip("The waiting player calls it off by walking farther than this from the NPC.")]
        [SerializeField] float leaveRange = 6f;

        [Header("Prompts ({0} = NPC name)")]
        [SerializeField] string startHint = "[F] Porozmawiaj: {0}";
        [SerializeField] string waitingHint = "Czekasz na drugiego gracza, żeby porozmawiać: {0}   [F] anuluj";
        [SerializeField] string joinHint = "[F] Dołącz do rozmowy: {0} (drugi gracz czeka)";
        [SerializeField] string comeHint = "Drugi gracz czeka na ciebie przy: {0}";

        readonly NetworkVariable<ulong> waiting = new(NoOne);
        InputAction interactAction;

        Vector3 NpcPosition => (npc != null ? npc : transform).position;
        bool Done => cutscene == null || (CutsceneSystem.Instance != null && cutscene.PlayOnce && CutsceneSystem.Instance.HasFinished(cutscene));

        void OnEnable() => interactAction = InputSystem.actions.FindAction("Player/Interact", true);

        static float FlatDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        bool Near(PlayerHealth player, float range) =>
            player != null && player.IsAlive && FlatDistance(player.transform.position, NpcPosition) <= range
            && Mathf.Abs(player.transform.position.y - NpcPosition.y) < 4f;

        static PlayerHealth Player(ulong clientId)
        {
            foreach (var player in PlayerHealth.All)
                if (player != null && player.OwnerClientId == clientId) return player;
            return null;
        }

        // ---------- Every machine: the local player's F ----------

        void Update()
        {
            if (!IsSpawned) return;
            if (IsServer) ServerUpdate();

            var me = PlayerHealth.Local;
            if (me == null || Done || CutsceneSystem.ControlsLocked || interactAction == null || !interactAction.WasPressedThisFrame()) return;
            if (Near(me, talkRange) || waiting.Value == me.OwnerClientId) PressRpc();
        }

        // ---------- Server ----------

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void PressRpc(RpcParams rpcParams = default) => ServerPress(rpcParams.Receive.SenderClientId);

        /// <summary>Server: a player pressed F at the NPC (start, join, or call off their own waiting).</summary>
        public void ServerPress(ulong clientId)
        {
            if (!IsServer || Done) return;
            var system = CutsceneSystem.Instance;
            if (system == null || system.IsBusy) return;

            if (waiting.Value == clientId)
            {
                waiting.Value = NoOne; // called off
                return;
            }
            // The server sees the players a moment late: a little slack on the distance.
            if (!Near(Player(clientId), talkRange + 1f)) return;

            if (waiting.Value == NoOne)
            {
                if (NetworkManager.ConnectedClientsIds.Count <= 1) StartTalk();
                else waiting.Value = clientId;
                return;
            }
            // The other player joins: both have to be at the NPC.
            if (Near(Player(waiting.Value), talkRange + 1f)) StartTalk();
        }

        void StartTalk()
        {
            waiting.Value = NoOne;
            CutsceneSystem.Instance.ServerPlay(cutscene);
        }

        void ServerUpdate()
        {
            if (waiting.Value == NoOne) return;
            // The waiting player left (walked off, went down, quit), or the talk can't happen any more.
            if (Done || !Near(Player(waiting.Value), leaveRange + 1f)) waiting.Value = NoOne;
        }

        // ---------- Prompts ----------

        void OnGUI()
        {
            if (!IsSpawned || Done || CutsceneSystem.HidesGameplayUI || CutsceneSystem.ControlsLocked) return;
            var me = PlayerHealth.Local;
            if (me == null || !me.IsAlive) return;

            ulong waiter = waiting.Value;
            string text = null;
            if (waiter == me.OwnerClientId) text = waitingHint;
            else if (waiter != NoOne) text = Near(me, talkRange) ? joinHint : comeHint;
            else if (Near(me, talkRange)) text = startHint;
            if (text == null) return;

            var style = new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            var content = new GUIContent(string.Format(text, npcName));
            float width = Mathf.Min(1000f, Screen.width - 40f);
            float height = Mathf.Max(44f, style.CalcHeight(content, width) + 8f);
            GUI.Box(new Rect((Screen.width - width) * 0.5f, Screen.height - 146f - height, width, height), content, style);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.9f, 1f);
            Gizmos.DrawWireSphere(NpcPosition, talkRange);
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.35f);
            Gizmos.DrawWireSphere(NpcPosition, leaveRange);
        }

        /// <summary>Editor setup.</summary>
        public void Setup(Transform talkTo, string displayName, Cutscene talk)
        {
            npc = talkTo;
            npcName = displayName;
            cutscene = talk;
        }
    }
}
