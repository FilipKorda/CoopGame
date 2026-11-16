using System.Collections.Generic;
using UnityEngine;

namespace CoopGame
{
    /// <summary>How a freeze ball flies and bounces; shared by the ball itself and the aim preview.</summary>
    [System.Serializable]
    public struct BounceSettings
    {
        public float gravity;
        public float radius;
        [Tooltip("Bounces off the surroundings; the next touch after the last one shatters it.")]
        public int bounces;
        [Tooltip("Share of the speed into a surface it keeps when it bounces off.")]
        public float restitution;
        [Tooltip("Share of the speed along a surface it keeps when it bounces off.")]
        public float friction;
        [Tooltip("Every bounce off the floor hops at least this high, so each one is a proper arc.")]
        public float minHop;
        [Tooltip("Seconds before it shatters anyway (flew off into nothing).")]
        public float maxLifetime;
    }

    /// <summary>The state of one freeze ball in flight.</summary>
    public struct BallFlight
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public int Bounces;
        public float Age;
        public bool Ended;
    }

    /// <summary>
    /// Fixed-step flight of a bouncing ball: falls under gravity, bounces off walls, floors, crates and the like,
    /// and flies straight through enemies, players and the charm robot. Deterministic for a given start, so the aim
    /// preview and every client's copy of the ball follow the same path.
    /// </summary>
    public static class BounceArc
    {
        public const float Step = 1f / 60f;

        static readonly List<(EnemyController enemy, float distance)> s_EnemyHits = new();
        static readonly RaycastHit[] s_Hits = new RaycastHit[32]; // the aim preview runs hundreds of steps a frame

        /// <summary>
        /// Advance <paramref name="ball"/> one <see cref="Step"/>. Returns true if it touched a surface this step
        /// (<paramref name="contact"/>: where); it has <see cref="BallFlight.Ended"/> once it touches something after
        /// its last bounce. Enemies it passes through this step are added to <paramref name="passed"/> (if given).
        /// </summary>
        public static bool Advance(ref BallFlight ball, in BounceSettings settings, List<EnemyController> passed, out Vector3 contact)
        {
            contact = ball.Position;
            if (ball.Ended) return false;

            ball.Age += Step;
            Vector3 velocity = ball.Velocity + Vector3.down * (settings.gravity * Step);
            Vector3 move = (ball.Velocity + velocity) * (0.5f * Step);
            float distance = move.magnitude;
            if (ball.Age > settings.maxLifetime || ball.Position.y < -50f) ball.Ended = true;
            if (distance < 0.00001f || ball.Ended)
            {
                ball.Velocity = velocity;
                return false;
            }

            Vector3 direction = move / distance;
            RaycastHit surface = default;
            float nearest = float.MaxValue;
            s_EnemyHits.Clear();
            int count = Physics.SphereCastNonAlloc(ball.Position, settings.radius, direction, s_Hits, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = s_Hits[i];
                var enemy = hit.collider.GetComponentInParent<EnemyController>();
                if (enemy != null)
                {
                    s_EnemyHits.Add((enemy, hit.distance));
                    continue;
                }
                if (hit.collider.GetComponentInParent<IEnemyTarget>() != null) continue; // players, the charm robot
                if (hit.distance <= 0f && hit.point == Vector3.zero) continue;            // already touching it (just bounced off it)
                if (hit.distance >= nearest) continue;
                nearest = hit.distance;
                surface = hit;
            }

            if (passed != null)
                foreach (var (enemy, at) in s_EnemyHits)
                    if (at <= nearest && !passed.Contains(enemy)) passed.Add(enemy);

            float into = nearest < float.MaxValue ? Vector3.Dot(velocity, surface.normal) : 0f;
            if (into >= 0f)
            {
                // Nothing in the way (or only grazing it).
                ball.Position += move;
                ball.Velocity = velocity;
                return false;
            }

            ball.Position += direction * nearest + surface.normal * 0.01f;
            contact = surface.point;
            ball.Bounces++;
            if (ball.Bounces > settings.bounces)
            {
                ball.Ended = true;
                ball.Velocity = Vector3.zero;
                return true;
            }

            Vector3 normalPart = into * surface.normal;
            velocity = (velocity - normalPart) * settings.friction - normalPart * settings.restitution;
            if (surface.normal.y > 0.6f)
                velocity.y = Mathf.Max(velocity.y, Mathf.Sqrt(2f * settings.gravity * settings.minHop));
            ball.Velocity = velocity;
            return true;
        }
    }

    /// <summary>
    /// One client's copy of a freeze ball (every client flies its own; see <see cref="FreezeBallWeapon"/>). Flies
    /// <see cref="BounceArc"/>, frosting the air behind it, and shatters into ice after its last bounce. The server's
    /// copy reports every enemy it passes through.
    /// </summary>
    public class FreezeBallProjectile : MonoBehaviour
    {
        FreezeBallWeapon owner;
        BounceSettings settings;
        BallFlight flight;
        Material material;
        float pending;
        float nextFrostTime;
        Transform ball;
        readonly List<EnemyController> passed = new();
        int reported;

        public static FreezeBallProjectile Spawn(FreezeBallWeapon owner, Vector3 start, Vector3 velocity, in BounceSettings settings, Material material)
        {
            var root = new GameObject("FreezeBall");
            root.transform.position = start;
            var projectile = root.AddComponent<FreezeBallProjectile>();
            projectile.owner = owner;
            projectile.settings = settings;
            projectile.material = material;
            projectile.flight = new BallFlight { Position = start, Velocity = velocity };

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(sphere.GetComponent<Collider>());
            if (material != null) sphere.GetComponent<Renderer>().sharedMaterial = material;
            sphere.transform.SetParent(root.transform, false);
            sphere.transform.localScale = Vector3.one * (settings.radius * 2f);
            projectile.ball = sphere.transform;
            // A few crystals sticking out, so the spin shows.
            for (int i = 0; i < 4; i++)
            {
                var crystal = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(crystal.GetComponent<Collider>());
                if (material != null) crystal.GetComponent<Renderer>().sharedMaterial = material;
                crystal.transform.SetParent(sphere.transform, false);
                crystal.transform.localRotation = Random.rotation;
                crystal.transform.localPosition = crystal.transform.localRotation * Vector3.forward * 0.45f;
                crystal.transform.localScale = new Vector3(0.3f, 0.3f, 0.6f);
            }
            return projectile;
        }

        void Update()
        {
            // The weapon (its player) is gone: so is the ball.
            if (owner == null)
            {
                Destroy(gameObject);
                return;
            }

            pending += Time.deltaTime;
            bool server = owner.IsServer;
            while (pending >= BounceArc.Step && !flight.Ended)
            {
                pending -= BounceArc.Step;
                if (BounceArc.Advance(ref flight, settings, server ? passed : null, out Vector3 contact) && !flight.Ended)
                    IceBurst(contact, 4, 3f);
            }

            if (server)
                for (; reported < passed.Count; reported++)
                    owner.OnBallPassedEnemy(passed[reported]);

            transform.position = flight.Position;
            ball.Rotate(new Vector3(9f, 4f, 0f) * (flight.Velocity.magnitude * Time.deltaTime * 6f), Space.Self);
            if (Time.time >= nextFrostTime)
            {
                nextFrostTime = Time.time + 0.03f;
                SmokePuff.Spawn(flight.Position, material);
            }

            if (flight.Ended)
            {
                IceBurst(flight.Position, 12, 5f);
                Destroy(gameObject);
            }
        }

        void IceBurst(Vector3 point, int count, float speed)
        {
            for (int i = 0; i < count; i++)
            {
                var shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
                shard.name = "IceShard";
                Destroy(shard.GetComponent<Collider>());
                if (material != null) shard.GetComponent<Renderer>().sharedMaterial = material;
                shard.transform.SetPositionAndRotation(point + Vector3.up * 0.1f, Random.rotation);
                shard.transform.localScale = Vector3.one * Random.Range(0.06f, 0.14f);
                var body = shard.AddComponent<Rigidbody>();
                body.linearVelocity = (Random.onUnitSphere + Vector3.up) * speed;
                Destroy(shard, 0.5f);
            }
        }
    }
}
