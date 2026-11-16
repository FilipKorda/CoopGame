using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Local-only placeholder shot visuals: a thin tracer cube from muzzle to impact plus a muzzle flash cube, and
    /// <see cref="Sparks"/> where a shot hits.
    /// </summary>
    public class ShotEffects : MonoBehaviour
    {
        const float Lifetime = 0.08f;

        Transform tracer;
        Transform flash;
        float startTime;
        float tracerWidth;

        public static void Spawn(Vector3 from, Vector3 to, Material material)
        {
            var root = new GameObject("Shot");
            var fx = root.AddComponent<ShotEffects>();
            fx.startTime = Time.time;
            fx.tracerWidth = 0.05f;

            Vector3 delta = to - from;
            fx.tracer = CreateCube(root.transform, material);
            fx.tracer.SetPositionAndRotation(from + delta * 0.5f, Quaternion.LookRotation(delta.sqrMagnitude > 0.0001f ? delta : Vector3.forward));
            fx.tracer.localScale = new Vector3(fx.tracerWidth, fx.tracerWidth, delta.magnitude);

            fx.flash = CreateCube(root.transform, material);
            fx.flash.position = from;
            fx.flash.rotation = Random.rotation;
            fx.flash.localScale = Vector3.one * 0.2f;
        }

        /// <summary>A small burst of sparks where a shot hit.</summary>
        public static void Sparks(Vector3 point, Material material, int count = 8)
        {
            for (int i = 0; i < count; i++)
            {
                var bit = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bit.name = "ShotSpark";
                Destroy(bit.GetComponent<Collider>());
                if (material != null) bit.GetComponent<Renderer>().sharedMaterial = material;
                bit.transform.SetPositionAndRotation(point, Random.rotation);
                bit.transform.localScale = Vector3.one * Random.Range(0.06f, 0.12f);
                var body = bit.AddComponent<Rigidbody>();
                body.useGravity = false;
                body.linearVelocity = Random.onUnitSphere * 4f;
                Destroy(bit, 0.3f);
            }
        }

        static Transform CreateCube(Transform parent, Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(cube.GetComponent<Collider>());
            var renderer = cube.GetComponent<Renderer>();
            if (material != null) renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            cube.transform.SetParent(parent, true);
            return cube.transform;
        }

        void Update()
        {
            float t = (Time.time - startTime) / Lifetime;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }

            float shrink = 1f - t;
            Vector3 scale = tracer.localScale;
            tracer.localScale = new Vector3(tracerWidth * shrink, tracerWidth * shrink, scale.z);
            flash.localScale = Vector3.one * 0.2f * shrink;
        }
    }
}
