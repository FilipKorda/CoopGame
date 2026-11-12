using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoopGame
{
    /// <summary>
    /// A blast for cutscenes: keep the object switched off and switch it on with a Timeline Activation clip; at
    /// that moment it explodes here (the game's explosion effect), shakes cutscene cameras that have a Cinemachine
    /// Impulse Listener, and optionally blows <see cref="debrisFrom"/> apart into chunks (hide that object with
    /// the Timeline at the same moment). Only a picture: it runs on every machine, like the rest of the Timeline.
    /// In the editor (Timeline preview) it only draws the fireball at full size while it's switched on.
    /// </summary>
    [ExecuteAlways]
    public class CutsceneExplosion : MonoBehaviour
    {
        [SerializeField] float radius = 2.5f;
        [SerializeField] Material fireMaterial;
        [SerializeField] Material coreMaterial;
        [Tooltip("Shake for cutscene cameras with a Cinemachine Impulse Listener (needs a Cinemachine Impulse Source here).")]
        [SerializeField] float shake = 0.5f;

        [Header("Debris")]
        [Tooltip("Optional: what this blast destroys. Chunks in its material fly off from inside its shape.")]
        [SerializeField] Renderer debrisFrom;
        [SerializeField] int debrisCount = 16;
        [SerializeField] Vector2 debrisSize = new Vector2(0.3f, 0.8f);
        [SerializeField] Vector2 debrisSpeed = new Vector2(4f, 11f);
        [SerializeField] float debrisLifetime = 5f;

        static Mesh previewCube;

        void OnEnable()
        {
            // Scrubbing the Timeline in the editor switches it on too: there only draw it (nothing spawned or saved).
            if (!Application.isPlaying)
            {
                RenderPipelineManager.beginCameraRendering += DrawPreview;
                return;
            }
            ExplosionEffect.Spawn(transform.position, radius, fireMaterial, coreMaterial, 0f);
            if (TryGetComponent(out CinemachineImpulseSource impulse)) impulse.GenerateImpulseWithForce(shake);
            if (debrisFrom != null) SpawnDebris();
        }

        void OnDisable() => RenderPipelineManager.beginCameraRendering -= DrawPreview;

        // Edit mode: the play-mode fireball and core (ExplosionEffect) at their biggest, every camera, this frame only.
        void DrawPreview(ScriptableRenderContext context, Camera cam)
        {
            if (this == null) return;
            if (previewCube == null) previewCube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            float size = radius * 1.6f;
            if (fireMaterial != null)
                Graphics.RenderMesh(new RenderParams(fireMaterial) { camera = cam }, previewCube, 0,
                    Matrix4x4.TRS(transform.position, Quaternion.Euler(30f, 45f, 0f), Vector3.one * size));
            if (coreMaterial != null)
                Graphics.RenderMesh(new RenderParams(coreMaterial) { camera = cam }, previewCube, 0,
                    Matrix4x4.TRS(transform.position, Quaternion.Euler(-20f, 10f, 35f), Vector3.one * size * 0.75f));
        }

        void SpawnDebris()
        {
            // Mesh bounds, not renderer bounds: the Timeline may already have switched the object off.
            Bounds local = debrisFrom.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null
                ? filter.sharedMesh.bounds
                : new Bounds(Vector3.zero, Vector3.one);
            Transform shape = debrisFrom.transform;
            Material material = debrisFrom.sharedMaterial;

            for (int i = 0; i < debrisCount; i++)
            {
                var point = new Vector3(
                    Random.Range(local.min.x, local.max.x),
                    Random.Range(local.min.y, local.max.y),
                    Random.Range(local.min.z, local.max.z));
                Vector3 position = shape.TransformPoint(point);

                var chunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
                chunk.name = "Debris";
                chunk.transform.SetPositionAndRotation(position, Random.rotation);
                chunk.transform.localScale = Vector3.one * Random.Range(debrisSize.x, debrisSize.y);
                if (material != null) chunk.GetComponent<Renderer>().sharedMaterial = material;

                var body = chunk.AddComponent<Rigidbody>();
                Vector3 away = position - transform.position;
                if (away.sqrMagnitude < 0.01f) away = Random.onUnitSphere;
                body.linearVelocity = (away.normalized + Random.insideUnitSphere * 0.4f + Vector3.up * 0.3f).normalized
                    * Random.Range(debrisSpeed.x, debrisSpeed.y);
                body.angularVelocity = Random.insideUnitSphere * 10f;
                Destroy(chunk, debrisLifetime * Random.Range(0.8f, 1.2f));
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
