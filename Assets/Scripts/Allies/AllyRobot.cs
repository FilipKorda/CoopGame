using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// The charm robot a player sets down with the <see cref="RobotWeapon"/>. It stands where it was put. When an
    /// enemy comes within <see cref="charmRange"/> it casts a spell at one of them (the nearest one nobody has
    /// charmed): once the spell lands, that enemy ignores the players and goes after the robot until the robot is
    /// destroyed. Meanwhile the robot lobs a small grenade at it every few seconds. When the enemy dies it looks for
    /// the next one in range; with nobody around it just stands there. One charmed enemy at a time.
    /// Enemies hurt it (<see cref="DamageTypes.Enemy"/>); the players' weapons don't. Server-driven; clients see the
    /// spell, the grenades, the head turning to its target and a health bar.
    /// </summary>
    [RequireComponent(typeof(Damageable))]
    public class AllyRobot : NetworkBehaviour, IEnemyTarget, IGrenadeThrower
    {
        const ulong NoTarget = ulong.MaxValue;

        [SerializeField] float charmRange = 10f;
        [Tooltip("How long the spell takes to fly to the enemy; the charm starts when it lands.")]
        [SerializeField] float spellFlightTime = 0.6f;
        [SerializeField] float attackInterval = 3f;
        [Tooltip("First grenade this long after the charm lands.")]
        [SerializeField] float firstAttackDelay = 1f;

        [Header("Grenades")]
        [SerializeField] int grenadeDamage = 1;
        [SerializeField] float grenadeBlastRadius = 1.3f;
        [SerializeField] float grenadeGravity = 20f;
        [Tooltip("Flight time for a close / far throw.")]
        [SerializeField] Vector2 grenadeFlightTime = new Vector2(0.6f, 1f);
        [SerializeField] float grenadeSize = 0.6f;

        [Header("Parts")]
        [SerializeField] Transform head;
        [Tooltip("Where grenades leave the robot.")]
        [SerializeField] Transform launcher;
        [Tooltip("Where the spell leaves the robot (the antenna tip).")]
        [SerializeField] Transform spellOrigin;
        [SerializeField] Transform healthBar;
        [SerializeField] Transform healthFill;

        [Header("Visuals")]
        [SerializeField] Material spellMaterial;
        [SerializeField] Material grenadeMaterial;
        [SerializeField] Material grenadeBandMaterial;
        [SerializeField] Material fireMaterial;
        [SerializeField] Material coreMaterial;
        [SerializeField] Material smokeMaterial;

        // NetworkObjectId of the charmed enemy (for the head and the link), NoTarget when idle.
        readonly NetworkVariable<ulong> charmedEnemy = new(NoTarget);

        Damageable damageable;
        readonly Dictionary<int, GrenadeProjectile> inFlight = new();

        // Server
        EnemyController target;
        EnemyController spellTarget;
        float spellLandsAt;
        float nextAttackTime;
        float nextSearchTime;
        int throwCount;

        public float CharmRange => charmRange;
        public bool IsAlive => this != null && IsSpawned && !damageable.IsDead;

        void Awake() => damageable = GetComponent<Damageable>();

        public override void OnNetworkSpawn()
        {
            if (IsServer) damageable.Died += OnDied;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                damageable.Died -= OnDied;
                LetGo();
            }
            foreach (var grenade in inFlight.Values)
                if (grenade != null) Destroy(grenade.gameObject);
            inFlight.Clear();
            for (int i = 0; i < 8; i++)
                SmokePuff.Spawn(transform.position + Vector3.up * 0.7f + Random.insideUnitSphere * 0.5f, smokeMaterial);
        }

        /// <summary>Enemies hit it through this (IEnemyTarget); nothing else can hurt it.</summary>
        public bool ApplyDamage(in DamageInfo damage)
        {
            if (!IsServer || !IsAlive) return false;
            damageable.ApplyDamage(damage.Amount, Vector3.zero, DamageTypes.Enemy);
            return true;
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (IsServer) ServerTick();
            UpdateVisuals();
        }

        // ---------- Server ----------

        void ServerTick()
        {
            // Killed it (or it's gone): look for the next one.
            if (target == null ? charmedEnemy.Value != NoTarget : !target.IsAlive) SetTarget(null);

            if (spellTarget != null)
            {
                if (Time.time < spellLandsAt) return;
                var landed = spellTarget;
                spellTarget = null;
                if (!landed.IsAlive) return;
                landed.ServerCharm(this);
                SetTarget(landed);
                nextAttackTime = Time.time + firstAttackDelay;
                return;
            }

            if (target == null)
            {
                if (Time.time < nextSearchTime) return;
                nextSearchTime = Time.time + 0.25f;
                var enemy = NearestCharmableEnemy();
                if (enemy == null) return; // nobody in range: stand still

                enemy.ServerReserve(this);
                spellTarget = enemy;
                spellLandsAt = Time.time + spellFlightTime;
                CastRpc(enemy.NetworkObject);
                return;
            }

            if (Time.time >= nextAttackTime)
            {
                nextAttackTime = Time.time + attackInterval;
                ThrowAt(target);
            }
        }

        EnemyController NearestCharmableEnemy()
        {
            EnemyController best = null;
            float bestSqr = charmRange * charmRange;
            foreach (var enemy in EnemyController.All)
            {
                if (!enemy.CanBeCharmed) continue;
                float sqr = (enemy.transform.position - transform.position).sqrMagnitude;
                if (sqr > bestSqr) continue;
                bestSqr = sqr;
                best = enemy;
            }
            return best;
        }

        void SetTarget(EnemyController enemy)
        {
            target = enemy;
            charmedEnemy.Value = enemy != null ? enemy.NetworkObjectId : NoTarget;
        }

        void OnDied(Vector3 direction) => LetGo();

        /// <summary>The robot is going away: free whoever it charmed (or was about to).</summary>
        void LetGo()
        {
            if (target != null) target.ServerReleaseCharm(this);
            if (spellTarget != null) spellTarget.ServerReleaseCharm(this);
            target = null;
            spellTarget = null;
        }

        void ThrowAt(EnemyController enemy)
        {
            Vector3 start = launcher != null ? launcher.position : transform.position + Vector3.up * 1.2f;
            Vector3 aim = enemy.transform.position + Vector3.up * 0.6f;
            Vector3 delta = aim - start;
            float planar = new Vector2(delta.x, delta.z).magnitude;
            float flight = Mathf.Lerp(grenadeFlightTime.x, grenadeFlightTime.y, Mathf.Clamp01(planar / charmRange));
            Vector3 velocity = delta / flight + Vector3.up * (0.5f * grenadeGravity * flight);
            ThrowRpc(start, velocity, ++throwCount);
        }

        // ---------- Spell and grenades (every client) ----------

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void CastRpc(NetworkObjectReference enemyRef)
        {
            if (!enemyRef.TryGet(out NetworkObject enemyObject)) return;
            Vector3 from = spellOrigin != null ? spellOrigin.position : transform.position + Vector3.up * 1.6f;
            SpellOrb.Spawn(from, enemyObject.transform, spellFlightTime, spellMaterial);
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void ThrowRpc(Vector3 start, Vector3 velocity, int id)
        {
            inFlight[id] = GrenadeProjectile.Spawn(this, id, start, velocity, grenadeGravity, 0.1f,
                grenadeMaterial, grenadeBandMaterial, grenadeSize, transform);
        }

        public void OnGrenadeImpact(int id, Vector3 point)
        {
            inFlight.Remove(id);
            if (!IsServer) return; // clients wait for the server's blast
            ServerBlast(point);
            PlayBlast(point);
            BlastRpc(id, point);
        }

        void ServerBlast(Vector3 point)
        {
            var hit = new HashSet<Damageable>();
            foreach (var collider in Physics.OverlapSphere(point, grenadeBlastRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                var victim = collider.GetComponentInParent<Damageable>();
                if (victim == null || victim.IsDead || !hit.Add(victim)) continue;
                if (victim.GetComponent<AllyRobot>() != null) continue; // never hurts robots
                Vector3 push = Vector3.ProjectOnPlane(victim.transform.position - point, Vector3.up).normalized;
                victim.ApplyDamage(grenadeDamage, push, DamageTypes.Grenade);
            }
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        void BlastRpc(int id, Vector3 point)
        {
            if (inFlight.TryGetValue(id, out var grenade))
            {
                inFlight.Remove(id);
                if (grenade != null) Destroy(grenade.gameObject);
            }
            PlayBlast(point);
        }

        void PlayBlast(Vector3 point) => ExplosionEffect.Spawn(point, grenadeBlastRadius, fireMaterial, coreMaterial, 0.08f);

        // ---------- Visuals ----------

        void UpdateVisuals()
        {
            // Head: track the charmed enemy, otherwise sweep slowly as if scanning.
            if (head != null)
            {
                Quaternion goal;
                var enemy = CharmedEnemyTransform();
                if (enemy != null)
                {
                    Vector3 flat = Vector3.ProjectOnPlane(enemy.position - head.position, Vector3.up);
                    goal = flat.sqrMagnitude > 0.001f ? Quaternion.LookRotation(flat) : head.rotation;
                }
                else
                {
                    goal = transform.rotation * Quaternion.Euler(0f, Mathf.Sin(Time.time * 0.8f) * 60f, 0f);
                }
                head.rotation = Quaternion.RotateTowards(head.rotation, goal, 360f * Time.deltaTime);
            }

            if (healthBar != null)
            {
                var cam = Camera.main;
                if (cam != null) healthBar.rotation = Quaternion.LookRotation(healthBar.position - cam.transform.position);
                float fraction = damageable.MaxHealth > 0 ? Mathf.Clamp01(damageable.Health / (float)damageable.MaxHealth) : 0f;
                if (healthFill != null)
                {
                    healthFill.localScale = new Vector3(Mathf.Max(0.001f, fraction), 1f, 1f);
                    healthFill.localPosition = new Vector3((fraction - 1f) * 0.5f, 0f, 0f);
                }
            }
        }

        Transform CharmedEnemyTransform()
        {
            ulong id = charmedEnemy.Value;
            if (id == NoTarget || NetworkManager == null) return null;
            return NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out var enemy) ? enemy.transform : null;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.9f, 0.3f, 1f);
            Gizmos.DrawWireSphere(transform.position, charmRange);
        }
    }

    /// <summary>The charm spell: a glowing magenta cube that flies in a soft arc from the robot to the enemy, then pops.</summary>
    public class SpellOrb : MonoBehaviour
    {
        Vector3 from;
        Transform target;
        Vector3 lastTargetPoint;
        float duration, startTime;
        Material material;

        public static void Spawn(Vector3 from, Transform target, float duration, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "CharmSpell";
            Destroy(go.GetComponent<Collider>());
            var renderer = go.GetComponent<Renderer>();
            if (material != null) renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.position = from;
            go.transform.localScale = Vector3.one * 0.3f;
            var orb = go.AddComponent<SpellOrb>();
            orb.from = from;
            orb.target = target;
            orb.lastTargetPoint = target.position + Vector3.up * 1.2f;
            orb.duration = Mathf.Max(0.05f, duration);
            orb.startTime = Time.time;
            orb.material = material;
        }

        void Update()
        {
            if (target != null) lastTargetPoint = target.position + Vector3.up * 1.2f;
            float t = (Time.time - startTime) / duration;
            if (t >= 1f)
            {
                Burst(lastTargetPoint);
                Destroy(gameObject);
                return;
            }
            transform.position = Vector3.Lerp(from, lastTargetPoint, t) + Vector3.up * (1.5f * 4f * t * (1f - t));
            transform.Rotate(new Vector3(1f, 2f, 0.5f), 720f * Time.deltaTime);
        }

        void Burst(Vector3 point)
        {
            for (int i = 0; i < 8; i++)
            {
                var spark = GameObject.CreatePrimitive(PrimitiveType.Cube);
                spark.name = "CharmSpark";
                Destroy(spark.GetComponent<Collider>());
                if (material != null) spark.GetComponent<Renderer>().sharedMaterial = material;
                spark.transform.SetPositionAndRotation(point, Random.rotation);
                spark.transform.localScale = Vector3.one * 0.1f;
                var body = spark.AddComponent<Rigidbody>();
                body.useGravity = false;
                body.linearVelocity = Random.onUnitSphere * 3f;
                Destroy(spark, 0.35f);
            }
        }
    }
}
