using UnityEngine;

namespace CoopGame
{
    /// <summary>Kinematic box movement that stops at whatever is in the way (used by objects the wrench beam moves).</summary>
    public static class ObjectSweep
    {
        const float Skin = 0.02f;

        /// <summary>
        /// How much of <paramref name="step"/> the box can travel before touching something that isn't part of
        /// <paramref name="owner"/>. Returns the allowed step (same direction, possibly shorter).
        /// </summary>
        public static Vector3 Sweep(BoxCollider box, Transform owner, Vector3 step, LayerMask mask)
        {
            float distance = step.magnitude;
            if (distance < 0.0001f || box == null) return step;

            Vector3 direction = step / distance;
            Vector3 center = box.transform.TransformPoint(box.center);
            Vector3 halfExtents = Vector3.Scale(box.size, box.transform.lossyScale) * 0.5f * 0.98f;
            float allowed = distance;
            foreach (var hit in Physics.BoxCastAll(center, halfExtents, direction, box.transform.rotation, distance, mask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(owner)) continue;

                if (hit.distance <= 0f)
                {
                    // Already touching (resting on the ground, pushed against a wall): it only blocks moving into it,
                    // so a box on the floor can still slide sideways but never sinks through.
                    Vector3 toward = hit.collider.ClosestPoint(center) - center;
                    if (toward.sqrMagnitude > 0.000001f && Vector3.Dot(toward.normalized, direction) > 0.3f) return Vector3.zero;
                    continue;
                }
                allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - Skin));
            }
            return direction * allowed;
        }
    }
}
