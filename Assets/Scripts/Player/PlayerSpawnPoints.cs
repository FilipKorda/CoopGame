using System.Collections.Generic;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Marks a scene as a playable level and provides one spawn point per player.
    /// Player objects wait for this to exist before they start simulating; when a new level loads, they move to its
    /// spawn points by themselves.
    /// A level can have several entrances: give each set of spawn points an <see cref="entryId"/>, and a
    /// <see cref="LevelExit"/> leading here can name the one to arrive at. The set without an id is the default.
    /// </summary>
    public class PlayerSpawnPoints : MonoBehaviour
    {
        static readonly List<PlayerSpawnPoints> s_All = new();
        static string s_RequestedEntry = "";

        [Tooltip("Which entrance this is; empty = the level's default spawn.")]
        [SerializeField] string entryId = "";
        [SerializeField] Transform[] spawnPoints;

        public string EntryId => entryId;

        /// <summary>
        /// The spawn points players use in the current level: the entrance they were sent to, else the default
        /// one (no id), else any. Null while no level is loaded.
        /// </summary>
        public static PlayerSpawnPoints Instance
        {
            get
            {
                PlayerSpawnPoints fallback = null;
                foreach (var points in s_All)
                {
                    if (points.entryId == s_RequestedEntry) return points;
                    if (fallback == null || (string.IsNullOrEmpty(points.entryId) && !string.IsNullOrEmpty(fallback.entryId))) fallback = points;
                }
                return fallback;
            }
        }

        /// <summary>Every client, before a level loads: which entrance of it the players arrive at.</summary>
        public static void SetRequestedEntry(string entry) => s_RequestedEntry = entry ?? "";

        /// <summary>
        /// Every client: a checkpoint in this level (e.g. a <see cref="CombatArena"/>): from now on players respawn at
        /// these points (player 1, player 2...) instead of the level's spawn points. Null = back to the spawn points.
        /// Forgotten when the level unloads.
        /// </summary>
        public static void SetCheckpoint(Transform[] points) => s_Checkpoint = points != null && points.Length > 0 ? points : null;

        static Transform[] s_Checkpoint;

        // A new play session starts at the default entrance (statics survive Play Mode without a domain reload).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetEntry()
        {
            s_RequestedEntry = "";
            s_Checkpoint = null;
        }

        void Awake() => s_All.Add(this);

        void OnDestroy()
        {
            s_All.Remove(this);
            s_Checkpoint = null; // the level is going away, and its checkpoint with it
        }

        public Pose GetSpawnPose(ulong clientId)
        {
            Transform[] points = spawnPoints;
            if (s_Checkpoint != null)
            {
                Transform checkpoint = s_Checkpoint[(int)(clientId % (ulong)s_Checkpoint.Length)];
                if (checkpoint != null) return new Pose(checkpoint.position, checkpoint.rotation);
            }
            if (points == null || points.Length == 0)
                return new Pose(transform.position, transform.rotation);

            Transform point = points[(int)(clientId % (ulong)points.Length)];
            return new Pose(point.position, point.rotation);
        }

        void OnDrawGizmos()
        {
            if (spawnPoints == null) return;
            Gizmos.color = Color.green;
            foreach (var point in spawnPoints)
            {
                if (point == null) continue;
                Gizmos.DrawWireSphere(point.position + Vector3.up, 0.5f);
                Gizmos.DrawRay(point.position + Vector3.up, point.forward);
            }
        }
    }
}
