using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Sword swing (the big sword enemy). Raises the sword over its shoulder while turning to face the target (the
    /// telegraph: the blade glows), then steps in and swings it across its front, hitting every player in the arc,
    /// then stands open for a moment before it can swing again. The swing keeps its direction once it starts, so
    /// players can dodge it. A charmed enemy only hurts the robot that charmed it.
    /// Runs on the server; every client poses the sword from the replicated phase.
    /// </summary>
    public class SwordAttack : EnemyAttack
    {
        enum Phase : byte { Idle, Windup, Swing, Recover }

        [Header("Damage")]
        [SerializeField] int damage = 2;
        [Tooltip("How far the blade reaches from the enemy's middle (after the lunge).")]
        [SerializeField] float reach = 2.8f;
        [Tooltip("Degrees either side of straight ahead the swing covers.")]
        [SerializeField] float arcHalfAngle = 75f;
        [Tooltip("Things this far above its feet still get hit.")]
        [SerializeField] float hitHeight = 2.4f;

        [Header("Timing")]
        [SerializeField] float windupTime = 0.6f;
        [SerializeField] float swingTime = 0.2f;
        [SerializeField] float recoverTime = 0.55f;
        [Tooltip("How far it steps forward during the swing.")]
        [SerializeField] float lunge = 0.8f;

        [Header("Visual")]
        [Tooltip("Pivot in the hand; the blade points along its local up.")]
        [SerializeField] Transform swordPivot;
        [SerializeField] Transform bladeTip;
        [SerializeField] Renderer bladeRenderer;
        [Tooltip("The blade lights up towards this while the swing winds up.")]
        [SerializeField] Color windupGlow = new Color(1f, 0.45f, 0.15f);
        [SerializeField] Material trailMaterial;
        [Tooltip("Sword poses as pivot angles (pitch, yaw, roll): yaw swings it across the front.")]
        [SerializeField] Vector3 idlePose = new Vector3(35f, 15f, 0f);
        [SerializeField] Vector3 windupPose = new Vector3(40f, 130f, 0f);
        [SerializeField] Vector3 followThroughPose = new Vector3(100f, -85f, 0f);

        const int TrailLinks = 10;
        const float TrailLife = 0.15f;

        readonly NetworkVariable<Phase> phase = new();
        readonly HashSet<IEnemyTarget> hitThisSwing = new();

        // Server
        float phaseTime;
        bool hitDone;

        // Every client
        float phaseStartTime;
        Color bladeBase;
        Material bladeMaterial;
        Transform trailRoot;
        Transform[] trailTransforms;
        readonly List<(Vector3 point, float time)> trail = new();

        void Reset()
        {
            minRange = 0f;
            maxRange = 2.6f;
            cooldown = 1.4f;
        }

        public override void OnNetworkSpawn()
        {
            phase.OnValueChanged += OnPhaseChanged;
            if (bladeRenderer != null)
            {
                bladeMaterial = bladeRenderer.material;
                bladeBase = bladeMaterial.color;
            }
        }

        public override void OnNetworkDespawn()
        {
            phase.OnValueChanged -= OnPhaseChanged;
            if (trailRoot != null) Destroy(trailRoot.gameObject);
        }

        // ---------- Server ----------

        protected override void OnBegin() => Enter(Phase.Windup);

        protected override bool OnTick(float dt)
        {
            phaseTime += dt;
            switch (phase.Value)
            {
                case Phase.Windup:
                    if (TargetAlive) Enemy.FaceTowards(Target.transform.position, dt);
                    if (phaseTime >= windupTime) Enter(Phase.Swing);
                    return true;

                case Phase.Swing:
                    float before = Mathf.Clamp01((phaseTime - dt) / swingTime);
                    float after = Mathf.Clamp01(phaseTime / swingTime);
                    // Step in (and keep to the ground: the attack owns the movement, so the controller's gravity is off).
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
            if (next == Phase.Swing)
            {
                hitDone = false;
                hitThisSwing.Clear();
            }
        }

        void HitInArc()
        {
            Vector3 center = transform.position;
            foreach (var collider in Physics.OverlapSphere(center + Vector3.up * (hitHeight * 0.5f), reach + 0.5f, ~0, QueryTriggerInteraction.Ignore))
            {
                var victim = Enemy.VictimOf(collider);
                if (victim == null || !victim.IsAlive || hitThisSwing.Contains(victim)) continue;
                // Charmed: it only has eyes for the robot; players next to it are left alone.
                if (Enemy.IsCharmed && victim != Target) continue;

                Vector3 offset = victim.transform.position - center;
                if (offset.y < -0.6f || offset.y > hitHeight) continue;
                Vector3 flat = Vector3.ProjectOnPlane(offset, Vector3.up);
                if (flat.magnitude > reach) continue;
                if (flat.sqrMagnitude > 0.01f && Vector3.Angle(transform.forward, flat) > arcHalfAngle) continue;

                hitThisSwing.Add(victim);
                victim.ApplyDamage(new DamageInfo(damage, flat.sqrMagnitude > 0.01f ? flat : transform.forward, gameObject));
            }
        }

        // ---------- Sword pose and trail (every client) ----------

        void OnPhaseChanged(Phase previous, Phase current)
        {
            phaseStartTime = Time.time;
            trail.Clear();
        }

        void Update()
        {
            if (!IsSpawned || swordPivot == null) return;
            float t = Time.time - phaseStartTime;
            float dt = Time.deltaTime;

            switch (phase.Value)
            {
                case Phase.Windup:
                    swordPivot.localRotation = Quaternion.Slerp(swordPivot.localRotation, Quaternion.Euler(windupPose), 1f - Mathf.Exp(-10f * dt));
                    SetGlow(Mathf.Clamp01(t / windupTime));
                    break;
                case Phase.Swing:
                    // Angle by angle, not Slerp: the short way round from the wind-up would swing through its back.
                    float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / swingTime), 2f);
                    swordPivot.localRotation = Quaternion.Euler(Vector3.Lerp(windupPose, followThroughPose, k));
                    SetGlow(1f - k * 0.5f);
                    if (bladeTip != null) trail.Add((bladeTip.position, Time.time));
                    break;
                case Phase.Recover:
                    swordPivot.localRotation = Quaternion.Slerp(swordPivot.localRotation, Quaternion.Euler(followThroughPose), 1f - Mathf.Exp(-10f * dt));
                    SetGlow(0f);
                    break;
                default:
                    swordPivot.localRotation = Quaternion.Slerp(swordPivot.localRotation, Quaternion.Euler(idlePose), 1f - Mathf.Exp(-6f * dt));
                    SetGlow(0f);
                    break;
            }
            UpdateTrail();
        }

        void SetGlow(float amount)
        {
            if (bladeMaterial != null) bladeMaterial.color = Color.Lerp(bladeBase, windupGlow, amount);
        }

        /// <summary>A fading streak behind the blade tip while it swings.</summary>
        void UpdateTrail()
        {
            while (trail.Count > 0 && (Time.time - trail[0].time > TrailLife || trail.Count > TrailLinks + 1)) trail.RemoveAt(0);
            if (trail.Count < 2 && trailRoot == null) return;
            if (trailRoot == null) BuildTrail();

            for (int i = 0; i < trailTransforms.Length; i++)
            {
                var link = trailTransforms[i];
                if (i + 1 >= trail.Count)
                {
                    link.localScale = Vector3.zero;
                    continue;
                }
                Vector3 a = trail[i].point, b = trail[i + 1].point;
                Vector3 delta = b - a;
                float width = 0.12f * (1f - (Time.time - trail[i].time) / TrailLife);
                if (delta.sqrMagnitude < 0.000001f || width <= 0f)
                {
                    link.localScale = Vector3.zero;
                    continue;
                }
                link.SetPositionAndRotation((a + b) * 0.5f, Quaternion.LookRotation(delta));
                link.localScale = new Vector3(width, width * 0.3f, delta.magnitude + width);
            }
        }

        void BuildTrail()
        {
            trailRoot = new GameObject("SwordTrail").transform; // world space: the trail stays where the blade went
            trailTransforms = new Transform[TrailLinks];
            for (int i = 0; i < TrailLinks; i++)
            {
                var link = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(link.GetComponent<Collider>());
                var renderer = link.GetComponent<Renderer>();
                if (trailMaterial != null) renderer.sharedMaterial = trailMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                link.transform.SetParent(trailRoot, false);
                link.transform.localScale = Vector3.zero;
                trailTransforms[i] = link.transform;
            }
        }

        public override void OnDestroy()
        {
            if (trailRoot != null) Destroy(trailRoot.gameObject);
            if (bladeMaterial != null) Destroy(bladeMaterial);
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
