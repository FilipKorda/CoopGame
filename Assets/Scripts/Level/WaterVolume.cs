using System.Collections.Generic;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// A body of swimmable water: an axis-aligned trigger box whose top face is the water surface.
    /// Players switch to swimming when their chest is inside it.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class WaterVolume : MonoBehaviour
    {
        static readonly List<WaterVolume> s_All = new();

        BoxCollider box;

        public float SurfaceY => box.bounds.max.y;

        void Awake()
        {
            box = GetComponent<BoxCollider>();
            box.isTrigger = true;
        }

        void OnEnable() => s_All.Add(this);
        void OnDisable() => s_All.Remove(this);

        public bool Contains(Vector3 point) => box.bounds.Contains(point);

        /// <summary>Inside the water's footprint, at any height.</summary>
        public bool ContainsHorizontally(Vector3 point)
        {
            var b = box.bounds;
            return point.x >= b.min.x && point.x <= b.max.x && point.z >= b.min.z && point.z <= b.max.z;
        }

        /// <summary>The water volume containing <paramref name="point"/>, or null.</summary>
        public static WaterVolume Find(Vector3 point)
        {
            foreach (var water in s_All)
                if (water.Contains(point)) return water;
            return null;
        }
    }
}
