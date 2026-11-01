using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Local-only placeholder explosion: a fireball cube that swells to the blast radius and collapses, a bright
    /// core, and chunks flung outwards. Shakes the local camera depending on how close the player is.
    /// </summary>
    public class ExplosionEffect : MonoBehaviour
    {
        const int ChunkCount = 12;
        const float Duration = 0.45f;

        Transform fireball, core;
        Transform[] chunks;
        Vector3[] chunkVelocities;
        float radius;
        float startTime;

        public static void Spawn(Vector3 position, float radius, Material fireMaterial, Material coreMaterial, float cameraShake)
        {
            var go = new GameObject("Explosion");
            go.transform.position = position;
            var effect = go.AddComponent<ExplosionEffect>();
            effect.radius = radius;
            effect.startTime = Time.time;
            effect.fireball = CreatePiece(go.transform, fireMaterial);
            effect.fireball.localRotation = Random.rotation;
            effect.core = CreatePiece(go.transform, coreMaterial);
            effect.core.localRotation = Random.rotation;
            effect.chunks = new Transform[ChunkCount];
            effect.chunkVelocities = new Vector3[ChunkCount];
            for (int i = 0; i < ChunkCount; i++)
            {
                effect.chunks[i] = CreatePiece(go.transform, i % 2 == 0 ? fireMaterial : coreMaterial);
                effect.chunks[i].localRotation = Random.rotation;
                effect.chunkVelocities[i] = (Random.onUnitSphere + Vector3.up * 0.6f).normalized * Random.Range(6f, 11f);
            }

            var rig = ThirdPersonCameraRig.Instance;
            if (rig != null && rig.Target != null)
            {
                float distance = Vector3.Distance(rig.Target.transform.position, position);
                float falloff = Mathf.Clamp01(1f - distance / (radius * 5f));
                if (falloff > 0f) rig.AddShake(cameraShake * falloff, 0.35f);
            }
        }

        static Transform CreatePiece(Transform parent, Material material)
        {
            var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(piece.GetComponent<Collider>());
            var renderer = piece.GetComponent<Renderer>();
            if (material != null) renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            piece.transform.SetParent(parent, false);
            piece.transform.localScale = Vector3.zero;
            return piece.transform;
        }

        void Update()
        {
            float age = Time.time - startTime;
            float t = age / Duration;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }

            // Swell fast to the blast size, then collapse.
            float swell = t < 0.3f ? t / 0.3f : 1f - (t - 0.3f) / 0.7f;
            float size = radius * 1.6f * Mathf.Sqrt(Mathf.Clamp01(swell));
            fireball.localScale = Vector3.one * size;
            fireball.Rotate(Vector3.up, 400f * Time.deltaTime);
            // Big enough for its corners to poke out through the fireball's faces.
            core.localScale = Vector3.one * size * 0.75f;
            core.Rotate(Vector3.right, -300f * Time.deltaTime);

            for (int i = 0; i < ChunkCount; i++)
            {
                chunkVelocities[i] += Vector3.down * (20f * Time.deltaTime);
                chunks[i].localPosition += chunkVelocities[i] * Time.deltaTime;
                chunks[i].localScale = Vector3.one * (0.35f * (1f - t));
            }
        }
    }
}
