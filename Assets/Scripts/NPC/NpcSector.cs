using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// The patch of ground one wandering NPC (<see cref="NpcWanderer"/>) may walk in. Edit it by hand in the scene:
    /// Box = the object's own unit square scaled by its Transform (move, rotate and scale it like a cube);
    /// Circle = <see cref="radius"/> metres round the object. Only x/z matter, height is ignored.
    /// </summary>
    public class NpcSector : MonoBehaviour
    {
        public enum Shape { Box, Circle }

        public Shape shape = Shape.Box;
        [Tooltip("Circle only: radius in metres.")]
        public float radius = 4f;
        public Color gizmoColor = new Color(0.2f, 0.9f, 0.4f, 1f);

        public bool Contains(Vector3 world, float margin = 0f)
        {
            if (shape == Shape.Circle)
            {
                Vector3 d = world - transform.position;
                d.y = 0f;
                return d.magnitude <= Mathf.Max(0f, radius - margin);
            }
            Vector3 l = transform.InverseTransformPoint(world);
            Vector3 s = transform.lossyScale;
            float mx = s.x > 0.001f ? margin / s.x : 0f, mz = s.z > 0.001f ? margin / s.z : 0f;
            return Mathf.Abs(l.x) <= 0.5f - mx && Mathf.Abs(l.z) <= 0.5f - mz;
        }

        public Vector3 RandomPoint(System.Random rng)
        {
            float a = (float)rng.NextDouble(), b = (float)rng.NextDouble();
            if (shape == Shape.Circle)
            {
                float ang = a * Mathf.PI * 2f, r = Mathf.Sqrt(b) * radius;
                return transform.position + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
            }
            return transform.TransformPoint(new Vector3(a - 0.5f, 0f, b - 0.5f));
        }

        void OnDrawGizmos() => Draw(0.35f);
        void OnDrawGizmosSelected() => Draw(1f);

        void Draw(float alpha)
        {
            Color c = gizmoColor;
            c.a = alpha;
            Gizmos.color = c;
            Vector3 up = Vector3.up * 2f;
            if (shape == Shape.Circle)
            {
                Vector3 p = transform.position, prev = p + new Vector3(radius, 0f, 0f);
                for (int i = 1; i <= 48; i++)
                {
                    float ang = i / 48f * Mathf.PI * 2f;
                    Vector3 q = p + new Vector3(Mathf.Cos(ang) * radius, 0f, Mathf.Sin(ang) * radius);
                    Gizmos.DrawLine(prev, q);
                    Gizmos.DrawLine(prev + up, q + up);
                    prev = q;
                }
                Gizmos.DrawLine(p, p + up);
                return;
            }
            Vector3 a = transform.TransformPoint(new Vector3(-0.5f, 0f, -0.5f));
            Vector3 b = transform.TransformPoint(new Vector3(0.5f, 0f, -0.5f));
            Vector3 cc = transform.TransformPoint(new Vector3(0.5f, 0f, 0.5f));
            Vector3 d = transform.TransformPoint(new Vector3(-0.5f, 0f, 0.5f));
            Vector3[] pts = { a, b, cc, d };
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = pts[i], q = pts[(i + 1) % 4];
                Gizmos.DrawLine(p, q);
                Gizmos.DrawLine(p + up, q + up);
                Gizmos.DrawLine(p, p + up);
            }
        }
    }
}
