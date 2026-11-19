using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// A close-range blow (a shield bash, a punch...): the enemy pulls back the striking part while turning to
    /// face the target (the telegraph: it glows), then lunges and strikes, hitting every target in a short arc in
    /// front, then needs a moment before it can go again. Once the strike starts its direction is fixed, so it can
    /// be dodged. <see cref="requires"/> ties it to an <see cref="EnemyShield"/>: a bash only while the shield
    /// lasts, a punch only once it's gone. A charmed enemy only hurts the robot that charmed it.
    /// Runs on the server; every client poses the striking part from the replicated phase.
    /// </summary>
    public class MeleeStrikeAttack : EnemyAttack
    {
        public enum Requires { Always, Shield, NoShield }
        enum Phase : byte { Idle, Windup, Strike, Recover }

        [Tooltip("Only with the shield up, only once it's broken, or always.")]
        [SerializeField] Requires requires = Requires.Always;

        [Header("Damage")]
        [SerializeField] int damage = 2;
        [Tooltip("How far the blow reaches from the enemy's middle (after the lunge).")]
        [SerializeField] float reach = 2.2f;
        [Tooltip("Degrees either side of straight ahead the blow covers.")]
        [SerializeField] float arcHalfAngle = 60f;
        [Tooltip("Things this far above its feet still get hit.")]
        [SerializeField] float hitHeight = 2.4f;
        [Tooltip("Extra shove for players hit (m/s, 0 = just the normal knockback): a bash sends them flying.")]
        [SerializeField] float shove;

        [Header("Timing")]
        [SerializeField] float windupTime = 0.5f;
        [SerializeField] float strikeTime = 0.15f;
        [SerializeField] float recoverTime = 0.5f;
        [Tooltip("How far it steps forward during the strike.")]
        [SerializeField] float lunge = 0.6f;

        [Header("Visual")]
        [Tooltip("What moves: the shield, the fist...")]
        [SerializeField] Transform pivot;
        [Tooltip("Lights up while winding up.")]
        [SerializeField] Renderer glowRenderer;
        [SerializeField] Color windupGlow = new Color(1f, 0.45f, 0.15f);
        [Tooltip("Pivot poses (local position, local angles).")]
        [SerializeField] Vector3 idlePosition, idleAngles;
        [SerializeField] Vector3 windupPosition, windupAngles;
        [SerializeField] Vector3 strikePosition, strikeAngles;

        readonly NetworkVariable<Phase> phase = new();
        readonly HashSet<IEnemyTarget> hitThisStrike = new();
        EnemyShield shield;

        // Server
        float phaseTime;
        bool hitDone;

        // Every client
        float phaseStartTime;
        Material glowMaterial;
        Color glowBase;

        protected override void Awake()
        {
            base.Awake();
            shield = GetComponent<EnemyShield>();
        }

        public override bool CanStart(IEnemyTarget target, float distance)
        {
            bool hasShield = shield != null && shield.HasShield;
            if (requires == Requires.Shield && !hasShield) return false;
            if (requires == Requires.NoShield && hasShield) return false;
            return base.CanStart(target, distance);
        }

        public override void OnNetworkSpawn()
        {
            phase.OnValueChanged += OnPhaseChanged;
            if (glowRenderer != null)
            {
                glowMaterial = glowRenderer.material;
                glowBase = glowMaterial.color;
            }
        }

        public override void OnNetworkDespawn() => phase.OnValueChanged -= OnPhaseChanged;

        // ---------- Server ----------

        protected override void OnBegin() => Enter(Phase.Windup);

        protected override bool OnTick(float dt)
        {
            phaseTime += dt;
            switch (phase.Value)
            {
                case Phase.Windup:
                    if (TargetAlive) Enemy.FaceTowards(Target.transform.position, dt);
                    if (phaseTime >= windupTime) Enter(Phase.Strike);
                    return true;

                case Phase.Strike:
                    float before = Mathf.Clamp01((phaseTime - dt) / strikeTime);
                    float after = Mathf.Clamp01(phaseTime / strikeTime);
                    // Lunge (and keep to the ground: the attack owns the movement, so the controller's gravity is off).
                    Enemy.Motor.Move(transform.forward * (lunge * (after - before)) + Vector3.down * (4f * dt));
                    if (!hitDone && after >= 0.5f)
                    {
                        hitDone = true;
                        HitInArc();
                    }
                    if (after >= 1f) Enter(Phase.Recover);
                    return true;

                case Phase.Recover:
                    if (phaseTime < recoverTime) return true;
                    Enter(Phase.Idle);
                    return false;
            }
            return false;
        }

        protected override void OnCancel() => Enter(Phase.Idle);

        void Enter(Phase next)
        {
            phase.Value = next;
            phaseTime = 0f;
            if (next == Phase.Strike)
            {
                hitDone = false;
                hitThisStrike.Clear();
            }
        }

        void HitInArc()
        {
            Vector3 center = transform.position;
            foreach (var collider in Physics.OverlapSphere(center + Vector3.up * (hitHeight * 0.5f), reach + 0.5f, ~0, QueryTriggerInteraction.Ignore))
            {
                var victim = Enemy.VictimOf(collider);
                if (victim == null || !victim.IsAlive || hitThisStrike.Contains(victim)) continue;
                if (Enemy.IsCharmed && victim != Target) continue;

                Vector3 offset = victim.transform.position - center;
                if (offset.y < -0.6f || offset.y > hitHeight) continue;
                Vector3 flat = Vector3.ProjectOnPlane(offset, Vector3.up);
                if (flat.magnitude > reach) continue;
                if (flat.sqrMagnitude > 0.01f && Vector3.Angle(transform.forward, flat) > arcHalfAngle) continue;

                hitThisStrike.Add(victim);
                Vector3 direction = flat.sqrMagnitude > 0.01f ? flat.normalized : transform.forward;
                bool landed = victim.ApplyDamage(new DamageInfo(damage, direction, gameObject));
                if (landed && shove > 0f && victim is PlayerHealth player)
                    player.Shove(direction * shove + Vector3.up * (shove * 0.35f), 0.45f);
            }
        }

        // ---------- Pose (every client) ----------

        void OnPhaseChanged(Phase previous, Phase current) => phaseStartTime = Time.time;

        void Update()
        {
            if (!IsSpawned || pivot == null) return;
            float t = Time.time - phaseStartTime;
            float follow = 1f - Mathf.Exp(-12f * Time.deltaTime);
            switch (phase.Value)
            {
                case Phase.Windup:
                    Pose(windupPosition, windupAngles, follow);
                    SetGlow(Mathf.Clamp01(t / windupTime));
                    break;
                case Phase.Strike:
                    float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / strikeTime), 2f);
                    pivot.SetLocalPositionAndRotation(Vector3.Lerp(windupPosition, strikePosition, k),
                        Quaternion.Euler(Vector3.Lerp(windupAngles, strikeAngles, k)));
                    SetGlow(1f - k * 0.5f);
                    break;
                case Phase.Recover:
                    Pose(strikePosition, strikeAngles, follow);
                    SetGlow(0f);
                    break;
                default:
                    Pose(idlePosition, idleAngles, 1f - Mathf.Exp(-6f * Time.deltaTime));
                    SetGlow(0f);
                    break;
            }
        }

        void Pose(Vector3 position, Vector3 angles, float amount) =>
            pivot.SetLocalPositionAndRotation(Vector3.Lerp(pivot.localPosition, position, amount),
                Quaternion.Slerp(pivot.localRotation, Quaternion.Euler(angles), amount));

        void SetGlow(float amount)
        {
            if (glowMaterial != null) glowMaterial.color = Color.Lerp(glowBase, windupGlow, amount);
        }

        public override void OnDestroy()
        {
            if (glowMaterial != null) Destroy(glowMaterial);
            base.OnDestroy();
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.1f);
            Vector3 origin = transform.position + Vector3.up;
            Vector3 previous = origin;
            for (int i = 0; i <= 10; i++)
            {
                Vector3 point = origin + Quaternion.Euler(0f, Mathf.Lerp(-arcHalfAngle, arcHalfAngle, i / 10f), 0f) * transform.forward * reach;
                Gizmos.DrawLine(previous, point);
                previous = point;
            }
            Gizmos.DrawLine(previous, origin);
        }
    }
}
