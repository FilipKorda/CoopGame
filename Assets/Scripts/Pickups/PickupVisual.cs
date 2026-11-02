using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Local-only placeholder pickup: bursts out on an arc, spins on the ground, and when claimed flies into
    /// the collecting player. It has no gameplay authority; <see cref="PickupSystem"/> decides everything.
    /// </summary>
    public class PickupVisual : MonoBehaviour
    {
        public const float ArcDuration = 0.6f;
        const float ArcHeight = 1.6f;
        const float RestHeight = 0.25f;

        Vector3 origin;
        Vector3 landing;
        float spawnTime;
        float size;
        float spinSpeed;
        bool pulse;
        Transform flyTarget;
        float flyHeight;
        float flySpeed;
        float timeOffset;

        /// <summary>Position along the burst arc; shared with the server so it measures the same spot.</summary>
        public static Vector3 EvaluateArc(Vector3 origin, Vector3 landing, float age)
        {
            Vector3 rest = landing + Vector3.up * RestHeight;
            float t = Mathf.Clamp01(age / ArcDuration);
            Vector3 position = Vector3.Lerp(origin, rest, t);
            position.y += ArcHeight * 4f * t * (1f - t);
            return position;
        }

        public static PickupVisual Create(PickupKind kind, Material material, Vector3 origin, Vector3 landing, bool alreadyLanded)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = kind.ToString();
            Destroy(go.GetComponent<Collider>());
            if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;

            var visual = go.AddComponent<PickupVisual>();
            // Bolts: small fast-spinning gold cubes. Health: bigger, slower, pulsing green cubes.
            visual.size = kind == PickupKind.Health ? 0.3f : 0.22f;
            visual.spinSpeed = kind == PickupKind.Health ? 120f : 270f;
            visual.pulse = kind == PickupKind.Health;
            visual.origin = origin;
            visual.landing = landing;
            visual.spawnTime = alreadyLanded ? Time.time - ArcDuration : Time.time;
            visual.timeOffset = Random.value * 10f;
            go.transform.localScale = Vector3.one * visual.size;
            go.transform.position = EvaluateArc(origin, landing, Time.time - visual.spawnTime);
            return visual;
        }

        public void FlyTo(Transform target, float height)
        {
            flyTarget = target;
            flyHeight = height;
            flySpeed = 4f;
        }

        void Update()
        {
            float time = Time.time + timeOffset;
            transform.rotation = Quaternion.Euler(45f, time * spinSpeed, 45f);

            if (flyTarget != null)
            {
                // Accelerate into the player, like being sucked in.
                flySpeed += 40f * Time.deltaTime;
                Vector3 goal = flyTarget.position + Vector3.up * flyHeight;
                transform.position = Vector3.MoveTowards(transform.position, goal, flySpeed * Time.deltaTime);
                transform.localScale = Vector3.one * size * Mathf.Clamp01(Vector3.Distance(transform.position, goal) / 1.5f + 0.4f);
                if ((transform.position - goal).sqrMagnitude < 0.04f) Destroy(gameObject);
                return;
            }

            if (pulse) transform.localScale = Vector3.one * size * (1f + 0.15f * Mathf.Sin(time * 6f));

            Vector3 position = EvaluateArc(origin, landing, Time.time - spawnTime);
            if (Time.time - spawnTime >= ArcDuration)
                position.y += Mathf.Sin(time * 3f) * 0.08f;
            transform.position = position;
        }
    }
}
