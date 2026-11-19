using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace CoopGame
{
    /// <summary>
    /// The protagonist's brother: a toxic boss (500 HP) that shows up on one of four pillars, stays 5 s, vanishes for
    /// 5 s and comes back on another. Between phases (at 400 / 300 / 200 / 100 HP) it can't be hurt and doesn't
    /// attack. Phases: 1 toxic globs from both hands (one per player) that leave puddles for good; 2 also poison gas
    /// round three pillars (one safe); 3 two toxic lasers instead of globs; 4 the side walls close in, clones on every
    /// pillar (one is real), tentacles from pillars and walls slam players near them; 5 two pillars crumble, a toxic
    /// barrier splits the arena: tentacles erupt under one player while the other breaks the spinning organic core
    /// (jumping its rays); then the boss can be finished off. The server runs the fight; every machine draws it.
    /// </summary>
    public class ToxicBoss : NetworkBehaviour
    {
        public enum Stage : byte { Waiting, Phase1, Phase2, Phase3, Phase4, Phase5, Defeated }

        [Header("Arena")]
        [SerializeField] string bossName = "BRAT PROTAGONISTY";
        [Tooltip("Size of the arena (world axes); it stands on this object's position (the middle of the floor).")]
        [SerializeField] Vector3 areaSize = new Vector3(51f, 18f, 49f);
        [Tooltip("Checkpoint once the fight has started (player 1, player 2).")]
        [SerializeField] Transform[] respawnPoints;

        [Header("Boss")]
        [SerializeField] Damageable body;
        [SerializeField] Transform[] pillars;
        [Tooltip("Pillars that crumble when the last phase starts.")]
        [SerializeField] int[] breakingPillars = { 1, 3 };
        [SerializeField] float showTime = 5f;
        [SerializeField] float hideTime = 5f;
        [SerializeField] float transitionTime = 4f;
        [Tooltip("Health at which phases 2, 3, 4 and 5 begin.")]
        [SerializeField] int[] thresholds = { 400, 300, 200, 100 };

        [Header("Phase 1: toxic globs (they stay as puddles)")]
        [SerializeField] float globInterval = 2.4f;
        [SerializeField] float globFlight = 1.1f;
        [SerializeField] int globDamage = 2;
        [SerializeField] float globRadius = 1.8f;
        [SerializeField] float puddleRadius = 1.6f;
        [SerializeField] int maxPuddles = 40;

        [Header("Phase 2: poison gas")]
        [SerializeField] float gasEvery = 9f;
        [SerializeField] float gasWarning = 1.5f;
        [SerializeField] float gasTime = 4f;

        [Header("Phase 3: toxic lasers")]
        [SerializeField] float laserTrackSpeed = 5f;
        [SerializeField] float laserRadius = 0.55f;

        [Header("Phase 4: walls, clones, tentacles")]
        [SerializeField] Transform[] walls;
        [SerializeField] float wallShift = 7f;
        [SerializeField] float tentacleReach = 6f;
        [SerializeField] float tentacleCooldown = 2.6f;
        [SerializeField] float tentacleWindup = 0.6f;
        [SerializeField] int tentacleDamage = 2;

        [Header("Phase 5: barrier and organic core")]
        [SerializeField] Damageable core;
        [SerializeField] Transform coreRays;
        [SerializeField] GameObject barrier;
        [SerializeField] Transform dodgerSpot;
        [SerializeField] Transform coreSpot;
        [SerializeField] float rayLength = 7.5f;
        [SerializeField] float raySpeed = 65f;
        [SerializeField] float rayHeight = 0.5f;
        [SerializeField] int rayCount = 4;
        [SerializeField] int rayDamage = 2;
        [SerializeField] float spikeEvery = 1.3f;
        [SerializeField] float spikeWarning = 0.8f;
        [SerializeField] int spikeDamage = 2;

        [Header("Looks")]
        [SerializeField] Material toxicGlow;
        [SerializeField] Material gasMaterial;
        [SerializeField] Material gasWarningMaterial;
        [SerializeField] Material tentacleMaterial;

        [Tooltip("Every machine (also a player who joins later): the boss is dead.")]
        public UnityEvent onDefeated = new();

        readonly NetworkVariable<byte> stage = new();
        readonly NetworkVariable<double> transitionUntil = new(-1.0);
        readonly NetworkVariable<sbyte> pillar = new(-1);
        readonly NetworkVariable<sbyte> gasSafe = new(-1);
        readonly NetworkVariable<double> gasAt = new(-1.0);
        readonly NetworkVariable<bool> laserOn = new();
        readonly NetworkVariable<Vector3> aimA = new();
        readonly NetworkVariable<Vector3> aimB = new();
        readonly NetworkVariable<double> coreStart = new();
        readonly NetworkVariable<bool> coreDown = new();
        readonly NetworkVariable<ulong> dodger = new(ulong.MaxValue);
        readonly NetworkVariable<ulong> runner = new(ulong.MaxValue);
        NetworkList<Vector3> puddles;

        // Geometry, the same on every machine.
        Vector3[] pillarTop; Renderer[][] pillarRenderers; Collider[][] pillarColliders;
        Vector3[] wallRest; float[] wallInward; float[] wallHalf;
        Vector3[] tentacleSpots;
        float bodyHalf = 1.75f; int maxHealth = 500;

        // Server only.
        double nextSwitch, nextGas, nextSpike, nextHazard;
        readonly double[] nextGlob = new double[2];
        double[] tentacleReady;
        int lastPillar = -1;
        readonly List<(Vector3 at, double land)> globs = new();
        readonly List<(Vector3 at, double hit)> slams = new();
        readonly List<(Vector3 at, double hit)> spikes = new();

        // Visuals.
        Transform[] arms; Transform[] clones; Transform[] beams; Transform[] gasZones;
        readonly List<Transform> puddleViews = new();
        ToxicTentacle[] tentacles;
        Renderer[] bodyRenderers; Collider bodyCollider; Targetable bodyTarget;
        Renderer[] coreRenderers; Collider coreCollider; Targetable coreTarget;
        bool defeatApplied, checkpointSet;
        string message; float messageUntil;
        GUIStyle nameStyle, smallStyle, boxStyle;

        double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : Time.timeAsDouble;
        public Stage CurrentStage => (Stage)stage.Value;
        public bool InTransition => transitionUntil.Value > Now;
        public int Pillar => pillar.Value;
        public bool CoreDown => coreDown.Value;
        public int Health => body != null && body.IsSpawned ? body.Health : CurrentStage == Stage.Defeated ? 0 : maxHealth;
        Vector3 Center => transform.position;
        float FloorY => transform.position.y;

        // ---------- Setup ----------

        void Awake()
        {
            puddles = new NetworkList<Vector3>();
            int n = pillars.Length;
            pillarTop = new Vector3[n]; pillarRenderers = new Renderer[n][]; pillarColliders = new Collider[n][];
            for (int i = 0; i < n; i++)
            {
                var b = pillars[i].GetComponent<Collider>().bounds;
                pillarTop[i] = new Vector3(b.center.x, b.max.y, b.center.z);
                pillarRenderers[i] = pillars[i].GetComponentsInChildren<Renderer>(true);
                pillarColliders[i] = pillars[i].GetComponentsInChildren<Collider>(true);
            }
            wallRest = new Vector3[walls.Length]; wallInward = new float[walls.Length]; wallHalf = new float[walls.Length];
            for (int i = 0; i < walls.Length; i++)
            {
                wallRest[i] = walls[i].position;
                wallInward[i] = Mathf.Sign(Center.x - walls[i].position.x);
                wallHalf[i] = walls[i].GetComponent<Collider>().bounds.extents.x;
            }
            // Tentacles: two at the foot of each pillar (towards the middle), three along each closed-in wall.
            var spots = new List<Vector3>();
            for (int i = 0; i < n; i++)
            {
                Vector3 toMid = Center - pillarTop[i]; toMid.y = 0f;
                Vector3 dx = new Vector3(Mathf.Sign(toMid.x), 0f, 0f), dz = new Vector3(0f, 0f, Mathf.Sign(toMid.z));
                Vector3 foot = new Vector3(pillarTop[i].x, FloorY, pillarTop[i].z);
                spots.Add(foot + dx * 2.6f); spots.Add(foot + dz * 2.6f);
            }
            for (int w = 0; w < walls.Length; w++)
                for (int k = 0; k < 3; k++)
                {
                    float x = wallRest[w].x + wallInward[w] * (wallShift + wallHalf[w] + 0.4f);
                    float z = Center.z + (k - 1) * areaSize.z * 0.32f;
                    spots.Add(new Vector3(x, FloorY, z));
                }
            tentacleSpots = spots.ToArray();
            tentacleReady = new double[tentacleSpots.Length];

            if (body != null)
            {
                bodyHalf = body.transform.lossyScale.y * 0.5f; maxHealth = body.MaxHealth;
                bodyRenderers = body.GetComponentsInChildren<Renderer>(true); bodyCollider = body.GetComponent<Collider>(); bodyTarget = body.GetComponent<Targetable>();
                if (body.TryGetComponent(out ToxicBossPart part)) part.Bind(this, false);
            }
            if (core != null)
            {
                coreRenderers = core.GetComponentsInChildren<Renderer>(true); coreCollider = core.GetComponent<Collider>(); coreTarget = core.GetComponent<Targetable>();
                if (core.TryGetComponent(out ToxicBossPart part)) part.Bind(this, true);
            }
            BuildLooks();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                if (body != null) body.Died += _ => ServerDefeat();
                if (core != null) core.Died += _ => ServerCoreDown();
            }
        }

        public override void OnDestroy()
        {
            puddles?.Dispose();
            base.OnDestroy();
        }

        // ---------- Damage gates (server) ----------

        /// <summary>Server: how much of a hit on the boss gets through (none while hidden, between phases, or shielded).</summary>
        public int ServerAbsorb(int amount)
        {
            var s = CurrentStage;
            if (s < Stage.Phase1 || s == Stage.Defeated || pillar.Value < 0 || InTransition) return 0;
            if (s == Stage.Phase5 && !coreDown.Value) return 0;
            int floor = s < Stage.Phase5 ? thresholds[(int)s - 1] : 0;
            int hp = body.Health;
            int through = Mathf.Clamp(amount, 0, hp - floor);
            if (floor > 0 && hp - through <= floor) ServerStartTransition();
            return through;
        }

        public int ServerAbsorbCore(int amount) => CurrentStage == Stage.Phase5 && !coreDown.Value ? amount : 0;

        // ---------- Server ----------

        void Update()
        {
            if (!IsSpawned) return;
            if (IsServer) ServerTick(Time.deltaTime);
            UpdateLooks(Time.deltaTime);
            LocalTick();
        }

        void ServerTick(float dt)
        {
            var s = CurrentStage;
            if (s == Stage.Waiting)
            {
                if (CutsceneSystem.Instance != null && CutsceneSystem.Instance.IsBusy) return;
                if (Targets().Count > 0) ServerStart();
                return;
            }
            if (s == Stage.Defeated) return;
            if (transitionUntil.Value > 0.0 && Now >= transitionUntil.Value) ServerEndTransition();
            bool between = InTransition;
            TickCycle(between);
            if (!between) TickAttacks(dt);
            TickPending();
            if (Now >= nextHazard) { nextHazard = Now + 0.2; TickHazards(); }
        }

        void ServerStart()
        {
            stage.Value = (byte)Stage.Phase1;
            Appear(PickPillar());
            SayRpc("Brat protagonisty: Myśleliście, że mój brat jest groźny? Poznajcie mnie!");
        }

        void ServerStartTransition()
        {
            if (InTransition) return;
            transitionUntil.Value = Now + transitionTime;
            laserOn.Value = false; gasSafe.Value = -1; gasAt.Value = -1.0;
            var next = CurrentStage + 1;
            SayRpc(next switch
            {
                Stage.Phase2 => "Faza 2: trujący gaz! Gdy się pojawi, uciekajcie do bezpiecznego filaru.",
                Stage.Phase3 => "Faza 3: toksyczne lasery! Nie stójcie w miejscu.",
                Stage.Phase4 => "Faza 4: ściany się zsuwają, a na filarach są klony. Tylko jeden boss jest prawdziwy!",
                _ => "Faza 5: toksyczna bariera! Jeden ucieka przed mackami, drugi niszczy organiczny rdzeń!",
            });
        }

        void ServerEndTransition()
        {
            transitionUntil.Value = -1.0;
            var s = CurrentStage + 1;
            stage.Value = (byte)s;
            switch (s)
            {
                case Stage.Phase2: nextGas = Now + 2.0; break;
                case Stage.Phase4: for (int i = 0; i < tentacleReady.Length; i++) tentacleReady[i] = Now + 1.5 + i * 0.15; break;
                case Stage.Phase5:
                    foreach (int p in breakingPillars) CrumbleRpc(p);
                    if (pillar.Value >= 0 && IsBroken(pillar.Value)) Appear(PickPillar());
                    ServerSplitPlayers();
                    coreStart.Value = Now;
                    nextSpike = Now + 2.5;
                    break;
            }
            nextSwitch = Now + showTime;
        }

        void ServerCoreDown()
        {
            if (coreDown.Value) return;
            coreDown.Value = true;
            spikes.Clear();
            nextSwitch = Now + showTime;
            SayRpc("Rdzeń zniszczony! Bariera opada. Dobijcie go!");
        }

        void ServerDefeat()
        {
            if (CurrentStage == Stage.Defeated) return;
            stage.Value = (byte)Stage.Defeated;
            pillar.Value = -1; laserOn.Value = false; gasSafe.Value = -1; gasAt.Value = -1.0; transitionUntil.Value = -1.0;
            globs.Clear(); slams.Clear(); spikes.Clear();
            SayRpc("Brat protagonisty pokonany! Drzwi dalej się otwierają.");
        }

        bool IsBroken(int p) => CurrentStage >= Stage.Phase5 && System.Array.IndexOf(breakingPillars, p) >= 0;

        int PickPillar()
        {
            var free = new List<int>();
            for (int i = 0; i < pillars.Length; i++) if (!IsBroken(i) && i != lastPillar) free.Add(i);
            if (free.Count == 0) for (int i = 0; i < pillars.Length; i++) if (!IsBroken(i)) free.Add(i);
            return free[Random.Range(0, free.Count)];
        }

        void Appear(int p)
        {
            pillar.Value = (sbyte)p; lastPillar = p;
            nextSwitch = Now + showTime;
            nextGlob[0] = Now + 0.8; nextGlob[1] = Now + 0.8 + globInterval * 0.5;
            aimA.Value = Hand(p, 0); aimB.Value = Hand(p, 1);
            AppearRpc(p);
        }

        void TickCycle(bool between)
        {
            if (between) return;
            if (CurrentStage == Stage.Phase5 && !coreDown.Value)
            {
                if (pillar.Value < 0) Appear(PickPillar());
                return;
            }
            if (Now < nextSwitch) return;
            if (pillar.Value >= 0) { pillar.Value = -1; laserOn.Value = false; nextSwitch = Now + hideTime; }
            else Appear(PickPillar());
        }

        void TickAttacks(float dt)
        {
            var s = CurrentStage;
            bool shown = pillar.Value >= 0;
            if (shown && (s == Stage.Phase1 || s == Stage.Phase2 || (s == Stage.Phase5 && coreDown.Value)))
                for (int h = 0; h < 2; h++)
                {
                    if (Now < nextGlob[h]) continue;
                    nextGlob[h] = Now + globInterval;
                    var target = Target(h);
                    if (target != null) ServerGlob(Hand(pillar.Value, h), target.transform.position);
                }

            if (s == Stage.Phase2)
            {
                if (gasAt.Value < 0.0 && Now >= nextGas) { gasSafe.Value = (sbyte)Random.Range(0, pillars.Length); gasAt.Value = Now; nextGas = Now + gasEvery; }
                else if (gasAt.Value >= 0.0 && Now >= gasAt.Value + gasWarning + gasTime) { gasSafe.Value = -1; gasAt.Value = -1.0; }
            }

            if (s == Stage.Phase3)
            {
                laserOn.Value = shown;
                if (shown)
                {
                    var a = Target(0); var b = Target(1);
                    if (a != null) aimA.Value = Vector3.MoveTowards(aimA.Value, a.transform.position + Vector3.up, laserTrackSpeed * dt);
                    if (b != null) aimB.Value = Vector3.MoveTowards(aimB.Value, b.transform.position + Vector3.up, laserTrackSpeed * dt);
                }
            }

            if (s == Stage.Phase4)
                for (int i = 0; i < tentacleSpots.Length; i++)
                {
                    if (Now < tentacleReady[i]) continue;
                    PlayerHealth near = null; float best = tentacleReach;
                    foreach (var p in Targets())
                    {
                        Vector3 d = p.transform.position - tentacleSpots[i]; d.y = 0f;
                        if (d.magnitude < best) { best = d.magnitude; near = p; }
                    }
                    if (near == null) continue;
                    Vector3 at = Ground(near.transform.position);
                    slams.Add((at, Now + tentacleWindup));
                    tentacleReady[i] = Now + tentacleWindup + tentacleCooldown;
                    SlamRpc(i, at, tentacleWindup);
                }

            if (s == Stage.Phase5 && !coreDown.Value && Now >= nextSpike)
            {
                nextSpike = Now + spikeEvery;
                var target = Player(dodger.Value) ?? Player(runner.Value);
                if (target != null && target.IsAlive)
                {
                    Vector3 at = Ground(target.transform.position);
                    spikes.Add((at, Now + spikeWarning));
                    SpikeRpc(at, spikeWarning);
                }
            }
        }

        void ServerGlob(Vector3 from, Vector3 feet)
        {
            Vector3 at = Ground(feet);
            globs.Add((at, Now + globFlight));
            GlobRpc(from, at, globFlight);
        }

        void TickPending()
        {
            double now = Now;
            for (int i = globs.Count - 1; i >= 0; i--)
            {
                if (now < globs[i].land) continue;
                var at = globs[i].at; globs.RemoveAt(i);
                foreach (var p in Targets())
                    if (Near(p, at, globRadius, 2f)) { p.ApplyDamage(new DamageInfo(globDamage, Away(p, at), gameObject)); p.ServerPoison(1, 3, 1f); }
                if (puddles.Count >= maxPuddles) puddles.RemoveAt(0);   // shortcut: the oldest puddle dries up past maxPuddles, so the floor can't fill up for good
                puddles.Add(at);
            }
            for (int i = slams.Count - 1; i >= 0; i--)
            {
                if (now < slams[i].hit) continue;
                var at = slams[i].at; slams.RemoveAt(i);
                foreach (var p in Targets()) if (Near(p, at, 1.6f, 2f)) p.ApplyDamage(new DamageInfo(tentacleDamage, Away(p, at), gameObject));
            }
            for (int i = spikes.Count - 1; i >= 0; i--)
            {
                if (now < spikes[i].hit) continue;
                var at = spikes[i].at; spikes.RemoveAt(i);
                foreach (var p in Targets()) if (Near(p, at, 1.3f, 3.5f)) p.ApplyDamage(new DamageInfo(spikeDamage, Vector3.up, gameObject));
            }
        }

        // Things that hurt while you stand in them (checked a few times a second).
        void TickHazards()
        {
            var s = CurrentStage;
            double now = Now;
            bool gasOn = gasAt.Value >= 0.0 && now >= gasAt.Value + gasWarning;
            foreach (var p in Targets())
            {
                Vector3 pos = p.transform.position;
                if (!p.IsPoisoned)
                    for (int i = 0; i < puddles.Count; i++)
                        if (Near(p, puddles[i], puddleRadius, 1f)) { p.ServerPoison(1, 3, 0.8f); break; }
                if (gasOn && Quadrant(pos) != gasSafe.Value) p.ApplyDamage(new DamageInfo(1, Vector3.zero, gameObject));
                if (laserOn.Value && pillar.Value >= 0)
                    for (int h = 0; h < 2; h++)
                        if (SegmentDistance(pos + Vector3.up, Hand(pillar.Value, h), h == 0 ? aimA.Value : aimB.Value) < laserRadius + 0.3f)
                            p.ApplyDamage(new DamageInfo(1, Away(p, Hand(pillar.Value, h)), gameObject));
                if (s == Stage.Phase5 && !coreDown.Value)
                {
                    if (barrier != null && Mathf.Abs(pos.x - barrier.transform.position.x) < 0.9f) p.ApplyDamage(new DamageInfo(1, new Vector3(Mathf.Sign(pos.x - barrier.transform.position.x), 0f, 0f), gameObject));
                    if (core != null && core.IsSpawned && OnRay(pos)) p.ApplyDamage(new DamageInfo(rayDamage, Away(p, core.transform.position), gameObject));
                }
            }
        }

        bool OnRay(Vector3 feet)
        {
            if (feet.y - FloorY > rayHeight + 0.3f) return false;   // jumped over it
            Vector3 c = core.transform.position; c.y = 0f; feet.y = 0f;
            Vector3 v = feet - c;
            float angle = RayAngle();
            for (int k = 0; k < rayCount; k++)
            {
                Vector3 dir = Quaternion.Euler(0f, angle + k * 360f / rayCount, 0f) * Vector3.forward;
                float along = Vector3.Dot(v, dir);
                if (along < 0.6f || along > rayLength) continue;
                if ((v - dir * along).magnitude < 0.55f) return true;
            }
            return false;
        }

        float RayAngle() => (float)((Now - coreStart.Value) * raySpeed % 360.0);

        // Phase 5: the first player dodges on one side, the other goes to the core on the other side.
        void ServerSplitPlayers()
        {
            var players = Targets();
            players.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));
            if (players.Count == 0) return;
            if (players.Count == 1) { dodger.Value = players[0].OwnerClientId; runner.Value = players[0].OwnerClientId; Place(players[0], coreSpot); return; }
            dodger.Value = players[0].OwnerClientId; runner.Value = players[1].OwnerClientId;
            Place(players[0], dodgerSpot); Place(players[1], coreSpot);
        }

        void Place(PlayerHealth p, Transform spot)
        {
            if (spot == null) return;
            PlaceRpc(spot.position, spot.rotation, RpcTarget.Single(p.OwnerClientId, RpcTargetUse.Temp));
        }

        // ---------- Helpers ----------

        List<PlayerHealth> Targets()
        {
            var list = new List<PlayerHealth>();
            foreach (var p in PlayerHealth.All) if (p != null && p.IsSpawned && p.IsAlive && Contains(p.transform.position)) list.Add(p);
            return list;
        }

        PlayerHealth Target(int hand)
        {
            var list = Targets();
            if (list.Count == 0) return null;
            list.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));
            return list[hand % list.Count];
        }

        static PlayerHealth Player(ulong client)
        {
            foreach (var p in PlayerHealth.All) if (p != null && p.OwnerClientId == client) return p;
            return null;
        }

        public bool Contains(Vector3 point)
        {
            Vector3 d = point - Center;
            return Mathf.Abs(d.x) <= areaSize.x * 0.5f && d.y >= -1f && d.y <= areaSize.y && Mathf.Abs(d.z) <= areaSize.z * 0.5f;
        }

        static bool Near(PlayerHealth p, Vector3 at, float radius, float height)
        {
            Vector3 d = p.transform.position - at;
            if (d.y < -0.5f || d.y > height) return false;
            d.y = 0f;
            return d.magnitude <= radius;
        }

        static Vector3 Away(PlayerHealth p, Vector3 from) { Vector3 d = p.transform.position - from; d.y = 0f; return d.sqrMagnitude > 0.01f ? d.normalized : Vector3.forward; }

        static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a; float t = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector3.Distance(p, a + ab * t);
        }

        Vector3 Ground(Vector3 feet)
        {
            foreach (var h in Physics.RaycastAll(feet + Vector3.up * 0.5f, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore))
                if (h.collider.GetComponentInParent<PlayerHealth>() == null && h.point.y <= feet.y + 0.6f) return new Vector3(feet.x, Mathf.Max(h.point.y, FloorY), feet.z);
            return new Vector3(feet.x, FloorY, feet.z);
        }

        // Quadrant of the arena around pillar i (by which side of the middle a point is on).
        int Quadrant(Vector3 point)
        {
            int best = 0; float bestDist = float.MaxValue;
            for (int i = 0; i < pillars.Length; i++)
            {
                bool sameX = Mathf.Sign(point.x - Center.x) == Mathf.Sign(pillarTop[i].x - Center.x);
                bool sameZ = Mathf.Sign(point.z - Center.z) == Mathf.Sign(pillarTop[i].z - Center.z);
                if (sameX && sameZ) return i;
                float d = (point - pillarTop[i]).sqrMagnitude; if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        Quaternion Facing(int p) { Vector3 d = Center - pillarTop[p]; d.y = 0f; return Quaternion.LookRotation(d.sqrMagnitude > 0.01f ? d : Vector3.forward); }
        Vector3 BodyAt(int p) => pillarTop[p] + Vector3.up * (bodyHalf + 0.05f);
        Vector3 Hand(int p, int h) => BodyAt(p) + Facing(p) * new Vector3(h == 0 ? -2.4f : 2.4f, 0.2f, 0.7f);

        // ---------- RPCs (every machine) ----------

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void SayRpc(string text) { message = text; messageUntil = Time.time + 5f; }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void AppearRpc(int p) => BossSpawnFlash.Spawn(new Vector3(pillarTop[p].x, pillarTop[p].y, pillarTop[p].z), 0.5f, toxicGlow);

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void GlobRpc(Vector3 from, Vector3 to, float flight) => ToxicGlob.Spawn(from, to, flight, toxicGlow, gasMaterial);

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void SlamRpc(int index, Vector3 at, float windup) { if (tentacles != null && index < tentacles.Length && tentacles[index] != null) tentacles[index].Strike(at, windup); }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void SpikeRpc(Vector3 at, float warning) => ToxicSpike.Spawn(at, warning, toxicGlow, tentacleMaterial);

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void CrumbleRpc(int p) => ExplosionEffect.Spawn(pillars[p].position, 4f, gasMaterial, toxicGlow, 0.6f);

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void PlaceRpc(Vector3 position, Quaternion rotation, RpcParams rpcParams)
        {
            var me = PlayerHealth.Local;
            if (me != null && me.TryGetComponent(out PlayerMovement movement)) movement.TeleportTo(new Pose(position, rotation));
        }

        // ---------- Looks (every machine) ----------

        static GameObject Prim(PrimitiveType type, string name, Transform parent, Material mat, Vector3 localPos, Vector3 localScale)
        {
            var g = GameObject.CreatePrimitive(type); g.name = name; Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false); g.transform.localPosition = localPos; g.transform.localScale = localScale;
            var r = g.GetComponent<Renderer>(); if (mat != null) r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return g;
        }

        Transform BuildArm(string name)
        {
            var root = new GameObject(name).transform; root.SetParent(transform, false);
            var bodyMat = bodyRenderers != null && bodyRenderers.Length > 0 ? bodyRenderers[0].sharedMaterial : tentacleMaterial;
            for (int h = 0; h < 2; h++)
            {
                float s = h == 0 ? -1f : 1f;
                Prim(PrimitiveType.Cube, "Arm" + h, root, bodyMat, new Vector3(s * 1.8f, 0.1f, 0.2f), new Vector3(1.4f, 0.7f, 0.7f));
                Prim(PrimitiveType.Sphere, "Hand" + h, root, toxicGlow, new Vector3(s * 2.4f, 0.2f, 0.7f), Vector3.one * 0.8f);
            }
            root.gameObject.SetActive(false);
            return root;
        }

        void BuildLooks()
        {
            arms = new[] { BuildArm("Boss Arms") };
            clones = new Transform[3];
            var mesh = body != null ? body.GetComponent<MeshFilter>().sharedMesh : null;
            var bodyMat = bodyRenderers != null && bodyRenderers.Length > 0 ? bodyRenderers[0].sharedMaterial : tentacleMaterial;
            for (int i = 0; i < clones.Length; i++)
            {
                var c = BuildArm("Boss Clone " + (i + 1));
                var cube = new GameObject("Body"); cube.transform.SetParent(c, false);
                cube.transform.localScale = body != null ? body.transform.lossyScale : Vector3.one * 3.5f;
                cube.AddComponent<MeshFilter>().sharedMesh = mesh; cube.AddComponent<MeshRenderer>().sharedMaterial = bodyMat;
                cube.AddComponent<BoxCollider>();
                cube.AddComponent<Targetable>();   // the aim locks on it too, so it can't tell clone from boss
                clones[i] = c;
            }
            beams = new Transform[2];
            for (int i = 0; i < 2; i++) { beams[i] = Prim(PrimitiveType.Cube, "Laser " + i, transform, toxicGlow, Vector3.zero, Vector3.one).transform; beams[i].gameObject.SetActive(false); }
            gasZones = new Transform[pillars.Length];
            for (int i = 0; i < gasZones.Length; i++) { gasZones[i] = Prim(PrimitiveType.Cube, "Gas Zone " + i, transform, gasMaterial, Vector3.zero, Vector3.one).transform; gasZones[i].gameObject.SetActive(false); }
        }

        void UpdateLooks(float dt)
        {
            var s = CurrentStage;
            bool fighting = s >= Stage.Phase1 && s <= Stage.Phase5;
            int p = pillar.Value;
            bool shown = fighting && p >= 0;
            bool shielded = InTransition || (s == Stage.Phase5 && !coreDown.Value);

            // The boss and its arms.
            if (body != null && fighting)
            {
                int at = p >= 0 ? p : Mathf.Max(0, lastShown);
                if (p >= 0) lastShown = p;
                float bob = Mathf.Sin(Time.time * 2f) * 0.15f;
                body.transform.SetPositionAndRotation(BodyAt(at) + Vector3.up * bob, Facing(at));
                SetShown(bodyRenderers, bodyCollider, bodyTarget, shown);
                body.SetStatusTint(new Color(0.4f, 1f, 0.3f), shown && shielded ? 0.45f + 0.25f * Mathf.Sin(Time.time * 8f) : 0f);
                arms[0].gameObject.SetActive(shown);
                if (shown) arms[0].SetPositionAndRotation(body.transform.position, body.transform.rotation);
            }
            else arms[0].gameObject.SetActive(false);

            // Phase 4: clones on the other pillars.
            int ci = 0;
            if (s == Stage.Phase4 && p >= 0)
                for (int i = 0; i < pillars.Length && ci < clones.Length; i++)
                {
                    if (i == p || IsBroken(i)) continue;
                    clones[ci].gameObject.SetActive(true);
                    clones[ci].SetPositionAndRotation(BodyAt(i) + Vector3.up * Mathf.Sin(Time.time * 2f + i) * 0.15f, Facing(i));
                    ci++;
                }
            for (; ci < clones.Length; ci++) clones[ci].gameObject.SetActive(false);

            // Phase 3: lasers.
            for (int h = 0; h < 2; h++)
            {
                bool on = laserOn.Value && shown;
                beams[h].gameObject.SetActive(on);
                if (!on) continue;
                Vector3 a = Hand(p, h), b = h == 0 ? aimA.Value : aimB.Value;
                Vector3 d = b - a; float len = Mathf.Max(0.1f, d.magnitude);
                beams[h].SetPositionAndRotation((a + b) * 0.5f, Quaternion.LookRotation(d / len));
                float w = laserRadius * 2f * (0.8f + 0.2f * Mathf.Sin(Time.time * 30f + h));
                beams[h].localScale = new Vector3(w, w, len);
            }

            // Phase 2: gas round three pillars.
            double now = Now;
            for (int i = 0; i < gasZones.Length; i++)
            {
                bool on = gasAt.Value >= 0.0 && i != gasSafe.Value && fighting;
                gasZones[i].gameObject.SetActive(on);
                if (!on) continue;
                bool warn = now < gasAt.Value + gasWarning;
                float h = warn ? 0.15f : 5f;
                Vector3 c = new Vector3(Center.x + Mathf.Sign(pillarTop[i].x - Center.x) * areaSize.x * 0.25f, FloorY + h * 0.5f + 0.02f, Center.z + Mathf.Sign(pillarTop[i].z - Center.z) * areaSize.z * 0.25f);
                gasZones[i].position = c;
                gasZones[i].localScale = new Vector3(areaSize.x * 0.5f - 0.3f, h, areaSize.z * 0.5f - 0.3f);
                var r = gasZones[i].GetComponent<Renderer>();
                r.sharedMaterial = warn ? gasWarningMaterial : gasMaterial;
                r.enabled = !warn || Mathf.Repeat(Time.time * 4f, 1f) < 0.6f;
            }

            // Phase 4: the side walls close in (and open again once it's over).
            bool closed = s >= Stage.Phase4 && s != Stage.Defeated;
            for (int i = 0; i < walls.Length; i++)
            {
                Vector3 want = wallRest[i] + Vector3.right * (closed ? wallInward[i] * wallShift : 0f);
                walls[i].position = Vector3.MoveTowards(walls[i].position, want, 2.5f * dt);
            }

            // Crumbled pillars.
            for (int i = 0; i < pillars.Length; i++)
            {
                bool gone = IsBroken(i) || (s == Stage.Defeated && System.Array.IndexOf(breakingPillars, i) >= 0 && brokeOnce);
                if (IsBroken(i)) brokeOnce = true;
                foreach (var r in pillarRenderers[i]) if (r != null) r.enabled = !gone;
                foreach (var c in pillarColliders[i]) if (c != null) c.enabled = !gone;
            }

            // Phase 4: tentacles.
            bool tentaclesOn = s == Stage.Phase4;
            if (tentaclesOn && tentacles == null)
            {
                tentacles = new ToxicTentacle[tentacleSpots.Length];
                for (int i = 0; i < tentacles.Length; i++) tentacles[i] = ToxicTentacle.Create(tentacleSpots[i], Center, transform, tentacleMaterial, toxicGlow);
            }
            if (!tentaclesOn && tentacles != null) { foreach (var t in tentacles) if (t != null) Destroy(t.gameObject); tentacles = null; }

            // Puddles (they stay).
            while (puddleViews.Count < puddles.Count)
                puddleViews.Add(Prim(PrimitiveType.Cylinder, "Toxic Puddle", transform, gasMaterial, Vector3.zero, Vector3.one).transform);
            while (puddleViews.Count > puddles.Count) { Destroy(puddleViews[^1].gameObject); puddleViews.RemoveAt(puddleViews.Count - 1); }
            for (int i = 0; i < puddles.Count; i++)
            {
                puddleViews[i].position = puddles[i] + Vector3.up * 0.04f;
                float wob = 1f + 0.04f * Mathf.Sin(Time.time * 3f + i);
                puddleViews[i].localScale = new Vector3(puddleRadius * 2f * wob, 0.03f, puddleRadius * 2f * wob);
            }

            // Phase 5: barrier and core.
            bool split = s == Stage.Phase5 && !coreDown.Value;
            if (barrier != null && barrier.activeSelf != split) barrier.SetActive(split);
            if (core != null)
            {
                SetShown(coreRenderers, coreCollider, coreTarget, split);
                core.transform.Rotate(0f, 40f * dt, 0f, Space.World);
            }
            if (coreRays != null)
            {
                bool rays = split && core != null;
                if (coreRays.gameObject.activeSelf != rays) coreRays.gameObject.SetActive(rays);
                if (rays) { coreRays.position = new Vector3(core.transform.position.x, FloorY + rayHeight, core.transform.position.z); coreRays.rotation = Quaternion.Euler(0f, RayAngle(), 0f); }
            }

            if (s == Stage.Defeated && !defeatApplied)
            {
                defeatApplied = true;
                foreach (var g in gasZones) g.gameObject.SetActive(false);
                onDefeated.Invoke();
            }
        }

        int lastShown;
        bool brokeOnce;

        static void SetShown(Renderer[] renderers, Collider col, Targetable target, bool shown)
        {
            if (renderers != null) foreach (var r in renderers) if (r != null && r.enabled != shown) r.enabled = shown;
            if (col != null && col.enabled != shown) col.enabled = shown;
            if (target != null && target.enabled != shown) target.enabled = shown;
        }

        // The local player: the checkpoint once it's on, and out of the way of the closing walls.
        void LocalTick()
        {
            var s = CurrentStage;
            if (!checkpointSet && s >= Stage.Phase1 && respawnPoints != null && respawnPoints.Length > 0) { checkpointSet = true; PlayerSpawnPoints.SetCheckpoint(respawnPoints); }
            var me = PlayerHealth.Local;
            if (me == null || !Contains(me.transform.position)) return;
            var cc = me.GetComponent<CharacterController>();
            if (cc == null || !cc.enabled) return;
            for (int i = 0; i < walls.Length; i++)
            {
                float face = walls[i].position.x + wallInward[i] * (wallHalf[i] + 0.6f);
                float over = (face - me.transform.position.x) * wallInward[i];
                if (over > 0f) cc.Move(new Vector3(over * wallInward[i], 0f, 0f));
            }
        }

        // ---------- Health bar and messages ----------

        void OnGUI()
        {
            if (!IsSpawned || CutsceneSystem.HidesGameplayUI) return;
            var me = PlayerHealth.Local;
            if (me == null) return;
            Vector3 d = me.transform.position - Center;
            bool near = Mathf.Abs(d.x) < areaSize.x * 0.5f + 15f && Mathf.Abs(d.z) < areaSize.z * 0.5f + 15f && d.y > -5f && d.y < areaSize.y + 10f;
            nameStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, richText = true };
            smallStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, richText = true };
            boxStyle ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, richText = true, wordWrap = true };
            nameStyle.fontSize = Mathf.Max(16, Screen.height / 36); smallStyle.fontSize = Mathf.Max(12, Screen.height / 58); boxStyle.fontSize = Mathf.Max(14, Screen.height / 50);

            if (Time.time < messageUntil && !string.IsNullOrEmpty(message) && near)
                GUI.Box(new Rect(Screen.width * 0.5f - 440f, Screen.height - 250f, 880f, 52f), message, boxStyle);

            var s = CurrentStage;
            if (s < Stage.Phase1 || s > Stage.Phase5 || !near) return;
            float width = Mathf.Min(Screen.width * 0.6f, 900f), height = Mathf.Max(14f, Screen.height * 0.022f);
            float x = (Screen.width - width) * 0.5f, y = Screen.height * 0.14f;   // under the level HUD at the top
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(x - 4f, y - nameStyle.fontSize * 1.5f - 4f, width + 8f, height + nameStyle.fontSize * 2.9f + 8f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            string state = InTransition ? "<color=#aaffaa>Zmienia fazę, nie da się go zranić.</color>"
                : s == Stage.Phase5 && !coreDown.Value ? "<color=#aaffaa>Osłonięty: zniszczcie rdzeń.</color>"
                : pillar.Value < 0 ? "Zniknął... zaraz pojawi się na innym filarze." : null;
            GUI.Label(new Rect(x, y - nameStyle.fontSize * 1.5f, width, nameStyle.fontSize * 1.4f), $"{bossName}   {Health} / {maxHealth}   FAZA {(int)s}/5", nameStyle);
            GUI.color = new Color(0.08f, 0.12f, 0.05f, 0.95f); GUI.DrawTexture(new Rect(x, y, width, height), Texture2D.whiteTexture);
            GUI.color = InTransition || (s == Stage.Phase5 && !coreDown.Value) ? new Color(0.35f, 0.5f, 0.3f) : new Color(0.45f, 0.95f, 0.2f);
            GUI.DrawTexture(new Rect(x, y, width * Mathf.Clamp01((float)Health / maxHealth), height), Texture2D.whiteTexture);
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            foreach (int t in thresholds) GUI.DrawTexture(new Rect(x + width * t / maxHealth - 1f, y, 2f, height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            string hint = null;
            ulong id = me.OwnerClientId;
            if (s == Stage.Phase5 && !coreDown.Value)
                hint = id == runner.Value && id == dodger.Value ? "Zniszcz organiczny rdzeń, przeskakując nad promieniami, i uciekaj przed mackami!"
                    : id == runner.Value ? "Zniszcz organiczny rdzeń! Przeskakuj nad toksycznymi promieniami."
                    : id == dodger.Value ? "Uciekaj! Macki wyrastają tam, gdzie stoisz." : null;
            else if (gasAt.Value >= 0.0 && Now < gasAt.Value + gasWarning) hint = "Gaz za chwilę! Biegnij do filaru bez zielonej strefy.";
            else if (s == Stage.Phase4) hint = "Tylko jeden boss jest prawdziwy. Uważajcie na macki przy filarach i ścianach!";
            string line = state != null && hint != null ? state + "   " + hint : state ?? hint;
            if (line != null) GUI.Label(new Rect(x - 60f, y + height + 4f, width + 120f, smallStyle.fontSize * 1.6f), line, smallStyle);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 1f, 0.3f, 0.8f);
            Gizmos.DrawWireCube(transform.position + Vector3.up * (areaSize.y * 0.5f), areaSize);
        }

        public void Setup(Damageable bossBody, Transform[] bossPillars, Transform[] movingWalls, Damageable organicCore, Transform rays, GameObject toxicBarrier,
            Transform dodgeAt, Transform coreAt, Transform[] respawns, Material glow, Material gas, Material gasWarn, Material tentacle)
        {
            body = bossBody; pillars = bossPillars; walls = movingWalls; core = organicCore; coreRays = rays; barrier = toxicBarrier;
            dodgerSpot = dodgeAt; coreSpot = coreAt; respawnPoints = respawns;
            toxicGlow = glow; gasMaterial = gas; gasWarningMaterial = gasWarn; tentacleMaterial = tentacle;
        }
    }

    /// <summary>On the boss's body and on its organic core: hits only get through when the fight allows it (server).</summary>
    public class ToxicBossPart : MonoBehaviour, IDamageAbsorber
    {
        [SerializeField] ToxicBoss fight;
        [SerializeField] bool isCore;

        public void Bind(ToxicBoss boss, bool core) { fight = boss; isCore = core; }

        public int Absorb(int amount, Vector3 hitDirection, DamageTypes type) =>
            fight == null ? 0 : isCore ? fight.ServerAbsorbCore(amount) : fight.ServerAbsorb(amount);
    }

    /// <summary>A glob of toxic goo lobbed in an arc; it splashes where it lands.</summary>
    public class ToxicGlob : MonoBehaviour
    {
        Vector3 from, to; float start, flight; Material splash;

        public static void Spawn(Vector3 from, Vector3 to, float flight, Material glow, Material splashMaterial)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere); g.name = "Toxic Glob"; Destroy(g.GetComponent<Collider>());
            g.GetComponent<Renderer>().sharedMaterial = glow; g.transform.localScale = Vector3.one * 0.7f; g.transform.position = from;
            var c = g.AddComponent<ToxicGlob>(); c.from = from; c.to = to; c.flight = flight; c.start = Time.time; c.splash = splashMaterial;
        }

        void Update()
        {
            float k = (Time.time - start) / Mathf.Max(0.05f, flight);
            if (k >= 1f) { ToxicPuff.Spawn(to + Vector3.up * 0.3f, 2.6f, 0.45f, splash); Destroy(gameObject); return; }
            transform.position = Vector3.Lerp(from, to, k) + Vector3.up * (4f * 4f * k * (1f - k));
        }
    }

    /// <summary>A sphere that swells and fades away (a splash, a puff of gas).</summary>
    public class ToxicPuff : MonoBehaviour
    {
        float start, life, size;

        public static void Spawn(Vector3 at, float size, float life, Material mat)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere); g.name = "Toxic Puff"; Destroy(g.GetComponent<Collider>());
            g.GetComponent<Renderer>().sharedMaterial = mat; g.transform.position = at;
            var p = g.AddComponent<ToxicPuff>(); p.start = Time.time; p.life = life; p.size = size;
        }

        void Update()
        {
            float k = (Time.time - start) / life;
            if (k >= 1f) { Destroy(gameObject); return; }
            transform.localScale = Vector3.one * Mathf.Lerp(0.3f, size, Mathf.Sqrt(k)) * (k > 0.7f ? 1f - (k - 0.7f) / 0.3f : 1f);
        }
    }

    /// <summary>A tentacle growing out of the floor: it sways, and when told to, rears back and slams down on a point.</summary>
    public class ToxicTentacle : MonoBehaviour
    {
        const float IdleLength = 2.4f;
        Transform stem;
        Vector3 target; float strikeAt = -10f, windup = 0.6f, seed;
        Vector3 rest;

        public static ToxicTentacle Create(Vector3 root, Vector3 lean, Transform parent, Material mat, Material tipMat)
        {
            var go = new GameObject("Tentacle"); go.transform.SetParent(parent, false); go.transform.position = root;
            var t = go.AddComponent<ToxicTentacle>();
            t.stem = new GameObject("Stem").transform; t.stem.SetParent(go.transform, false);
            for (int i = 0; i < 6; i++)
            {
                float w = Mathf.Lerp(0.75f, 0.3f, i / 5f);
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(c.GetComponent<Collider>());
                c.transform.SetParent(t.stem, false); c.transform.localPosition = new Vector3(0f, (i + 0.5f) / 6f, 0f); c.transform.localScale = new Vector3(w, 1f / 6f + 0.02f, w);
                c.GetComponent<Renderer>().sharedMaterial = i == 5 ? tipMat : mat;
            }
            Vector3 d = lean - root; d.y = 0f; t.rest = d.sqrMagnitude > 0.01f ? d.normalized : Vector3.forward;
            t.seed = Random.value * 10f;
            return t;
        }

        public void Strike(Vector3 at, float windupTime) { target = at; windup = windupTime; strikeAt = Time.time; }

        void Update()
        {
            float t = Time.time - strikeAt;
            Vector3 toTarget = target - transform.position; toTarget.y = 0f;
            float dist = Mathf.Clamp(toTarget.magnitude, 1.5f, 7f);
            Vector3 flatDir = toTarget.sqrMagnitude > 0.01f ? toTarget.normalized : rest;
            Vector3 dir; float length;
            if (t < windup)
            {
                // Rear back, growing.
                float k = t / windup;
                dir = Vector3.Slerp(Vector3.up, (Vector3.up - flatDir * 0.45f).normalized, k);
                length = Mathf.Lerp(IdleLength, dist + 0.5f, k);
            }
            else if (t < windup + 0.12f)
            {
                float k = (t - windup) / 0.12f;
                dir = Vector3.Slerp((Vector3.up - flatDir * 0.45f).normalized, (flatDir + Vector3.down * 0.08f).normalized, k);
                length = dist + 0.5f;
            }
            else if (t < windup + 0.8f) { dir = (flatDir + Vector3.down * 0.08f).normalized; length = dist + 0.5f; }
            else if (t < windup + 1.4f)
            {
                float k = (t - windup - 0.8f) / 0.6f;
                dir = Vector3.Slerp((flatDir + Vector3.down * 0.08f).normalized, Vector3.up, k);
                length = Mathf.Lerp(dist + 0.5f, IdleLength, k);
            }
            else
            {
                float sway = Mathf.Sin(Time.time * 1.7f + seed) * 0.18f;
                dir = (Vector3.up + rest * sway + Vector3.Cross(Vector3.up, rest) * Mathf.Cos(Time.time * 1.3f + seed) * 0.12f).normalized;
                length = IdleLength;
            }
            stem.rotation = Quaternion.FromToRotation(Vector3.up, dir);
            stem.localScale = new Vector3(1f, length, 1f);
        }
    }

    /// <summary>A warning ring on the floor, then a tentacle shooting straight up out of it.</summary>
    public class ToxicSpike : MonoBehaviour
    {
        Transform ring, spike; float start, warning;

        public static void Spawn(Vector3 at, float warning, Material glow, Material mat)
        {
            var go = new GameObject("Toxic Spike"); go.transform.position = at;
            var s = go.AddComponent<ToxicSpike>(); s.start = Time.time; s.warning = warning;
            s.ring = MakePart(PrimitiveType.Cylinder, go.transform, glow); s.ring.localScale = new Vector3(2.4f, 0.02f, 2.4f); s.ring.localPosition = Vector3.up * 0.03f;
            s.spike = MakePart(PrimitiveType.Cube, go.transform, mat); s.spike.gameObject.SetActive(false);
        }

        static Transform MakePart(PrimitiveType type, Transform parent, Material mat)
        {
            var g = GameObject.CreatePrimitive(type); Destroy(g.GetComponent<Collider>()); g.transform.SetParent(parent, false);
            g.GetComponent<Renderer>().sharedMaterial = mat; return g.transform;
        }

        void Update()
        {
            float t = Time.time - start;
            if (t < warning)
            {
                float pulse = 1f + 0.15f * Mathf.Sin(t * 25f);
                ring.localScale = new Vector3(2.4f * pulse, 0.02f, 2.4f * pulse);
                return;
            }
            ring.gameObject.SetActive(false); spike.gameObject.SetActive(true);
            float u = t - warning;
            float h = u < 0.12f ? Mathf.Lerp(0.1f, 3.6f, u / 0.12f) : u < 0.55f ? 3.6f : Mathf.Lerp(3.6f, 0f, (u - 0.55f) / 0.3f);
            if (u > 0.85f) { Destroy(gameObject); return; }
            spike.localScale = new Vector3(0.8f, h, 0.8f); spike.localPosition = Vector3.up * (h * 0.5f);
        }
    }
}
