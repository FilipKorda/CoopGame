using System.Collections.Generic;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// A story break between two levels: on a dark screen the narrator tells part of the story, line by line with
    /// subtitles, while the next level loads underneath (<see cref="SceneFlow"/> plays it). Make one with
    /// Create > Coop Game > Story Interlude and add it to the SceneFlow on the GameSession prefab.
    /// </summary>
    [CreateAssetMenu(menuName = "Coop Game/Story Interlude", fileName = "Interlude")]
    public class StoryInterlude : ScriptableObject
    {
        [System.Serializable]
        public class Line
        {
            [TextArea(2, 5)] public string text = "";
            [Tooltip("How long the line stays up (seconds). With a voice clip it's at least the clip's length.")]
            [Min(0.5f)] public float seconds = 5f;
            [Tooltip("The narrator reading this line (optional; subtitles only without it).")]
            public AudioClip voice;
        }

        [Tooltip("Level it plays after (scene name); empty = any.")]
        public string fromScene = "";
        [Tooltip("Level it plays before (scene name).")]
        public string toScene = "";
        [Tooltip("Shown above the lines (e.g. Narrator); empty = nothing.")]
        public string speaker = "Narrator";
        [Tooltip("Optional title shown at the top the whole time (e.g. the act's name).")]
        public string title = "";
        [Tooltip("Gap between two lines (seconds).")]
        [Min(0f)] public float pause = 0.4f;
        public List<Line> lines = new();

        public float LineLength(int index)
        {
            var line = lines[index];
            return Mathf.Max(line.seconds, line.voice != null ? line.voice.length + 0.3f : 0f);
        }

        /// <summary>Total length (seconds).</summary>
        public float Duration
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < lines.Count; i++) total += LineLength(i) + pause;
                return total;
            }
        }

        /// <summary>Which line is up <paramref name="time"/> seconds in (-1 = between lines or after the end), and how far into it.</summary>
        public int LineAt(float time, out float into)
        {
            into = 0f;
            float t = 0f;
            for (int i = 0; i < lines.Count; i++)
            {
                float length = LineLength(i);
                if (time >= t && time < t + length)
                {
                    into = time - t;
                    return i;
                }
                t += length + pause;
            }
            return -1;
        }

        public bool Matches(string from, string to) =>
            toScene == to && (string.IsNullOrEmpty(fromScene) || fromScene == from);
    }
}
