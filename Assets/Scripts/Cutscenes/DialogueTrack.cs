using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CoopGame
{
    /// <summary>
    /// Timeline track of subtitles: each clip is one line (who says what), shown at the bottom of the screen
    /// while the clip runs; ease in/out fades it. Needs no binding. Drawn by <see cref="CutsceneSystem"/>.
    /// </summary>
    [TrackColor(1f, 0.82f, 0.3f)]
    [TrackClipType(typeof(DialogueClip))]
    public class DialogueTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount) =>
            ScriptPlayable<DialogueMixer>.Create(graph, inputCount);
    }

    /// <summary>Shows the line of the strongest clip under the playhead.</summary>
    public class DialogueMixer : PlayableBehaviour
    {
        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            DialogueLine best = null;
            float bestWeight = 0f;
            for (int i = 0; i < playable.GetInputCount(); i++)
            {
                float weight = playable.GetInputWeight(i);
                if (weight <= bestWeight) continue;
                var input = (ScriptPlayable<DialogueLine>)playable.GetInput(i);
                best = input.GetBehaviour();
                bestWeight = weight;
            }

            if (best != null) CutsceneSubtitles.Show(this, best.Speaker, best.Text, best.SpeakerColor, bestWeight);
            else CutsceneSubtitles.Hide(this);
        }

        public override void OnBehaviourPause(Playable playable, FrameData info) => CutsceneSubtitles.Hide(this);

        public override void OnPlayableDestroy(Playable playable) => CutsceneSubtitles.Hide(this);
    }

    /// <summary>The line one <see cref="DialogueClip"/> shows.</summary>
    public class DialogueLine : PlayableBehaviour
    {
        public string Speaker;
        public string Text;
        public Color SpeakerColor;
    }

    /// <summary>The subtitle on screen right now (set by dialogue tracks, read by the cutscene overlay).</summary>
    public static class CutsceneSubtitles
    {
        static object s_Owner;

        public static string Speaker { get; private set; } = "";
        public static string Text { get; private set; } = "";
        public static Color SpeakerColor { get; private set; } = Color.white;
        /// <summary>0..1, the clip's blend weight (ease in/out).</summary>
        public static float Alpha { get; private set; }
        public static bool Visible => s_Owner != null && !string.IsNullOrEmpty(Text) && Alpha > 0.01f;

        public static void Show(object owner, string speaker, string text, Color speakerColor, float alpha)
        {
            s_Owner = owner;
            Speaker = speaker ?? "";
            Text = text ?? "";
            SpeakerColor = speakerColor;
            Alpha = Mathf.Clamp01(alpha);
        }

        /// <summary>Clears the line if <paramref name="owner"/> put it there.</summary>
        public static void Hide(object owner)
        {
            if (s_Owner == owner) s_Owner = null;
        }

        public static void Clear() => s_Owner = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Owner = null;
    }
}
