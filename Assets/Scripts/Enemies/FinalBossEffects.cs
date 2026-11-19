using UnityEngine;

namespace CoopGame
{
    /// <summary>Pictures for the final boss fight. Every machine draws its own; only the server (<see cref="FinalBoss"/>) hurts anyone.</summary>
    static class FinalBossFx
    {
        public static Transform Part(PrimitiveType type, string name, Transform parent, Material mat)
        {
            var g = GameObject.CreatePrimitive(type);
            g.name = name;
            Object.Destroy(g.GetComponent<Collider>());
            var r = g.GetComponent<Renderer>();
            if (mat != null) r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (parent != null) g.transform.SetParent(parent, false);
            return g.transform;
        }
    }

    /// <summary>A red warning disc on the floor; it pulses faster as its time runs out (life 0 = stays until destroyed).</summary>
    public class FinalBossMarker : MonoBehaviour
    {
        float start, life, radius;

        public static FinalBossMarker Spawn(Transform parent, Vector3 ground, float radius, float life, Material mat)
        {
            var t = FinalBossFx.Part(PrimitiveType.Cylinder, "Boss Warning", parent, mat);
            var m = t.gameObject.AddComponent<FinalBossMarker>();
            m.start = Time.time; m.life = life; m.radius = radius;
            m.Place(ground);
            m.Update();
            return m;
        }

        public void Place(Vector3 ground) => transform.position = ground + Vector3.up * 0.05f;

        void Update()
        {
            float age = Time.time - start;
            if (life > 0f && age >= life) { Destroy(gameObject); return; }
            float k = life > 0f ? age / life : 0.5f;
            float d = radius * 2f * (1f + 0.07f * Mathf.Sin(age * Mathf.Lerp(8f, 32f, k)));
            transform.localScale = new Vector3(d, 0.02f, d);
        }
    }

    /// <summary>A flashing strip on the floor showing where the charge will go.</summary>
    public class FinalBossLine : MonoBehaviour
    {
        float start, life;
        Renderer look;

        public static void Spawn(Transform parent, Vector3 from, Vector3 dir, float length, float width, float life, Material mat)
        {
            var t = FinalBossFx.Part(PrimitiveType.Cube, "Charge Line", parent, mat);
            t.SetPositionAndRotation(from + dir * (length * 0.5f) + Vector3.up * 0.05f, Quaternion.LookRotation(dir));
            t.localScale = new Vector3(width, 0.03f, Mathf.Max(0.1f, length));
            var l = t.gameObject.AddComponent<FinalBossLine>();
            l.start = Time.time; l.life = life; l.look = t.GetComponent<Renderer>();
        }

        void Update()
        {
            float age = Time.time - start;
            if (age >= life) { Destroy(gameObject); return; }
            look.enabled = Mathf.Repeat(age * Mathf.Lerp(5f, 14f, age / life), 1f) < 0.65f;
        }
    }

    /// <summary>A shockwave: a ring of blocks running out along the floor (jump over it). Blocks past the arena walls are hidden.</summary>
    public class FinalBossWave : MonoBehaviour
    {
        const int Count = 64;
        Transform[] pieces;
        float start, speed, maxRadius, height;
        Vector3 center, boxMin, boxMax;

        public static void Spawn(Transform parent, Vector3 center, float speed, float maxRadius, float height, Vector3 boxMin, Vector3 boxMax, Material mat)
        {
            var go = new GameObject("Boss Shockwave");
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            var w = go.AddComponent<FinalBossWave>();
            w.start = Time.time; w.speed = speed; w.maxRadius = maxRadius; w.height = height;
            w.center = center; w.boxMin = boxMin; w.boxMax = boxMax;
            w.pieces = new Transform[Count];
            for (int i = 0; i < Count; i++) w.pieces[i] = FinalBossFx.Part(PrimitiveType.Cube, "Piece", go.transform, mat);
            w.Update();
        }

        void Update()
        {
            float r = FinalBoss.WaveStartRadius + (Time.time - start) * speed;
            if (r > maxRadius) { Destroy(gameObject); return; }
            float segment = 2f * Mathf.PI * r / Count;
            for (int i = 0; i < Count; i++)
            {
                float a = i * Mathf.PI * 2f / Count;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 p = center + dir * r;
                bool inside = p.x > boxMin.x && p.x < boxMax.x && p.z > boxMin.z && p.z < boxMax.z;
                var piece = pieces[i];
                if (piece.gameObject.activeSelf != inside) piece.gameObject.SetActive(inside);
                if (!inside) continue;
                float h = height * (1f + 0.15f * Mathf.Sin(Time.time * 30f + i));
                piece.SetPositionAndRotation(p + Vector3.up * (h * 0.5f), Quaternion.LookRotation(Vector3.Cross(Vector3.up, dir)));
                piece.localScale = new Vector3(0.35f, h, segment * 1.05f);
            }
        }
    }

    /// <summary>A bomb: shows up under the boss once the warning is over, falls onto its mark and blows up there.</summary>
    public class FinalBossBomb : MonoBehaviour
    {
        Vector3 from, to;
        float dropAt, fall, radius;
        Material fire, core;
        Transform ball;

        public static void Spawn(Transform parent, Vector3 from, Vector3 to, float delay, float fall, float radius, Material shell, Material fire, Material core)
        {
            var go = new GameObject("Boss Bomb");
            go.transform.SetParent(parent, false);
            go.transform.position = from;
            var b = go.AddComponent<FinalBossBomb>();
            b.from = from; b.to = to; b.dropAt = Time.time + delay; b.fall = fall; b.radius = radius; b.fire = fire; b.core = core;
            b.ball = FinalBossFx.Part(PrimitiveType.Sphere, "Ball", go.transform, shell);
            b.ball.localScale = Vector3.one * 0.8f;
            var fuse = FinalBossFx.Part(PrimitiveType.Cube, "Fuse", b.ball, fire);
            fuse.localPosition = Vector3.up * 0.55f;
            fuse.localScale = new Vector3(0.2f, 0.3f, 0.2f);
            b.ball.gameObject.SetActive(false);
        }

        void Update()
        {
            float t = Time.time - dropAt;
            if (t < 0f) return;
            if (!ball.gameObject.activeSelf) ball.gameObject.SetActive(true);
            float k = Mathf.Clamp01(t / Mathf.Max(0.05f, fall));
            transform.position = Vector3.Lerp(from, to, k * k);
            ball.Rotate(400f * Time.deltaTime, 0f, 0f, Space.Self);
            if (k < 1f) return;
            ExplosionEffect.Spawn(to + Vector3.up * 0.5f, radius, fire, core, 0.35f);
            Destroy(gameObject);
        }
    }

    /// <summary>One machine's copy of a boss pistol shot: flies straight until a wall (or the server says it hit someone).</summary>
    public class FinalBossShot : MonoBehaviour
    {
        FinalBoss owner;
        int id;
        Vector3 dir;
        float speed, life;
        Material mat;

        public static FinalBossShot Spawn(FinalBoss owner, Transform parent, int id, Vector3 from, Vector3 dir, float speed, float radius, float life, Material mat)
        {
            var t = FinalBossFx.Part(PrimitiveType.Sphere, "Boss Shot", parent, mat);
            t.SetPositionAndRotation(from, Quaternion.LookRotation(dir));
            t.localScale = new Vector3(radius * 1.6f, radius * 1.6f, radius * 6f);
            var s = t.gameObject.AddComponent<FinalBossShot>();
            s.owner = owner; s.id = id; s.dir = dir; s.speed = speed; s.life = life; s.mat = mat;
            return s;
        }

        void Update()
        {
            float step = speed * Time.deltaTime;
            life -= Time.deltaTime;
            if (owner != null && owner.WallHit(transform.position, dir, step, out Vector3 point)) { End(point); return; }
            transform.position += dir * step;
            if (life <= 0f) End(transform.position);
        }

        public void End(Vector3 point)
        {
            ShotEffects.Sparks(point, mat, 6);
            if (owner != null) owner.ForgetShot(id);
            Destroy(gameObject);
        }
    }

    /// <summary>A thrown blade: flies straight, sticks in a wall and stays; when pulled it flies back to the boss and is gone.</summary>
    public class FinalBossBlade : MonoBehaviour
    {
        Vector3 from, to;
        float start, time;
        bool pulling, stuck;

        public static FinalBossBlade Spawn(Transform parent, Vector3 from, Vector3 to, float time, Material blade, Material hilt)
        {
            var go = new GameObject("Boss Blade");
            go.transform.SetParent(parent, false);
            var b = go.AddComponent<FinalBossBlade>();
            var edge = FinalBossFx.Part(PrimitiveType.Cube, "Edge", go.transform, blade);
            edge.localPosition = new Vector3(0f, 0f, 0.2f); edge.localScale = new Vector3(0.07f, 0.22f, 1.1f);
            var guard = FinalBossFx.Part(PrimitiveType.Cube, "Guard", go.transform, hilt);
            guard.localPosition = new Vector3(0f, 0f, -0.38f); guard.localScale = new Vector3(0.12f, 0.45f, 0.08f);
            var grip = FinalBossFx.Part(PrimitiveType.Cube, "Grip", go.transform, hilt);
            grip.localPosition = new Vector3(0f, 0f, -0.55f); grip.localScale = new Vector3(0.08f, 0.08f, 0.3f);
            b.Fly(from, to, time, false);
            return b;
        }

        public void Pull(Vector3 at, float speed) =>
            Fly(transform.position, at, Vector3.Distance(transform.position, at) / Mathf.Max(0.1f, speed), true);

        void Fly(Vector3 a, Vector3 b, float t, bool pull)
        {
            from = a; to = b; time = Mathf.Max(0.05f, t); start = Time.time; pulling = pull; stuck = false;
            transform.position = a;
            if ((b - a).sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(b - a);
        }

        void Update()
        {
            if (stuck) return;
            float k = (Time.time - start) / time;
            if (k >= 1f)
            {
                transform.position = to;
                if (pulling) Destroy(gameObject);
                else { stuck = true; transform.rotation = Quaternion.LookRotation(to - from); }
                return;
            }
            transform.position = Vector3.Lerp(from, to, k);
            if ((to - from).sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(to - from) * Quaternion.Euler(0f, 0f, Time.time * 900f);
        }
    }
}
