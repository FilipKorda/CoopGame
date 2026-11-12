using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CoopGame
{
    /// <summary>One subtitle line on a <see cref="DialogueTrack"/>: shown for as long as the clip lasts.</summary>
    [System.Serializable]
    public class DialogueClip : PlayableAsset, ITimelineClipAsset
    {
        [Tooltip("Who speaks (shown above the line); empty = no name.")]
        public string speaker = "";
        [TextArea(2, 5)]
        public string text = "";
        [Tooltip("Colour of the speaker's name.")]
        public Color speakerColor = new Color(1f, 0.82f, 0.3f);

        // Ease in/out fades the line in and out.
        public ClipCaps clipCaps => ClipCaps.Blending;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<DialogueLine>.Create(graph);
            var line = playable.GetBehaviour();
            line.Speaker = speaker;
            line.Text = text;
            line.SpeakerColor = speakerColor;
            return playable;
        }
    }
}
