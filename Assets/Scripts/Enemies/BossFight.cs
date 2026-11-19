using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace CoopGame
{
    /// <summary>
    /// A boss fight in segments (the left arm, then the right arm, then the head). Only the active segment can be hurt;
    /// when it breaks the boss sways for a few seconds, can't be hurt and doesn't attack, then the next segment wakes
    /// up. Its attack: every few seconds it picks one player at random and spits a salvo of fireballs from its mouth;
    /// each fireball keeps turning towards where that player is now, so moving and jumping is how to dodge them.
    /// The fight starts when a player walks into the arena (<see cref="areaSize"/>, standing on this transform).
    /// Server-authoritative; every machine flies its own copies of the fireballs (the server's copy decides what it
    /// hits), sways the body and draws the boss's health bar (segment by segment) from the shared state.
    /// Each segment is its own NetworkObject with a <see cref="Damageable"/> (its share of the health) and a
    /// <see cref="BossSegment"/>.
    /// </summary>
    public class BossFight : NetworkBehaviour
    {
        public enum Phase : byte { Waiting, Fighting, Staggering, Defeated }

        /// <summary>
        /// Salvo: <c>count</c> fireballs one after another at one random player. Spread: <c>count</c> fireballs at once
        /// in a fan around the line to one random player. Volcanoes: a fire volcano under every player. Stream: one
        /// fireball after another, each at a random player, without a break.
        /// </summary>
        public enum AttackKind : byte { Salvo, Spread, Volcanoes, Stream }

        [System.Serializable]
        public class PhaseAttack
        {
            public AttackKind kind = AttackKind.Salvo;
            [Tooltip("Salvo: fireballs per salvo. Spread: fireballs in the fan.")]
            public int count = 5;
            [Tooltip("Salvo/Spread: seconds from the end of one attack to the next. Volcanoes: between new volcanoes. Stream: between fireballs.")]
            public float interval = 3f;
            [Tooltip("Spread: degrees between neighbouring fireballs.")]
            public float spreadAngle = 15f;
        }

        [SerializeField] string bossName = "BOSS";
        [Tooltip("In the order they open up: left arm, right arm, head.")]
        [SerializeField] BossSegment[] segments;
        [Tooltip("The whole body: it sways when a segment breaks and is hidden once the boss is beaten.")]
        [SerializeField] Transform body;
        [Tooltip("Where the fireballs come out (the head's mouth).")]
        [SerializeField] Transform mouth;

        [Header("Arena")]
        [Tooltip("Size of the arena (world axes), standing on this transform. The fight starts when a player is in it.")]
        [SerializeField] Vector3 areaSize = new Vector3(56f, 12f, 54f);
        [Tooltip("Optional: played the first time a player walks into the arena; the fight starts once it's over.")]
        [SerializeField] Cutscene introCutscene;

        [Header("Fireballs")]
        [SerializeField] float attackInterval = 3f;
        [SerializeField] int salvoSize = 5;
        [Tooltip("Seconds between the fireballs of one salvo.")]
        [SerializeField] float salvoGap = 0.3f;
        [Tooltip("The mouth glows for this long before a salvo (warning).")]
        [SerializeField] float chargeTime = 0.6f;
        [SerializeField] int fireballDamage = 1;
        [Tooltip("Each fireball flies straight at where the player was when it left the mouth (no homing).")]
        [SerializeField] float fireballSpeed = 20f;
        [SerializeField] float fireballRadius = 0.45f;
        [SerializeField] float fireballLifetime = 7f;
        [Tooltip("Height above the target's feet the fireballs go for.")]
        [SerializeField] float aimHeight = 1f;
        [SerializeField] Material fireMaterial;
        [SerializeField] Material coreMaterial;

        [Header("Attacks per phase")]
        [Tooltip("One per segment, in the segments' order: the attack while that segment is open (= how many are " +
                 "broken). Empty: every phase uses the salvo above.")]
        [SerializeField] PhaseAttack[] phaseAttacks;

        [Header("Fire volcanoes")]
        [Tooltip("A glowing ring shows where a volcano will erupt, this long before.")]
        [SerializeField] float volcanoWarning = 0.9f;
        [Tooltip("How long it spits fire straight up.")]
        [SerializeField] float volcanoDuration = 2f;
        [SerializeField] float volcanoRadius = 1.6f;
        [SerializeField] float volcanoHeight = 7f;
        [SerializeField] int volcanoDamage = 5;

        [Header("Segment broken")]
        [Tooltip("Seconds the boss sways (can't be hurt, doesn't attack) after a segment breaks.")]
        [SerializeField] float staggerTime = 5f;
        [SerializeField] float swayAngle = 6f;
        [SerializeField] float swaySpeed = 9f;
        [Tooltip("Tint of the segments that can't be hurt right now.")]
        [SerializeField] Color lockedTint = new Color(0.15f, 0.15f, 0.18f);
        [SerializeField, Range(0f, 1f)] float lockedTintAmount = 0.55f;

        [Header("Last segment: enemies join in")]
        [Tooltip("While only the last segment (the head) is left, ordinary enemies keep coming (no count): these kinds, at random.")]
        [SerializeField] GameObject[] addPrefabs;
        [SerializeField] Transform[] addSpawnPoints;
        [SerializeField] int maxAdds = 3;
        [Tooltip("Seconds between new enemies.")]
        [SerializeField] float addInterval = 4f;
        [Tooltip("A light column shows where one is coming, this long before it appears.")]
        [SerializeField] float addWarning = 0.8f;
        [Tooltip("New enemies never appear closer than this to a player.")]
        [SerializeField] float addMinPlayerDistance = 7f;
        [SerializeField] Material addFlashMaterial;
        [SerializeField] string addsMessage = "Boss wzywa posiłki! Zniszczcie głowę, żeby to zatrzymać!";

        [Header("After the fight")]
        [Tooltip("Optional: played for everyone right after the boss is beaten. It shows the boss (its own stand-in), so the " +
                 "real body is hidden once it starts (its On Started / On Finished call HideBody). Empty: the body blows up.")]
        [SerializeField] Cutscene defeatCutscene;
        [Tooltip("Enemies sent in after the boss is beaten (ServerSpawnArmy, e.g. from the cutscene's server event): one per point, these kinds in turn.")]
        [SerializeField] GameObject[] armyPrefabs;
        [SerializeField] Transform[] armySpawnPoints;

        [Header("End")]
        [Tooltip("Every machine, when the boss is beaten (also for a player who joins later): open the way on.")]
        public UnityEvent onDefeated = new();

        readonly NetworkVariable<Phase> phase = new(Phase.Waiting);
        readonly NetworkVariable<byte> active = new(0);
        readonly NetworkVariable<double> staggerStart = new(-1.0);
        readonly NetworkVariable<double> chargeStart = new(-1.0);

        // Server
        float attackTimer;
        int salvoLeft;
        float salvoTimer;
        ulong salvoTarget;
        int nextFireballId;
        // Volcanoes erupting or about to (server): where, when the fire starts, who it already burned.
        readonly List<(Vector3 position, double erupt, HashSet<ulong> burned)> volcanoes = new();
        readonly List<Damageable> adds = new();
        readonly List<(Vector3 position, float at)> pendingAdds = new();
        float nextAddAt;
        bool addsStarted;

        // Every machine
        readonly Dictionary<int, BossFireball> inFlight = new();
        readonly List<BossVolcano> volcanoViews = new();
        Quaternion bodyRest;
        bool defeatShown;
        Transform mouthGlow;
        GUIStyle nameStyle, smallStyle;
        // Each segment's share and name, kept for the bar after it breaks off (its object is gone then).
        float addsShownAt = -1f;
        int[] segmentMax;
        string[] segmentNames;

        public Phase CurrentPhase => phase.Value;
        public int ActiveSegment => active.Value;
        public IReadOnlyList<BossSegment> Segments => segments;
        public Material FireMaterial => fireMaterial;

        /// <summary>Part of the boss itself (fireballs fly out through it).</summary>
        public bool IsBossCollider(Collider c) => c != null && body != null && c.transform.IsChildOf(body);

        double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : 0.0;

        void Awake()
        {
            if (body != null) bodyRest = body.localRotation;
            segmentMax = new int[segments.Length];
            segmentNames = new string[segments.Length];
            for (int i = 0; i < segments.Length; i++)
            {
                segmentMax[i] = segments[i] != null ? segments[i].MaxHealth : 0;
                segmentNames[i] = segments[i] != null ? segments[i].DisplayName : "";
            }
            for (int i = 0; i < segments.Length; i++)
                if (segments[i] != null) segments[i].Bind(this, i);
        }

        public override void OnNetworkSpawn()
        {
            phase.OnValueChanged += (_, current) =>
            {
                if (current == Phase.Defeated) ShowDefeat();
                if (current != Phase.Fighting) { foreach (var v in volcanoViews) if (v != null) Destroy(v.gameObject); volcanoViews.Clear(); }
            };
            if (phase.Value == Phase.Defeated) ShowDefeat();
            if (mouth != null) mouthGlow = CreateMouthGlow();
        }

        public override void OnNetworkDespawn()
        {
            foreach (var ball in inFlight.Values)
                if (ball != null) Destroy(ball.gameObject);
            inFlight.Clear();
        }

        /// <summary>Whether a point (feet) is in the arena.</summary>
        public bool Contains(Vector3 point)
        {
            Vector3 local = point - transform.position - Vector3.up * (areaSize.y * 0.5f);
            return Mathf.Abs(local.x) <= areaSize.x * 0.5f && Mathf.Abs(local.y) <= areaSize.y * 0.5f && Mathf.Abs(local.z) <= areaSize.z * 0.5f;
        }

        /// <summary>Server: whether segment <paramref name="index"/> takes hits right now.</summary>
        public bool ServerCanHurt(int index) => phase.Value == Phase.Fighting && index == active.Value;

        /// <summary>Server: segment <paramref name="index"/> just broke.</summary>
        public void ServerSegmentDown(int index)
        {
            if (!IsServer || phase.Value == Phase.Defeated) return;
            salvoLeft = 0;
            chargeStart.Value = -1.0;
            volcanoes.Clear();   // their fire goes out on every machine with the phase change
            if (index >= segments.Length - 1)
            {
                phase.Value = Phase.Defeated;
                ServerClearAdds();
                // The cutscene after the fight; if it can't play, the army comes straight away.
                bool playing = defeatCutscene != null && CutsceneSystem.Instance != null && CutsceneSystem.Instance.ServerPlay(defeatCutscene);
                if (!playing) ServerSpawnArmy();
                return;
            }
            staggerStart.Value = Now;
            phase.Value = Phase.Staggering;
        }

        // ---------- Server ----------

        void Update()
        {
            if (!IsSpawned) return;
            if (IsServer) ServerTick(Time.deltaTime);
            UpdateVisuals();
        }

        void ServerTick(float dt)
        {
            switch (phase.Value)
            {
                case Phase.Waiting:
                    // Not while a cutscene runs (e.g. the one that throws the players into the arena).
                    if (CutsceneSystem.Instance != null && CutsceneSystem.Instance.IsBusy) break;
                    foreach (var player in PlayerHealth.All)
                        if (player != null && player.IsSpawned && player.IsAlive && Contains(player.transform.position))
                        {
                            // The boss is shown first; the fight starts on the frame after that cutscene is over.
                            if (introCutscene != null && CutsceneSystem.Instance != null && !CutsceneSystem.Instance.HasFinished(introCutscene)
                                && CutsceneSystem.Instance.ServerPlay(introCutscene)) break;
                            phase.Value = Phase.Fighting;
                            attackTimer = CurrentInterval - 1.5f; // the first attack comes quickly
                            break;
                        }
                    break;

                case Phase.Staggering:
                    if (Now - staggerStart.Value >= staggerTime)
                    {
                        active.Value = (byte)Mathf.Min(active.Value + 1, segments.Length - 1);
                        phase.Value = Phase.Fighting;
                        attackTimer = CurrentInterval - 1f;
                    }
                    break;

                case Phase.Fighting:
                    TickAttack(dt);
                    TickVolcanoes();
                    if (active.Value >= segments.Length - 1) TickAdds();
                    break;
            }
        }

        // The attack of the open segment; without a phase list, the salvo set above.
        PhaseAttack CurrentAttack => phaseAttacks != null && active.Value < phaseAttacks.Length ? phaseAttacks[active.Value] : null;
        AttackKind CurrentKind => CurrentAttack != null ? CurrentAttack.kind : AttackKind.Salvo;
        float CurrentInterval => CurrentAttack != null ? Mathf.Max(0.05f, CurrentAttack.interval) : attackInterval;
        int CurrentCount => CurrentAttack != null ? Mathf.Max(1, CurrentAttack.count) : salvoSize;

        void TickAttack(float dt)
        {
            var kind = CurrentKind;
            float interval = CurrentInterval;

            if (kind == AttackKind.Stream)
            {
                // Fire without a break, the mouth glowing all the time.
                if (chargeStart.Value < 0.0) chargeStart.Value = Now - chargeTime;
                attackTimer += dt;
                if (attackTimer < interval) return;
                attackTimer = 0f;
                var target = RandomTarget();
                if (target != null) FireAt(target, 0f);
                return;
            }

            if (kind == AttackKind.Volcanoes)
            {
                attackTimer += dt;
                if (attackTimer < interval) return;
                attackTimer = 0f;
                foreach (var player in PlayerHealth.All)
                    if (player != null && player.IsSpawned && player.IsAlive && Contains(player.transform.position))
                        ServerVolcano(player.transform.position);
                return;
            }

            if (salvoLeft > 0)
            {
                salvoTimer -= dt;
                if (salvoTimer > 0f) return;
                var target = Player(salvoTarget);
                if (target == null || !target.IsAlive) { salvoLeft = 0; chargeStart.Value = -1.0; return; }
                FireAt(target, 0f);
                salvoLeft--;
                salvoTimer = salvoGap;
                if (salvoLeft == 0) chargeStart.Value = -1.0;
                return;
            }

            attackTimer += dt;
            if (attackTimer < interval - chargeTime) return;
            if (chargeStart.Value < 0.0)
            {
                // Pick who gets it now, so the warning glow comes before the shots.
                var picked = RandomTarget();
                if (picked == null) { attackTimer = interval - chargeTime; return; }
                salvoTarget = picked.OwnerClientId;
                chargeStart.Value = Now;
            }
            if (attackTimer < interval) return;
            attackTimer = 0f;
            if (kind == AttackKind.Spread)
            {
                // A fan: the middle one straight at the player, the others turned by spreadAngle steps either side.
                var target = Player(salvoTarget);
                int count = CurrentCount;
                if (target != null && target.IsAlive)
                    for (int i = 0; i < count; i++) FireAt(target, (i - (count - 1) * 0.5f) * CurrentAttack.spreadAngle);
                chargeStart.Value = -1.0;
                return;
            }
            salvoLeft = CurrentCount;
            salvoTimer = 0f;
        }

        PlayerHealth RandomTarget()
        {
            var targets = new List<PlayerHealth>();
            foreach (var player in PlayerHealth.All)
                if (player != null && player.IsSpawned && player.IsAlive && Contains(player.transform.position)) targets.Add(player);
            return targets.Count > 0 ? targets[Random.Range(0, targets.Count)] : null;
        }

        // One fireball from the mouth at where the player is now, turned sideways by yaw degrees.
        void FireAt(PlayerHealth target, float yaw)
        {
            Vector3 start = MouthPosition;
            Vector3 direction = (target.transform.position + Vector3.up * aimHeight - start).normalized;
            if (yaw != 0f) direction = Quaternion.AngleAxis(yaw, Vector3.up) * direction;
            FireRpc(++nextFireballId, start, direction, target.OwnerClientId);
        }

        // ---------- Fire volcanoes ----------

        void ServerVolcano(Vector3 feet)
        {
            // On the ground under the player (they may be in the air).
            Vector3 ground = feet;
            if (Physics.Raycast(feet + Vector3.up * 0.5f, Vector3.down, out var hit, 30f, ~0, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<PlayerHealth>() == null) ground = hit.point;
            volcanoes.Add((ground, Now + volcanoWarning, new HashSet<ulong>()));
            VolcanoRpc(ground, volcanoWarning, volcanoDuration, volcanoRadius, volcanoHeight);
        }

        void TickVolcanoes()
        {
            double now = Now;
            for (int i = volcanoes.Count - 1; i >= 0; i--)
            {
                var v = volcanoes[i];
                if (now >= v.erupt + volcanoDuration) { volcanoes.RemoveAt(i); continue; }
                if (now < v.erupt) continue;
                foreach (var player in PlayerHealth.All)
                {
                    if (player == null || !player.IsSpawned || !player.IsAlive || v.burned.Contains(player.OwnerClientId)) continue;
                    Vector3 d = player.transform.position - v.position;
                    if (d.y < -0.5f || d.y > volcanoHeight) continue;
                    d.y = 0f;
                    if (d.magnitude > volcanoRadius + 0.4f) continue;
                    // Once per volcano: a hit, then it's up to the player to get out.
                    v.burned.Add(player.OwnerClientId);
                    player.ApplyDamage(new DamageInfo(volcanoDamage, d.sqrMagnitude > 0.01f ? d.normalized : Vector3.forward, gameObject));
                }
            }
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void VolcanoRpc(Vector3 position, float warning, float duration, float radius, float height)
        {
            volcanoViews.RemoveAll(v => v == null);
            volcanoViews.Add(BossVolcano.Spawn(position, warning, duration, radius, height, fireMaterial, coreMaterial));
        }

        // ---------- Enemies joining in for the last segment (server) ----------

        void TickAdds()
        {
            if (addPrefabs == null || addPrefabs.Length == 0 || addSpawnPoints == null || addSpawnPoints.Length == 0) return;
            if (!addsStarted)
            {
                addsStarted = true;
                nextAddAt = Time.time + 1f;
            }

            for (int i = pendingAdds.Count - 1; i >= 0; i--)
            {
                if (Time.time < pendingAdds[i].at) continue;
                var position = pendingAdds[i].position;
                pendingAdds.RemoveAt(i);
                SpawnAdd(position);
            }

            adds.RemoveAll(d => d == null || !d.IsSpawned || d.IsDead);
            if (Time.time < nextAddAt || adds.Count + pendingAdds.Count >= maxAdds) return;
            if (!TryPickAddPoint(out Vector3 point)) return;
            pendingAdds.Add((point, Time.time + addWarning));
            AddFlashRpc(point, addWarning);
            nextAddAt = Time.time + addInterval;
        }

        bool TryPickAddPoint(out Vector3 point)
        {
            point = default;
            var good = new List<Vector3>();
            Vector3 farthest = default;
            float farthestDistance = -1f;
            foreach (var spawn in addSpawnPoints)
            {
                if (spawn == null) continue;
                float nearest = float.MaxValue;
                foreach (var player in PlayerHealth.All)
                    if (player != null && player.IsSpawned) nearest = Mathf.Min(nearest, Vector3.Distance(player.transform.position, spawn.position));
                bool taken = false;
                foreach (var add in adds) if (add != null && (add.transform.position - spawn.position).sqrMagnitude < 2.25f) taken = true;
                foreach (var p in pendingAdds) if ((p.position - spawn.position).sqrMagnitude < 4f) taken = true;
                if (taken) continue;
                if (nearest >= addMinPlayerDistance) good.Add(spawn.position);
                if (nearest > farthestDistance) { farthestDistance = nearest; farthest = spawn.position; }
            }
            if (good.Count > 0) point = good[Random.Range(0, good.Count)];
            else if (farthestDistance >= 0f) point = farthest;
            else return false;
            return true;
        }

        void SpawnAdd(Vector3 position)
        {
            var prefab = addPrefabs[Random.Range(0, addPrefabs.Length)];
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
            if (!instance.TryGetComponent(out NetworkObject networkObject))
            {
                Destroy(instance);
                return;
            }
            networkObject.Spawn(destroyWithScene: true);
            if (instance.TryGetComponent(out EnemyController enemy)) enemy.AggroRange = Mathf.Max(areaSize.x, areaSize.z) * 1.5f;
            if (instance.TryGetComponent(out Damageable damageable)) adds.Add(damageable);
        }

        /// <summary>Server: the boss is beaten — every enemy still in the arena goes with it.</summary>
        void ServerClearAdds()
        {
            pendingAdds.Clear();
            foreach (var enemy in new List<EnemyController>(EnemyController.All))
                if (enemy != null && enemy.IsAlive && Contains(enemy.transform.position))
                    enemy.Damageable.ServerKill(Vector3.up);
            adds.Clear();
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void AddFlashRpc(Vector3 position, float warning) => BossSpawnFlash.Spawn(position, warning, addFlashMaterial);

        static PlayerHealth Player(ulong clientId)
        {
            foreach (var player in PlayerHealth.All)
                if (player != null && player.OwnerClientId == clientId) return player;
            return null;
        }

        Vector3 MouthPosition => mouth != null ? mouth.position : (body != null ? body.position : transform.position);

        // ---------- Fireballs (every machine) ----------

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void FireRpc(int id, Vector3 start, Vector3 direction, ulong targetClient)
        {
            inFlight[id] = BossFireball.Spawn(this, id, start, direction, fireballSpeed, fireballRadius, fireballLifetime, fireMaterial, coreMaterial);
        }

        /// <summary>Server: a fireball touched <paramref name="victim"/>. Returns whether it hurt it.</summary>
        public bool ServerFireballHit(IEnemyTarget victim, Vector3 direction) =>
            victim.IsAlive && victim.ApplyDamage(new DamageInfo(fireballDamage, Vector3.ProjectOnPlane(direction, Vector3.up), gameObject));

        /// <summary>This machine's copy of a fireball ended; the server tells everyone else.</summary>
        public void OnFireballEnd(int id, Vector3 point, bool burst)
        {
            inFlight.Remove(id);
            if (burst) Burst(point);
            if (IsServer) EndRpc(id, point, burst);
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        void EndRpc(int id, Vector3 point, bool burst)
        {
            if (!inFlight.TryGetValue(id, out var ball)) return;
            inFlight.Remove(id);
            if (ball != null) Destroy(ball.gameObject);
            if (burst) Burst(point);
        }

        void Burst(Vector3 point)
        {
            if (fireMaterial != null && coreMaterial != null) ExplosionEffect.Spawn(point, 0.9f, fireMaterial, coreMaterial, 0f);
            else if (fireMaterial != null) ShotEffects.Sparks(point, fireMaterial, 10);
        }

        // ---------- Body, tints, mouth glow (every machine) ----------

        void UpdateVisuals()
        {
            var p = phase.Value;
            if (body != null && !defeatShown)
            {
                Quaternion sway = Quaternion.identity;
                if (p == Phase.Staggering)
                {
                    float t = (float)(Now - staggerStart.Value);
                    float fade = Mathf.Clamp01(t / 0.3f) * Mathf.Clamp01((staggerTime - t) / 0.6f);
                    sway = Quaternion.Euler(Mathf.Sin(t * swaySpeed * 0.7f) * swayAngle * 0.5f * fade, 0f, Mathf.Sin(t * swaySpeed) * swayAngle * fade);
                }
                body.localRotation = bodyRest * sway;
            }

            for (int i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                if (segment == null || segment.Health == null) continue;
                bool open = p == Phase.Fighting && i == active.Value;
                segment.SetOpen(open, lockedTint, lockedTintAmount, p == Phase.Waiting || p == Phase.Fighting || p == Phase.Staggering);
            }

            if (mouthGlow != null)
            {
                float size = 0f;
                if (p == Phase.Fighting && chargeStart.Value >= 0.0)
                {
                    float t = Mathf.Clamp01((float)(Now - chargeStart.Value) / Mathf.Max(0.05f, chargeTime));
                    size = 1.1f * t * (1f + 0.15f * Mathf.Sin(Time.time * 40f));
                }
                mouthGlow.localScale = Vector3.one * size;
            }
        }

        Transform CreateMouthGlow()
        {
            var glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            glow.name = "MouthGlow";
            Destroy(glow.GetComponent<Collider>());
            if (fireMaterial != null) glow.GetComponent<Renderer>().sharedMaterial = fireMaterial;
            glow.transform.SetParent(mouth, false);
            glow.transform.localScale = Vector3.zero;
            return glow.transform;
        }

        void ShowDefeat()
        {
            if (defeatShown) return;
            defeatShown = true;
            foreach (var ball in inFlight.Values)
                if (ball != null) Destroy(ball.gameObject);
            inFlight.Clear();
            // With a cutscene the boss isn't blown up: the cutscene shows it fainted (and hides this body when it starts).
            if (body != null && defeatCutscene == null)
            {
                if (fireMaterial != null && coreMaterial != null)
                    ExplosionEffect.Spawn(body.position + Vector3.up * 2f, 5f, fireMaterial, coreMaterial, 0.6f);
                body.gameObject.SetActive(false);
            }
            onDefeated.Invoke();
        }

        /// <summary>Every machine: hide the real boss body (the defeat cutscene's stand-in takes its place).</summary>
        public void HideBody()
        {
            if (body != null) body.gameObject.SetActive(false);
        }

        bool armySpawned;

        /// <summary>
        /// Server: the army the boss's master sends in after the fight, one enemy per army point (the kinds in turn).
        /// Once only. Callable on any machine (from an event); only the server acts.
        /// </summary>
        public void ServerSpawnArmy()
        {
            if (!IsServer || armySpawned || armyPrefabs == null || armyPrefabs.Length == 0 || armySpawnPoints == null) return;
            armySpawned = true;
            for (int i = 0; i < armySpawnPoints.Length; i++)
            {
                var point = armySpawnPoints[i];
                var prefab = armyPrefabs[i % armyPrefabs.Length];
                if (point == null || prefab == null) continue;
                var instance = Instantiate(prefab, point.position, point.rotation);
                if (!instance.TryGetComponent(out NetworkObject networkObject)) { Destroy(instance); continue; }
                networkObject.Spawn(destroyWithScene: true);
                if (instance.TryGetComponent(out EnemyController enemy)) enemy.AggroRange = Mathf.Max(areaSize.x, areaSize.z) * 1.5f;
            }
        }

        // ---------- Health bar (every machine) ----------

        void OnGUI()
        {
            if (!IsSpawned || CutsceneSystem.HidesGameplayUI) return;
            var p = phase.Value;
            if (p == Phase.Waiting || p == Phase.Defeated) return;
            var me = PlayerHealth.Local;
            if (me == null || !NearArena(me.transform.position)) return;

            nameStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            smallStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            nameStyle.fontSize = Mathf.Max(16, Screen.height / 36);
            smallStyle.fontSize = Mathf.Max(12, Screen.height / 60);

            float width = Mathf.Min(Screen.width * 0.6f, 900f), height = Mathf.Max(14f, Screen.height * 0.022f);
            float x = (Screen.width - width) * 0.5f, y = Screen.height * 0.075f;

            int total = 0, current = 0;
            for (int i = 0; i < segments.Length; i++) { total += segmentMax[i]; current += Current(i); }
            if (total <= 0) return;

            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(x - 4f, y - nameStyle.fontSize * 1.5f - 4f, width + 8f, height + nameStyle.fontSize * 1.5f + 8f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            string state = p == Phase.Staggering ? "  —  segment zniszczony!" : "";
            // The last segment is open: say once (for a few seconds) that enemies are coming too.
            bool lastOpen = p == Phase.Fighting && active.Value >= segments.Length - 1 && addPrefabs != null && addPrefabs.Length > 0;
            if (lastOpen && addsShownAt < 0f) addsShownAt = Time.time;
            if (lastOpen && Time.time - addsShownAt < 4f && !string.IsNullOrEmpty(addsMessage))
            {
                var warn = new GUIStyle(nameStyle) { fontSize = Mathf.Max(14, Screen.height / 48) };
                GUI.color = new Color(1f, 0.75f, 0.3f);
                GUI.Label(new Rect(x, y + height + smallStyle.fontSize * 1.8f, width, warn.fontSize * 1.6f), addsMessage, warn);
                GUI.color = Color.white;
            }
            GUI.Label(new Rect(x, y - nameStyle.fontSize * 1.5f, width, nameStyle.fontSize * 1.4f), bossName + "   " + current + " / " + total + state, nameStyle);

            float cursor = x;
            for (int i = 0; i < segments.Length; i++)
            {
                int max = segmentMax[i], now = Current(i);
                if (max <= 0) continue;
                float w = width * max / total;
                var cell = new Rect(cursor + 1f, y, w - 2f, height);
                GUI.color = new Color(0.12f, 0.06f, 0.06f, 0.95f);
                GUI.DrawTexture(cell, Texture2D.whiteTexture);
                float fill = (float)now / max;
                bool open = p == Phase.Fighting && i == active.Value;
                bool waitingTurn = i > active.Value || (i == active.Value && p != Phase.Fighting && now > 0);
                Color c = open ? new Color(0.9f, 0.15f, 0.12f) : waitingTurn ? new Color(0.45f, 0.18f, 0.16f) : new Color(0.3f, 0.3f, 0.3f);
                if (open && Mathf.Repeat(Time.time * 2f, 1f) < 0.15f) c = Color.Lerp(c, Color.white, 0.25f);
                GUI.color = c;
                GUI.DrawTexture(new Rect(cell.x, cell.y, cell.width * fill, cell.height), Texture2D.whiteTexture);
                GUI.color = new Color(1f, 1f, 1f, 0.85f);
                GUI.Label(new Rect(cell.x, cell.yMax + 1f, cell.width, smallStyle.fontSize * 1.5f), segmentNames[i] + (now <= 0 ? " ✕" : ""), smallStyle);
                cursor += w;
            }
            GUI.color = Color.white;
        }

        int Current(int i) => segments[i] != null ? segments[i].CurrentHealth : 0;

        bool NearArena(Vector3 point)
        {
            Vector3 local = point - transform.position - Vector3.up * (areaSize.y * 0.5f);
            return Mathf.Abs(local.x) <= areaSize.x * 0.5f + 15f && Mathf.Abs(local.y) <= areaSize.y * 0.5f + 10f && Mathf.Abs(local.z) <= areaSize.z * 0.5f + 15f;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.8f);
            Gizmos.DrawWireCube(transform.position + Vector3.up * (areaSize.y * 0.5f), areaSize);
        }

        /// <summary>Editor setup.</summary>
        public void Setup(BossSegment[] parts, Transform bossBody, Transform bossMouth, Vector3 area, Material fire, Material core)
        {
            segments = parts;
            body = bossBody;
            mouth = bossMouth;
            areaSize = area;
            fireMaterial = fire;
            coreMaterial = core;
        }
    }

    /// <summary>A column of light that grows where an enemy is about to appear, then collapses as it does.</summary>
    public class BossSpawnFlash : MonoBehaviour
    {
        float start, length;
        Vector3 ground;

        public static void Spawn(Vector3 position, float warning, Material material)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "BossSpawnFlash";
            Destroy(body.GetComponent<Collider>());
            var renderer = body.GetComponent<Renderer>();
            if (material != null) renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            body.transform.position = position;
            body.transform.localScale = Vector3.zero;
            var flash = body.AddComponent<BossSpawnFlash>();
            flash.start = Time.time;
            flash.length = warning;
            flash.ground = position;
        }

        void Update()
        {
            float age = Time.time - start;
            const float collapse = 0.25f;
            if (age > length + collapse) { Destroy(gameObject); return; }
            float grow = length > 0f ? Mathf.Clamp01(age / length) : 1f;
            float shrink = 1f - Mathf.Clamp01((age - length) / collapse);
            float radius = Mathf.Lerp(0.3f, 1.1f, grow) * shrink * (1f + 0.12f * Mathf.Sin(age * 30f));
            float height = Mathf.Lerp(0.5f, 1.6f, grow);
            transform.localScale = new Vector3(radius, height, radius);
            transform.position = ground + Vector3.up * height; // a unit cylinder is 2 tall around its centre
        }
    }

    /// <summary>
    /// One machine's copy of a boss fireball: flies straight on (at where its target was when it was fired) until it hits
    /// a player, a wall or burns out. Only the server's copy hurts anyone.
    /// </summary>
    public class BossFireball : MonoBehaviour
    {
        BossFight owner;
        int id;
        Vector3 direction;
        float speed, radius, lifetime, age;
        Material material;
        float nextPuffTime;
        readonly HashSet<IEnemyTarget> passed = new();

        public static BossFireball Spawn(BossFight owner, int id, Vector3 start, Vector3 direction, float speed,
            float radius, float lifetime, Material fire, Material core)
        {
            var root = new GameObject("BossFireball");
            root.transform.SetPositionAndRotation(start, Quaternion.LookRotation(direction));
            var ball = root.AddComponent<BossFireball>();
            ball.owner = owner;
            ball.id = id;
            ball.direction = direction.normalized;
            ball.speed = speed;
            ball.radius = radius;
            ball.lifetime = lifetime;
            ball.material = fire;

            var shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(shell.GetComponent<Collider>());
            if (fire != null) shell.GetComponent<Renderer>().sharedMaterial = fire;
            shell.transform.SetParent(root.transform, false);
            shell.transform.localScale = new Vector3(radius * 2f, radius * 2f, radius * 2.6f);
            var inner = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(inner.GetComponent<Collider>());
            if (core != null) inner.GetComponent<Renderer>().sharedMaterial = core;
            inner.transform.SetParent(root.transform, false);
            inner.transform.localScale = Vector3.one * radius * 1.2f;
            return ball;
        }

        void Update()
        {
            if (owner == null)
            {
                Destroy(gameObject);
                return;
            }
            float dt = Time.deltaTime;
            age += dt;

            float step = speed * dt;
            Vector3 position = transform.position;
            bool server = owner.IsServer;
            RaycastHit stop = default;
            float nearest = float.MaxValue;
            foreach (var hit in Physics.SphereCastAll(position, radius, direction, step, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.distance >= nearest) continue;
                if (hit.collider.GetComponentInParent<BossSegment>() != null || hit.collider.GetComponentInParent<EnemyController>() != null) continue;
                if (owner.IsBossCollider(hit.collider)) continue;
                var victim = hit.collider.GetComponentInParent<IEnemyTarget>();
                if (victim != null)
                {
                    if (!server || passed.Contains(victim)) continue;
                    if (!owner.ServerFireballHit(victim, direction))
                    {
                        passed.Add(victim);
                        continue;
                    }
                }
                nearest = hit.distance;
                stop = hit;
            }

            if (nearest < float.MaxValue)
            {
                Vector3 point = stop.distance > 0f || stop.point != Vector3.zero ? stop.point : position;
                End(point, true);
                return;
            }

            transform.SetPositionAndRotation(position + direction * step, Quaternion.LookRotation(direction));
            if (age >= lifetime)
            {
                End(transform.position, true);
                return;
            }
            if (Time.time >= nextPuffTime)
            {
                nextPuffTime = Time.time + 0.04f;
                SmokePuff.Spawn(position, material);
            }
        }

        void End(Vector3 point, bool burst)
        {
            owner.OnFireballEnd(id, point, burst);
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// One machine's view of a fire volcano: a glowing ring on the ground as a warning, then a column of fire spitting
    /// straight up for a while. Only a picture; the server does the burning.
    /// </summary>
    public class BossVolcano : MonoBehaviour
    {
        float start, warning, duration, radius, height;
        Transform ring, column, core;
        Material fire;
        float nextPuff;
        bool erupted;

        public static BossVolcano Spawn(Vector3 ground, float warning, float duration, float radius, float height, Material fire, Material coreMaterial)
        {
            var root = new GameObject("BossVolcano");
            root.transform.position = ground;
            var v = root.AddComponent<BossVolcano>();
            v.start = Time.time; v.warning = warning; v.duration = duration; v.radius = radius; v.height = height; v.fire = fire;
            v.ring = Part(root.transform, fire, "Ring");
            v.column = Part(root.transform, fire, "Column");
            v.core = Part(root.transform, coreMaterial != null ? coreMaterial : fire, "Core");
            v.column.localScale = v.core.localScale = Vector3.zero;
            return v;
        }

        static Transform Part(Transform parent, Material material, string name)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            g.name = name;
            Destroy(g.GetComponent<Collider>());
            var r = g.GetComponent<Renderer>();
            if (material != null) r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            g.transform.SetParent(parent, false);
            return g.transform;
        }

        void Update()
        {
            float age = Time.time - start;
            if (age > warning + duration) { Destroy(gameObject); return; }
            if (age < warning)
            {
                // Warning: a flat ring that grows to the full size and flickers faster as the eruption nears.
                float t = age / Mathf.Max(0.01f, warning);
                float d = radius * 2f * Mathf.Lerp(0.4f, 1f, t) * (1f + 0.08f * Mathf.Sin(age * Mathf.Lerp(10f, 40f, t)));
                ring.localScale = new Vector3(d, 0.02f, d);
                ring.localPosition = Vector3.up * 0.03f;
                return;
            }
            if (!erupted)
            {
                erupted = true;
                ShotEffects.Sparks(transform.position + Vector3.up * 0.3f, fire, 14);
            }
            // Eruption: a column that shoots up, roars (its width flickers) and sinks back at the end.
            float e = age - warning;
            float rise = Mathf.Clamp01(e / 0.2f) * Mathf.Clamp01((duration - e) / 0.3f);
            float h = height * rise;
            float w = radius * 1.4f * (1f + 0.12f * Mathf.Sin(e * 37f)) * Mathf.Max(0.2f, rise);
            column.localScale = new Vector3(w, h * 0.5f, w);
            column.localPosition = Vector3.up * (h * 0.5f);
            core.localScale = new Vector3(w * 0.5f, h * 0.5f, w * 0.5f);
            core.localPosition = Vector3.up * (h * 0.5f);
            ring.localScale = new Vector3(radius * 2.2f, 0.02f, radius * 2.2f);
            if (Time.time >= nextPuff && rise > 0.5f)
            {
                nextPuff = Time.time + 0.05f;
                SmokePuff.Spawn(transform.position + Vector3.up * Random.Range(0.5f, h) + Random.insideUnitSphere * radius * 0.4f, fire);
            }
        }
    }
}
