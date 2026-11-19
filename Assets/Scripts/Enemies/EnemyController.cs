using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace CoopGame
{
    /// <summary>Which side an enemy fights for. Enemies of different factions fight each other (and the players).</summary>
    public enum EnemyFaction : byte
    {
        /// <summary>The protagonist's army: every enemy by default.</summary>
        Protagonist,
        /// <summary>The island's police (blue, with a cap).</summary>
        Police,
    }

    /// <summary>
    /// Shared enemy brain (server only): picks the nearest living player, walks towards them, and hands
    /// control to an <see cref="EnemyAttack"/> when one is ready. Getting hit by the wrench cancels the
    /// current attack, knocks the enemy back and stuns it. Movement replicates via a server-authoritative
    /// NetworkTransform. Ranged enemies (<see cref="retreatDistance"/>) keep their distance instead: they stop well
    /// short of the target and back away, still facing it, when it comes too close.
    /// Charmed by an <see cref="AllyRobot"/>'s spell, it ignores the players and goes after that robot (wherever
    /// it is) until the robot is gone; every client sees a spinning halo over its head meanwhile.
    /// Factions (<see cref="EnemyFaction"/>): an enemy also goes after the nearest enemy of another faction, and its
    /// attacks hurt those (never its own side). Police enemies are blue and wear a cap on every client.
    /// Frozen (the freeze ball), it stands locked in ice for a moment — no moving, turning or attacking, hits don't
    /// knock it back — then thaws but stays slowed for a while: walking, turning and attacks all run at a fraction
    /// of their speed.
    /// </summary>
    /// <remarks>
    /// Pathfinding: a <see cref="NavMeshAgent"/> (server only) plans the way to the target around walls and
    /// obstacles on the level's baked NavMesh; the enemy still moves itself with its CharacterController along that
    /// path, so knockback, gravity, attack lunges and leaps keep working. Off the NavMesh (falling, flying in a
    /// mortar pod, a level with no NavMesh baked) it walks straight at the target as before and gets back on the
    /// mesh as soon as it lands near it.
    /// </remarks>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(Damageable))]
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyController : NetworkBehaviour
    {
        const ulong NotCharmed = ulong.MaxValue;

        static readonly List<EnemyController> s_All = new();
        /// <summary>Every spawned enemy.</summary>
        public static IReadOnlyList<EnemyController> All => s_All;

        [Header("Chase")]
        [SerializeField] float moveSpeed = 3f;
        [SerializeField] float turnSpeed = 360f;
        [SerializeField] float aggroRange = 10f;
        [Tooltip("Stops walking this close to the target and waits for an attack to be ready.")]
        [SerializeField] float stopDistance = 3.5f;
        [Tooltip("Ranged enemies: backs away (still facing the target) when the target gets closer than this. 0 = never.")]
        [SerializeField] float retreatDistance;
        [SerializeField] float retreatSpeed = 3f;
        [SerializeField] float gravity = 30f;

        [Header("Hit Reaction")]
        [SerializeField] float knockbackSpeed = 9f;
        [SerializeField] float knockbackUpSpeed = 4f;
        [SerializeField] float knockbackDrag = 20f;
        [SerializeField] float stunTime = 0.45f;

        [Header("Charm")]
        [Tooltip("Halo shown over a charmed enemy.")]
        [SerializeField] Material charmMaterial;
        [SerializeField] float haloHeight = 2.1f;

        [Header("Freeze")]
        [Tooltip("Ice shards around a frozen enemy and the frost falling off a slowed one.")]
        [SerializeField] Material iceMaterial;
        [Tooltip("The body is tinted towards this while frozen or slowed.")]
        [SerializeField] Color iceTint = new Color(0.65f, 0.9f, 1f);

        [Header("Faction")]
        [Tooltip("Body colour of a police enemy.")]
        [SerializeField] Color policeColor = new Color(0.22f, 0.38f, 0.95f);
        [SerializeField] Color policeCapColor = new Color(0.08f, 0.1f, 0.25f);

        /// <summary>
        /// Server: hits between enemies of different factions are scaled by this (enemy weapons are tuned for
        /// players' small health; a fight among them should not take forever).
        /// </summary>
        public static float InfightDamageScale { get; set; } = 1f;

        // NetworkObjectId of the robot that charmed this enemy, NotCharmed otherwise.
        readonly NetworkVariable<ulong> charmer = new(NotCharmed);
        readonly NetworkVariable<byte> faction = new();
        // Server time until which it is frozen solid, then slowed.
        readonly NetworkVariable<double> frozenUntil = new();
        readonly NetworkVariable<double> slowedUntil = new();

        [Header("Pathfinding")]
        [Tooltip("Keeps closing in until it can see the target (a shooter behind a corner). Off for enemies that fire over walls (the mortar).")]
        [SerializeField] bool needsLineOfSight = true;
        [Tooltip("How often (seconds) the path to the target is worked out again.")]
        [SerializeField] float repathInterval = 0.25f;
        [Tooltip("Gets back on the NavMesh if it's within this distance of it (after a fall, a knockback, a landing).")]
        [SerializeField] float navMeshSnapDistance = 1.5f;

        CharacterController motor;
        Damageable damageable;
        NavMeshAgent agent;
        float nextRepathTime;
        Vector3 lastDestination = new Vector3(float.MaxValue, 0f, 0f);
        EnemyAttack[] attacks;
        EnemyAttack currentAttack;
        Vector3 knockback;
        float verticalVelocity;
        float stunnedUntil;
        AllyRobot charmedBy;
        AllyRobot reservedBy;
        Transform halo;
        float slowFactor = 1f;
        Transform iceShell;
        float nextFrostTime;
        Transform policeCap;
        EnemyAsTarget asTarget;

        public CharacterController Motor => motor;
        public EnemyFaction Faction => (EnemyFaction)faction.Value;
        /// <summary>Server: this enemy as something other enemies can chase and hit.</summary>
        public IEnemyTarget AsTarget => asTarget ??= new EnemyAsTarget(this);
        public Damageable Damageable => damageable;
        /// <summary>Server: how far away it notices players (an arena sets it so its enemies hunt the whole arena).</summary>
        public float AggroRange { get => aggroRange; set => aggroRange = value; }
        public bool IsAlive => IsSpawned && !damageable.IsDead;
        public bool IsCharmed => charmer.Value != NotCharmed;
        /// <summary>Locked in ice (any client).</summary>
        public bool IsFrozen => IsSpawned && NetworkManager.ServerTime.Time < frozenUntil.Value;
        /// <summary>Thawed but still slowed (any client).</summary>
        public bool IsSlowed => IsSpawned && !IsFrozen && NetworkManager.ServerTime.Time < slowedUntil.Value;

        void Awake()
        {
            motor = GetComponent<CharacterController>();
            damageable = GetComponent<Damageable>();
            attacks = GetComponents<EnemyAttack>();
            agent = GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                // The agent only plans; the CharacterController does the moving.
                agent.updatePosition = false;
                agent.updateRotation = false;
                agent.autoBraking = false;
                agent.enabled = false; // switched on (server only) once it stands on the NavMesh
            }
        }

        public override void OnNetworkSpawn()
        {
            s_All.Add(this);
            if (IsServer) damageable.Damaged += OnDamaged;
            charmer.OnValueChanged += OnCharmChanged;
            OnCharmChanged(NotCharmed, charmer.Value);
            faction.OnValueChanged += OnFactionChanged;
            OnFactionChanged(0, faction.Value);
        }

        public override void OnNetworkDespawn()
        {
            s_All.Remove(this);
            damageable.Damaged -= OnDamaged;
            charmer.OnValueChanged -= OnCharmChanged;
            faction.OnValueChanged -= OnFactionChanged;
            if (halo != null) Destroy(halo.gameObject);
            if (policeCap != null) Destroy(policeCap.gameObject);
            if (iceShell != null) Destroy(iceShell.gameObject);
            damageable.SetStatusTint(iceTint, 0f);
        }

        // ---------- Factions ----------

        /// <summary>Server: which side it fights for (set right after spawning it).</summary>
        public void ServerSetFaction(EnemyFaction side)
        {
            if (IsServer) faction.Value = (byte)side;
        }

        /// <summary>Server: whether it should chase and may hurt <paramref name="other"/> (another faction; a charmed enemy only has eyes for its robot).</summary>
        public bool IsHostileTo(EnemyController other) =>
            other != null && other != this && other.IsAlive && other.faction.Value != faction.Value && !IsCharmed && !other.IsCharmed;

        /// <summary>
        /// Server: what this enemy's attack hurts when it touches <paramref name="collider"/>: a player or ally as
        /// before, or an enemy of another faction. Null for anything else (its own side included).
        /// </summary>
        public IEnemyTarget VictimOf(Collider collider)
        {
            var target = collider.GetComponentInParent<IEnemyTarget>();
            if (target != null) return target;
            var enemy = collider.GetComponentInParent<EnemyController>();
            return enemy != null && IsHostileTo(enemy) ? enemy.AsTarget : null;
        }

        void OnFactionChanged(byte previous, byte current)
        {
            if ((EnemyFaction)current != EnemyFaction.Police) return;
            damageable.SetBaseColor(policeColor);
            if (policeCap == null) policeCap = BuildPoliceCap();
        }

        // A flat cap with a badge on top of the body, so police enemies read as police.
        Transform BuildPoliceCap()
        {
            Bounds bounds = default;
            bool any = false;
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer) continue;
                if (!any) bounds = r.bounds; else bounds.Encapsulate(r.bounds);
                any = true;
            }
            float top = any ? bounds.max.y - transform.position.y : 2f;
            float width = any ? Mathf.Clamp(Mathf.Min(bounds.size.x, bounds.size.z), 0.4f, 1.4f) : 0.8f;

            var source = GetComponentInChildren<Renderer>();
            var capMaterial = new Material(source.sharedMaterial) { color = policeCapColor };
            var badgeMaterial = new Material(source.sharedMaterial) { color = new Color(1f, 0.8f, 0.25f) };
            var root = new GameObject("PoliceCap").transform;
            root.SetParent(transform, false);
            root.localPosition = Vector3.up * (top - 0.05f);
            void Part(Vector3 position, Vector3 size, Material material)
            {
                var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(part.GetComponent<Collider>());
                part.GetComponent<Renderer>().sharedMaterial = material;
                part.transform.SetParent(root, false);
                part.transform.localPosition = position;
                part.transform.localScale = size;
            }
            Part(new Vector3(0f, 0.03f, 0.08f * width), new Vector3(width * 1.05f, 0.07f, width * 1.2f), capMaterial);
            Part(new Vector3(0f, 0.18f, 0f), new Vector3(width * 0.8f, 0.26f, width * 0.8f), capMaterial);
            Part(new Vector3(0f, 0.2f, width * 0.41f), new Vector3(width * 0.22f, 0.14f, 0.04f), badgeMaterial);
            return root;
        }

        /// <summary>Another enemy as a target for enemies of other factions (server).</summary>
        sealed class EnemyAsTarget : IEnemyTarget
        {
            readonly EnemyController enemy;
            public EnemyAsTarget(EnemyController enemy) => this.enemy = enemy;
            public Transform transform => enemy.transform;
            public bool IsAlive => enemy != null && enemy.IsAlive;

            public bool ApplyDamage(in DamageInfo damage)
            {
                if (!IsAlive) return false;
                int amount = Mathf.Max(1, Mathf.RoundToInt(damage.Amount * InfightDamageScale));
                Vector3 push = damage.Direction.sqrMagnitude > 0.0001f ? damage.Direction.normalized : enemy.transform.forward;
                enemy.damageable.ApplyDamage(amount, push);
                return true;
            }
        }

        // ---------- Charm (server API, used by AllyRobot) ----------

        /// <summary>Server: free to be picked by a robot (not charmed, and no robot's spell already on its way).</summary>
        public bool CanBeCharmed => IsAlive && !IsCharmed && reservedBy == null;

        /// <summary>Server: a robot cast its spell at this enemy; nobody else may pick it while the spell flies.</summary>
        public void ServerReserve(AllyRobot robot) => reservedBy = robot;

        /// <summary>Server: the spell landed. From now on this enemy only goes after <paramref name="robot"/>.</summary>
        public void ServerCharm(AllyRobot robot)
        {
            if (!IsServer || !IsAlive) return;
            reservedBy = null;
            charmedBy = robot;
            charmer.Value = robot.NetworkObjectId;
            // Drop whatever it was doing to a player and turn on the robot.
            if (currentAttack != null)
            {
                currentAttack.Cancel();
                currentAttack = null;
            }
        }

        /// <summary>Server: the robot is gone (or let go); back to hunting players.</summary>
        public void ServerReleaseCharm(AllyRobot robot)
        {
            if (!IsServer) return;
            if (reservedBy == robot) reservedBy = null;
            if (charmedBy != robot) return;
            charmedBy = null;
            charmer.Value = NotCharmed;
            if (currentAttack != null)
            {
                currentAttack.Cancel();
                currentAttack = null;
            }
        }

        // ---------- Freeze (server API, used by the freeze ball) ----------

        /// <summary>
        /// Server: locked in ice for <paramref name="freezeTime"/> seconds, then slowed to <paramref name="factor"/>
        /// of its speed for <paramref name="slowTime"/> more. Freezing again starts it over.
        /// </summary>
        public void ServerFreeze(float freezeTime, float slowTime, float factor)
        {
            if (!IsServer || !IsAlive) return;
            double now = NetworkManager.ServerTime.Time;
            frozenUntil.Value = now + freezeTime;
            slowedUntil.Value = now + freezeTime + slowTime;
            slowFactor = factor;
            knockback = Vector3.zero;
            if (currentAttack != null)
            {
                currentAttack.Cancel();
                currentAttack = null;
            }
        }

        /// <summary>Server: how fast it moves, turns and attacks right now (0 frozen, the slow factor while slowed).</summary>
        public float SpeedFactor => IsFrozen ? 0f : IsSlowed ? slowFactor : 1f;

        /// <summary>
        /// Server: who it is after this frame — null while it has nobody in range, is stunned or frozen. Weapons
        /// that don't take over its movement (an <see cref="EnemyShooter"/>) aim at this.
        /// </summary>
        public IEnemyTarget CurrentTarget { get; private set; }

        void OnDamaged(Vector3 direction)
        {
            if (currentAttack != null)
            {
                currentAttack.Cancel();
                currentAttack = null;
            }
            if (IsFrozen) return; // a block of ice doesn't get knocked about
            knockback = direction * knockbackSpeed;
            verticalVelocity = knockbackUpSpeed;
            stunnedUntil = Time.time + stunTime;
        }

        void Update()
        {
            if (halo != null && halo.gameObject.activeSelf)
            {
                halo.localRotation = Quaternion.Euler(0f, Time.time * 180f, 0f);
                halo.localPosition = Vector3.up * (haloHeight + 0.08f * Mathf.Sin(Time.time * 4f));
            }

            if (IsSpawned) UpdateFreezeVisuals();

            if (!IsServer || !IsSpawned) return;
            float dt = Time.deltaTime;
            // Frozen or slowed, its own actions run slower (or not at all); falling and knockback don't.
            float speed = SpeedFactor;

            // A running attack owns the enemy's movement.
            if (currentAttack != null)
            {
                currentAttack.Tick(dt * speed);
                // The tick can end it from outside (hit, killed, despawned): then it's already cleared.
                if (currentAttack != null && currentAttack.IsRunning) return;
                currentAttack = null;
                verticalVelocity = 0f;
            }

            Vector3 chase = Vector3.zero;
            IEnemyTarget target = Time.time >= stunnedUntil && speed > 0f ? FindTarget() : null;
            CurrentTarget = target;
            bool onMesh = UpdateAgent();
            if (target != null)
            {
                Vector3 toTarget = Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up);
                float distance = toTarget.magnitude;

                // Attacks decide for themselves what they need (a mortar lobs over walls); they turn to the target.
                if (motor.isGrounded && TryStartAttack(target, distance)) return;
                if (distance > stopDistance || (needsLineOfSight && distance > 1.5f && !CanSee(target)))
                {
                    Vector3 way = onMesh ? PathDirection(target.transform.position) : toTarget / distance;
                    chase = way * (moveSpeed * speed);
                    // Out of sight it looks where it's going (round a corner), not through the wall at the target.
                    if (way.sqrMagnitude > 0.01f && !CanSee(target)) FaceTowards(transform.position + way, dt * speed);
                    else FaceTowards(target.transform.position, dt * speed);
                }
                // Too close: back off, still facing it (unless that would walk it off an edge).
                else if (distance < retreatDistance && distance > 0.01f && motor.isGrounded && CanBackAway(-toTarget / distance, onMesh))
                    chase = -toTarget / distance * (retreatSpeed * speed);
                else FaceTowards(target.transform.position, dt * speed);
            }
            else if (onMesh && agent.hasPath) agent.ResetPath();

            knockback = Vector3.MoveTowards(knockback, Vector3.zero, knockbackDrag * dt);

            if (motor.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            else verticalVelocity -= gravity * dt;

            motor.Move((chase + knockback + Vector3.up * verticalVelocity) * dt);
        }

        // ---------- Pathfinding (server) ----------

        /// <summary>
        /// Keeps the agent on the NavMesh where the enemy really is. Returns false while it's off the mesh (in the air,
        /// knocked off, no NavMesh in this level): then it walks straight, as without pathfinding.
        /// </summary>
        bool UpdateAgent()
        {
            if (agent == null) return false;
            if (!agent.enabled)
            {
                if (!motor.isGrounded || !NavMesh.SamplePosition(transform.position, out var hit, navMeshSnapDistance, agent.areaMask)) return false;
                agent.enabled = true;
                agent.updatePosition = false;
                agent.updateRotation = false;
                if (!agent.Warp(hit.position)) { agent.enabled = false; return false; }
                lastDestination = new Vector3(float.MaxValue, 0f, 0f);
            }
            if (!agent.isOnNavMesh)
            {
                agent.enabled = false;
                return false;
            }
            agent.nextPosition = transform.position;
            // Pushed or fallen too far from the mesh: let go until it's back.
            if ((agent.nextPosition - transform.position).sqrMagnitude > navMeshSnapDistance * navMeshSnapDistance && motor.isGrounded)
            {
                agent.enabled = false;
                return false;
            }
            return true;
        }

        /// <summary>Which way to walk to get to <paramref name="destination"/> along the NavMesh (flat, unit length).</summary>
        Vector3 PathDirection(Vector3 destination)
        {
            if (Time.time >= nextRepathTime || (destination - lastDestination).sqrMagnitude > 4f)
            {
                nextRepathTime = Time.time + repathInterval;
                lastDestination = destination;
                agent.SetDestination(destination);
            }
            Vector3 toward = agent.hasPath ? agent.steeringTarget : destination;
            Vector3 flat = Vector3.ProjectOnPlane(toward - transform.position, Vector3.up);
            if (flat.sqrMagnitude < 0.0004f)
                flat = Vector3.ProjectOnPlane(destination - transform.position, Vector3.up);
            return flat.sqrMagnitude > 0.0001f ? flat.normalized : Vector3.zero;
        }

        /// <summary>Nothing solid between its eyes and the target's middle (other enemies and players don't count).</summary>
        bool CanSee(IEnemyTarget target)
        {
            Vector3 from = transform.position + Vector3.up * (motor.height * 0.7f);
            Vector3 to = target.transform.position + Vector3.up * 1f;
            foreach (var hit in Physics.RaycastAll(from, to - from, Vector3.Distance(from, to), ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.collider.GetComponentInParent<EnemyController>() != null || hit.collider.GetComponentInParent<IEnemyTarget>() != null) continue;
                if (hit.collider.attachedRigidbody != null && !hit.collider.attachedRigidbody.isKinematic) continue; // loose crates
                return false;
            }
            return true;
        }

        /// <summary>Room to step back in <paramref name="direction"/>: NavMesh that way (or, off the mesh, floor).</summary>
        bool CanBackAway(Vector3 direction, bool onMesh)
        {
            if (!onMesh) return GroundBehind(direction);
            Vector3 from = agent.nextPosition;
            return !NavMesh.Raycast(from, from + direction * (motor.radius + 1f), out _, agent.areaMask);
        }

        /// <summary>Whether there is floor a step away in <paramref name="direction"/> (not a drop).</summary>
        bool GroundBehind(Vector3 direction)
        {
            Vector3 probe = transform.position + direction * (motor.radius + 0.6f) + Vector3.up * 0.5f;
            foreach (var hit in Physics.RaycastAll(probe, Vector3.down, 1.6f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.GetComponentInParent<EnemyController>() == null && hit.collider.GetComponentInParent<IEnemyTarget>() == null)
                    return true;
            return false;
        }

        bool TryStartAttack(IEnemyTarget target, float distance)
        {
            foreach (var attack in attacks)
            {
                if (!attack.enabled || !attack.CanStart(target, distance)) continue;
                currentAttack = attack;
                attack.Begin(target);
                return true;
            }
            return false;
        }

        /// <summary>Turn on the spot towards a world position (yaw only).</summary>
        public void FaceTowards(Vector3 point, float dt)
        {
            Vector3 flat = Vector3.ProjectOnPlane(point - transform.position, Vector3.up);
            if (flat.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat, Vector3.up), turnSpeed * dt);
        }

        // ---------- Charm halo (every client) ----------

        void OnCharmChanged(ulong previous, ulong current)
        {
            bool charmed = current != NotCharmed;
            if (charmed && halo == null) halo = BuildHalo();
            if (halo != null) halo.gameObject.SetActive(charmed);
        }

        Transform BuildHalo()
        {
            var material = charmMaterial;
            if (material == null)
            {
                var body = GetComponentInChildren<Renderer>();
                material = new Material(body.sharedMaterial) { color = new Color(0.9f, 0.3f, 1f) };
            }

            var root = new GameObject("CharmHalo").transform;
            root.SetParent(transform, false);
            root.localPosition = Vector3.up * haloHeight;
            const int pieces = 6;
            for (int i = 0; i < pieces; i++)
            {
                var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(piece.GetComponent<Collider>());
                piece.GetComponent<Renderer>().sharedMaterial = material;
                piece.transform.SetParent(root, false);
                float angle = i * Mathf.PI * 2f / pieces;
                piece.transform.localPosition = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 0.42f;
                piece.transform.localRotation = Quaternion.Euler(45f, i * 60f, 45f);
                piece.transform.localScale = Vector3.one * 0.13f;
            }
            return root;
        }

        // ---------- Ice (every client) ----------

        void UpdateFreezeVisuals()
        {
            bool frozen = IsFrozen;
            bool slowed = !frozen && IsSlowed;
            if (frozen && iceShell == null) iceShell = BuildIceShell();
            if (iceShell != null && iceShell.gameObject.activeSelf != frozen)
            {
                iceShell.gameObject.SetActive(frozen);
                if (!frozen) BurstIce(); // thawed: the shell cracks off
            }

            damageable.SetStatusTint(iceTint, frozen ? 0.7f : slowed ? 0.4f + 0.15f * Mathf.Sin(Time.time * 6f) : 0f);
            if (slowed && Time.time >= nextFrostTime)
            {
                nextFrostTime = Time.time + 0.12f;
                SpawnIceBit(Random.insideUnitSphere * 0.4f + Vector3.down * 0.3f, 0.07f, 0.5f);
            }
        }

        Material IceMaterial
        {
            get
            {
                if (iceMaterial == null) iceMaterial = new Material(GetComponentInChildren<Renderer>().sharedMaterial) { color = iceTint };
                return iceMaterial;
            }
        }

        /// <summary>Slabs of ice standing around the body, plus a few chunks on top.</summary>
        Transform BuildIceShell()
        {
            var root = new GameObject("IceShell").transform;
            root.SetParent(transform, false);
            root.localPosition = motor.center;
            float radius = motor.radius + 0.1f;
            float half = motor.height * 0.5f;

            const int slabs = 7;
            for (int i = 0; i < slabs; i++)
            {
                float angle = i * 360f / slabs + Random.Range(-10f, 10f);
                var rotation = Quaternion.Euler(Random.Range(-8f, 8f), angle, Random.Range(-8f, 8f));
                var slab = IcePiece(root, rotation * Vector3.forward * radius + Vector3.up * Random.Range(-0.1f, 0.1f),
                    new Vector3(radius * 0.95f, motor.height * Random.Range(0.75f, 0.95f), 0.16f));
                slab.localRotation = rotation;
            }
            for (int i = 0; i < 3; i++)
            {
                var chunk = IcePiece(root, new Vector3(Random.Range(-0.15f, 0.15f), half - 0.05f, Random.Range(-0.15f, 0.15f)), Vector3.one * Random.Range(0.25f, 0.38f));
                chunk.localRotation = Random.rotation;
            }
            return root;
        }

        Transform IcePiece(Transform parent, Vector3 localPosition, Vector3 scale)
        {
            var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            piece.name = "Ice";
            Destroy(piece.GetComponent<Collider>());
            piece.GetComponent<Renderer>().sharedMaterial = IceMaterial;
            piece.transform.SetParent(parent, false);
            piece.transform.localPosition = localPosition;
            piece.transform.localScale = scale;
            return piece.transform;
        }

        void BurstIce()
        {
            for (int i = 0; i < 10; i++)
                SpawnIceBit(Random.onUnitSphere * motor.radius + Vector3.up * Random.Range(-0.3f, 0.5f), Random.Range(0.1f, 0.2f), 0.6f);
        }

        /// <summary>A bit of ice flying or falling off the body (<paramref name="offset"/> from its middle).</summary>
        void SpawnIceBit(Vector3 offset, float size, float life)
        {
            var bit = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bit.name = "IceBit";
            Destroy(bit.GetComponent<Collider>());
            bit.GetComponent<Renderer>().sharedMaterial = IceMaterial;
            bit.transform.SetPositionAndRotation(transform.TransformPoint(motor.center) + offset, Random.rotation);
            bit.transform.localScale = Vector3.one * size;
            var body = bit.AddComponent<Rigidbody>();
            body.linearVelocity = new Vector3(offset.x, 0.5f, offset.z) * 3f;
            Destroy(bit, life);
        }

        /// <summary>
        /// The charming robot while it lives (at any distance), otherwise the nearest player or enemy of another
        /// faction in aggro range (rivals preferred unless a player is clearly closer).
        /// </summary>
        IEnemyTarget FindTarget()
        {
            if (charmedBy != null && charmedBy.IsAlive) return charmedBy;
            if (IsCharmed)
            {
                // The robot went away without letting go (destroyed): the spell breaks.
                charmedBy = null;
                charmer.Value = NotCharmed;
            }
            IEnemyTarget best = FindNearestPlayer(out float bestSqr);
            foreach (var other in s_All)
            {
                if (!IsHostileTo(other)) continue;
                // Rivals count as ~30% nearer than they are: a battle between the factions, not everyone on the players.
                float sqr = (other.transform.position - transform.position).sqrMagnitude * 0.5f;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = other.AsTarget;
                }
            }
            return best;
        }

        PlayerHealth FindNearestPlayer(out float bestSqr)
        {
            PlayerHealth nearest = null;
            bestSqr = aggroRange * aggroRange;
            foreach (var client in NetworkManager.ConnectedClientsList)
            {
                if (client.PlayerObject == null || !client.PlayerObject.TryGetComponent(out PlayerHealth health) || !health.IsAlive) continue;
                float sqr = (client.PlayerObject.transform.position - transform.position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    nearest = health;
                }
            }
            return nearest;
        }
    }
}
