using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Ballistic flight shared by the thrown grenade and its aiming preview, so the arc you see is the arc it flies.
    /// Collisions skip players (no friendly hits), triggers and loose physics debris.
    /// </summary>
    public static class GrenadeArc
    {
        public static Vector3 PositionAt(Vector3 start, Vector3 velocity, float gravity, float time)
            => start + velocity * time + Vector3.down * (0.5f * gravity * time * time);

        /// <summary>
        /// First solid thing a sphere of <paramref name="radius"/> hits moving from → to, skipping anything under
        /// <paramref name="ignore"/> (e.g. the robot that threw it).
        /// </summary>
        public static bool Sweep(Vector3 from, Vector3 to, float radius, out RaycastHit nearest, Transform ignore = null)
        {
            nearest = default;
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.0001f) return false;

            float best = float.MaxValue;
            foreach (var hit in Physics.SphereCastAll(from, radius, delta / distance, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.distance >= best || Ignored(hit.collider)) continue;
                if (ignore != null && hit.collider.transform.IsChildOf(ignore)) continue;
                best = hit.distance;
                nearest = hit;
            }
            if (best == float.MaxValue) return false;
            // Already overlapping at the start: SphereCast reports zero distance and no point.
            if (nearest.distance <= 0f) nearest.point = from;
            return true;
        }

        static bool Ignored(Collider collider)
        {
            if (collider.GetComponentInParent<PlayerMovement>() != null) return true;
            var body = collider.attachedRigidbody;
            // Flying debris from broken crates; a physics crate (it has health) still stops the grenade.
            return body != null && !body.isKinematic && body.GetComponent<Damageable>() == null;
        }
    }

    /// <summary>Whoever threw a <see cref="GrenadeProjectile"/> and decides what its impact does.</summary>
    public interface IGrenadeThrower
    {
        void OnGrenadeImpact(int id, Vector3 point);
    }

    /// <summary>
    /// A grenade in flight. Every client flies its own copy along the same arc (same start, velocity, gravity);
    /// it reports the impact to its thrower (<see cref="GrenadeWeapon"/>, <see cref="AllyRobot"/>), which decides
    /// what happens (the server's copy is the one that deals damage). If the thrower is gone by then, it fizzles.
    /// </summary>
    public class GrenadeProjectile : MonoBehaviour
    {
        const float MaxLifetime = 4f;

        IGrenadeThrower owner;
        int id;
        Vector3 start, velocity;
        float gravity, radius, startTime;
        Vector3 spinAxis;
        Transform ignore;

        public static GrenadeProjectile Spawn(IGrenadeThrower owner, int id, Vector3 start, Vector3 velocity, float gravity, float radius,
            Material bodyMaterial, Material bandMaterial, float size = 1f, Transform ignore = null)
        {
            var go = new GameObject("Grenade");
            go.transform.position = start;
            var projectile = go.AddComponent<GrenadeProjectile>();
            projectile.owner = owner;
            projectile.id = id;
            projectile.start = start;
            projectile.velocity = velocity;
            projectile.gravity = gravity;
            projectile.radius = radius;
            projectile.startTime = Time.time;
            projectile.spinAxis = Random.onUnitSphere;
            projectile.ignore = ignore;

            AddPiece(go.transform, Vector3.one * (0.24f * size), bodyMaterial);
            AddPiece(go.transform, new Vector3(0.27f, 0.07f, 0.27f) * size, bandMaterial);
            return projectile;
        }

        static void AddPiece(Transform parent, Vector3 scale, Material material)
        {
            var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(piece.GetComponent<Collider>());
            if (material != null) piece.GetComponent<Renderer>().sharedMaterial = material;
            piece.transform.SetParent(parent, false);
            piece.transform.localScale = scale;
        }

        void Update()
        {
            float age = Time.time - startTime;
            Vector3 from = transform.position;
            Vector3 to = GrenadeArc.PositionAt(start, velocity, gravity, age);
            transform.Rotate(spinAxis, 720f * Time.deltaTime, Space.World);

            if (GrenadeArc.Sweep(from, to, radius, out RaycastHit hit, ignore))
            {
                Impact(hit.point);
                return;
            }
            transform.position = to;
            if (age > MaxLifetime) Impact(to);
        }

        void Impact(Vector3 point)
        {
            enabled = false;
            // A thrower that's been destroyed (a robot taken out mid-throw) still compares unequal to null here.
            bool ownerGone = owner == null || (owner is Object unityOwner && unityOwner == null);
            if (!ownerGone) owner.OnGrenadeImpact(id, point);
            Destroy(gameObject);
        }
    }
}
