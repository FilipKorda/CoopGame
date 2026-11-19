using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Basic enemy attack: crouch (telegraph), leap at where the target stood, deal damage on contact.
    /// On a miss the enemy hops back to where it jumped from; on a hit it bounces back there too
    /// unless <see cref="returnAfterHit"/> is off. A charmed enemy only hurts the robot that charmed it.
    /// </summary>
    public class LeapAttack : EnemyAttack
    {
        [Header("Damage")]
        [SerializeField] int damage = 1;
        [Tooltip("Radius of the contact capsule around the enemy's body; reaches below the feet so landing on a player's head counts.")]
        [SerializeField] float hitRadius = 0.75f;
        [SerializeField] float hitCapsuleBottom = 0.1f;
        [SerializeField] float hitCapsuleTop = 1.3f;

        [Header("Timing")]
        [SerializeField] float windupTime = 0.5f;
        [SerializeField] float leapHorizontalSpeed = 9f;
        [SerializeField] float leapHeight = 2.2f;
        [SerializeField] float recoverTime = 0.35f;
        [SerializeField] float returnDuration = 0.6f;
        [SerializeField] float returnHeight = 1.6f;
        [SerializeField] bool returnAfterHit = true;

        [Header("Visual")]
        [SerializeField] Transform visual;
        [SerializeField] float windupSquash = 0.65f;

        [SerializeField] LayerMask groundMask = ~0;

        enum Phase { Windup, Leap, Recover, Return }

        readonly NetworkVariable<bool> isWindingUp = new();

        Phase phase;
        float phaseTime;
        Vector3 jumpOrigin;
        Vector3 arcFrom;
        Vector3 arcTo;
        float arcDuration;
        float arcHeight;
        bool hitPlayer;

        void Reset()
        {
            minRange = 2f;
            maxRange = 7f;
            cooldown = 2.5f;
        }

        protected override void OnBegin()
        {
            phase = Phase.Windup;
            phaseTime = 0f;
            hitPlayer = false;
            jumpOrigin = transform.position;
            isWindingUp.Value = true;
        }

        protected override bool OnTick(float dt)
        {
            phaseTime += dt;
            switch (phase)
            {
                case Phase.Windup:
                    if (TargetAlive) Enemy.FaceTowards(Target.transform.position, dt);
                    if (phaseTime < windupTime) return true;
                    isWindingUp.Value = false;
                    if (!TargetAlive) return false;

                    // Commit to where the target is now; players can dodge by moving away.
                    Vector3 landing = GroundBelow(Target.transform.position);
                    float distance = Vector3.ProjectOnPlane(landing - transform.position, Vector3.up).magnitude;
                    StartArc(Phase.Leap, landing, Mathf.Clamp(distance / leapHorizontalSpeed, 0.35f, 0.9f), leapHeight);
                    return true;

                case Phase.Leap:
                    MoveAlongArc();
                    if (TryHitPlayer())
                    {
                        hitPlayer = true;
                        if (returnAfterHit) StartArc(Phase.Return, jumpOrigin, returnDuration, returnHeight);
                        else EnterRecover();
                        return true;
                    }
                    if (phaseTime >= arcDuration) EnterRecover();
                    return true;

                case Phase.Recover:
                    if (phaseTime < recoverTime) return true;
                    // Missed: hop back to where the leap started.
                    if (!hitPlayer)
                    {
                        StartArc(Phase.Return, jumpOrigin, returnDuration, returnHeight);
                        return true;
                    }
                    return false;

                case Phase.Return:
                    MoveAlongArc();
                    return phaseTime < arcDuration;
            }
            return false;
        }

        protected override void OnCancel()
        {
            isWindingUp.Value = false;
        }

        void EnterRecover()
        {
            phase = Phase.Recover;
            phaseTime = 0f;
        }

        void StartArc(Phase next, Vector3 to, float duration, float height)
        {
            phase = next;
            phaseTime = 0f;
            arcFrom = transform.position;
            arcTo = to;
            arcDuration = duration;
            arcHeight = height;
        }

        void MoveAlongArc()
        {
            float t = Mathf.Clamp01(phaseTime / arcDuration);
            Vector3 desired = Vector3.Lerp(arcFrom, arcTo, t) + Vector3.up * (arcHeight * 4f * t * (1f - t));
            Enemy.Motor.Move(desired - transform.position);
        }

        bool TryHitPlayer()
        {
            Vector3 bottom = transform.position + Vector3.up * hitCapsuleBottom;
            Vector3 top = transform.position + Vector3.up * hitCapsuleTop;
            foreach (var collider in Physics.OverlapCapsule(bottom, top, hitRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                var victim = Enemy.VictimOf(collider);
                if (victim == null || !victim.IsAlive) continue;
                // Charmed: it only has eyes for the robot; players it lands next to are left alone.
                if (Enemy.IsCharmed && victim != Target) continue;

                Vector3 push = victim.transform.position - transform.position;
                victim.ApplyDamage(new DamageInfo(damage, push, gameObject));
                return true;
            }
            return false;
        }

        Vector3 GroundBelow(Vector3 point)
        {
            // Players may be mid-jump; aim for the ground under them.
            var hits = Physics.RaycastAll(point + Vector3.up * 0.5f, Vector3.down, 30f, groundMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 ground = point;
            foreach (var hit in hits)
            {
                if (hit.collider is CharacterController || hit.collider.GetComponentInParent<Damageable>() != null) continue;
                if (hit.distance < best)
                {
                    best = hit.distance;
                    ground = hit.point;
                }
            }
            return ground;
        }

        void Update()
        {
            if (visual == null) return;
            Vector3 scale = visual.localScale;
            scale.y = Mathf.MoveTowards(scale.y, isWindingUp.Value ? windupSquash : 1f, 4f * Time.deltaTime);
            visual.localScale = scale;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * hitCapsuleBottom, hitRadius);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * hitCapsuleTop, hitRadius);
            Gizmos.color = new Color(1f, 0.5f, 0f);
            Gizmos.DrawWireSphere(transform.position, maxRange);
        }
    }
}
