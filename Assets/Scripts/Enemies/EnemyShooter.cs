using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Ranged attack for an enemy: every couple of seconds, while it has a target in range and in sight, it charges
    /// its cannon for a moment (the telegraph: a glow swells at the muzzle) and fires a bolt straight at where the
    /// target is at that moment — no homing, so moving dodges it. Unlike an <see cref="EnemyAttack"/> it doesn't take
    /// over the enemy's movement, so the enemy keeps walking, holding its distance or backing away while it shoots.
    /// Frozen it can't shoot; slowed it reloads slower. A charmed enemy's bolts only hurt the robot that charmed it.
    /// Runs on the server; every client flies its own copy of each bolt (<see cref="EnemyBolt"/>), the server's copy
    /// decides what it hit.
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class EnemyShooter : NetworkBehaviour
    {
        [SerializeField] int damage = 2;
        [Tooltip("Seconds from one shot to the next (the charge-up included).")]
        [SerializeField] float fireInterval = 2f;
        [Tooltip("Seconds it visibly charges before each shot.")]
        [SerializeField] float chargeTime = 0.5f;
        [Tooltip("Shoots at targets up to this far away.")]
        [SerializeField] float fireRange = 18f;
        [SerializeField] float boltSpeed = 14f;
        [SerializeField] float boltRadius = 0.22f;
        [Tooltip("How far a bolt flies before it fizzles.")]
        [SerializeField] float boltRange = 30f;
        [Tooltip("Height above the target's feet it aims at.")]
        [SerializeField] float aimHeight = 1f;

        [Header("Visual")]
        [SerializeField] Transform muzzle;
        [Tooltip("Swells at the muzzle while charging.")]
        [SerializeField] Transform chargeGlow;
        [SerializeField] float chargeGlowSize = 0.45f;
        [SerializeField] Material boltMaterial;

        readonly NetworkVariable<bool> charging = new();
        readonly Dictionary<int, EnemyBolt> inFlight = new();

        EnemyController enemy;
        float timer;
        int boltCount;
        float chargeStartTime;

        Vector3 Muzzle => muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.4f;

        void Awake() => enemy = GetComponent<EnemyController>();

        /// <summary>The enemy firing (its faction decides which other enemies its bolts may hurt).</summary>
        public EnemyController Controller => enemy;

        public override void OnNetworkSpawn()
        {
            charging.OnValueChanged += OnChargingChanged;
            if (chargeGlow != null) chargeGlow.localScale = Vector3.zero;
        }

        public override void OnNetworkDespawn()
        {
            charging.OnValueChanged -= OnChargingChanged;
            foreach (var bolt in inFlight.Values)
                if (bolt != null) Destroy(bolt.gameObject);
            inFlight.Clear();
        }

        void Update()
        {
            UpdateChargeGlow();
            if (!IsServer || !IsSpawned) return;

            var target = enemy.CurrentTarget;
            float speed = enemy.SpeedFactor;
            if (target == null || !target.IsAlive || speed <= 0f || !InRangeAndSight(target))
            {
                // Nobody to shoot (or frozen): drop the charge; once there is, charge up again before firing.
                timer = Mathf.Min(timer, fireInterval - chargeTime);
                if (charging.Value) charging.Value = false;
                return;
            }

            timer += Time.deltaTime * speed;
            if (!charging.Value && timer >= fireInterval - chargeTime) charging.Value = true;
            if (timer < fireInterval) return;

            timer = 0f;
            charging.Value = false;
            Vector3 start = Muzzle;
            Vector3 direction = AimPoint(target) - start;
            // Charmed: only the robot it's after can be hurt; everyone else is flown through.
            var onlyHurt = enemy.IsCharmed ? target : null;
            FireRpc(start, direction.normalized, ++boltCount);
            if (inFlight.TryGetValue(boltCount, out var bolt) && bolt != null) bolt.OnlyHurt = onlyHurt;
        }

        Vector3 AimPoint(IEnemyTarget target) => target.transform.position + Vector3.up * aimHeight;

        bool InRangeAndSight(IEnemyTarget target)
        {
            Vector3 start = Muzzle;
            Vector3 toTarget = AimPoint(target) - start;
            float distance = toTarget.magnitude;
            if (distance > fireRange) return false;
            foreach (var hit in Physics.RaycastAll(start, toTarget / distance, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<EnemyController>() != null) continue; // its own body, other enemies
                if (hit.collider.GetComponentInParent<IEnemyTarget>() != null) continue;    // the target itself, other players
                return false;
            }
            return true;
        }

        // ---------- Bolts (every client) ----------

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void FireRpc(Vector3 start, Vector3 direction, int id)
        {
            inFlight[id] = EnemyBolt.Spawn(this, id, start, direction, boltSpeed, boltRadius, boltRange, boltMaterial);
        }

        /// <summary>Server: this shooter's bolt touched <paramref name="victim"/>. Returns whether it hurt it (a dodging player or one already down lets it fly on).</summary>
        public bool ServerBoltHit(IEnemyTarget victim, Vector3 direction)
        {
            return victim.IsAlive && victim.ApplyDamage(new DamageInfo(damage, Vector3.ProjectOnPlane(direction, Vector3.up), gameObject));
        }

        /// <summary>This client's copy of a bolt ended (hit something or fizzled); the server tells everyone else.</summary>
        public void OnBoltEnd(int id, Vector3 point, bool burst)
        {
            inFlight.Remove(id);
            if (burst) ShotEffects.Sparks(point, boltMaterial);
            if (IsServer) EndRpc(id, point, burst);
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        void EndRpc(int id, Vector3 point, bool burst)
        {
            if (!inFlight.TryGetValue(id, out var bolt)) return; // this copy already ended by itself
            inFlight.Remove(id);
            if (bolt != null) Destroy(bolt.gameObject);
            if (burst) ShotEffects.Sparks(point, boltMaterial);
        }

        // ---------- Charge-up telegraph (every client) ----------

        void OnChargingChanged(bool previous, bool current) => chargeStartTime = Time.time;

        void UpdateChargeGlow()
        {
            if (chargeGlow == null) return;
            float size = 0f;
            if (charging.Value)
            {
                float t = Mathf.Clamp01((Time.time - chargeStartTime) / chargeTime);
                size = chargeGlowSize * t * (1f + 0.15f * Mathf.Sin(Time.time * 40f)); // swelling, flickering
            }
            chargeGlow.localScale = Vector3.one * size;
        }
    }

    /// <summary>
    /// One client's copy of an enemy bolt: flies straight, flies through enemies, stops at the first wall, crate or
    /// player it touches. Only the server's copy hurts anyone; the others stop at walls by themselves and at players
    /// when the server says so.
    /// </summary>
    public class EnemyBolt : MonoBehaviour
    {
        EnemyShooter owner;
        int id;
        Vector3 direction;
        float speed, radius, range, travelled;
        Material material;
        float nextPuffTime;
        readonly HashSet<IEnemyTarget> passed = new();

        /// <summary>Server: if set, the only one this bolt may hurt (a charmed enemy's shot at its robot).</summary>
        public IEnemyTarget OnlyHurt { get; set; }

        public static EnemyBolt Spawn(EnemyShooter owner, int id, Vector3 start, Vector3 direction, float speed, float radius, float range, Material material)
        {
            var root = new GameObject("EnemyBolt");
            root.transform.SetPositionAndRotation(start, Quaternion.LookRotation(direction));
            var bolt = root.AddComponent<EnemyBolt>();
            bolt.owner = owner;
            bolt.id = id;
            bolt.direction = direction;
            bolt.speed = speed;
            bolt.radius = radius;
            bolt.range = range;
            bolt.material = material;

            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(core.GetComponent<Collider>());
            if (material != null) core.GetComponent<Renderer>().sharedMaterial = material;
            core.transform.SetParent(root.transform, false);
            core.transform.localScale = new Vector3(radius * 2f, radius * 2f, radius * 3.2f);
            return bolt;
        }

        void Update()
        {
            if (owner == null)
            {
                Destroy(gameObject);
                return;
            }

            float step = speed * Time.deltaTime;
            Vector3 position = transform.position;
            bool server = owner.IsServer;
            RaycastHit stop = default;
            float nearest = float.MaxValue;
            foreach (var hit in Physics.SphereCastAll(position, radius, direction, step, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.distance >= nearest) continue;
                IEnemyTarget victim;
                var enemyHit = hit.collider.GetComponentInParent<EnemyController>();
                if (enemyHit != null)
                {
                    // Its own side: flies through. Another faction: the server decides, like for players.
                    if (!server || !owner.Controller.IsHostileTo(enemyHit)) continue;
                    victim = enemyHit.AsTarget;
                }
                else victim = hit.collider.GetComponentInParent<IEnemyTarget>();
                if (victim != null)
                {
                    // Players and the robot: the server decides; the other copies fly on until it says.
                    if (!server || passed.Contains(victim)) continue;
                    if ((OnlyHurt != null && victim != OnlyHurt) || !owner.ServerBoltHit(victim, direction))
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

            transform.position = position + direction * step;
            travelled += step;
            if (travelled >= range)
            {
                End(transform.position, false);
                return;
            }
            if (Time.time >= nextPuffTime)
            {
                nextPuffTime = Time.time + 0.03f;
                SmokePuff.Spawn(position, material);
            }
        }

        void End(Vector3 point, bool burst)
        {
            owner.OnBoltEnd(id, point, burst);
            Destroy(gameObject);
        }
    }
}
