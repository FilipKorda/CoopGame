using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// When a gate (a <see cref="DoorSequence"/>) opens, one enemy appears at every spawn point, all at once, loose on the
    /// ground (no arena: nothing happens when they die). Watches the gate's networked "opened" flag, so it needs no
    /// NetworkObject of its own: every machine shows the warning flash, only the server spawns the enemies (networked
    /// prefabs, so clients get them through NGO). Fires once.
    /// </summary>
    public class GateEnemySpawn : MonoBehaviour
    {
        [SerializeField] DoorSequence gate;
        [SerializeField] GameObject enemyPrefab;
        [SerializeField] Transform[] spawnPoints;
        [Tooltip("A light column shows where they're coming, this long before they appear.")]
        [SerializeField] float warning = 0.8f;
        [Tooltip("Enemies spawned here chase players this far.")]
        [SerializeField] float aggroRange = 20f;
        [SerializeField] Material flashMaterial;

        bool watching, wasOpen, fired;

        void Update()
        {
            if (fired || gate == null || !gate.IsSpawned) return;
            bool open = gate.IsOpened;
            if (!watching)
            {
                // A late joiner who finds the gate already open sees the enemies the server spawned; no flash.
                watching = true;
                wasOpen = open;
                if (open) { fired = true; return; }
            }
            if (!open || wasOpen) return;
            fired = true;
            StartCoroutine(Ambush());
        }

        IEnumerator Ambush()
        {
            if (spawnPoints == null) yield break;
            foreach (var point in spawnPoints)
                if (point != null) BossSpawnFlash.Spawn(point.position, warning, flashMaterial);
            yield return new WaitForSeconds(warning);
            var network = NetworkManager.Singleton;
            if (network == null || !network.IsServer || enemyPrefab == null) yield break;
            foreach (var point in spawnPoints)
            {
                if (point == null) continue;
                var instance = Instantiate(enemyPrefab, point.position, point.rotation);
                if (!instance.TryGetComponent(out NetworkObject networkObject)) { Destroy(instance); continue; }
                networkObject.Spawn(destroyWithScene: true);
                if (instance.TryGetComponent(out EnemyController enemy)) enemy.AggroRange = aggroRange;
            }
        }

        void OnDrawGizmosSelected()
        {
            if (spawnPoints == null) return;
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.8f);
            foreach (var p in spawnPoints) if (p != null) Gizmos.DrawWireSphere(p.position + Vector3.up, 0.6f);
        }

        /// <summary>Editor setup.</summary>
        public void Setup(DoorSequence door, GameObject prefab, Transform[] points, Material flash)
        {
            gate = door;
            enemyPrefab = prefab;
            spawnPoints = points;
            flashMaterial = flash;
        }
    }
}
