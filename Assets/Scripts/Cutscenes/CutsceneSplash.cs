using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// A splash for cutscenes: keep the object switched off and switch it on with a Timeline Activation clip; at
    /// that moment drops of water fly up from here and a ring of foam spreads on the surface. Only a picture: it
    /// runs on every machine, like the rest of the Timeline.
    /// </summary>
    public class CutsceneSplash : MonoBehaviour
    {
        [SerializeField] Material foamMaterial;
        [SerializeField] int drops = 22;
        [SerializeField] float size = 1f;

        void OnEnable()
        {
            // Scrubbing the Timeline in the editor switches it on too; only splash in play.
            if (!Application.isPlaying) return;
            for (int i = 0; i < drops; i++)
            {
                var drop = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                drop.name = "SplashDrop";
                Destroy(drop.GetComponent<Collider>());
                if (foamMaterial != null) drop.GetComponent<Renderer>().sharedMaterial = foamMaterial;
                Vector2 around = Random.insideUnitCircle * 0.6f * size;
                drop.transform.position = transform.position + new Vector3(around.x, 0.1f, around.y);
                drop.transform.localScale = Vector3.one * Random.Range(0.12f, 0.3f) * size;
                var body = drop.AddComponent<Rigidbody>();
                body.linearVelocity = new Vector3(around.x * 3f, Random.Range(4f, 8f) * Mathf.Sqrt(size), around.y * 3f);
                Destroy(drop, Random.Range(0.8f, 1.3f));
            }
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "SplashRing";
            Destroy(ring.GetComponent<Collider>());
            if (foamMaterial != null) ring.GetComponent<Renderer>().sharedMaterial = foamMaterial;
            ring.transform.position = transform.position + Vector3.up * 0.02f;
            ring.AddComponent<SplashRing>().size = size;
        }

        /// <summary>Editor setup.</summary>
        public void Setup(Material foam, float scale)
        {
            foamMaterial = foam;
            size = scale;
        }

        /// <summary>The foam ring: spreads out flat and thins away.</summary>
        class SplashRing : MonoBehaviour
        {
            public float size = 1f;
            float age;

            void Update()
            {
                age += Time.deltaTime;
                float t = age / 1.2f;
                if (t >= 1f) { Destroy(gameObject); return; }
                float radius = Mathf.Lerp(0.4f, 2.2f, Mathf.Sqrt(t)) * size;
                transform.localScale = new Vector3(radius, 0.02f * (1f - t), radius);
            }
        }
    }
}
