using UnityEngine;

namespace CoopGame
{
    public enum RockPlatformStyle { Platform, Rock }

    /// <summary>
    /// A jump platform in the StyleMountains look (rock with a grass lid) that fits any box size.
    /// The pivot is the middle of the top (walkable) surface; <see cref="size"/> is the box it stands for, going down
    /// from the pivot. The BoxCollider always matches that box exactly, so gameplay is the same as the greybox cube.
    /// The look is rebuilt in the editor (Inspector button, or Tools > Coop Game > Rock Platforms).
    /// </summary>
    [ExecuteAlways, RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(BoxCollider))]
    public class RockPlatform : MonoBehaviour
    {
        [Tooltip("Width (X), height (Y, downwards from the top) and depth (Z) of the platform, in metres.")]
        public Vector3 size = new(6f, 6f, 6f);
        [Tooltip("Changes the random shape of the rock.")]
        public int seed = 1;
        [Tooltip("Grass on top. Off: bare rock lid.")]
        public bool grassTop = true;
        [Tooltip("Platform: rock with an overhanging grass lid, for jump platforms. Rock: plain rock fitted tight to the box, no grass, nothing below it; for walls, door frames, lintels and hidden passages.")]
        public RockPlatformStyle style = RockPlatformStyle.Platform;
        [Tooltip("Cut a straight rectangular tunnel through the block, along its local Z (front to back).")]
        public bool tunnel;
        [Tooltip("Tunnel width and height. The tunnel starts at the bottom of the block.")]
        public Vector2 tunnelSize = new(4f, 3f);
        [Tooltip("Sideways offset of the tunnel from the block centre (local X).")]
        public float tunnelOffsetX;

        /// <summary>Keeps the collider on the box (also called by the editor builder).</summary>
        public void SyncCollider()
        {
            var boxes = GetComponents<BoxCollider>();
            if (!tunnel || boxes.Length < 3) { boxes[0].center = new Vector3(0f, -size.y * 0.5f, 0f); boxes[0].size = size; return; }
            // Tunnel: left part, right part and the part above the tunnel (the editor builder adds the extra colliders).
            float hx = size.x * 0.5f, w = Mathf.Clamp(tunnelSize.x, 0.3f, size.x - 0.2f), th = Mathf.Clamp(tunnelSize.y, 0.3f, size.y - 0.2f);
            float a = Mathf.Clamp(tunnelOffsetX - w * 0.5f, -hx + 0.1f, hx - 0.1f - w), e = a + w;
            boxes[0].center = new Vector3((-hx + a) * 0.5f, -size.y * 0.5f, 0f); boxes[0].size = new Vector3(a + hx, size.y, size.z);
            boxes[1].center = new Vector3((e + hx) * 0.5f, -size.y * 0.5f, 0f); boxes[1].size = new Vector3(hx - e, size.y, size.z);
            boxes[2].center = new Vector3((a + e) * 0.5f, -(size.y - th) * 0.5f, 0f); boxes[2].size = new Vector3(w, size.y - th, size.z);
        }
        /// <summary>The tunnel in local X range (a, e) and height th from the bottom.</summary>
        public void TunnelRect(out float a, out float e, out float th)
        {
            float hx = size.x * 0.5f, w = Mathf.Clamp(tunnelSize.x, 0.3f, size.x - 0.2f); th = Mathf.Clamp(tunnelSize.y, 0.3f, size.y - 0.2f);
            a = Mathf.Clamp(tunnelOffsetX - w * 0.5f, -hx + 0.1f, hx - 0.1f - w); e = a + w;
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            size = Vector3.Max(size, new Vector3(0.5f, 0.3f, 0.5f));
            SyncCollider();
            if (!Application.isPlaying) UnityEditor.EditorApplication.delayCall += () => { if (this != null) RebuildRequested?.Invoke(this); };
        }
        /// <summary>Set by the editor builder.</summary>
        public static System.Action<RockPlatform> RebuildRequested;
#endif
    }
}
