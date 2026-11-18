using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// A way out of the level. When every player stands in its area for a moment (co-op: nobody gets left behind),
    /// the server takes everyone to <see cref="targetScene"/>, arriving at the spawn points named
    /// <see cref="targetEntry"/> (<see cref="SceneFlow"/>). A player waiting in it alone sees a prompt.
    /// No NetworkObject needed: every machine sees the same player positions, and only the server acts.
    /// </summary>
    public class LevelExit : MonoBehaviour
    {
        [Tooltip("Scene to go to (its name; it must be in the Build Settings).")]
        [SerializeField] string targetScene = "";
        [Tooltip("Which entrance of that scene to arrive at (PlayerSpawnPoints with this Entry Id); empty = its default.")]
        [SerializeField] string targetEntry = "";
        [Tooltip("Everyone has to stand in it. Off: the first player in takes everyone along.")]
        [SerializeField] bool requireAllPlayers = true;
        [Tooltip("Size of the area (local axes); it stands on the transform's position.")]
        [SerializeField] Vector3 areaSize = new Vector3(3f, 3f, 3f);
        [Tooltip("Seconds everyone has to stay in it before the level changes.")]
        [SerializeField] float holdTime = 1f;
        [Tooltip("Shown to a player standing in it.")]
        [SerializeField] string label = "Wyjście";
        [Tooltip("Played first (the act's closing cutscene); the level changes when it ends or is skipped. Empty = leave straight away.")]
        [SerializeField] Cutscene outroCutscene;

        int inside, total;
        bool localInside;
        float readySince = -1f;

        public string TargetScene => targetScene;

        void Update()
        {
            var network = NetworkManager.Singleton;
            if (network == null || !network.IsListening)
            {
                localInside = false;
                return;
            }

            inside = total = 0;
            localInside = false;
            foreach (var player in PlayerHealth.All)
            {
                if (player == null || !player.IsSpawned) continue;
                total++;
                if (!Contains(player.transform.position)) continue;
                inside++;
                if (player.IsOwner) localInside = true;
            }

            bool ready = total > 0 && (requireAllPlayers ? inside == total : inside > 0);
            if (!network.IsServer || !ready)
            {
                readySince = -1f;
                return;
            }
            if (readySince < 0f) readySince = Time.time;
            if (Time.time - readySince < holdTime || SceneFlow.Instance == null) return;

            var cutscenes = CutsceneSystem.Instance;
            if (outroCutscene != null && cutscenes != null && cutscenes.IsSpawned
                && !(outroCutscene.PlayOnce && cutscenes.HasFinished(outroCutscene)))
            {
                // The cutscene takes everyone on to the target when it's over.
                if (!cutscenes.IsBusy) cutscenes.ServerPlay(outroCutscene, targetScene, targetEntry);
                return;
            }
            SceneFlow.Instance.ServerGoTo(targetScene, targetEntry);
        }

        /// <summary>Whether a point (a player's feet) is in the area.</summary>
        public bool Contains(Vector3 point)
        {
            Vector3 local = transform.InverseTransformPoint(point) - Vector3.up * (areaSize.y * 0.5f);
            return Mathf.Abs(local.x) <= areaSize.x * 0.5f && Mathf.Abs(local.y) <= areaSize.y * 0.5f && Mathf.Abs(local.z) <= areaSize.z * 0.5f;
        }

        void OnGUI()
        {
            if (!localInside || CutsceneSystem.ControlsLocked) return;
            string text = requireAllPlayers && inside < total
                ? $"{label}: czekam na drugiego gracza ({inside}/{total})"
                : $"{label}: przechodzimy...";
            var style = new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
            GUI.Box(new Rect(Screen.width * 0.5f - 260f, Screen.height - 190f, 520f, 44f), text, style);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.8f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.up * (areaSize.y * 0.5f), areaSize);
        }
    }
}
