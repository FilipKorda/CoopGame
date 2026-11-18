using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace CoopGame
{
    /// <summary>
    /// A fighting arena: once the players are in its area, enemies keep appearing at its spawn points (a few at a
    /// time) and hunt them; when the players have killed <see cref="killsToWin"/> of them the arena is done,
    /// no more enemies come and any left over are wiped out. If every player goes down, the arena resets and
    /// starts over when they come back in. Server-authoritative: the server spawns the enemies (network prefabs) and
    /// counts the kills; every screen shows the count, the spawn flashes and the arena's barriers.
    /// </summary>
    public class CombatArena : NetworkBehaviour
    {
        public enum Phase : byte { Waiting, Fighting, Completed }

        [System.Serializable]
        public class EnemyType
        {
            [Tooltip("Enemy network prefab (registered in the network prefab list).")]
            public GameObject prefab;
            [Tooltip("How often it's picked compared to the others.")]
            [Min(0)] public int weight = 1;
            [Tooltip("Only comes once this many enemies have been killed (tougher enemies later in the fight).")]
            [Min(0)] public int fromKill;
        }

        [Header("Goal")]
        [Tooltip("Kills that finish the arena.")]
        [SerializeField, Min(1)] int killsToWin = 15;
        [SerializeField] List<EnemyType> enemies = new();
        [Tooltip("Which side the arena's enemies are (Police: blue, with a cap).")]
        [SerializeField] EnemyFaction faction = EnemyFaction.Protagonist;

        [Header("Spawning")]
        [Tooltip("Where enemies appear (on the floor).")]
        [SerializeField] Transform[] spawnPoints;
        [Tooltip("Most enemies in the arena at once.")]
        [SerializeField, Min(1)] int maxAlive = 4;
        [Tooltip("Seconds from the start to the first enemy.")]
        [SerializeField] float startDelay = 2f;
        [Tooltip("Seconds between two enemies appearing (while there's room for more).")]
        [SerializeField] float spawnInterval = 2f;
        [Tooltip("A flash marks the spot this long before the enemy appears.")]
        [SerializeField] float spawnWarning = 0.8f;
        [Tooltip("Enemies don't appear closer than this to a player (if any spawn point allows it).")]
        [SerializeField] float minPlayerDistance = 7f;
        [Tooltip("How far the arena's enemies notice players: the whole arena, so they come straight for them.")]
        [SerializeField] float enemyAggroRange = 80f;
        [SerializeField] Material spawnFlashMaterial;

        [Header("Start")]
        [Tooltip("Size of the arena (local axes); it stands on the transform's position. The fight starts when the players are inside.")]
        [SerializeField] Vector3 areaSize = new Vector3(30f, 10f, 30f);
        [Tooltip("Wait until every player is inside. Off: the first one in starts it.")]
        [SerializeField] bool requireAllPlayers = true;

        [Header("While fighting")]
        [Tooltip("Switched on during the fight only (force fields closing the way out...).")]
        [SerializeField] GameObject[] barriers;
        [Tooltip("Checkpoint: from the start of the fight on, players respawn here (player 1, player 2) after everyone went down. Empty = the level's spawn points.")]
        [SerializeField] Transform[] respawnPoints;
        [Tooltip("Everyone down: the arena clears and starts over when the players come back in.")]
        [SerializeField] bool resetOnTeamWipe = true;

        [Header("End")]
        [Tooltip("Enemies still in the arena when it's won are destroyed (they drop their loot).")]
        [SerializeField] bool clearEnemiesOnWin = true;
        [Tooltip("Every machine, when the fight starts.")]
        public UnityEvent onStarted = new();
        [Tooltip("Every machine, when the arena is won (also for a player who joins later).")]
        public UnityEvent onCompleted = new();
        [Tooltip("Server only, once, when the arena is won: networked rewards, opening networked doors...")]
        public UnityEvent onCompletedServer = new();

        readonly NetworkVariable<Phase> phase = new(Phase.Waiting);
        readonly NetworkVariable<int> kills = new();

        // Server
        struct PendingSpawn
        {
            public GameObject Prefab;
            public Vector3 Position;
            public float At;
        }
        readonly List<Damageable> spawned = new();
        readonly List<PendingSpawn> pending = new();
        float nextSpawnAt;

        // Every machine
        struct Flash
        {
            public Transform Body;
            public Vector3 Ground;
            public float Start, Length;
        }
        readonly List<Flash> flashes = new();
        bool startedShown;
        float bannerUntil;
        string banner = "";
        GUIStyle boxStyle, bannerStyle;

        public Phase CurrentPhase => phase.Value;
        public int Kills => kills.Value;
        public int KillsToWin => killsToWin;

        public override void OnNetworkSpawn()
        {
            phase.OnValueChanged += OnPhaseChanged;
            ApplyPhase(phase.Value, joined: true);
        }

        public override void OnNetworkDespawn()
        {
            phase.OnValueChanged -= OnPhaseChanged;
            foreach (var flash in flashes)
                if (flash.Body != null) Destroy(flash.Body.gameObject);
            flashes.Clear();
        }

        void Update()
        {
            if (IsSpawned && IsServer) ServerUpdate();
            UpdateFlashes();
        }

        // ---------- Server ----------

        void ServerUpdate()
        {
            switch (phase.Value)
            {
                case Phase.Waiting:
                    if (PlayersReady() && !CutsceneBusy()) ServerStart();
                    break;
                case Phase.Fighting:
                    if (resetOnTeamWipe && EveryoneDown())
                    {
                        ServerReset();
                        return;
                    }
                    spawned.RemoveAll(d => d == null || !d.IsSpawned);
                    SpawnDue();
                    ScheduleSpawns();
                    break;
            }
        }

        // Also waits for the level's opening cutscene, which starts only once the level change's black screen is gone.
        bool CutsceneBusy() => (CutsceneSystem.Instance != null && CutsceneSystem.Instance.IsBusy) || Cutscene.OpeningPending(gameObject.scene);

        bool PlayersReady()
        {
            int inside = 0, total = 0;
            foreach (var player in PlayerHealth.All)
            {
                // Downed players don't count: after a lost fight they're still lying in the arena until they respawn.
                if (player == null || !player.IsSpawned || player.IsDowned) continue;
                total++;
                if (Contains(player.transform.position)) inside++;
            }
            return total > 0 && (requireAllPlayers ? inside == total : inside > 0);
        }

        static bool EveryoneDown()
        {
            int count = 0;
            foreach (var player in PlayerHealth.All)
            {
                if (player == null || !player.IsSpawned) continue;
                count++;
                if (!player.IsDowned) return false;
            }
            return count > 0;
        }

        void ServerStart()
        {
            kills.Value = 0;
            spawned.Clear();
            pending.Clear();
            nextSpawnAt = Time.time + startDelay;
            phase.Value = Phase.Fighting;
        }

        /// <summary>Server: start the fight now (e.g. from a cutscene's event), players inside or not.</summary>
        public void ServerBegin()
        {
            if (IsServer && phase.Value == Phase.Waiting) ServerStart();
        }

        void ScheduleSpawns()
        {
            if (Time.time < nextSpawnAt || CutsceneBusy()) return;
            int coming = spawned.Count + pending.Count;
            // Never more than are still needed to win, so nothing is left over at the end.
            if (coming >= maxAlive || coming >= killsToWin - kills.Value) return;

            GameObject prefab = PickEnemy();
            if (prefab == null || !TryPickSpawnPoint(out Vector3 position)) return;
            pending.Add(new PendingSpawn { Prefab = prefab, Position = position, At = Time.time + spawnWarning });
            FlashRpc(position, spawnWarning);
            nextSpawnAt = Time.time + spawnInterval;
        }

        void SpawnDue()
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (Time.time < pending[i].At) continue;
                var spawn = pending[i];
                pending.RemoveAt(i);
                SpawnEnemy(spawn.Prefab, spawn.Position);
            }
        }

        void SpawnEnemy(GameObject prefab, Vector3 position)
        {
            Vector3 toPlayer = NearestPlayer(position) - position;
            toPlayer.y = 0f;
            Quaternion rotation = toPlayer.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toPlayer) : Quaternion.identity;

            var instance = Instantiate(prefab, position, rotation);
            if (!instance.TryGetComponent(out NetworkObject networkObject))
            {
                Debug.LogWarning($"[CombatArena] '{prefab.name}' has no NetworkObject; it can't be an arena enemy.", this);
                Destroy(instance);
                return;
            }
            networkObject.Spawn(destroyWithScene: true);
            if (instance.TryGetComponent(out EnemyController enemy))
            {
                enemy.AggroRange = enemyAggroRange;
                if (faction != EnemyFaction.Protagonist) enemy.ServerSetFaction(faction);
            }
            if (instance.TryGetComponent(out Damageable damageable))
            {
                spawned.Add(damageable);
                damageable.Died += _ => OnEnemyDied();
            }
        }

        void OnEnemyDied()
        {
            if (phase.Value != Phase.Fighting) return;
            kills.Value++;
            if (kills.Value >= killsToWin) ServerComplete();
        }

        void ServerComplete()
        {
            pending.Clear();
            phase.Value = Phase.Completed;
            if (clearEnemiesOnWin)
            {
                // Everything hostile still in the arena goes (the arena's own enemies and whatever they brought).
                foreach (var enemy in new List<EnemyController>(EnemyController.All))
                    if (enemy != null && enemy.IsAlive && Contains(enemy.transform.position))
                        enemy.Damageable.ServerKill(Vector3.up);
            }
            onCompletedServer.Invoke();
        }

        /// <summary>Server: everyone went down. Enemies vanish (no loot), the count starts over.</summary>
        void ServerReset()
        {
            pending.Clear();
            foreach (var enemy in new List<EnemyController>(EnemyController.All))
            {
                if (enemy == null || !enemy.IsSpawned || !Contains(enemy.transform.position)) continue;
                FlashRpc(enemy.transform.position, 0.2f);
                enemy.NetworkObject.Despawn(true);
            }
            spawned.Clear();
            kills.Value = 0;
            phase.Value = Phase.Waiting;
        }

        GameObject PickEnemy()
        {
            int total = 0;
            foreach (var type in enemies)
                if (type.prefab != null && type.weight > 0 && kills.Value >= type.fromKill) total += type.weight;
            if (total == 0) return null;
            int roll = Random.Range(0, total);
            foreach (var type in enemies)
            {
                if (type.prefab == null || type.weight <= 0 || kills.Value < type.fromKill) continue;
                if (roll < type.weight) return type.prefab;
                roll -= type.weight;
            }
            return null;
        }

        bool TryPickSpawnPoint(out Vector3 position)
        {
            position = default;
            if (spawnPoints == null || spawnPoints.Length == 0) return false;

            // Points far enough from the players and not taken: pick one at random. None: the farthest one.
            var good = new List<Vector3>();
            Vector3 farthest = default;
            float farthestDistance = -1f;
            foreach (var point in spawnPoints)
            {
                if (point == null || Occupied(point.position)) continue;
                float distance = Vector3.Distance(NearestPlayer(point.position), point.position);
                if (distance >= minPlayerDistance) good.Add(point.position);
                if (distance > farthestDistance)
                {
                    farthestDistance = distance;
                    farthest = point.position;
                }
            }
            if (good.Count > 0) position = good[Random.Range(0, good.Count)];
            else if (farthestDistance >= 0f) position = farthest;
            else return false;
            return true;
        }

        bool Occupied(Vector3 point)
        {
            foreach (var spawn in pending)
                if ((spawn.Position - point).sqrMagnitude < 4f) return true;
            foreach (var enemy in EnemyController.All)
                if (enemy != null && (enemy.transform.position - point).sqrMagnitude < 2.25f) return true;
            return false;
        }

        static Vector3 NearestPlayer(Vector3 from)
        {
            Vector3 best = from + Vector3.forward * 1000f;
            float bestSqr = float.PositiveInfinity;
            foreach (var player in PlayerHealth.All)
            {
                if (player == null || !player.IsSpawned) continue;
                float sqr = (player.transform.position - from).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = player.transform.position;
                }
            }
            return best;
        }

        /// <summary>Whether a point (feet) is inside the arena.</summary>
        public bool Contains(Vector3 point)
        {
            Vector3 local = transform.InverseTransformPoint(point) - Vector3.up * (areaSize.y * 0.5f);
            return Mathf.Abs(local.x) <= areaSize.x * 0.5f && Mathf.Abs(local.y) <= areaSize.y * 0.5f && Mathf.Abs(local.z) <= areaSize.z * 0.5f;
        }

        // ---------- Every machine ----------

        void OnPhaseChanged(Phase previous, Phase current) => ApplyPhase(current, joined: false);

        void ApplyPhase(Phase current, bool joined)
        {
            if (barriers != null)
                foreach (var barrier in barriers)
                    if (barrier != null) barrier.SetActive(current == Phase.Fighting);

            if (current != Phase.Waiting && respawnPoints != null && respawnPoints.Length > 0)
                PlayerSpawnPoints.SetCheckpoint(respawnPoints);

            if (current == Phase.Fighting)
            {
                if (!joined) ShowBanner($"Arena! Pokonajcie {killsToWin} wrogów");
                if (!startedShown) onStarted.Invoke();
                startedShown = true;
            }
            else if (current == Phase.Completed)
            {
                if (!joined) ShowBanner("Arena ukończona!");
                onCompleted.Invoke();
            }
            else if (!joined)
            {
                startedShown = false;
                ShowBanner("Arena przegrana... wróćcie i spróbujcie jeszcze raz");
            }
        }

        void ShowBanner(string text)
        {
            banner = text;
            bannerUntil = Time.time + 3f;
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void FlashRpc(Vector3 position, float warning)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "ArenaSpawnFlash";
            Destroy(body.GetComponent<Collider>());
            var renderer = body.GetComponent<Renderer>();
            if (spawnFlashMaterial != null) renderer.sharedMaterial = spawnFlashMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            body.transform.position = position;
            body.transform.localScale = Vector3.zero;
            flashes.Add(new Flash { Body = body.transform, Ground = position, Start = Time.time, Length = warning });
        }

        // A column of light that grows while the enemy is coming, then collapses as it appears.
        void UpdateFlashes()
        {
            for (int i = flashes.Count - 1; i >= 0; i--)
            {
                var flash = flashes[i];
                float age = Time.time - flash.Start;
                const float collapse = 0.25f;
                if (flash.Body == null || age > flash.Length + collapse)
                {
                    if (flash.Body != null) Destroy(flash.Body.gameObject);
                    flashes.RemoveAt(i);
                    continue;
                }
                float grow = flash.Length > 0f ? Mathf.Clamp01(age / flash.Length) : 1f;
                float shrink = 1f - Mathf.Clamp01((age - flash.Length) / collapse);
                float pulse = 1f + 0.12f * Mathf.Sin(age * 30f);
                float radius = Mathf.Lerp(0.3f, 1.1f, grow) * shrink * pulse;
                float height = Mathf.Lerp(0.5f, 1.6f, grow) * (age > flash.Length ? 1f + (1f - shrink) * 1.5f : 1f);
                // A unit cylinder is 2 tall around its centre: lift it by its half height so it stands on the floor.
                flash.Body.localScale = new Vector3(radius, height, radius);
                flash.Body.SetPositionAndRotation(flash.Ground + Vector3.up * height, Quaternion.Euler(0f, age * 360f, 0f));
            }
        }

        void OnGUI()
        {
            if (!IsSpawned || CutsceneSystem.HidesGameplayUI) return;
            bool fighting = phase.Value == Phase.Fighting;
            bool showBanner = Time.time < bannerUntil;
            if (!fighting && !showBanner) return;

            if (boxStyle == null)
            {
                boxStyle = new GUIStyle(GUI.skin.box) { fontSize = 20, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                bannerStyle = new GUIStyle(GUI.skin.label) { fontSize = 34, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            }

            if (fighting)
            {
                // Counter with a bar, top centre (below the dev banner).
                var rect = new Rect(Screen.width * 0.5f - 170f, 42f, 340f, 46f);
                GUI.Box(rect, "", boxStyle);
                float fill = Mathf.Clamp01(kills.Value / (float)killsToWin);
                GUI.color = new Color(1f, 0.35f, 0.2f, 0.8f);
                GUI.DrawTexture(new Rect(rect.x + 4f, rect.yMax - 10f, (rect.width - 8f) * fill, 6f), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(rect.x, rect.y, rect.width, rect.height - 6f), $"ARENA   {kills.Value} / {killsToWin}", boxStyle);
            }

            if (showBanner)
            {
                float alpha = Mathf.Clamp01((bannerUntil - Time.time) / 0.5f);
                var rect = new Rect(0f, Screen.height * 0.22f, Screen.width, 60f);
                GUI.color = new Color(0f, 0f, 0f, 0.7f * alpha);
                GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), banner, bannerStyle);
                GUI.color = new Color(1f, 0.85f, 0.35f, alpha);
                GUI.Label(rect, banner, bannerStyle);
                GUI.color = Color.white;
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.8f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.up * (areaSize.y * 0.5f), areaSize);
            Gizmos.matrix = Matrix4x4.identity;
            if (spawnPoints != null)
                foreach (var point in spawnPoints)
                    if (point != null) Gizmos.DrawWireSphere(point.position + Vector3.up, 0.6f);
            Gizmos.color = Color.green;
            if (respawnPoints != null)
                foreach (var point in respawnPoints)
                    if (point != null) Gizmos.DrawWireSphere(point.position + Vector3.up, 0.5f);
        }
    }
}
