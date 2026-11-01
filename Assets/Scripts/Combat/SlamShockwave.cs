using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Local-only placeholder impact effect for the slam: a ring of small cubes bursting outward along the
    /// ground to the edge of the damage radius, plus a flat square flash at the center.
    /// </summary>
    public class SlamShockwave : MonoBehaviour
    {
        const int PieceCount = 14;
        const float Duration = 0.35f;

        Transform[] pieces;
        Vector3[] directions;
        Transform flash;
        float radius;
        float startTime;

        public static void Spawn(Vector3 position, float radius, Material material)
        {
            var go = new GameObject("SlamShockwave");
            go.transform.position = position + Vector3.up * 0.1f;
            var wave = go.AddComponent<SlamShockwave>();
            wave.radius = radius;
            wave.startTime = Time.time;
            wave.pieces = new Transform[PieceCount];
            wave.directions = new Vector3[PieceCount];

            for (int i = 0; i < PieceCount; i++)
            {
                float angle = i * Mathf.PI * 2f / PieceCount;
                wave.directions[i] = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                wave.pieces[i] = CreatePiece(go.transform, material, Vector3.one * 0.3f);
                wave.pieces[i].localRotation = Quaternion.LookRotation(wave.directions[i]);
            }
            wave.flash = CreatePiece(go.transform, material, new Vector3(1f, 0.05f, 1f));
        }

        static Transform CreatePiece(Transform parent, Material material, Vector3 scale)
        {
            var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(piece.GetComponent<Collider>());
            if (material != null) piece.GetComponent<Renderer>().sharedMaterial = material;
            piece.transform.SetParent(parent, false);
            piece.transform.localScale = scale;
            return piece.transform;
        }

        void Update()
        {
            float t = (Time.time - startTime) / Duration;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }

            float eased = 1f - (1f - t) * (1f - t);
            float shrink = 1f - t;
            for (int i = 0; i < PieceCount; i++)
            {
                pieces[i].localPosition = directions[i] * (radius * eased) + Vector3.up * (0.4f * Mathf.Sin(t * Mathf.PI));
                pieces[i].localScale = new Vector3(0.3f, 0.3f, 0.6f) * shrink;
            }
            flash.localScale = new Vector3(radius * 2f * eased, 0.05f, radius * 2f * eased) * shrink;
        }
    }
}
