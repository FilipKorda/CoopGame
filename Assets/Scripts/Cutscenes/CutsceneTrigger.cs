using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Starts a cutscene when players walk into this area: a trigger collider on this object, or a box
    /// (<see cref="areaSize"/>). Optionally only after a mechanism is activated (a bridge unlocked). Like
    /// <see cref="LevelExit"/> it needs no collider or NetworkObject: every machine sees the same player positions
    /// and only the server acts. Fires once per visit to the level.
    /// </summary>
    public class CutsceneTrigger : MonoBehaviour
    {
        [SerializeField] Cutscene cutscene;
        [Tooltip("Wait until every player is in the area. Off: the first player in starts it.")]
        [SerializeField] bool requireAllPlayers;
        [Tooltip("Size of the area (local axes); it stands on the transform's position. Not used when this object has a trigger collider: then the collider is the area.")]
        [SerializeField] Vector3 areaSize = new Vector3(4f, 3f, 4f);
        [Tooltip("Optional: only once this mechanism is activated (e.g. the bridge leading here is unlocked).")]
        [SerializeField] CoopMechanism requiredMechanism;
        [Tooltip("A player still riding a zipline doesn't count as in the area (one that ends in it): only once they've let go.")]
        [SerializeField] bool ignoreZipliners = true;

        bool fired;
        Collider area;

        void Awake()
        {
            // A trigger collider on this object is the area (any shape, sized in the scene view).
            if (TryGetComponent(out Collider found) && found.isTrigger) area = found;
        }

        void Update()
        {
            if (fired || cutscene == null) return;
            var network = NetworkManager.Singleton;
            if (network == null || !network.IsServer) return;
            var system = CutsceneSystem.Instance;
            if (system == null || !system.IsSpawned || system.IsBusy) return;
            if (cutscene.PlayOnce && system.HasFinished(cutscene))
            {
                fired = true;
                return;
            }
            if (requiredMechanism != null && !requiredMechanism.IsActivated) return;

            int inside = 0, total = 0;
            foreach (var player in PlayerHealth.All)
            {
                if (player == null || !player.IsSpawned) continue;
                total++;
                bool riding = ignoreZipliners && player.TryGetComponent(out PlayerMovement movement) && movement.IsZipliningForVisuals;
                if (!riding && Contains(player.transform.position)) inside++;
            }
            bool ready = total > 0 && (requireAllPlayers ? inside == total : inside > 0);
            if (ready) fired = system.ServerPlay(cutscene);
        }

        /// <summary>Whether a point (a player's feet) is in the area.</summary>
        public bool Contains(Vector3 point)
        {
            if (area != null)
            {
                // Checked by position, not trigger events: the server moves remote players by their transforms.
                foreach (float height in new[] { 0.3f, 1f, 1.7f })
                {
                    Vector3 sample = point + Vector3.up * height;
                    if ((area.ClosestPoint(sample) - sample).sqrMagnitude < 0.0001f) return true;
                }
                return false;
            }
            Vector3 local = transform.InverseTransformPoint(point) - Vector3.up * (areaSize.y * 0.5f);
            return Mathf.Abs(local.x) <= areaSize.x * 0.5f && Mathf.Abs(local.y) <= areaSize.y * 0.5f && Mathf.Abs(local.z) <= areaSize.z * 0.5f;
        }

        void OnDrawGizmos()
        {
            if (TryGetComponent(out Collider found) && found.isTrigger) return; // the collider draws itself
            Gizmos.color = new Color(1f, 0.4f, 1f, 0.8f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.up * (areaSize.y * 0.5f), areaSize);
        }
    }
}
