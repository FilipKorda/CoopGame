using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace CoopGame
{
    /// <summary>
    /// The final fight with the Protagonist (400 HP) in A7's arena; the server runs it, every machine draws it.
    /// Phase 1 (400..300): flies round the players with a pistol, keeps one player as the target for 5 s, then the
    /// other, firing a salvo of 3 fast shots every second. Phase 2 (300..200): the jetpack is gone; it takes turns
    /// leaping onto a player (the landing sends out a shockwave to jump over) and charging across the arena at one.
    /// Phase 3 (200..100): emergency jetpack, always in this order: shots from the air (hurtable only while landed
    /// for 2 s after it), bombs on 4 red marks (never hurtable), a dive at one player (a miss overloads the jetpack:
    /// hurtable for 5 s). Phase 4 (100..0): hurtable all the time; spins with two swords bouncing round the arena,
    /// throws blades in all directions every 5 s of spinning (they stick in the walls), then stops and pulls them back.
    /// It can't be hurt between phases. The body is its own NetworkObject (Damageable + NetworkTransform +
    /// <see cref="FinalBossPart"/>) that the server moves; the fight starts once no cutscene runs and a player is in the arena.
    /// </summary>
    public class FinalBoss : NetworkBehaviour
    {
        public enum Stage : byte { Waiting, Phase1, Phase2, Phase3, Phase4, Defeated }

        public enum Act : byte
        {
            None, Fly, Fall, Stand, LeapWindup, Leap, SlamRecover, ChargeWindup, Charge, ChargeStun,
            Rise, AirAim, AirShoot, Land, Grounded, BombRise, BombWarn, BombDrop, DiveTrack, Dive, Overload, DiveRecover,
            Emergency, DrawSwords, Spin, SpinSlow, PullWindup, Pull,
        }

        public const float WaveStartRadius = 1f;
        const ulong NoTarget = ulong.MaxValue;

        [Header("Arena")]
        [SerializeField] string bossName = "PROTAGONISTA";
        [Tooltip("Inside of the arena (world axes); this object stands in the middle of its floor.")]
        [SerializeField] Vector3 areaSize = new Vector3(28.7f, 14f, 29.6f);
        [Tooltip("Checkpoint once the fight has started (player 1, player 2).")]
        [SerializeField] Transform[] respawnPoints;
        [Tooltip("Hidden for good once the fight starts (the showdown cutscene's stand-in of the boss).")]
        [SerializeField] GameObject[] hideOnStart;
        [Tooltip("How far from the walls the boss keeps on the ground / in the air.")]
        [SerializeField] float groundInset = 2f;
        [SerializeField] float airInset = 2.5f;

        [Header("Boss")]
        [SerializeField] Damageable body;
        [Tooltip("Child of the body that bobs, leans and spins (the server only moves the body itself).")]
        [SerializeField] Transform visual;
        [SerializeField] GameObject gun;
        [SerializeField] Transform muzzle;
        [SerializeField] GameObject swords;
        [SerializeField] ParticleSystem[] thrusters;
        [SerializeField] Transform[] flames;
        [SerializeField] Transform[] nozzles;
        [Tooltip("Health at which phases 2, 3 and 4 begin.")]
        [SerializeField] int[] thresholds = { 300, 200, 100 };
        [Tooltip("Seconds between phases (can't be hurt).")]
        [SerializeField] float transitionTime = 2f;

        [Header("Phase 1: flying with the pistol")]
        [SerializeField] float flyHeight = 6f;
        [SerializeField] float orbitRadius = 9f;
        [SerializeField] float orbitSpeed = 28f;
        [SerializeField] float flySpeed = 9f;
        [Tooltip("Seconds one player stays the target before the other one is.")]
        [SerializeField] float targetTime = 5f;
        [SerializeField] float salvoEvery = 1f;
        [SerializeField] int salvoCount = 3;
        [SerializeField] float salvoGap = 0.09f;
        [SerializeField] float shotSpeed = 34f;
        [SerializeField] float shotRadius = 0.25f;
        [SerializeField] float shotLife = 2f;
        [SerializeField] int shotDamage = 1;

        [Header("Phase 2: leap + shockwave, charge")]
        [SerializeField] float groundRest = 1f;
        [SerializeField] float leapWindup = 0.45f;
        [SerializeField] float leapTime = 1f;
        [SerializeField] float leapHeight = 6f;
        [SerializeField] float slamRadius = 2.8f;
        [SerializeField] int slamDamage = 3;
        [SerializeField] float slamRecover = 0.9f;
        [SerializeField] float waveSpeed = 10f;
        [SerializeField] float waveMaxRadius = 22f;
        [Tooltip("How close to the ring a player has to be to get hit.")]
        [SerializeField] float waveBand = 0.6f;
        [Tooltip("Feet this high above the floor clear the shockwave.")]
        [SerializeField] float waveClear = 0.55f;
        [SerializeField] float waveHeight = 0.5f;
        [SerializeField] int waveDamage = 2;
        [Tooltip("Turning towards the target, then the line locks for chargeLock seconds.")]
        [SerializeField] float chargeAim = 0.6f;
        [SerializeField] float chargeLock = 0.4f;
        [SerializeField] float chargeSpeed = 24f;
        [SerializeField] float chargeRadius = 2f;
        [SerializeField] float chargeStun = 1.2f;
        [SerializeField] int chargeDamage = 3;

        [Header("Phase 3: emergency jetpack")]
        [SerializeField] float airHeight = 6f;
        [SerializeField] float riseTime = 0.9f;
        [SerializeField] float airAim = 1f;
        [SerializeField] int airShots = 5;
        [SerializeField] float airShotGap = 0.2f;
        [SerializeField] float landTime = 0.6f;
        [Tooltip("Hurtable on the ground after the air attack.")]
        [SerializeField] float groundedTime = 2f;
        [SerializeField] float bombHeight = 10f;
        [SerializeField] float bombWarning = 1.5f;
        [SerializeField] float bombFall = 0.6f;
        [SerializeField] float bombRadius = 2.6f;
        [SerializeField] int bombDamage = 3;
        [Tooltip("Follows the target this long (its mark follows too), then dives at the spot.")]
        [SerializeField] float diveTrack = 1.4f;
        [SerializeField] float diveTime = 0.45f;
        [SerializeField] float diveHeight = 8f;
        [SerializeField] float diveStandOff = 7f;
        [SerializeField] float diveRadius = 3f;
        [SerializeField] int diveDamage = 4;
        [Tooltip("Hurtable this long after a missed dive.")]
        [SerializeField] float overloadTime = 5f;
        [SerializeField] float diveRecover = 1f;

        [Header("Phase 4: whirl of blades")]
        [SerializeField] float spinTime = 6f;
        [SerializeField] float spinSpeed = 9f;
        [SerializeField] float spinSlow = 1.4f;
        [SerializeField] float bladeReach = 3f;
        [SerializeField] int spinDamage = 2;
        [SerializeField] float burstEvery = 5f;
        [SerializeField] int burstBlades = 12;
        [SerializeField] float bladeSpeed = 18f;
        [SerializeField] float bladeHeight = 1.3f;
        [SerializeField] float bladeHitRadius = 0.6f;
        [SerializeField] int bladeDamage = 2;
        [SerializeField] float pullWindup = 0.6f;
        [SerializeField] float pullSpeed = 22f;

        [Header("Looks")]
        [SerializeField] Material warningMaterial;
        [SerializeField] Material glowMaterial;
        [SerializeField] Material shotMaterial;
        [SerializeField] Material fireMaterial;
        [SerializeField] Material coreMaterial;
        [SerializeField] Material waveMaterial;
        [SerializeField] Material bladeMaterial;
        [SerializeField] Material hiltMaterial;
        [SerializeField] Material bombMaterial;
        [SerializeField] Material smokeMaterial;
        [SerializeField] Material sparkMaterial;

        [Tooltip("Every machine (also a player who joins later): the boss is beaten. He stays standing until Hide Body.")]
        public UnityEvent onDefeated = new();

        /// <summary>This machine: hide the beaten boss (the end cutscene's own stand-in takes over).</summary>
        public void HideBody() => bodyHidden = true;

        readonly NetworkVariable<byte> stage = new();
        readonly NetworkVariable<byte> act = new();
        readonly NetworkVariable<double> actAt = new();
        readonly NetworkVariable<bool> vulnerable = new();
        readonly NetworkVariable<bool> transition = new();
        readonly NetworkVariable<ulong> marked = new(NoTarget);
        readonly NetworkVariable<Vector3> diveSpot = new();

        // Server only.
        class Shot { public int id; public Vector3 pos, dir; public float life; public readonly HashSet<PlayerHealth> passed = new(); }
        class Wave { public Vector3 at; public float start; public readonly HashSet<ulong> hit = new(); }
        class Blade { public Vector3 from, to, last; public float start, time; public bool pulling, arrived; public readonly HashSet<ulong> hit = new(); }
        readonly List<Shot> shots = new();
        readonly List<Wave> waves = new();
        readonly List<Blade> blades = new();
        int nextShot, targetTurn;

        // Every machine.
        Transform bt, fxRoot, targetMark;
        Renderer[] bodyRenderers;
        Collider bodyCollider;
        Targetable bodyTarget;
        Vector3[] flameScale;
        readonly Dictionary<int, FinalBossShot> shotViews = new();
        readonly List<FinalBossBlade> bladeViews = new();
        FinalBossMarker diveMark;
        Vector3 lastBodyPos, velocity;
        float spinAngle, nextPuff, nextSpark;
        bool flamesOn = true, shownNow = true, defeatApplied, checkpointSet, bodyHidden;
        int maxHealth = 400;
        string message;
        float messageUntil;
        GUIStyle nameStyle, smallStyle, boxStyle;

        double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : Time.timeAsDouble;
        public Stage CurrentStage => (Stage)stage.Value;
        public Act CurrentAct => (Act)act.Value;
        public bool IsVulnerable => vulnerable.Value;
        public int Health => CurrentStage == Stage.Defeated ? 0 : body != null && body.IsSpawned ? body.Health : maxHealth;
        Vector3 Center => transform.position;
        float Floor => transform.position.y;
        Vector3 Half => new Vector3(areaSize.x * 0.5f, 0f, areaSize.z * 0.5f);
        bool Airborne => bt.position.y > Floor + 0.05f;
        Vector3 Muzzle => muzzle != null ? muzzle.position : bt.position + Vector3.up * 2.4f + bt.forward;

        // ---------- Setup ----------

        void Awake()
        {
            fxRoot = new GameObject("Boss FX").transform;
            fxRoot.SetParent(transform, false);
            if (body != null)
            {
                bt = body.transform;
                maxHealth = body.MaxHealth;
                bodyRenderers = bt.GetComponentsInChildren<Renderer>(true);
                bodyCollider = body.GetComponent<Collider>();
                bodyTarget = body.GetComponent<Targetable>();
                lastBodyPos = bt.position;
                SetShown(false);   // straight away, so it doesn't flash up before the network starts
            }
            flameScale = new Vector3[flames != null ? flames.Length : 0];
            for (int i = 0; i < flameScale.Length; i++) flameScale[i] = flames[i] != null ? flames[i].localScale : Vector3.one;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer && body != null) body.Died += _ => ServerDefeat();
        }

        public override void OnNetworkDespawn() => ClearViews();

        // ---------- Damage gate (server) ----------

        /// <summary>Server: how much of a hit on the boss gets through (none between phases or while it's invulnerable).</summary>
        public int ServerAbsorb(int amount)
        {
            var s = CurrentStage;
            if (s < Stage.Phase1 || s == Stage.Defeated || transition.Value || !vulnerable.Value || body == null) return 0;
            // The last hit leaves 1 HP: the body doesn't break apart, he's still there for the end cutscene.
            int floor = s < Stage.Phase4 ? thresholds[(int)s - 1] : 1;
            int through = Mathf.Clamp(amount, 0, body.Health - floor);
            if (body.Health - through <= floor)
            {
                if (s == Stage.Phase4) ServerDefeat();
                else ServerStartTransition();
            }
            return through;
        }

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
                if (body != null && Targets().Count > 0) ServerStart();
                return;
            }
            if (s == Stage.Defeated) return;
            TickShots(dt);
            TickWaves();
            TickBlades();
        }

        void ServerStart()
        {
            stage.Value = (byte)Stage.Phase1;
            SayRpc("Protagonista: Zobaczymy, jak poradzicie sobie z ogniem z powietrza!");
            StartCoroutine(PhaseLoop(Stage.Phase1));
        }

        IEnumerator PhaseLoop(Stage s) => s switch
        {
            Stage.Phase1 => Phase1(),
            Stage.Phase2 => Phase2(),
            Stage.Phase3 => Phase3(),
            _ => Phase4(),
        };

        void ServerStartTransition()
        {
            if (transition.Value) return;
            StopAllCoroutines();
            ServerClearHazards();
            transition.Value = true;
            vulnerable.Value = false;
            marked.Value = NoTarget;
            StartCoroutine(Transition((Stage)(stage.Value + 1)));
        }

        IEnumerator Transition(Stage next)
        {
            switch (next)
            {
                case Stage.Phase2:
                    SayRpc("Protagonista: Mój plecak! ...Nieważne, zmiażdżę was i bez niego!");
                    yield return FallToGround();
                    yield return Hold(Act.Stand, transitionTime);
                    break;
                case Stage.Phase3:
                    SayRpc("Protagonista: Tryb awaryjny plecaka! Jeszcze ze mną nie skończyliście!");
                    if (Airborne) yield return FallToGround();
                    yield return Hold(Act.Emergency, transitionTime);
                    break;
                default:
                    SayRpc("Protagonista: Dość zabawy! Poznajcie moje ostrza!");
                    if (Airborne) yield return FallToGround();
                    yield return Hold(Act.DrawSwords, transitionTime);
                    break;
            }
            stage.Value = (byte)next;
            transition.Value = false;
            StartCoroutine(PhaseLoop(next));
        }

        void ServerDefeat()
        {
            if (CurrentStage == Stage.Defeated) return;
            StopAllCoroutines();
            stage.Value = (byte)Stage.Defeated;
            SetAct(Act.Stand);
            transition.Value = false;
            vulnerable.Value = false;
            marked.Value = NoTarget;
            blades.Clear();
            ServerClearHazards();
            SayRpc("Protagonista pokonany!");
        }

        void ServerClearHazards()
        {
            shots.Clear();
            waves.Clear();
            ClearRpc();
        }

        // ---------- Phase 1 ----------

        IEnumerator Phase1()
        {
            vulnerable.Value = true;
            SetAct(Act.Fly);
            Vector3 pivot = PlayersMiddle();
            Vector3 off = bt.position - pivot;
            float angle = off.x * off.x + off.z * off.z > 0.01f ? Mathf.Atan2(off.x, off.z) * Mathf.Rad2Deg : 0f;
            float turn = 1f, flipAt = Time.time + Random.Range(6f, 10f);
            var aim = NextTarget();
            float switchAt = Time.time + targetTime, salvoAt = Time.time + salvoEvery;
            while (true)
            {
                float dt = Time.deltaTime;
                if (Time.time >= switchAt || !IsValid(aim)) { aim = NextTarget(); switchAt = Time.time + targetTime; }
                marked.Value = aim != null ? aim.OwnerClientId : NoTarget;
                if (Time.time >= flipAt) { turn = -turn; flipAt = Time.time + Random.Range(6f, 10f); }

                pivot = Vector3.MoveTowards(pivot, PlayersMiddle(), 4f * dt);
                angle += orbitSpeed * turn * dt;
                Vector3 want = ClampXZ(pivot + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * orbitRadius, airInset);
                want.y = Floor + flyHeight + Mathf.Sin(Time.time * 0.8f) * 0.8f;
                bt.position = Vector3.MoveTowards(bt.position, want, flySpeed * dt);

                if (aim == null) salvoAt = Time.time + 0.5f;
                else
                {
                    Face(aim.transform.position, 360f);
                    if (Time.time >= salvoAt) { salvoAt = Time.time + salvoEvery; StartCoroutine(Salvo(aim, salvoCount, salvoGap)); }
                }
                yield return null;
            }
        }

        IEnumerator Salvo(PlayerHealth aim, int count, float gap)
        {
            for (int i = 0; i < count; i++)
            {
                if (IsValid(aim)) FireAt(aim);
                yield return Wait(gap);
            }
        }

        void FireAt(PlayerHealth aim)
        {
            Vector3 from = Muzzle;
            Vector3 dir = aim.transform.position + Vector3.up * 1.1f - from;
            if (dir.sqrMagnitude < 0.01f) return;
            dir.Normalize();
            var s = new Shot { id = ++nextShot, pos = from, dir = dir, life = shotLife };
            shots.Add(s);
            ShotRpc(s.id, from, dir);
        }

        void TickShots(float dt)
        {
            float step = shotSpeed * dt;
            for (int i = shots.Count - 1; i >= 0; i--)
            {
                var s = shots[i];
                var hits = Physics.SphereCastAll(s.pos, shotRadius, s.dir, step, ~0, QueryTriggerInteraction.Ignore);
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                bool end = false;
                Vector3 point = s.pos + s.dir * step;
                foreach (var h in hits)
                {
                    if (IsBossCollider(h.collider)) continue;
                    var p = h.collider.GetComponentInParent<PlayerHealth>();
                    if (p != null)
                    {
                        if (s.passed.Contains(p)) continue;
                        if (!p.IsAlive || !p.ApplyDamage(new DamageInfo(shotDamage, Flat(s.dir), gameObject))) { s.passed.Add(p); continue; }
                    }
                    end = true;
                    point = h.distance > 0f ? h.point : s.pos;
                    break;
                }
                s.life -= dt;
                if (end || s.life <= 0f)
                {
                    shots.RemoveAt(i);
                    ShotEndRpc(s.id, point);
                    continue;
                }
                s.pos += s.dir * step;
            }
        }

        // ---------- Phase 2 ----------

        IEnumerator Phase2()
        {
            vulnerable.Value = true;
            bool leap = true;
            while (true)
            {
                var aim = RandomTarget();
                SetAct(Act.Stand);
                marked.Value = NoTarget;
                for (float end = Time.time + groundRest; Time.time < end;)
                {
                    if (IsValid(aim)) Face(aim.transform.position, 240f);
                    yield return null;
                }
                if (!IsValid(aim)) { yield return null; continue; }
                if (leap) yield return Leap(aim);
                else yield return Charge(aim);
                leap = !leap;
            }
        }

        IEnumerator Leap(PlayerHealth aim)
        {
            marked.Value = aim.OwnerClientId;
            SetAct(Act.LeapWindup);
            for (float end = Time.time + leapWindup; Time.time < end;)
            {
                if (IsValid(aim)) Face(aim.transform.position, 360f);
                yield return null;
            }
            Vector3 from = bt.position;
            Vector3 to = ClampXZ(IsValid(aim) ? aim.transform.position : from, groundInset);
            to.y = Floor;
            MarkerRpc(to, slamRadius, leapTime + 0.15f);
            SetAct(Act.Leap);
            marked.Value = NoTarget;
            for (float t = 0f; t < leapTime; t += Time.deltaTime)
            {
                float k = t / leapTime;
                bt.position = Vector3.Lerp(from, to, k) + Vector3.up * (4f * leapHeight * k * (1f - k));
                Face(to, 720f);
                yield return null;
            }
            bt.position = to;
            SetAct(Act.SlamRecover);
            SlamRpc(to, slamRadius);
            HitArea(to, slamRadius, 3f, slamDamage);
            waves.Add(new Wave { at = to, start = Time.time });
            WaveRpc(to);
            yield return Wait(slamRecover);
        }

        void TickWaves()
        {
            for (int i = waves.Count - 1; i >= 0; i--)
            {
                var w = waves[i];
                float r = WaveStartRadius + (Time.time - w.start) * waveSpeed;
                if (r > waveMaxRadius) { waves.RemoveAt(i); continue; }
                foreach (var p in Targets())
                {
                    if (w.hit.Contains(p.OwnerClientId)) continue;
                    Vector3 d = p.transform.position - w.at;
                    float up = d.y;
                    d.y = 0f;
                    if (Mathf.Abs(d.magnitude - r) > waveBand || up > waveClear) continue;
                    w.hit.Add(p.OwnerClientId);
                    p.ApplyDamage(new DamageInfo(waveDamage, Away(p, w.at), gameObject));
                }
            }
        }

        IEnumerator Charge(PlayerHealth aim)
        {
            marked.Value = aim.OwnerClientId;
            SetAct(Act.ChargeWindup);
            for (float end = Time.time + chargeAim; Time.time < end;)
            {
                if (IsValid(aim)) Face(aim.transform.position, 400f);
                yield return null;
            }
            Vector3 dir = Flat(IsValid(aim) ? aim.transform.position - bt.position : bt.forward);
            if (dir == Vector3.zero) dir = Flat(bt.forward);
            if (dir == Vector3.zero) dir = Vector3.forward;
            bt.rotation = Quaternion.LookRotation(dir);
            float dist = DistanceToWall(bt.position, dir, groundInset);
            ChargeLineRpc(bt.position, dir, dist, chargeLock);
            yield return Wait(chargeLock);

            SetAct(Act.Charge);
            marked.Value = NoTarget;
            var hit = new HashSet<ulong>();
            for (float moved = 0f; moved < dist;)
            {
                float step = Mathf.Min(chargeSpeed * Time.deltaTime, dist - moved);
                bt.position += dir * step;
                moved += step;
                foreach (var p in Targets())
                {
                    if (hit.Contains(p.OwnerClientId) || !Near(p, bt.position, chargeRadius, 3f)) continue;
                    hit.Add(p.OwnerClientId);
                    p.ApplyDamage(new DamageInfo(chargeDamage, (Away(p, bt.position) + dir).normalized, gameObject));
                }
                yield return null;
            }
            SparksRpc(bt.position + dir * 1.2f + Vector3.up * 1.8f);
            yield return Hold(Act.ChargeStun, chargeStun);
        }

        // ---------- Phase 3 ----------

        IEnumerator Phase3()
        {
            while (true)
            {
                yield return AirAttack();
                yield return Bombing();
                yield return Dive();
            }
        }

        IEnumerator AirAttack()
        {
            vulnerable.Value = false;
            var aim = NextTarget();
            if (aim == null) { yield return LandHere(); yield return Hold(Act.Stand, 0.5f); yield break; }
            marked.Value = aim.OwnerClientId;
            Vector3 up = ClampXZ(bt.position, airInset);
            up.y = Floor + airHeight;
            yield return MoveTo(Act.Rise, up, riseTime, 1, aim);
            SetAct(Act.AirAim);
            for (float end = Time.time + airAim; Time.time < end;)
            {
                if (IsValid(aim)) Face(aim.transform.position, 360f);
                yield return null;
            }
            SetAct(Act.AirShoot);
            for (int i = 0; i < airShots; i++)
            {
                if (IsValid(aim)) FireAt(aim);
                for (float end = Time.time + airShotGap; Time.time < end;)
                {
                    if (IsValid(aim)) Face(aim.transform.position, 360f);
                    yield return null;
                }
            }
            marked.Value = NoTarget;
            yield return LandHere();
            vulnerable.Value = true;
            yield return Hold(Act.Grounded, groundedTime);
            vulnerable.Value = false;
        }

        IEnumerator LandHere()
        {
            if (!Airborne) yield break;
            Vector3 down = bt.position;
            down.y = Floor;
            yield return MoveTo(Act.Land, down, landTime, 2, null);
            SlamRpc(down, 1.5f);
        }

        IEnumerator Bombing()
        {
            vulnerable.Value = false;
            marked.Value = NoTarget;
            Vector3 high = Center + Vector3.up * bombHeight;
            yield return MoveTo(Act.BombRise, high, Mathf.Max(0.6f, Vector3.Distance(bt.position, high) / 10f), 1, null);
            var spots = BombSpots();
            BombsRpc(bt.position, spots[0], spots[1], spots[2], spots[3]);
            yield return Hold(Act.BombWarn, bombWarning);
            yield return Hold(Act.BombDrop, bombFall);
            foreach (var s in spots) HitArea(s, bombRadius, 3f, bombDamage);
            yield return Wait(0.5f);
        }

        // Four marks: one under each player, the rest anywhere on the floor (not on top of each other).
        Vector3[] BombSpots()
        {
            var list = new List<Vector3>();
            foreach (var p in Targets())
            {
                var s = ClampXZ(p.transform.position, 1.5f);
                s.y = Floor;
                list.Add(s);
            }
            for (int tries = 0; list.Count < 4 && tries < 60; tries++)
            {
                var s = ClampXZ(Center + new Vector3(Random.Range(-Half.x, Half.x), 0f, Random.Range(-Half.z, Half.z)), 1.5f);
                s.y = Floor;
                bool free = true;
                foreach (var o in list) if ((o - s).sqrMagnitude < 4.5f * 4.5f) free = false;
                if (free || tries > 50) list.Add(s);
            }
            while (list.Count < 4) list.Add(Center);
            return list.ToArray();
        }

        IEnumerator Dive()
        {
            vulnerable.Value = false;
            var aim = NextTarget();
            if (aim == null) { yield return LandHere(); yield return Hold(Act.Stand, 0.5f); yield break; }
            marked.Value = aim.OwnerClientId;
            SetAct(Act.DiveTrack);
            Vector3 spot = bt.position;
            spot.y = Floor;
            for (float end = Time.time + diveTrack; Time.time < end;)
            {
                if (IsValid(aim)) { spot = ClampXZ(aim.transform.position, groundInset); spot.y = Floor; }
                diveSpot.Value = spot;
                Vector3 back = Flat(bt.position - spot);
                if (back == Vector3.zero) back = Vector3.forward;
                Vector3 want = ClampXZ(spot + back * diveStandOff, airInset);
                want.y = Floor + diveHeight;
                bt.position = Vector3.MoveTowards(bt.position, want, 9f * Time.deltaTime);
                Face(spot, 360f);
                yield return null;
            }
            marked.Value = NoTarget;
            yield return MoveTo(Act.Dive, spot, diveTime, 2, null);
            ImpactRpc(spot);
            if (HitArea(spot, diveRadius, 3f, diveDamage) == 0)
            {
                // Missed: it hits the floor and the jetpack overloads.
                SayRpc("Protagonista: Nie! Plecak się przeciążył!");
                vulnerable.Value = true;
                yield return Hold(Act.Overload, overloadTime);
                vulnerable.Value = false;
            }
            else yield return Hold(Act.DiveRecover, diveRecover);
        }

        // ---------- Phase 4 ----------

        IEnumerator Phase4()
        {
            vulnerable.Value = true;
            marked.Value = NoTarget;
            Vector3 dir = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
            float burst = 0f;
            while (true)
            {
                SetAct(Act.Spin);
                float wanderAt = Time.time + Random.Range(0.7f, 1.5f);
                for (float t = 0f; t < spinTime; t += Time.deltaTime)
                {
                    if (Time.time >= wanderAt) { dir = Quaternion.Euler(0f, Random.Range(-45f, 45f), 0f) * dir; wanderAt = Time.time + Random.Range(0.7f, 1.5f); }
                    dir = SpinStep(dir, spinSpeed);
                    SpinHits();
                    burst += Time.deltaTime;
                    if (burst >= burstEvery) { burst = 0f; BladeBurst(); }
                    yield return null;
                }
                SetAct(Act.SpinSlow);
                for (float t = 0f; t < spinSlow; t += Time.deltaTime)
                {
                    float k = t / spinSlow;
                    dir = SpinStep(dir, spinSpeed * (1f - k));
                    if (k < 0.5f) SpinHits();
                    yield return null;
                }
                if (blades.Count > 0)
                {
                    yield return Hold(Act.PullWindup, pullWindup);
                    SetAct(Act.Pull);
                    yield return Wait(ServerPull() + 0.1f);
                }
            }
        }

        // One step of the whirl: straight on, bouncing off the walls in a new, slightly random direction.
        Vector3 SpinStep(Vector3 dir, float speed)
        {
            Vector3 p = bt.position + dir * (speed * Time.deltaTime);
            Vector3 inset = new Vector3(groundInset, 0f, groundInset);
            Vector3 min = Center - Half + inset, max = Center + Half - inset;
            bool bounced = false;
            if (p.x < min.x) { p.x = min.x; dir.x = Mathf.Abs(dir.x); bounced = true; }
            else if (p.x > max.x) { p.x = max.x; dir.x = -Mathf.Abs(dir.x); bounced = true; }
            if (p.z < min.z) { p.z = min.z; dir.z = Mathf.Abs(dir.z); bounced = true; }
            else if (p.z > max.z) { p.z = max.z; dir.z = -Mathf.Abs(dir.z); bounced = true; }
            if (bounced)
            {
                dir = Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f) * dir;
                if ((p.x <= min.x && dir.x < 0f) || (p.x >= max.x && dir.x > 0f)) dir.x = -dir.x;
                if ((p.z <= min.z && dir.z < 0f) || (p.z >= max.z && dir.z > 0f)) dir.z = -dir.z;
                if (speed > 2f) SparksRpc(p + Vector3.up * 1.8f);
            }
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
            p.y = Floor;
            bt.position = p;
            return dir;
        }

        void SpinHits()
        {
            foreach (var p in Targets())
                if (Near(p, bt.position, bladeReach, 2.6f)) p.ApplyDamage(new DamageInfo(spinDamage, Away(p, bt.position), gameObject));
        }

        void BladeBurst()
        {
            Vector3 o = bt.position + Vector3.up * bladeHeight;
            float offset = Random.Range(0f, 360f);
            for (int i = 0; i < burstBlades; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, offset + i * 360f / burstBlades, 0f) * Vector3.forward;
                Vector3 to = WallHit(o, dir, 40f, out Vector3 point) ? point - dir * 0.3f : o + dir * 40f;
                var b = new Blade { from = o, to = to, last = o, start = Time.time, time = Mathf.Max(0.05f, Vector3.Distance(o, to) / bladeSpeed) };
                blades.Add(b);
                BladeRpc(o, to, b.time);
            }
        }

        // All blades (stuck or still flying) come back to the boss; returns how long the farthest one takes.
        float ServerPull()
        {
            Vector3 at = bt.position + Vector3.up * bladeHeight;
            float longest = 0f;
            foreach (var b in blades)
            {
                Vector3 now = b.arrived ? b.to : Vector3.Lerp(b.from, b.to, Mathf.Clamp01((Time.time - b.start) / b.time));
                b.from = now; b.last = now; b.to = at; b.start = Time.time;
                b.time = Mathf.Max(0.05f, Vector3.Distance(now, at) / pullSpeed);
                b.pulling = true; b.arrived = false;
                b.hit.Clear();
                longest = Mathf.Max(longest, b.time);
            }
            PullRpc(at);
            return longest;
        }

        void TickBlades()
        {
            for (int i = blades.Count - 1; i >= 0; i--)
            {
                var b = blades[i];
                if (b.arrived) continue;
                float k = Mathf.Clamp01((Time.time - b.start) / b.time);
                Vector3 at = Vector3.Lerp(b.from, b.to, k);
                foreach (var p in Targets())
                {
                    if (b.hit.Contains(p.OwnerClientId)) continue;
                    Vector3 feet = p.transform.position;
                    bool touched = false;
                    for (int s = 1; s <= 3 && !touched; s++)   // a few points along this frame's flight, so fast blades don't skip a player
                        touched = SegmentDistance(Vector3.Lerp(b.last, at, s / 3f), feet + Vector3.up * 0.3f, feet + Vector3.up * 1.7f) <= bladeHitRadius;
                    if (!touched) continue;
                    b.hit.Add(p.OwnerClientId);
                    p.ApplyDamage(new DamageInfo(bladeDamage, Flat(b.to - b.from), gameObject));
                }
                b.last = at;
                if (k < 1f) continue;
                if (b.pulling) blades.RemoveAt(i);
                else b.arrived = true;
            }
        }

        // ---------- Movement helpers (server) ----------

        void SetAct(Act a)
        {
            act.Value = (byte)a;
            actAt.Value = Now;
        }

        IEnumerator Hold(Act a, float seconds)
        {
            SetAct(a);
            yield return Wait(seconds);
        }

        static IEnumerator Wait(float seconds)
        {
            for (float end = Time.time + seconds; Time.time < end;) yield return null;
        }

        // ease: 0 even, 1 slowing down at the end, 2 speeding up (falling, diving).
        IEnumerator MoveTo(Act a, Vector3 to, float time, int ease, PlayerHealth face)
        {
            SetAct(a);
            Vector3 from = bt.position;
            for (float t = 0f; t < time; t += Time.deltaTime)
            {
                float k = t / time;
                k = ease == 1 ? 1f - (1f - k) * (1f - k) : ease == 2 ? k * k : k;
                bt.position = Vector3.Lerp(from, to, k);
                if (IsValid(face)) Face(face.transform.position, 360f);
                yield return null;
            }
            bt.position = to;
        }

        IEnumerator FallToGround()
        {
            SetAct(Act.Fall);
            float v = 0f;
            while (bt.position.y > Floor + 0.01f)
            {
                v += 25f * Time.deltaTime;
                var p = ClampXZ(bt.position, groundInset);
                p.y = Mathf.Max(Floor, p.y - v * Time.deltaTime);
                bt.position = p;
                yield return null;
            }
            SlamRpc(bt.position, 2f);
        }

        void Face(Vector3 point, float degreesPerSecond)
        {
            Vector3 d = point - bt.position;
            d.y = 0f;
            if (d.sqrMagnitude < 0.01f) return;
            bt.rotation = Quaternion.RotateTowards(bt.rotation, Quaternion.LookRotation(d), degreesPerSecond * Time.deltaTime);
        }

        int HitArea(Vector3 at, float radius, float height, int damage)
        {
            int n = 0;
            foreach (var p in Targets())
                if (Near(p, at, radius, height) && p.ApplyDamage(new DamageInfo(damage, Away(p, at), gameObject))) n++;
            return n;
        }

        Vector3 ClampXZ(Vector3 p, float inset)
        {
            Vector3 c = Center, h = Half;
            p.x = Mathf.Clamp(p.x, c.x - h.x + inset, c.x + h.x - inset);
            p.z = Mathf.Clamp(p.z, c.z - h.z + inset, c.z + h.z - inset);
            return p;
        }

        // How far the boss can go from p along dir before it gets within inset of a wall.
        float DistanceToWall(Vector3 p, Vector3 dir, float inset)
        {
            Vector3 c = Center;
            float hx = Half.x - inset, hz = Half.z - inset, best = 60f;
            if (dir.x > 0.001f) best = Mathf.Min(best, (c.x + hx - p.x) / dir.x);
            else if (dir.x < -0.001f) best = Mathf.Min(best, (c.x - hx - p.x) / dir.x);
            if (dir.z > 0.001f) best = Mathf.Min(best, (c.z + hz - p.z) / dir.z);
            else if (dir.z < -0.001f) best = Mathf.Min(best, (c.z - hz - p.z) / dir.z);
            return Mathf.Max(0f, best);
        }

        Vector3 PlayersMiddle()
        {
            var list = Targets();
            if (list.Count == 0) return Center;
            Vector3 sum = Vector3.zero;
            foreach (var p in list) sum += p.transform.position;
            return sum / list.Count;
        }

        List<PlayerHealth> Targets()
        {
            var list = new List<PlayerHealth>();
            foreach (var p in PlayerHealth.All) if (IsValid(p)) list.Add(p);
            return list;
        }

        bool IsValid(PlayerHealth p) => p != null && p.IsSpawned && p.IsAlive && Contains(p.transform.position);

        // The players take turns being the target.
        PlayerHealth NextTarget()
        {
            var list = Targets();
            if (list.Count == 0) return null;
            list.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));
            targetTurn++;
            return list[targetTurn % list.Count];
        }

        PlayerHealth RandomTarget()
        {
            var list = Targets();
            return list.Count > 0 ? list[Random.Range(0, list.Count)] : null;
        }

        static PlayerHealth Player(ulong client)
        {
            foreach (var p in PlayerHealth.All) if (p != null && p.OwnerClientId == client) return p;
            return null;
        }

        public bool Contains(Vector3 point)
        {
            Vector3 d = point - Center;
            return Mathf.Abs(d.x) <= Half.x + 0.5f && d.y >= -1f && d.y <= areaSize.y && Mathf.Abs(d.z) <= Half.z + 0.5f;
        }

        bool IsBossCollider(Collider c) => c != null && bt != null && c.transform.IsChildOf(bt);

        /// <summary>The first wall (not a player, not the boss) along a line, as a visual or a blade would meet it.</summary>
        public bool WallHit(Vector3 from, Vector3 dir, float distance, out Vector3 point)
        {
            point = from + dir * distance;
            var hits = Physics.RaycastAll(from, dir, distance, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            foreach (var h in hits)
            {
                if (h.distance >= best || IsBossCollider(h.collider) || h.collider.GetComponentInParent<PlayerHealth>() != null) continue;
                best = h.distance; point = h.point; found = true;
            }
            return found;
        }

        static bool Near(PlayerHealth p, Vector3 at, float radius, float height)
        {
            Vector3 d = p.transform.position - at;
            if (d.y < -0.5f || d.y > height) return false;
            d.y = 0f;
            return d.magnitude <= radius;
        }

        static Vector3 Away(PlayerHealth p, Vector3 from)
        {
            Vector3 d = Flat(p.transform.position - from);
            return d != Vector3.zero ? d : Vector3.forward;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.zero;
        }

        static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector3.Distance(p, a + ab * t);
        }

        // ---------- RPCs (every machine) ----------

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void SayRpc(string text) { message = text; messageUntil = Time.time + 5f; }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void ShotRpc(int id, Vector3 from, Vector3 dir) =>
            shotViews[id] = FinalBossShot.Spawn(this, fxRoot, id, from, dir, shotSpeed, shotRadius, shotLife, shotMaterial);

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void ShotEndRpc(int id, Vector3 at)
        {
            if (shotViews.TryGetValue(id, out var s) && s != null) s.End(at);
            shotViews.Remove(id);
        }

        public void ForgetShot(int id) => shotViews.Remove(id);

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void MarkerRpc(Vector3 at, float radius, float life) => FinalBossMarker.Spawn(fxRoot, at, radius, life, warningMaterial);

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void SlamRpc(Vector3 at, float radius)
        {
            SlamShockwave.Spawn(at, radius, waveMaterial);
            ShotEffects.Sparks(at + Vector3.up * 0.3f, waveMaterial, 10);
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void WaveRpc(Vector3 at) =>
            FinalBossWave.Spawn(fxRoot, at, waveSpeed, waveMaxRadius, waveHeight, Center - Half, Center + Half, waveMaterial);

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void ChargeLineRpc(Vector3 from, Vector3 dir, float length, float life) =>
            FinalBossLine.Spawn(fxRoot, new Vector3(from.x, Floor, from.z), dir, length + 1.5f, chargeRadius * 1.6f, life, warningMaterial);

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void BombsRpc(Vector3 from, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            foreach (var spot in new[] { a, b, c, d })
            {
                FinalBossMarker.Spawn(fxRoot, spot, bombRadius, bombWarning + bombFall, warningMaterial);
                FinalBossBomb.Spawn(fxRoot, from, spot, bombWarning, bombFall, bombRadius, bombMaterial, fireMaterial, coreMaterial);
            }
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void ImpactRpc(Vector3 at)
        {
            ExplosionEffect.Spawn(at + Vector3.up * 0.5f, diveRadius, fireMaterial, coreMaterial, 0.6f);
            SlamShockwave.Spawn(at, diveRadius, waveMaterial);
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void SparksRpc(Vector3 at) => ShotEffects.Sparks(at, sparkMaterial, 12);

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void BladeRpc(Vector3 from, Vector3 to, float time)
        {
            bladeViews.RemoveAll(b => b == null);
            bladeViews.Add(FinalBossBlade.Spawn(fxRoot, from, to, time, bladeMaterial, hiltMaterial));
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void PullRpc(Vector3 at)
        {
            foreach (var b in bladeViews) if (b != null) b.Pull(at, pullSpeed);
            bladeViews.Clear();   // they're on their way back and vanish on arrival
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void ClearRpc() => ClearViews();

        void ClearViews()
        {
            if (fxRoot != null) for (int i = fxRoot.childCount - 1; i >= 0; i--) Destroy(fxRoot.GetChild(i).gameObject);
            shotViews.Clear();
            bladeViews.Clear();
        }

        // ---------- Looks (every machine) ----------

        static bool IsAirAct(Act a) => a == Act.Fly || a == Act.Rise || a == Act.AirAim || a == Act.AirShoot || a == Act.Land
            || a == Act.BombRise || a == Act.BombWarn || a == Act.BombDrop || a == Act.DiveTrack || a == Act.Dive;

        void UpdateLooks(float dt)
        {
            var s = CurrentStage;
            var a = CurrentAct;
            bool fighting = s >= Stage.Phase1 && s <= Stage.Phase4;
            float since = (float)(Now - actAt.Value);

            if (fighting && hideOnStart != null)
                foreach (var g in hideOnStart) if (g != null && g.activeSelf) g.SetActive(false);

            if (bt != null)
            {
                SetShown(fighting || (s == Stage.Defeated && !bodyHidden));
                body.SetStatusTint(new Color(0.35f, 0.45f, 0.75f), fighting && !vulnerable.Value ? 0.4f + 0.15f * Mathf.Sin(Time.time * 6f) : 0f);
                Vector3 v = dt > 0f ? (bt.position - lastBodyPos) / dt : Vector3.zero;
                lastBodyPos = bt.position;
                velocity = Vector3.Lerp(velocity, v, 1f - Mathf.Exp(-8f * dt));
                AnimateVisual(a, since, dt);
                UpdateJetpack(s, a, fighting);
                if (gun != null) { bool want = fighting && (s == Stage.Phase1 || s == Stage.Phase3); if (gun.activeSelf != want) gun.SetActive(want); }
                if (swords != null) { bool want = fighting && (s == Stage.Phase4 || a == Act.DrawSwords); if (swords.activeSelf != want) swords.SetActive(want); }
            }

            // A mark over the head of the player the boss is after.
            bool marking = fighting && (a == Act.Fly || a == Act.AirAim || a == Act.AirShoot || a == Act.DiveTrack || a == Act.LeapWindup || a == Act.ChargeWindup);
            var who = marking && marked.Value != NoTarget ? Player(marked.Value) : null;
            if (targetMark == null) targetMark = FinalBossFx.Part(PrimitiveType.Cube, "Boss Target Mark", transform, glowMaterial);
            if (targetMark.gameObject.activeSelf != (who != null)) targetMark.gameObject.SetActive(who != null);
            if (who != null)
            {
                targetMark.SetPositionAndRotation(who.transform.position + Vector3.up * (2.7f + 0.15f * Mathf.Sin(Time.time * 5f)),
                    Quaternion.Euler(0f, Time.time * 200f, 0f) * Quaternion.Euler(0f, 0f, 45f));
                targetMark.localScale = new Vector3(0.42f, 0.42f, 0.12f);
            }

            // The dive's mark: follows the target, then stays where it will hit.
            bool diving = fighting && (a == Act.DiveTrack || a == Act.Dive);
            if (diving && diveMark == null) diveMark = FinalBossMarker.Spawn(transform, diveSpot.Value, diveRadius, 0f, warningMaterial);
            if (!diving && diveMark != null) { Destroy(diveMark.gameObject); diveMark = null; }
            if (diveMark != null)
            {
                Vector3 at = diveMark.transform.position - Vector3.up * 0.05f;
                diveMark.Place(Vector3.Lerp(at, diveSpot.Value, 1f - Mathf.Exp(-20f * dt)));
            }

            if (s == Stage.Defeated && !defeatApplied)
            {
                defeatApplied = true;
                ClearViews();
                onDefeated.Invoke();
            }
        }

        void SetShown(bool shown)
        {
            if (shownNow == shown) return;
            shownNow = shown;
            if (bodyRenderers != null) foreach (var r in bodyRenderers) if (r != null) r.enabled = shown;
            if (bodyCollider != null) bodyCollider.enabled = shown;
            if (bodyTarget != null) bodyTarget.enabled = shown;
        }

        void AnimateVisual(Act a, float since, float dt)
        {
            if (visual == null) return;
            float rate = a == Act.Spin ? 900f : a == Act.SpinSlow ? Mathf.Lerp(900f, 0f, Mathf.Clamp01(since / spinSlow)) : 0f;
            if (rate > 1f) spinAngle = Mathf.Repeat(spinAngle + rate * dt, 360f);
            else spinAngle = Mathf.MoveTowardsAngle(spinAngle, 0f, 540f * dt);

            // Lean with the speed, bob while flying.
            Vector3 local = bt.InverseTransformDirection(velocity);
            float pitch = Mathf.Clamp(local.z * 1.6f, -18f, 25f), roll = Mathf.Clamp(-local.x * 1.6f, -18f, 18f);
            float bob = IsAirAct(a) ? Mathf.Sin(Time.time * 3.3f) * 0.25f : 0f;
            switch (a)
            {
                case Act.ChargeWindup: pitch = -8f; roll = 0f; break;
                case Act.Charge: pitch = 28f; roll = 0f; break;
                case Act.ChargeStun: pitch = -6f; roll = Mathf.Sin(Time.time * 9f) * 8f; break;
                case Act.LeapWindup: bob = -0.35f; pitch = 10f; break;
                case Act.Dive: pitch = 55f; break;
                case Act.Overload: pitch = 22f + Mathf.Sin(Time.time * 23f) * 3f; roll = Mathf.Sin(Time.time * 17f) * 4f; bob = -0.3f; break;
                case Act.Emergency: roll = Mathf.Sin(Time.time * 20f) * 3f; break;
                case Act.Spin: case Act.SpinSlow: case Act.PullWindup: case Act.Pull: pitch = 0f; roll = 0f; break;
            }
            visual.localPosition = Vector3.up * bob;
            visual.localRotation = Quaternion.Euler(pitch, spinAngle, roll);
        }

        void UpdateJetpack(Stage s, Act a, bool fighting)
        {
            // Phase 1 flies on full flames; phase 3 (and switching it on) sputters; phase 2 and 4 don't fly.
            bool on = fighting && ((IsAirAct(a) && (s == Stage.Phase1 || s == Stage.Phase3)) || a == Act.Emergency);
            bool sputter = s == Stage.Phase3 || a == Act.Emergency;
            bool show = on && (!sputter || Mathf.PerlinNoise(Time.time * 9f, 0.3f) > 0.3f);
            if (show != flamesOn)
            {
                flamesOn = show;
                if (thrusters != null) foreach (var ps in thrusters) if (ps != null) { if (show) ps.Play(); else ps.Stop(); }
                if (flames != null) foreach (var f in flames) if (f != null) f.gameObject.SetActive(show);
            }
            if (show && flames != null)
                for (int i = 0; i < flames.Length; i++)
                    if (flames[i] != null) flames[i].localScale = Vector3.Scale(flameScale[i], new Vector3(1f, 0.8f + 0.5f * Mathf.PerlinNoise(Time.time * 14f, i), 1f));

            // A broken jetpack smokes; an overloaded one throws sparks too.
            bool smoke = fighting && (s == Stage.Phase2 || a == Act.Fall || a == Act.Overload || a == Act.Emergency);
            if (smoke && nozzles != null && Time.time >= nextPuff)
            {
                nextPuff = Time.time + 0.12f;
                foreach (var n in nozzles) if (n != null) SmokePuff.Spawn(n.position + Random.insideUnitSphere * 0.15f, smokeMaterial);
            }
            if (a == Act.Overload && nozzles != null && nozzles.Length > 0 && Time.time >= nextSpark)
            {
                nextSpark = Time.time + 0.15f;
                var n = nozzles[Random.Range(0, nozzles.Length)];
                if (n != null) ShotEffects.Sparks(n.position + Vector3.up * 0.4f, sparkMaterial, 5);
            }
        }

        // The local player's checkpoint once the fight is on.
        void LocalTick()
        {
            if (checkpointSet || CurrentStage < Stage.Phase1 || respawnPoints == null || respawnPoints.Length == 0) return;
            checkpointSet = true;
            PlayerSpawnPoints.SetCheckpoint(respawnPoints);
        }

        // ---------- Health bar and messages ----------

        void OnGUI()
        {
            if (!IsSpawned || CutsceneSystem.HidesGameplayUI) return;
            var me = PlayerHealth.Local;
            if (me == null) return;
            Vector3 d = me.transform.position - Center;
            bool near = Mathf.Abs(d.x) < Half.x + 15f && Mathf.Abs(d.z) < Half.z + 15f && d.y > -5f && d.y < areaSize.y + 10f;
            if (!near) return;
            nameStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, richText = true };
            smallStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, richText = true };
            boxStyle ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, richText = true, wordWrap = true };
            nameStyle.fontSize = Mathf.Max(16, Screen.height / 36);
            smallStyle.fontSize = Mathf.Max(12, Screen.height / 58);
            boxStyle.fontSize = Mathf.Max(14, Screen.height / 50);

            if (Time.time < messageUntil && !string.IsNullOrEmpty(message))
                GUI.Box(new Rect(Screen.width * 0.5f - 440f, Screen.height - 250f, 880f, 52f), message, boxStyle);

            var s = CurrentStage;
            if (s < Stage.Phase1 || s > Stage.Phase4) return;
            float width = Mathf.Min(Screen.width * 0.6f, 900f), height = Mathf.Max(14f, Screen.height * 0.022f);
            float x = (Screen.width - width) * 0.5f, y = Screen.height * 0.14f;   // under the level HUD at the top
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(x - 4f, y - nameStyle.fontSize * 1.5f - 4f, width + 8f, height + nameStyle.fontSize * 2.9f + 8f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(x, y - nameStyle.fontSize * 1.5f, width, nameStyle.fontSize * 1.4f), $"{bossName}   {Health} / {maxHealth}   FAZA {(int)s}/4", nameStyle);
            bool open = vulnerable.Value && !transition.Value;
            GUI.color = new Color(0.1f, 0.05f, 0.12f, 0.95f);
            GUI.DrawTexture(new Rect(x, y, width, height), Texture2D.whiteTexture);
            GUI.color = open ? new Color(0.85f, 0.25f, 0.95f) : new Color(0.4f, 0.35f, 0.5f);
            GUI.DrawTexture(new Rect(x, y, width * Mathf.Clamp01((float)Health / maxHealth), height), Texture2D.whiteTexture);
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            foreach (int t in thresholds) GUI.DrawTexture(new Rect(x + width * t / maxHealth - 1f, y, 2f, height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            string state = transition.Value ? "<color=#bbbbff>Zmienia fazę, nie da się go zranić.</color>"
                : !open ? "<color=#bbbbff>Niewrażliwy.</color>"
                : s == Stage.Phase3 ? "<color=#ffdd55>PODATNY! Atakujcie!</color>" : null;
            string hint = Hint(CurrentAct, marked.Value == me.OwnerClientId);
            string line = state != null && hint != null ? state + "   " + hint : state ?? hint;
            if (line != null) GUI.Label(new Rect(x - 80f, y + height + 4f, width + 160f, smallStyle.fontSize * 1.6f), line, smallStyle);
        }

        static string Hint(Act a, bool me) => a switch
        {
            Act.Fly => me ? "Celuje w ciebie! Ruszaj się." : "Strzela do drugiego gracza, atakuj!",
            Act.LeapWindup or Act.Leap => "Skok! Uciekaj z czerwonego pola, potem przeskocz falę uderzeniową.",
            Act.SlamRecover => "Przeskocz falę uderzeniową!",
            Act.ChargeWindup => me ? "Szarża na ciebie! Zejdź z czerwonej linii." : "Szarża! Zejdź z czerwonej linii.",
            Act.Charge => "Szarża!",
            Act.ChargeStun => "Ogłuszony po szarży, atakuj!",
            Act.Rise or Act.AirAim or Act.AirShoot => me ? "Celuje w ciebie z powietrza!" : "W powietrzu jest niewrażliwy.",
            Act.Grounded => "Wylądował!",
            Act.BombRise or Act.BombWarn or Act.BombDrop => "Bombardowanie! Uciekajcie z czerwonych pól.",
            Act.DiveTrack or Act.Dive => me ? "Nurkuje na ciebie! Zrób unik tuż przed uderzeniem." : "Nurkuje na drugiego gracza!",
            Act.Overload => "Plecak przeciążony!",
            Act.Spin or Act.SpinSlow => "Wir ostrzy! Trzymajcie dystans i przeskakujcie lecące ostrza.",
            Act.PullWindup or Act.Pull => "Przyciąga ostrza! Zejdźcie z ich drogi.",
            _ => null,
        };

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.85f, 0.3f, 1f, 0.8f);
            Gizmos.DrawWireCube(transform.position + Vector3.up * (areaSize.y * 0.5f), areaSize);
        }
    }
}
