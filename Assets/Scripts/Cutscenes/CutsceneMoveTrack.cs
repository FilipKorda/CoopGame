using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CoopGame
{
    /// <summary>
    /// Timeline track that moves one object (a character stand-in, a camera, a projectile) between points placed
    /// in the scene: bind the object, add <see cref="CutsceneMoveClip"/>s (Point → Point (1) → ...). A clip without
    /// a From point carries on from where the previous one ended. Before its first clip the object waits where
    /// that clip starts; after the last one it stays where it ended. Scrubbing in the editor previews it and puts
    /// everything back afterwards.
    /// </summary>
    [TrackColor(0.35f, 0.8f, 0.45f)]
    [TrackClipType(typeof(CutsceneMoveClip))]
    [TrackBindingType(typeof(Transform))]
    public class CutsceneMoveTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            // Each clip needs to know where it sits on the track (to chain moves and to hold between them).
            foreach (var clip in GetClips())
                if (clip.asset is CutsceneMoveClip move)
                {
                    move.windowStart = clip.start;
                    move.windowEnd = clip.end;
                }
            return ScriptPlayable<CutsceneMoveMixer>.Create(graph, inputCount);
        }

        public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
        {
            // Editor preview: Timeline restores these when the preview ends.
            if (director.GetGenericBinding(this) is Transform target)
            {
                driver.AddFromName<Transform>(target.gameObject, "m_LocalPosition");
                driver.AddFromName<Transform>(target.gameObject, "m_LocalRotation");
            }
            base.GatherProperties(director, driver);
        }
    }

    /// <summary>Places the bound transform for the current time from the track's move clips.</summary>
    public class CutsceneMoveMixer : PlayableBehaviour
    {
        readonly List<(CutsceneMoveBehaviour move, float weight)> moves = new();
        Transform bound;
        Vector3 originPosition;
        Quaternion originRotation;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (!(playerData is Transform target)) return;
            if (target != bound)
            {
                // Where it stands in the scene: the start for a first clip without a From point.
                bound = target;
                originPosition = target.position;
                originRotation = target.rotation;
            }

            moves.Clear();
            for (int i = 0; i < playable.GetInputCount(); i++)
            {
                var move = ((ScriptPlayable<CutsceneMoveBehaviour>)playable.GetInput(i)).GetBehaviour();
                if (move != null && move.Clip != null) moves.Add((move, playable.GetInputWeight(i)));
            }
            if (moves.Count == 0) return;
            moves.Sort((a, b) => a.move.Start.CompareTo(b.move.Start));

            // Chain the moves: each starts where the previous ended unless it names its own From point.
            Vector3 position = originPosition;
            Quaternion rotation = originRotation;
            foreach (var (move, _) in moves)
            {
                Prepare(move, position, rotation);
                position = move.EndPosition;
                rotation = Evaluate(move, move.End).rotation;
            }

            double time = playable.GetGraph().GetResolver() is PlayableDirector director ? director.time : playable.GetTime();
            bool any = false;
            float total = 0f;
            Pose pose = default;
            foreach (var (move, weight) in moves)
            {
                if (weight <= 0f) continue;
                Pose p = Evaluate(move, time);
                if (!any)
                {
                    pose = p;
                    total = weight;
                    any = true;
                    continue;
                }
                total += weight;
                float k = weight / total;
                pose = new Pose(Vector3.Lerp(pose.position, p.position, k), Quaternion.Slerp(pose.rotation, p.rotation, k));
            }

            if (!any)
            {
                // Between clips: hold the end of the last one; before the first: wait at its start.
                CutsceneMoveBehaviour last = null;
                foreach (var (move, _) in moves)
                    if (move.End <= time) last = move;
                pose = last != null ? Evaluate(last, last.End) : Evaluate(moves[0].move, moves[0].move.Start);
            }

            target.SetPositionAndRotation(pose.position, pose.rotation);
        }

        static void Prepare(CutsceneMoveBehaviour move, Vector3 previousPosition, Quaternion previousRotation)
        {
            var clip = move.Clip;
            move.StartPosition = move.From != null ? move.From.position + move.From.rotation * clip.startOffset : previousPosition;
            move.StartRotation = move.From != null && clip.facing == CutsceneMoveClip.Facing.MatchPoints ? move.From.rotation : previousRotation;
            move.EndPosition = move.To != null
                ? move.To.position + move.To.rotation * clip.endOffset
                : move.StartPosition + (clip.endOffset - (move.From != null ? clip.startOffset : Vector3.zero));
            move.ViaPosition = move.Via != null ? move.Via.position : Vector3.Lerp(move.StartPosition, move.EndPosition, 0.5f);
            if (clip.fixedHeight)
                move.StartPosition.y = move.EndPosition.y = move.ViaPosition.y = clip.height;

            switch (clip.facing)
            {
                case CutsceneMoveClip.Facing.Movement:
                    // Along a curve it arrives heading the way the curve ends.
                    Vector3 travel = Vector3.ProjectOnPlane(move.EndPosition - (move.Via != null ? move.ViaPosition : move.StartPosition), Vector3.up);
                    move.EndRotation = travel.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(travel) : move.StartRotation;
                    break;
                case CutsceneMoveClip.Facing.LookAtTarget:
                    move.EndRotation = FaceTowards(move.LookAt, move.EndPosition, move.StartRotation);
                    break;
                case CutsceneMoveClip.Facing.MatchPoints:
                    move.EndRotation = move.To != null ? move.To.rotation : move.StartRotation;
                    break;
                default:
                    move.EndRotation = move.StartRotation;
                    break;
            }
        }

        static Pose Evaluate(CutsceneMoveBehaviour move, double time)
        {
            var clip = move.Clip;
            double length = System.Math.Max(0.0001, move.End - move.Start);
            float t = Mathf.Clamp01((float)((time - move.Start) / length));
            float eased = move.Ease(t);

            // Straight, or a curve bending towards the Via point (quadratic Bezier).
            float rest = 1f - eased;
            Vector3 position = move.Via != null
                ? rest * rest * move.StartPosition + 2f * rest * eased * move.ViaPosition + eased * eased * move.EndPosition
                : Vector3.LerpUnclamped(move.StartPosition, move.EndPosition, eased);
            if (clip.arcHeight != 0f)
            {
                position += Vector3.up * (clip.arcHeight * 4f * t * (1f - t));
            }
            else if (clip.stepBob > 0f && clip.stepLength > 0.01f)
            {
                float walked = Vector3.ProjectOnPlane(move.EndPosition - move.StartPosition, Vector3.up).magnitude * eased;
                position += Vector3.up * (Mathf.Abs(Mathf.Sin(Mathf.PI * walked / clip.stepLength)) * clip.stepBob);
            }

            float turn = clip.turnTime > 0f ? Mathf.Clamp01((float)(time - move.Start) / clip.turnTime) : 1f;
            turn = turn * turn * (3f - 2f * turn);
            Quaternion heading = move.EndRotation;
            if (move.Via != null && clip.facing == CutsceneMoveClip.Facing.Movement)
            {
                // Facing along the curve.
                Vector3 tangent = Vector3.ProjectOnPlane(rest * (move.ViaPosition - move.StartPosition) + eased * (move.EndPosition - move.ViaPosition), Vector3.up);
                if (tangent.sqrMagnitude > 0.0001f) heading = Quaternion.LookRotation(tangent);
            }
            Quaternion rotation = clip.facing switch
            {
                CutsceneMoveClip.Facing.Movement => Quaternion.Slerp(move.StartRotation, heading, turn),
                CutsceneMoveClip.Facing.LookAtTarget => Quaternion.Slerp(move.StartRotation, FaceTowards(move.LookAt, position, move.StartRotation), turn),
                CutsceneMoveClip.Facing.MatchPoints => Quaternion.Slerp(move.StartRotation, move.EndRotation, eased),
                _ => move.StartRotation,
            };
            if (clip.tumble != 0f) rotation *= Quaternion.AngleAxis(clip.tumble * t, Vector3.right);
            return new Pose(position, rotation);
        }

        static Quaternion FaceTowards(Transform target, Vector3 from, Quaternion fallback)
        {
            if (target == null) return fallback;
            Vector3 direction = Vector3.ProjectOnPlane(target.position - from, Vector3.up);
            return direction.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(direction) : fallback;
        }
    }
}
