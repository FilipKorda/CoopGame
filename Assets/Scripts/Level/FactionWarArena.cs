using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace CoopGame
{
    /// <summary>
    /// A three-way battle: two enemy factions (the protagonist's army and the police) pour into the arena, fight
    /// each other and the players. Each side has its own count of fallen fighters (whoever brought them down); the
    /// arena is won once both counts reach their target. Optionally starts only after a cutscene has played (and
    /// never while one is on), and switches a <see cref="ShipBombardment"/> on for the fight.
    /// Everyone down: the battle clears and starts over when the players are back in. Server-authoritative; the
    /// counters, banners and spawn flashes show on every machine.
    /// </summary>
    public class FactionWarArena : NetworkBehaviour
    {
        public enum Phase : byte { Waiting, Fighting, Completed }

        [System.Serializable]
        public class Side
        {
            public string displayName = "Wrogowie";
            public EnemyFaction faction;
            [Min(1)] public int killsToWin = 50;
            public List<CombatArena.EnemyType> enemies = new();
            [Tooltip("Where this side's fighters come in (on the floor).")]
            public Transform[] spawnPoints;
            [Min(1)] public int maxAlive = 5;
            public float spawnInterval = 2f;
            public Color hudColor = Color.white;

            [System.NonSerialized] public readonly List<Damageable> spawned = new();
            [System.NonSerialized] public int pending;
            [System.NonSerialized] public float nextSpawnAt;
        }

        [SerializeField] Side protagonistSide = new() { displayName = "Armia protagonisty", faction = EnemyFaction.Protagonist, hudColor = new Color(0.85f, 0.45f, 1f) };
        [SerializeField] Side policeSide = new() { displayName = "Stróże prawa", faction = EnemyFaction.Police, hudColor = new Color(0.4f, 0.65f, 1f) };

        [Header("Start")]
        [Tooltip("Size of the arena (local axes); it stands on the transform's position. The fight starts when a player is inside.")]
        [SerializeField] Vector3 areaSize = new(60f, 15f, 60f);
        [Tooltip("Optional: the fight waits until this cutscene has played.")]
        [SerializeField] Cutscene startAfter;
        [SerializeField] float startDelay = 1.5f;

        [Header("Fight")]
        [SerializeField] float spawnWarning = 0.8f;
        [Tooltip("Fighters don't appear closer than this to a player (if a spawn point allows it).")]
        [SerializeField] float minPlayerDistance = 8f;
        [SerializeField] float enemyAggroRange = 120f;
        [Tooltip("Enemy-on-enemy hits do this many times their damage (enemy weapons are tuned for players).")]
        [SerializeField] float infightDamageScale = 2f;
        [Tooltip("Optional: shells the beach while the fight is on.")]
        [SerializeField] ShipBombardment bombardment;
        [SerializeField] Material spawnFlashMaterial;
        [Tooltip("Checkpoint: from the start of the fight on, players respawn here (player 1, player 2).")]
        [SerializeField] Transform[] respawnPoints;
        [SerializeField] bool resetOnTeamWipe = true;

        [Header("End")]
        [Tooltip("Every machine, when the battle is won.")]
        public UnityEvent onCompleted = new();
        [Tooltip("Server only, once, when the battle is won.")]
        public UnityEvent onCompletedServer = new();

        readonly NetworkVariable<Phase> phase = new();
        readonly NetworkVariable<int> protagonistKills = new();
        readonly NetworkVariable<int> policeKills = new();

        float startAt = -1f;
        string banner;
        float bannerUntil;
        GUIStyle boxStyle, bannerStyle;

        public Phase CurrentPhase => phase.Value;
        public int ProtagonistKills => protagonistKills.Value;
        public int PoliceKills => policeKills.Value;
        public int ProtagonistKillsToWin => protagonistSide.killsToWin;
        public int PoliceKillsToWin => policeSide.killsToWin;

        public override void OnNetworkSpawn()
        {
            phase.OnValueChanged += OnPhaseChanged;
            if (phase.Value != Phase.Waiting && respawnPoints != null && respawnPoints.Length > 0) PlayerSpawnPoints.SetCheckpoint(respawnPoints);
            if (phase.Value == Phase.Completed) onCompleted.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            phase.OnValueChanged -= OnPhaseChanged;
            if (IsServer && phase.Value == Phase.Fighting) EnemyController.InfightDamageScale = 1f;
        }

        void Update()
        {
            if (!IsSpawned || !IsServer) return;
            switch (phase.Value)
            {
                case Phase.Waiting:
                    if (!CanStart()) { startAt = -1f; break; }
                    if (startAt < 0f) startAt = Time.time + startDelay;
                    if (Time.time >= startAt) ServerStart();
                    break;
                case Phase.Fighting:
                    if (resetOnTeamWipe && EveryoneDown()) { ServerReset(); return; }
                    if (CutsceneBusy()) return;
                    Spawn(protagonistSide, protagonistKills);
                    Spawn(policeSide, policeKills);
                    break;
            }
        }

        static bool CutsceneBusy() => CutsceneSystem.Instance != null && CutsceneSystem.Instance.IsBusy;

        bool CanStart()
        {
            if (CutsceneBusy() || Cutscene.OpeningPending(gameObject.scene)) return false;
            if (startAfter != null && (CutsceneSystem.Instance == null || !CutsceneSystem.Instance.HasFinished(startAfter))) return false;
            foreach (var player in PlayerHealth.All)
                if (player != null && player.IsSpawned && !player.IsDowned && Contains(player.transform.position)) return true;
            return false;
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
            protagonistKills.Value = 0;
            policeKills.Value = 0;
            foreach (var side in new[] { protagonistSide, policeSide })
            {
                side.spawned.Clear();
                side.pending = 0;
                side.nextSpawnAt = Time.time;
            }
            EnemyController.InfightDamageScale = infightDamageScale;
            if (bombardment != null) bombardment.ServerSetActive(true);
            phase.Value = Phase.Fighting;
        }

        // ---------- Spawning (server) ----------

        void Spawn(Side side, NetworkVariable<int> kills)
        {
            side.spawned.RemoveAll(d => d == null || !d.IsSpawned || d.IsDead);
            if (Time.time < side.nextSpawnAt) return;
            int coming = side.spawned.Count + side.pending;
            // Never more than this side still needs to lose.
            if (coming >= side.maxAlive || coming >= side.killsToWin - kills.Value) return;
            GameObject prefab = PickEnemy(side, kills.Value);
            if (prefab == null || !TryPickSpawnPoint(side, out Vector3 position)) return;
            side.pending++;
            side.nextSpawnAt = Time.time + side.spawnInterval;
            FlashRpc(position, spawnWarning);
            StartCoroutine(SpawnLater(side, prefab, position));
        }

        System.Collections.IEnumerator SpawnLater(Side side, GameObject prefab, Vector3 position)
        {
            yield return new WaitForSeconds(spawnWarning);
            side.pending = Mathf.Max(0, side.pending - 1);
            if (phase.Value != Phase.Fighting) yield break;

            Vector3 look = NearestPlayer(position) - position;
            look.y = 0f;
            var instance = Instantiate(prefab, position, look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity);
            if (!instance.TryGetComponent(out NetworkObject networkObject)) { Destroy(instance); yield break; }
            networkObject.Spawn(destroyWithScene: true);
            if (instance.TryGetComponent(out EnemyController enemy))
            {
                enemy.ServerSetFaction(side.faction);
                enemy.AggroRange = enemyAggroRange;
            }
            if (instance.TryGetComponent(out Damageable damageable))
            {
                side.spawned.Add(damageable);
                var kills = side == protagonistSide ? protagonistKills : policeKills;
                damageable.Died += _ => OnFighterDown(side, kills);
            }
        }

        void OnFighterDown(Side side, NetworkVariable<int> kills)
        {
            if (phase.Value != Phase.Fighting) return;
            kills.Value = Mathf.Min(side.killsToWin, kills.Value + 1);
            if (protagonistKills.Value >= protagonistSide.killsToWin && policeKills.Value >= policeSide.killsToWin) ServerComplete();
        }

        void ServerComplete()
        {
            phase.Value = Phase.Completed;
            EnemyController.InfightDamageScale = 1f;
            if (bombardment != null) bombardment.ServerSetActive(false);
            // Whatever is still fighting in the arena (mortar minions and the like) goes.
            foreach (var enemy in new List<EnemyController>(EnemyController.All))
                if (enemy != null && enemy.IsAlive && Contains(enemy.transform.position))
                    enemy.Damageable.ServerKill(Vector3.up);
            onCompletedServer.Invoke();
        }

        /// <summary>Server: everyone went down. Fighters vanish, both counts start over.</summary>
        void ServerReset()
        {
            StopAllCoroutines();
            foreach (var enemy in new List<EnemyController>(EnemyController.All))
            {
                if (enemy == null || !enemy.IsSpawned || !Contains(enemy.transform.position)) continue;
                FlashRpc(enemy.transform.position, 0.2f);
                enemy.NetworkObject.Despawn(true);
            }
            foreach (var side in new[] { protagonistSide, policeSide }) { side.spawned.Clear(); side.pending = 0; }
            protagonistKills.Value = 0;
            policeKills.Value = 0;
            EnemyController.InfightDamageScale = 1f;
            if (bombardment != null) bombardment.ServerSetActive(false);
            startAt = -1f;
            phase.Value = Phase.Waiting;
        }

        static GameObject PickEnemy(Side side, int kills)
        {
            int total = 0;
            foreach (var type in side.enemies)
                if (type.prefab != null && type.weight > 0 && kills >= type.fromKill) total += type.weight;
            if (total == 0) return null;
            int roll = Random.Range(0, total);
            foreach (var type in side.enemies)
            {
                if (type.prefab == null || type.weight <= 0 || kills < type.fromKill) continue;
                if (roll < type.weight) return type.prefab;
                roll -= type.weight;
            }
            return null;
        }

        bool TryPickSpawnPoint(Side side, out Vector3 position)
        {
            position = default;
            if (side.spawnPoints == null || side.spawnPoints.Length == 0) return false;
            var good = new List<Vector3>();
            Vector3 farthest = default;
            float farthestDistance = -1f;
            foreach (var point in side.spawnPoints)
            {
                if (point == null || Occupied(point.position)) continue;
                float distance = Vector3.Distance(NearestPlayer(point.position), point.position);
                if (distance >= minPlayerDistance) good.Add(point.position);
                if (distance > farthestDistance) { farthestDistance = distance; farthest = point.position; }
            }
            if (good.Count > 0) position = good[Random.Range(0, good.Count)];
            else if (farthestDistance >= 0f) position = farthest;
            else return false;
            return true;
        }

        static bool Occupied(Vector3 point)
        {
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
                if (sqr < bestSqr) { bestSqr = sqr; best = player.transform.position; }
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

        void OnPhaseChanged(Phase previous, Phase current)
        {
            if (current != Phase.Waiting && respawnPoints != null && respawnPoints.Length > 0) PlayerSpawnPoints.SetCheckpoint(respawnPoints);
            if (current == Phase.Fighting) ShowBanner("Bitwa na plaży! Trzy strony walczą ze sobą");
            else if (current == Phase.Completed) { ShowBanner("Bitwa wygrana!"); onCompleted.Invoke(); }
            else ShowBanner("Polegliście... wróćcie na plażę i spróbujcie jeszcze raz");
        }

        void ShowBanner(string text)
        {
            banner = text;
            bannerUntil = Time.time + 3f;
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void FlashRpc(Vector3 position, float time) => BossSpawnFlash.Spawn(position, time, spawnFlashMaterial);

        void OnGUI()
        {
            if (!IsSpawned || CutsceneSystem.HidesGameplayUI) return;
            bool fighting = phase.Value == Phase.Fighting;
            bool showBanner = Time.time < bannerUntil;
            if (!fighting && !showBanner) return;
            if (boxStyle == null)
            {
                boxStyle = new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                bannerStyle = new GUIStyle(GUI.skin.label) { fontSize = 34, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            }
            if (fighting)
            {
                Counter(new Rect(Screen.width * 0.5f - 345f, 42f, 335f, 46f), protagonistSide, protagonistKills.Value);
                Counter(new Rect(Screen.width * 0.5f + 10f, 42f, 335f, 46f), policeSide, policeKills.Value);
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

        void Counter(Rect rect, Side side, int kills)
        {
            GUI.Box(rect, "", boxStyle);
            float fill = Mathf.Clamp01(kills / (float)side.killsToWin);
            GUI.color = new Color(side.hudColor.r, side.hudColor.g, side.hudColor.b, 0.85f);
            GUI.DrawTexture(new Rect(rect.x + 4f, rect.yMax - 10f, (rect.width - 8f) * fill, 6f), Texture2D.whiteTexture);
            GUI.color = kills >= side.killsToWin ? new Color(0.5f, 1f, 0.5f) : Color.white;
            GUI.Label(new Rect(rect.x, rect.y, rect.width, rect.height - 6f), $"{side.displayName.ToUpperInvariant()}   {kills} / {side.killsToWin}", boxStyle);
            GUI.color = Color.white;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.8f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.up * (areaSize.y * 0.5f), areaSize);
            Gizmos.matrix = Matrix4x4.identity;
            foreach (var side in new[] { protagonistSide, policeSide })
            {
                if (side?.spawnPoints == null) continue;
                Gizmos.color = side.hudColor;
                foreach (var point in side.spawnPoints)
                    if (point != null) Gizmos.DrawWireSphere(point.position + Vector3.up, 0.6f);
            }
        }

        /// <summary>Editor setup.</summary>
        public void Setup(Side protagonist, Side police, Vector3 size, Cutscene after, ShipBombardment ship, Material flash, Transform[] respawns)
        {
            protagonistSide = protagonist;
            policeSide = police;
            areaSize = size;
            startAfter = after;
            bombardment = ship;
            spawnFlashMaterial = flash;
            respawnPoints = respawns;
        }
    }
}
