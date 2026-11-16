using UnityEngine;

namespace CoopGame
{
    /// <summary>Flight settings for <see cref="RocketProjectile"/>, set on the launcher.</summary>
    [System.Serializable]
    public struct RocketFlight
    {
        public float launchSpeed;
        public float maxSpeed;
        public float acceleration;
        [Tooltip("Turn rate at launch, degrees per second; it tightens the longer the rocket flies.")]
        public float turnRate;
        [Tooltip("Seconds of flying straight out of the tube before homing in.")]
        public float homingDelay;
        public float lifetime;
        public float radius;

        public static RocketFlight Default => new RocketFlight
        {
            launchSpeed = 9f, maxSpeed = 26f, acceleration = 45f, turnRate = 280f, homingDelay = 0.12f, lifetime = 5f, radius = 0.15f,
        };
    }

    /// <summary>
    /// A homing rocket. It leaves the tube along its launch direction, then turns towards its target (following it
    /// as it moves) and goes off on the first solid thing it touches. With no target, or once the target is gone,
    /// it flies to the last known spot. Every client flies its own copy and reports the impact to its
    /// <see cref="RocketLauncherWeapon"/>; the server's copy is the one that deals damage.
    /// </summary>
    public class RocketProjectile : MonoBehaviour
    {
        const float PuffInterval = 0.03f;

        RocketLauncherWeapon owner;
        int id;
        Targetable target;
        Vector3 targetPoint;
        Vector3 direction;
        float speed;
        RocketFlight flight;
        float startTime;
        float nextPuffTime;
        Transform flame;
        Material smokeMaterial;

        public static RocketProjectile Spawn(RocketLauncherWeapon owner, int id, Vector3 start, Vector3 launchDirection, Targetable target,
            Vector3 targetPoint, RocketFlight flight, Material body, Material nose, Material flameMaterial, Material smoke)
        {
            var go = new GameObject("Rocket");
            go.transform.SetPositionAndRotation(start, Quaternion.LookRotation(launchDirection));
            var rocket = go.AddComponent<RocketProjectile>();
            rocket.owner = owner;
            rocket.id = id;
            rocket.target = target;
            rocket.targetPoint = targetPoint;
            rocket.direction = launchDirection.normalized;
            rocket.flight = flight;
            rocket.speed = flight.launchSpeed;
            rocket.startTime = Time.time;
            rocket.smokeMaterial = smoke;

            AddPiece(go.transform, new Vector3(0f, 0f, 0f), new Vector3(0.12f, 0.12f, 0.42f), body);
            AddPiece(go.transform, new Vector3(0f, 0f, 0.25f), new Vector3(0.09f, 0.09f, 0.1f), nose);
            rocket.flame = AddPiece(go.transform, new Vector3(0f, 0f, -0.27f), new Vector3(0.1f, 0.1f, 0.14f), flameMaterial);
            return rocket;
        }

        static Transform AddPiece(Transform parent, Vector3 localPosition, Vector3 scale, Material material)
        {
            var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(piece.GetComponent<Collider>());
            var renderer = piece.GetComponent<Renderer>();
            if (material != null) renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            piece.transform.SetParent(parent, false);
            piece.transform.localPosition = localPosition;
            piece.transform.localScale = scale;
            return piece.transform;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            float age = Time.time - startTime;
            bool tracking = target != null && target.IsTargetable;
            if (tracking) targetPoint = target.AimPoint;

            speed = Mathf.MoveTowards(speed, flight.maxSpeed, flight.acceleration * dt);
            Vector3 position = transform.position;
            Vector3 toTarget = targetPoint - position;
            if (age >= flight.homingDelay && toTarget.sqrMagnitude > 0.0001f)
            {
                // Tighten the turn over time so a rocket never circles its target for long; close in, aim straight.
                float rate = flight.turnRate * (1f + 2f * age) * Mathf.Deg2Rad;
                direction = toTarget.sqrMagnitude < 4f
                    ? toTarget.normalized
                    : Vector3.RotateTowards(direction, toTarget.normalized, rate * dt, 0f).normalized;
            }

            Vector3 next = position + direction * (speed * dt);
            if (GrenadeArc.Sweep(position, next, flight.radius, out RaycastHit hit))
            {
                Impact(hit.point);
                return;
            }
            // Target gone (or none): go off at the spot it was heading for.
            if (!tracking && (targetPoint - next).sqrMagnitude < 0.25f)
            {
                Impact(next);
                return;
            }

            transform.SetPositionAndRotation(next, Quaternion.LookRotation(direction));
            flame.localScale = new Vector3(0.1f, 0.1f, Random.Range(0.1f, 0.25f));
            if (Time.time >= nextPuffTime)
            {
                nextPuffTime = Time.time + PuffInterval;
                SmokePuff.Spawn(position - direction * 0.3f, smokeMaterial);
            }
            if (age > flight.lifetime) Impact(next);
        }

        void Impact(Vector3 point)
        {
            enabled = false;
            if (owner != null) owner.OnRocketImpact(id, point);
            Destroy(gameObject);
        }
    }

    /// <summary>A bit of rocket exhaust: a small grey cube that swells and fades away.</summary>
    public class SmokePuff : MonoBehaviour
    {
        const float Lifetime = 0.45f;
        float startTime;

        public static void Spawn(Vector3 position, Material material)
        {
            var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            piece.name = "SmokePuff";
            Destroy(piece.GetComponent<Collider>());
            var renderer = piece.GetComponent<Renderer>();
            if (material != null) renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            piece.transform.SetPositionAndRotation(position, Random.rotation);
            piece.transform.localScale = Vector3.one * 0.1f;
            piece.AddComponent<SmokePuff>().startTime = Time.time;
        }

        void Update()
        {
            float t = (Time.time - startTime) / Lifetime;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }
            // Grows a little, then shrinks to nothing.
            transform.localScale = Vector3.one * (0.1f + 0.2f * Mathf.Sin(t * Mathf.PI));
            transform.position += Vector3.up * (0.4f * Time.deltaTime);
        }
    }
}
