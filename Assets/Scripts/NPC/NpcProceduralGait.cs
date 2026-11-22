using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Two-state procedural animation (IDLE / WALK) for the cube-figure NPCs (Actor_Island* prefabs): bends the
    /// prefab joints (Hips, UpperBody, Neck, Shoulder/Elbow/Wrist, Hip/Knee/Ankle) on top of their rest pose.
    /// Speed comes from how fast <see cref="body"/> (default: the parent, i.e. the NPC root) moves, so it walks
    /// both under <see cref="NpcWanderer"/> and when a cutscene drags the root along. Purely cosmetic and local.
    /// </summary>
    public class NpcProceduralGait : MonoBehaviour
    {
        [Tooltip("What moves through the world. Empty = parent.")]
        public Transform body;
        [Tooltip("Speed (m/s) that counts as full WALK.")]
        public float walkSpeed = 1.0f;
        [Tooltip("Metres per step (one foot).")]
        public float stepLength = 0.5f;
        [Tooltip("Seconds to blend between IDLE and WALK.")]
        public float blendTime = 0.25f;
        [Range(0f, 2f)] public float amount = 1f;

        enum J { Hips, Upper, Neck, ShL, ShR, ElL, ElR, WrL, WrR, HipL, HipR, KnL, KnR, AnL, AnR }
        static readonly string[] Names =
            { "Hips", "UpperBody", "Neck", "Shoulder_L", "Shoulder_R", "Elbow_L", "Elbow_R", "Wrist_L", "Wrist_R",
              "Hip_L", "Hip_R", "Knee_L", "Knee_R", "Ankle_L", "Ankle_R" };

        Transform[] joints;
        Quaternion[] rest;
        Vector3 hipsPos, upperPos;
        Vector3 lastPos;
        float speed, walk, walkVel, phase, seed;

        /// <summary>0 = IDLE, 1 = WALK (blended).</summary>
        public float WalkBlend => walk;

        void Awake()
        {
            if (body == null) body = transform.parent != null ? transform.parent : transform;
            var all = GetComponentsInChildren<Transform>(true);
            joints = new Transform[Names.Length];
            rest = new Quaternion[Names.Length];
            for (int i = 0; i < Names.Length; i++)
            {
                foreach (var t in all) if (t.name == Names[i]) { joints[i] = t; break; }
                if (joints[i]) rest[i] = joints[i].localRotation;
            }
            if (joints[(int)J.Hips]) hipsPos = joints[(int)J.Hips].localPosition;
            if (joints[(int)J.Upper]) upperPos = joints[(int)J.Upper].localPosition;
            lastPos = body.position;
            unchecked { int h = 17; foreach (char c in name + body.name) h = h * 31 + c; seed = (h & 0xffff) * 0.0137f; }
        }

        void OnEnable()
        {
            if (body != null) lastPos = body.position;
        }

        void Set(J j, float x, float y = 0f, float z = 0f)
        {
            var t = joints[(int)j];
            if (t) t.localRotation = rest[(int)j] * Quaternion.Euler(x * amount, y * amount, z * amount);
        }

        void LateUpdate()
        {
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 d = body.position - lastPos;
            d.y = 0f;
            lastPos = body.position;
            float s = d.magnitude / dt;
            if (s > 8f) s = 0f; // a teleport, not a walk
            speed = Mathf.Lerp(speed, s, 1f - Mathf.Exp(-dt * 10f));
            float target = Mathf.Clamp01((speed - 0.08f) / Mathf.Max(0.1f, walkSpeed * 0.5f));
            walk = Mathf.SmoothDamp(walk, target, ref walkVel, blendTime);
            float w = walk, idle = 1f - walk;

            // Steps follow the distance walked; when it stops, the stride eases back to the rest pose via w.
            phase = Mathf.Repeat(phase + speed * dt / Mathf.Max(0.1f, stepLength) * Mathf.PI, Mathf.PI * 2f);
            float sn = Mathf.Sin(phase), cs = Mathf.Cos(phase);
            float t = Time.time + seed;

            // Legs (negative X = forward). The knee bends while that leg swings forward; the ankle keeps the foot flat.
            float hipA = 24f * w, kneeA = 36f * w;
            float hL = -hipA * sn, hR = hipA * sn;
            float kL = kneeA * Mathf.Max(0f, cs) + 4f * w, kR = kneeA * Mathf.Max(0f, -cs) + 4f * w;
            Set(J.HipL, hL); Set(J.HipR, hR);
            Set(J.KnL, kL); Set(J.KnR, kR);
            Set(J.AnL, -(hL + kL) * 0.6f); Set(J.AnR, -(hR + kR) * 0.6f);

            // Arms swing against the legs; idle: a slow small sway.
            float armA = 18f * w;
            float idleArm = 2.5f * idle * Mathf.Sin(t * 1.3f);
            Set(J.ShL, armA * sn + idleArm); Set(J.ShR, -armA * sn + idleArm);
            Set(J.ElL, -10f * w - 3f * idle * (0.5f + 0.5f * Mathf.Sin(t * 0.9f)));
            Set(J.ElR, -10f * w - 3f * idle * (0.5f + 0.5f * Mathf.Sin(t * 0.9f + 1.3f)));
            Set(J.WrL, 4f * w * sn); Set(J.WrR, -4f * w * sn);

            // Body: bob twice per stride, lean and roll while walking; breathing and looking around when idle.
            float bob = w * 0.035f * (Mathf.Abs(sn) - 0.6f);
            float breath = idle * 0.008f * Mathf.Sin(t * 1.7f);
            if (joints[(int)J.Hips]) joints[(int)J.Hips].localPosition = hipsPos + new Vector3(0f, bob, 0f);
            if (joints[(int)J.Upper]) joints[(int)J.Upper].localPosition = upperPos + new Vector3(0f, bob + breath, 0f);
            Set(J.Hips, 0f, 6f * w * sn);
            Set(J.Upper, 5f * w + 1.5f * idle * Mathf.Sin(t * 1.7f), -5f * w * sn, 2.5f * w * cs);
            float look = idle * 18f * Mathf.Sin(t * 0.35f) * Mathf.Sin(t * 0.13f + 1f);
            Set(J.Neck, -3f * w + 3f * idle * Mathf.Sin(t * 0.5f), look + 4f * w * sn);
        }
    }
}
