using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Mortar that lobs little robots instead of bombs. Every couple of seconds, while its target is in (long) range,
    /// it plants itself (an attack owns the enemy's movement, so it stands still), swings its barrel round, charges
    /// for a moment and fires a pod in a high arc at where the target stands. Where the pod comes down a minion robot
    /// (<see cref="minionPrefab"/>) pops out and goes after the players. It never fires at anyone closer than
    /// <see cref="EnemyAttack.minRange"/>: up close it's helpless and the <see cref="EnemyController"/> just backs it
    /// away (set its retreat distance to the same value). Keeps it up until it's destroyed.
    /// The pod flies on every client along the same arc (<see cref="GrenadeProjectile"/>); the server's copy lands
    /// the minion.
    /// </summary>
    public class MortarAttack : EnemyAttack, IGrenadeThrower
    {
        [Header("Minions")]
        [SerializeField] GameObject minionPrefab;
        [Tooltip("Stops firing while this many of its minions are still around.")]
        [SerializeField] int maxMinions = 8;

        [Header("Shot")]
        [SerializeField] float windupTime = 0.7f;
        [SerializeField] float recoverTime = 0.35f;
        [SerializeField] float podGravity = 20f;
        [Tooltip("Seconds in the air for the nearest and the furthest shots (longer shots lob higher).")]
        [SerializeField] Vector2 flightTime = new Vector2(1.2f, 2.8f);
        [Tooltip("Random spread around the target's feet where the pod lands.")]
        [SerializeField] float scatter = 1f;
        [SerializeField] float podRadius = 0.3f;
        [SerializeField] float podSize = 1.8f;

        [Header("Visual")]
        [SerializeField] Transform barrel;
        [SerializeField] Transform muzzle;
        [SerializeField] Transform chargeGlow;
        [SerializeField] float chargeGlowSize = 0.6f;
        [SerializeField] float recoilDistance = 0.3f;
        [SerializeField] Material podMaterial;
        [SerializeField] Material podBandMaterial;

        readonly NetworkVariable<bool> charging = new();
        readonly List<NetworkObject> minions = new();
        readonly Dictionary<int, GrenadeProjectile> inFlight = new();

        enum Phase { Windup, Recover }
        Phase phase;
        float phaseTime;
        int shotCount;
        Vector3 barrelRest;
        float chargeStart, recoilTime = float.NegativeInfinity;

        void Reset()
        {
            minRange = 10f;
            maxRange = 40f;
            cooldown = 0.95f;
        }

        protected override void Awake()
        {
            base.Awake();
            if (barrel != null) barrelRest = barrel.localPosition;
        }

        public override void OnNetworkSpawn()
        {
            charging.OnValueChanged += OnChargingChanged;
            if (chargeGlow != null) chargeGlow.localScale = Vector3.zero;
        }

        public override void OnNetworkDespawn()
        {
            charging.OnValueChanged -= OnChargingChanged;
            foreach (var pod in inFlight.Values)
                if (pod != null) Destroy(pod.gameObject);
            inFlight.Clear();
        }

        public override bool CanStart(IEnemyTarget target, float distance)
        {
            minions.RemoveAll(m => m == null || !m.IsSpawned);
            return minionPrefab != null && minions.Count < maxMinions && base.CanStart(target, distance);
        }

        protected override void OnBegin()
        {
            phase = Phase.Windup;
            phaseTime = 0f;
            charging.Value = true;
        }

        protected override bool OnTick(float dt)
        {
            phaseTime += dt;
            if (phase == Phase.Windup)
            {
                if (!TargetAlive) return false;
                Enemy.FaceTowards(Target.transform.position, dt);
                if (phaseTime < windupTime) return true;
                Fire(Target.transform.position);
                phase = Phase.Recover;
                phaseTime = 0f;
                return true;
            }
            return phaseTime < recoverTime;
        }

        protected override void OnCancel()
        {
            charging.Value = false;
        }

        void Fire(Vector3 targetFeet)
        {
            charging.Value = false;
            Vector2 spread = Random.insideUnitCircle * scatter;
            Vector3 landing = targetFeet + new Vector3(spread.x, 0f, spread.y);
            Vector3 start = muzzle != null ? muzzle.position : transform.position + Vector3.up * 2f;

            float distance = Vector3.ProjectOnPlane(landing - start, Vector3.up).magnitude;
            float time = Mathf.Lerp(flightTime.x, flightTime.y, Mathf.InverseLerp(minRange, maxRange, distance));
            // Lands on `landing` after `time` under `podGravity`.
            Vector3 velocity = (landing - start) / time + Vector3.up * (0.5f * podGravity * time);
            FireRpc(start, velocity, ++shotCount);
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void FireRpc(Vector3 start, Vector3 velocity, int id)
        {
            recoilTime = Time.time;
            inFlight[id] = GrenadeProjectile.Spawn(this, id, start, velocity, podGravity, podRadius, podMaterial, podBandMaterial, podSize, transform);
        }

        /// <summary>Every client: its copy of a pod came down. The server's copy drops off a minion there.</summary>
        public void OnGrenadeImpact(int id, Vector3 point)
        {
            inFlight.Remove(id);
            if (podMaterial != null) ShotEffects.Sparks(point, podMaterial, 10);
            if (!IsServer || !IsSpawned) return;

            // Stand it on the floor under the impact (the pod may have hit a wall or a crate side).
            Vector3 spawn = point + Vector3.up * 0.3f;
            float best = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(point + Vector3.up * 1.5f, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.distance >= best) continue;
                if (hit.collider.GetComponentInParent<IEnemyTarget>() != null || hit.collider.GetComponentInParent<EnemyController>() != null) continue;
                best = hit.distance;
                spawn = hit.point + Vector3.up * 0.05f;
            }
            Vector3 face = Vector3.ProjectOnPlane(transform.position - spawn, Vector3.up);
            var minion = Instantiate(minionPrefab, spawn, face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(-face) : Quaternion.identity);
            var networkObject = minion.GetComponent<NetworkObject>();
            networkObject.Spawn(true);
            minions.Add(networkObject);
            // Its minions fight for the same side.
            if (minion.TryGetComponent(out EnemyController minionBrain))
            {
                minionBrain.ServerSetFaction(Enemy.Faction);
                minionBrain.AggroRange = Mathf.Max(minionBrain.AggroRange, Enemy.AggroRange);
            }
        }

        // ---------- Visuals (every client) ----------

        void OnChargingChanged(bool previous, bool current) => chargeStart = Time.time;

        void Update()
        {
            if (chargeGlow != null)
            {
                float size = 0f;
                if (charging.Value)
                {
                    float t = Mathf.Clamp01((Time.time - chargeStart) / windupTime);
                    size = chargeGlowSize * t * (1f + 0.15f * Mathf.Sin(Time.time * 40f));
                }
                chargeGlow.localScale = Vector3.one * size;
            }
            if (barrel != null)
            {
                // Kicks back on firing and eases forward again.
                float age = Time.time - recoilTime;
                float kick = age < 0.05f ? age / 0.05f : Mathf.Clamp01(1f - (age - 0.05f) / 0.4f);
                barrel.localPosition = barrelRest - barrel.localRotation * Vector3.forward * (recoilDistance * kick);
            }
        }
    }
}
