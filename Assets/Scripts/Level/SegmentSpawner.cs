using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Ambushes along a route (an escape tunnel), segment by segment. The first time a player steps onto a segment's
    /// floor, one enemy appears at every one of that segment's spawn points at once; after that the segment is spent
    /// and never spawns again. Optionally only after a boss is beaten (the way out opens then).
    /// Server-authoritative; the enemies are networked prefabs, the spawn flash plays everywhere.
    /// </summary>
    public class SegmentSpawner : NetworkBehaviour
    {
        [System.Serializable]
        public class Segment
        {
            public string name = "Segment";
            [Tooltip("The floor of this segment (a box): a player standing on it sets off the ambush.")]
            public Transform floor;
            [Tooltip("How high above the floor a player still counts as on it.")]
            public float height = 10f;
            [Tooltip("One enemy appears at each of these, all at once.")]
            public Transform[] spawnPoints;

            [System.NonSerialized] public bool done;
        }

        [SerializeField] List<Segment> segments = new();
        [Tooltip("Enemy kinds, picked at random.")]
        [SerializeField] GameObject[] enemyPrefabs;
        [Tooltip("Optional: nothing spawns before this boss is beaten.")]
        [SerializeField] BossFight afterBoss;
        [Tooltip("A light column shows where they're coming, this long before they appear.")]
        [SerializeField] float warning = 0.8f;
        [Tooltip("Enemies spawned here chase players this far.")]
        [SerializeField] float aggroRange = 60f;
        [SerializeField] Material flashMaterial;

        public IReadOnlyList<Segment> Segments => segments;

        /// <summary>Whether a point (feet) stands on <paramref name="segment"/>'s floor.</summary>
        public static bool OnFloor(Segment segment, Vector3 point)
        {
            if (segment.floor == null) return false;
            // The floor is a box (unit cube scaled): its top face, extended upwards by Height.
            Vector3 local = segment.floor.InverseTransformPoint(point);
            if (Mathf.Abs(local.x) > 0.5f || Mathf.Abs(local.z) > 0.5f) return false;
            Vector3 top = segment.floor.TransformPoint(new Vector3(local.x, 0.5f, local.z));
            float above = point.y - top.y;
            return above > -1f && above < segment.height;
        }

        void Update()
        {
            if (!IsServer || !IsSpawned) return;
            if (afterBoss != null && afterBoss.CurrentPhase != BossFight.Phase.Defeated) return;
            if (CutsceneSystem.Instance != null && CutsceneSystem.Instance.IsBusy) return;

            foreach (var s in segments)
            {
                if (s.done) continue;
                foreach (var player in PlayerHealth.All)
                {
                    if (player == null || !player.IsSpawned || !player.IsAlive || !OnFloor(s, player.transform.position)) continue;
                    s.done = true;
                    StartCoroutine(Ambush(s));
                    break;
                }
            }
        }

        System.Collections.IEnumerator Ambush(Segment s)
        {
            if (s.spawnPoints == null) yield break;
            foreach (var point in s.spawnPoints)
                if (point != null) FlashRpc(point.position, warning);
            yield return new WaitForSeconds(warning);
            foreach (var point in s.spawnPoints)
                if (point != null) Spawn(point.position);
        }

        void Spawn(Vector3 position)
        {
            if (enemyPrefabs == null || enemyPrefabs.Length == 0) return;
            var prefab = enemyPrefabs[Random.Range(0, enemyPrefabs.Length)];
            if (prefab == null) return;
            Vector3 look = Vector3.zero;
            float best = float.MaxValue;
            foreach (var player in PlayerHealth.All)
            {
                if (player == null || !player.IsSpawned) continue;
                float d = (player.transform.position - position).sqrMagnitude;
                if (d < best) { best = d; look = player.transform.position - position; }
            }
            look.y = 0f;
            var instance = Instantiate(prefab, position, look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity);
            if (!instance.TryGetComponent(out NetworkObject networkObject)) { Destroy(instance); return; }
            networkObject.Spawn(destroyWithScene: true);
            if (instance.TryGetComponent(out EnemyController enemy)) enemy.AggroRange = aggroRange;
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void FlashRpc(Vector3 position, float time) => BossSpawnFlash.Spawn(position, time, flashMaterial);

        void OnDrawGizmosSelected()
        {
            foreach (var s in segments)
            {
                if (s.floor == null) continue;
                Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.8f);
                Gizmos.matrix = s.floor.localToWorldMatrix;
                Gizmos.DrawWireCube(Vector3.up * 0.5f, new Vector3(1f, 0.05f, 1f));
                Gizmos.matrix = Matrix4x4.identity;
                if (s.spawnPoints == null) continue;
                foreach (var p in s.spawnPoints) if (p != null) Gizmos.DrawWireSphere(p.position + Vector3.up, 0.6f);
            }
        }

        /// <summary>Editor setup.</summary>
        public void Setup(List<Segment> parts, GameObject[] prefabs, BossFight boss, Material flash)
        {
            segments = parts;
            enemyPrefabs = prefabs;
            afterBoss = boss;
            flashMaterial = flash;
        }
    }
}
