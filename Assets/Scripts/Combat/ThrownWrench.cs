using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// The wrench in flight, simulated locally on every client. It spins out to the locked target (homing on it)
    /// or straight along the aim until it hits something or reaches max range, then boomerangs back to the hand.
    /// Only the thrower's copy reports hits (<see cref="WrenchThrow"/> forwards them to the server).
    /// </summary>
    public class ThrownWrench : MonoBehaviour
    {
        WrenchThrow thrower;
        Transform hand;
        Targetable target;
        bool hasTarget;
        bool reportsHits;
        Vector3 targetPoint;
        Vector3 direction;
        float traveled;
        float flightTime;
        float speed;
        bool returning;
        WrenchThrow.FlightSettings settings;

        public static ThrownWrench Launch(WrenchThrow thrower, Transform heldWrench, Vector3 origin, Targetable target, Vector3 direction,
            bool reportsHits, WrenchThrow.FlightSettings settings)
        {
            // A copy of the held wrench meshes flies; the held one is hidden until the catch.
            var go = Instantiate(heldWrench.gameObject, origin, Quaternion.LookRotation(direction));
            go.name = "ThrownWrench";
            go.SetActive(true);

            var wrench = go.AddComponent<ThrownWrench>();
            wrench.thrower = thrower;
            wrench.hand = heldWrench;
            wrench.target = target;
            wrench.hasTarget = target != null;
            wrench.targetPoint = target != null ? target.AimPoint : origin + direction * settings.MaxDistance;
            wrench.direction = direction.normalized;
            wrench.reportsHits = reportsHits;
            wrench.settings = settings;
            wrench.speed = settings.ThrowSpeed;
            return wrench;
        }

        void Update()
        {
            if (thrower == null || hand == null)
            {
                Destroy(gameObject);
                return;
            }

            float dt = Time.deltaTime;
            flightTime += dt;
            transform.Rotate(Vector3.up, settings.SpinSpeed * dt, Space.Self);

            if (returning) FlyBack(dt);
            else if (hasTarget) FlyToTarget(dt);
            else FlyStraight(dt);
        }

        void FlyToTarget(float dt)
        {
            // Home in while the target lives; otherwise finish at its last known spot.
            if (target != null && target.IsTargetable) targetPoint = target.AimPoint;

            transform.position = Vector3.MoveTowards(transform.position, targetPoint, speed * dt);
            if ((transform.position - targetPoint).sqrMagnitude <= settings.HitRadius * settings.HitRadius)
            {
                if (reportsHits && target != null && target.IsTargetable && target.Damageable != null)
                    thrower.ReportHit(target.Damageable, (targetPoint - hand.position).normalized);
                returning = true;
            }
            else if (flightTime > settings.MaxOutboundTime)
            {
                returning = true;
            }
        }

        void FlyStraight(float dt)
        {
            float step = speed * dt;
            if (SweepForHit(transform.position, step, out RaycastHit hit))
            {
                transform.position = hit.point;
                var damageable = hit.collider.GetComponentInParent<Damageable>();
                if (reportsHits && damageable != null && damageable.IsSpawned && !damageable.IsDead)
                    thrower.ReportHit(damageable, direction);
                returning = true;
                return;
            }

            transform.position += direction * step;
            traveled += step;
            if (traveled >= settings.MaxDistance) returning = true;
        }

        void FlyBack(float dt)
        {
            speed = Mathf.Max(speed, settings.ReturnSpeed) + settings.ReturnAcceleration * dt;
            transform.position = Vector3.MoveTowards(transform.position, hand.position, speed * dt);
            if ((transform.position - hand.position).sqrMagnitude <= settings.CatchRadius * settings.CatchRadius)
            {
                thrower.OnWrenchCaught(this);
                Destroy(gameObject);
            }
        }

        /// <summary>First solid thing ahead that isn't a player (the wrench flies through teammates).</summary>
        bool SweepForHit(Vector3 from, float distance, out RaycastHit nearest)
        {
            nearest = default;
            float best = float.MaxValue;
            foreach (var hit in Physics.SphereCastAll(from, settings.HitRadius, direction, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<PlayerMovement>() != null) continue;
                // A sweep starting inside a collider reports distance 0 and no point; skip those.
                if (hit.distance <= 0f || hit.distance >= best) continue;
                best = hit.distance;
                nearest = hit;
            }
            return best < float.MaxValue;
        }
    }
}
