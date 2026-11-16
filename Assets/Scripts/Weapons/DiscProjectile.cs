using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// A spinning disc from the <see cref="DiscLauncherWeapon"/>. It flies one leg at a time: straight at its target
    /// (following it as it moves) or, with no target, straight ahead until it hits something or runs out of reach.
    /// At the end of a leg it asks its launcher what happens next; the server's copy decides (bounce on to another
    /// enemy, or stop) and the other copies wait for that word, so everyone sees the same bounces.
    /// </summary>
    public class DiscProjectile : MonoBehaviour
    {
        const float TrailInterval = 0.04f;
        const float WaitTimeout = 1.5f;

        DiscLauncherWeapon owner;
        Targetable target;
        Vector3 targetPoint;
        Vector3 direction;
        float speed, radius, reach, travelled;
        bool waiting;
        float waitStart;
        Transform ignore;
        float nextTrailTime;
        Transform visual;
        Material trailMaterial;

        public int Id { get; private set; }
        /// <summary>Server: how many things this disc has hit so far.</summary>
        public int Hits { get; set; }
        /// <summary>Server: the last thing it hit (the next bounce skips it).</summary>
        public Damageable LastHit { get; set; }
        public Targetable Target => target;
        public Vector3 Direction => direction;

        public static DiscProjectile Spawn(DiscLauncherWeapon owner, int id, Vector3 from, Vector3 direction, Targetable target,
            float speed, float radius, float reach, Material discMaterial, Material hubMaterial)
        {
            var go = new GameObject("Disc");
            var disc = go.AddComponent<DiscProjectile>();
            disc.owner = owner;
            disc.Id = id;
            disc.speed = speed;
            disc.radius = radius;
            disc.trailMaterial = discMaterial;

            disc.visual = new GameObject("Spin").transform;
            disc.visual.SetParent(go.transform, false);
            AddPiece(disc.visual, new Vector3(0.44f, 0.05f, 0.44f), Quaternion.identity, discMaterial);
            AddPiece(disc.visual, new Vector3(0.44f, 0.05f, 0.44f), Quaternion.Euler(0f, 45f, 0f), discMaterial);
            AddPiece(disc.visual, new Vector3(0.16f, 0.08f, 0.16f), Quaternion.identity, hubMaterial);

            disc.StartLeg(from, direction, target, reach);
            return disc;
        }

        static void AddPiece(Transform parent, Vector3 scale, Quaternion rotation, Material material)
        {
            var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(piece.GetComponent<Collider>());
            var renderer = piece.GetComponent<Renderer>();
            if (material != null) renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            piece.transform.SetParent(parent, false);
            piece.transform.localRotation = rotation;
            piece.transform.localScale = scale;
        }

        /// <summary>
        /// Start a new leg from <paramref name="from"/>: at <paramref name="newTarget"/>, or along
        /// <paramref name="newDirection"/>, passing through <paramref name="ignoreOnTheWay"/> (what it bounced off).
        /// </summary>
        public void StartLeg(Vector3 from, Vector3 newDirection, Targetable newTarget, float newReach, Transform ignoreOnTheWay = null)
        {
            transform.position = from;
            target = newTarget;
            ignore = ignoreOnTheWay;
            targetPoint = newTarget != null ? newTarget.AimPoint : from + newDirection * newReach;
            direction = (targetPoint - from).sqrMagnitude > 0.0001f ? (targetPoint - from).normalized : newDirection.normalized;
            reach = newReach;
            travelled = 0f;
            waiting = false;
            visual.gameObject.SetActive(true);
        }

        /// <summary>Client copy: this leg is done; hide and wait for the server to say bounce or stop.</summary>
        public void Wait()
        {
            waiting = true;
            waitStart = Time.time;
            visual.gameObject.SetActive(false);
        }

        void Update()
        {
            if (waiting)
            {
                if (Time.time - waitStart > WaitTimeout) Destroy(gameObject); // the word never came
                return;
            }

            float dt = Time.deltaTime;
            visual.Rotate(Vector3.up, 1440f * dt, Space.Self);

            if (target != null && target.IsTargetable) targetPoint = target.AimPoint;
            Vector3 position = transform.position;
            Vector3 toTarget = targetPoint - position;
            if (toTarget.sqrMagnitude > 0.0001f) direction = toTarget.normalized;

            float step = speed * dt;
            Vector3 next = position + direction * step;
            if (GrenadeArc.Sweep(position, next, radius, out RaycastHit hit, ignore))
            {
                transform.position = hit.point;
                owner.OnDiscLegEnd(this, hit.collider, hit.point);
                return;
            }
            travelled += step;
            bool arrived = toTarget.sqrMagnitude <= step * step;
            if (arrived || travelled >= reach)
            {
                transform.position = arrived ? targetPoint : next;
                // Reached the target spot without touching a collider (e.g. it was destroyed): nothing to hit there.
                owner.OnDiscLegEnd(this, null, transform.position);
                return;
            }

            transform.position = next;
            if (Time.time >= nextTrailTime)
            {
                nextTrailTime = Time.time + TrailInterval;
                SmokePuff.Spawn(position, trailMaterial);
            }
        }

        /// <summary>A small burst of glowing chips where a disc hits or bounces.</summary>
        public static void Sparks(Vector3 point, Material material)
        {
            for (int i = 0; i < 6; i++)
            {
                var spark = GameObject.CreatePrimitive(PrimitiveType.Cube);
                spark.name = "DiscSpark";
                Destroy(spark.GetComponent<Collider>());
                if (material != null) spark.GetComponent<Renderer>().sharedMaterial = material;
                spark.transform.SetPositionAndRotation(point, Random.rotation);
                spark.transform.localScale = Vector3.one * 0.08f;
                var body = spark.AddComponent<Rigidbody>();
                body.useGravity = false;
                body.linearVelocity = Random.onUnitSphere * 4f;
                Destroy(spark, 0.3f);
            }
        }
    }
}
