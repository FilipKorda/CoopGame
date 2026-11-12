using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CoopGame
{
    /// <summary>
    /// One move on a <see cref="CutsceneMoveTrack"/>: the bound object goes from <see cref="from"/> to
    /// <see cref="to"/> over the clip's length (walk, run, fly in an arc, a camera dolly, a boat curving in to moor
    /// through <see cref="curveThrough"/>...). Before its first clip
    /// an object stands where that clip starts; after a clip it stays where it ended.
    /// </summary>
    [System.Serializable]
    public class CutsceneMoveClip : PlayableAsset, ITimelineClipAsset
    {
        public enum Facing
        {
            /// <summary>Turn towards where it's going (walking characters).</summary>
            Movement,
            /// <summary>Turn towards <see cref="lookAt"/> (yaw only; also standing still).</summary>
            LookAtTarget,
            /// <summary>Turn from the From point's rotation to the To point's (cameras, exact poses).</summary>
            MatchPoints,
            /// <summary>Keep the rotation it had at the start of the clip.</summary>
            Keep,
        }

        public enum Easing { Smooth, Linear, EaseIn, EaseOut }

        [Tooltip("Where it starts. Empty = where the previous clip on this track left it (or where it stands in the scene).")]
        public ExposedReference<Transform> from;
        [Tooltip("Where it goes. Empty = stays at the start (plus the end offset), e.g. to turn around or be lifted.")]
        public ExposedReference<Transform> to;
        [Tooltip("Added to the From point, in that point's axes (e.g. x -1 / +1 so two players walk side by side).")]
        public Vector3 startOffset;
        [Tooltip("Added to the To point, in that point's axes.")]
        public Vector3 endOffset;
        [Tooltip("Optional: the path bends towards this point instead of going straight (a smooth curve, e.g. a boat " +
                 "turning in to moor). Facing Movement then follows the curve.")]
        public ExposedReference<Transform> curveThrough;
        [Tooltip("Keep the whole move at Height (world Y), whatever the points' own heights (a boat on the water).")]
        public bool fixedHeight;
        public float height;

        [Header("Motion")]
        public Easing easing = Easing.Smooth;
        [Tooltip("Height of an arc above the straight line, in the middle (jumps, being thrown). 0 = along the ground.")]
        public float arcHeight;
        [Tooltip("Up-down bob of footsteps while walking (only without an arc).")]
        public float stepBob = 0.06f;
        [Tooltip("Length of one step for the bob.")]
        public float stepLength = 0.75f;
        [Tooltip("Somersaults: degrees turned forward over the clip (tumbling through the air).")]
        public float tumble;

        [Header("Rotation")]
        public Facing facing = Facing.Movement;
        [Tooltip("What to face with Facing = Look At Target.")]
        public ExposedReference<Transform> lookAt;
        [Tooltip("Seconds to turn to the new direction.")]
        public float turnTime = 0.35f;

        // The clip's place on the track, set by the track when the graph is built.
        [System.NonSerialized] internal double windowStart, windowEnd;

        public ClipCaps clipCaps => ClipCaps.Blending;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<CutsceneMoveBehaviour>.Create(graph);
            var move = playable.GetBehaviour();
            var resolver = graph.GetResolver();
            move.From = from.Resolve(resolver);
            move.To = to.Resolve(resolver);
            move.LookAt = lookAt.Resolve(resolver);
            move.Via = curveThrough.Resolve(resolver);
            move.Clip = this;
            move.Start = windowStart;
            move.End = windowEnd;
            return playable;
        }
    }

    /// <summary>A move clip's data in the playing graph.</summary>
    public class CutsceneMoveBehaviour : PlayableBehaviour
    {
        public CutsceneMoveClip Clip;
        public Transform From, To, LookAt, Via;
        public double Start, End;

        // Worked out by the mixer every frame (a clip without From starts where the one before ended).
        internal Vector3 StartPosition, EndPosition, ViaPosition;
        internal Quaternion StartRotation, EndRotation;

        public float Ease(float t) => Clip.easing switch
        {
            CutsceneMoveClip.Easing.Linear => t,
            CutsceneMoveClip.Easing.EaseIn => t * t,
            CutsceneMoveClip.Easing.EaseOut => 1f - (1f - t) * (1f - t),
            _ => t * t * (3f - 2f * t),
        };
    }
}
