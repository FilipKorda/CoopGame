using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// A warship shelling the shore: while switched on (by a <see cref="FactionWarArena"/>), every few seconds its
    /// cannons fire a salvo of bombs in high arcs. Each bomb lands near a player or near an enemy of a faction it
    /// is hostile to; a red ring on the ground shows where for the whole flight, so it can be dodged. The blast hurts
    /// players and enemies of other factions in its radius (never the ship's own side).
    /// The server picks targets and deals the damage; every machine flies its own copy of each bomb and blasts it.
    /// </summary>
    public class ShipBombardment : NetworkBehaviour
    {
        [Tooltip("Muzzles the bombs leave from (the ship's cannons).")]
        [SerializeField] Transform[] cannons;
        [Tooltip("The ship's side: its blasts never hurt enemies of this faction.")]
        [SerializeField] EnemyFaction faction = EnemyFaction.Protagonist;
        [SerializeField] float salvoInterval = 4.5f;
        [SerializeField] int bombsPerSalvo = 3;
        [Tooltip("Seconds between two bombs of one salvo.")]
        [SerializeField] float bombGap = 0.35f;
        [SerializeField] float flightTime = 2.6f;
        [SerializeField] float arcHeight = 28f;
        [Tooltip("How far from the chosen victim a bomb lands (min, max).")]
        [SerializeField] Vector2 scatter = new(1.5f, 6f);
        [Tooltip("Share of bombs aimed at players (the rest at enemies of other factions).")]
        [SerializeField, Range(0f, 1f)] float playerShare = 0.5f;

        [Header("Blast")]
        [SerializeField] float radius = 4f;
        [SerializeField] int playerDamage = 2;
        [SerializeField] int enemyDamage = 8;
        [SerializeField] float cameraShake = 0.5f;

        [Header("Look")]
        [SerializeField] Material bombMaterial;
        [SerializeField] Material warningMaterial;
        [SerializeField] Material fireMaterial;
        [SerializeField] Material coreMaterial;

        readonly NetworkVariable<bool> active = new();
        float nextSalvoAt;
        int bombCount;

        public bool IsActive => active.Value;

        /// <summary>Server: start or stop shelling.</summary>
        public void ServerSetActive(bool on)
        {
            if (!IsServer) return;
            active.Value = on;
            nextSalvoAt = Time.time + 2f;
        }

        void Update()
        {
            if (!IsSpawned || !IsServer || !active.Value) return;
            if (CutsceneSystem.Instance != null && CutsceneSystem.Instance.IsBusy) return;
            if (Time.time < nextSalvoAt || cannons == null || cannons.Length == 0) return;
            nextSalvoAt = Time.time + salvoInterval;
            StartCoroutine(Salvo());
        }

        System.Collections.IEnumerator Salvo()
        {
            for (int i = 0; i < bombsPerSalvo; i++)
            {
                if (!active.Value) yield break;
                if (TryPickLanding(out Vector3 landing))
                {
                    var cannon = cannons[Random.Range(0, cannons.Length)];
                    if (cannon != null)
                    {
                        FireRpc(cannon.position, landing, flightTime, ++bombCount);
                        StartCoroutine(Land(landing));
                    }
                }
                yield return new WaitForSeconds(bombGap);
            }
        }

        bool TryPickLanding(out Vector3 landing)
        {
            landing = default;
            var victims = new List<Vector3>();
            bool atPlayer = Random.value < playerShare;
            if (atPlayer)
            {
                foreach (var player in PlayerHealth.All)
                    if (player != null && player.IsSpawned && player.IsAlive) victims.Add(player.transform.position);
            }
            if (victims.Count == 0)
            {
                foreach (var enemy in EnemyController.All)
                    if (enemy != null && enemy.IsAlive && enemy.Faction != faction) victims.Add(enemy.transform.position);
            }
            if (victims.Count == 0)
                foreach (var player in PlayerHealth.All)
                    if (player != null && player.IsSpawned && player.IsAlive) victims.Add(player.transform.position);
            if (victims.Count == 0) return false;

            Vector3 around = victims[Random.Range(0, victims.Count)];
            Vector2 offset = Random.insideUnitCircle.normalized * Random.Range(scatter.x, scatter.y);
            landing = Ground(around + new Vector3(offset.x, 0f, offset.y));
            return true;
        }

        static Vector3 Ground(Vector3 point)
        {
            float best = float.MaxValue;
            Vector3 result = point;
            foreach (var hit in Physics.RaycastAll(point + Vector3.up * 6f, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<EnemyController>() != null || hit.collider.GetComponentInParent<IEnemyTarget>() != null) continue;
                if (hit.distance < best) { best = hit.distance; result = hit.point; }
            }
            return result;
        }

        System.Collections.IEnumerator Land(Vector3 point)
        {
            yield return new WaitForSeconds(flightTime);
            foreach (var player in PlayerHealth.All)
            {
                if (player == null || !player.IsSpawned || !player.IsAlive) continue;
                Vector3 away = player.transform.position - point;
                if (away.magnitude > radius) continue;
                away.y = 0f;
                player.ApplyDamage(new DamageInfo(playerDamage, away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward, gameObject));
            }
            foreach (var enemy in new List<EnemyController>(EnemyController.All))
            {
                if (enemy == null || !enemy.IsAlive || enemy.Faction == faction) continue;
                Vector3 away = enemy.transform.position - point;
                if (away.magnitude > radius) continue;
                away.y = 0f;
                enemy.Damageable.ApplyDamage(enemyDamage, away.sqrMagnitude > 0.01f ? away.normalized * 1.5f : Vector3.up);
            }
        }

        // ---------- Every machine ----------

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void FireRpc(Vector3 start, Vector3 landing, float time, int id)
        {
            ExplosionEffect.Spawn(start, 1.6f, fireMaterial, coreMaterial, 0f); // muzzle flash
            var bomb = new GameObject("ShipBomb").AddComponent<ShipBomb>();
            bomb.Launch(start, landing, time, arcHeight, radius, bombMaterial, warningMaterial, () =>
                ExplosionEffect.Spawn(landing + Vector3.up * 0.5f, radius, fireMaterial, coreMaterial, cameraShake));
        }

        public override void OnNetworkDespawn()
        {
            StopAllCoroutines();
        }

        void OnDrawGizmosSelected()
        {
            if (cannons == null) return;
            Gizmos.color = Color.red;
            foreach (var c in cannons) if (c != null) Gizmos.DrawWireSphere(c.position, 1f);
        }

        /// <summary>Editor setup.</summary>
        public void Setup(Transform[] muzzles, EnemyFaction side, Material bomb, Material warning, Material fire, Material core)
        {
            cannons = muzzles;
            faction = side;
            bombMaterial = bomb;
            warningMaterial = warning;
            fireMaterial = fire;
            coreMaterial = core;
        }
    }

    /// <summary>One machine's copy of a ship bomb: flies its arc, a ring on the ground marks where; blasts on landing.</summary>
    public class ShipBomb : MonoBehaviour
    {
        Vector3 start, end;
        float time, arc, age, ringRadius;
        Transform ball, ring;
        System.Action onLand;

        public void Launch(Vector3 from, Vector3 to, float flight, float arcHeight, float radius, Material bombMaterial, Material warningMaterial, System.Action landed)
        {
            start = from; end = to; time = Mathf.Max(0.1f, flight); arc = arcHeight; ringRadius = radius; onLand = landed;
            ball = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
            Destroy(ball.GetComponent<Collider>());
            ball.SetParent(transform, false);
            ball.localScale = Vector3.one * 1.1f;
            if (bombMaterial != null) ball.GetComponent<Renderer>().sharedMaterial = bombMaterial;
            var trail = ball.gameObject.AddComponent<TrailRenderer>();
            trail.time = 0.25f; trail.startWidth = 0.8f; trail.endWidth = 0f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (warningMaterial != null) trail.sharedMaterial = warningMaterial;

            ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform;
            Destroy(ring.GetComponent<Collider>());
            ring.SetParent(transform, false);
            ring.position = to + Vector3.up * 0.06f;
            var ringRenderer = ring.GetComponent<Renderer>();
            ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (warningMaterial != null) ringRenderer.sharedMaterial = warningMaterial;
            Update();
        }

        void Update()
        {
            if (ball == null) return;
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / time);
            ball.position = Vector3.Lerp(start, end, t) + Vector3.up * (arc * 4f * t * (1f - t));
            // The ring grows to the blast radius while the bomb comes down, pulsing faster near the end.
            float size = Mathf.Lerp(0.3f, 1f, t) * ringRadius * 2f * (1f + 0.06f * Mathf.Sin(age * Mathf.Lerp(8f, 30f, t)));
            ring.localScale = new Vector3(size, 0.03f, size);
            if (t >= 1f)
            {
                onLand?.Invoke();
                Destroy(gameObject);
            }
        }
    }
}
