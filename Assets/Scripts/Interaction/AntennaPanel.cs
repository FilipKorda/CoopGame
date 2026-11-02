using TMPro;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// The control panel of one <see cref="AntennaRig"/>: its screens show the right angles for the antenna, but only
    /// while someone holds that antenna with the wrench beam; otherwise "no signal". Once the antenna is aimed it says
    /// so. A player standing at the panel also gets the same text on screen. Not networked itself: it reads the
    /// antenna's shared state, so everyone sees the same.
    /// </summary>
    public class AntennaPanel : MonoBehaviour
    {
        [SerializeField] AntennaRig antenna;
        [SerializeField] TMP_Text[] screens;
        [Tooltip("Players this close (metres) to the panel get its text on screen too.")]
        [SerializeField] float readRange = 9f;
        [SerializeField] Color signalColor = new Color(0.35f, 1f, 0.55f);
        [SerializeField] Color noSignalColor = new Color(1f, 0.5f, 0.25f);
        [SerializeField] Color alignedColor = new Color(0.3f, 0.85f, 1f);

        string text;
        Color color;

        void Update()
        {
            if (antenna == null) return;
            string title = antenna.DisplayName.ToUpperInvariant();
            if (antenna.IsAligned)
            {
                text = $"{title}\nUSTAWIONA\nsygnał połączony";
                color = alignedColor;
            }
            else if (antenna.IsBlocked)
            {
                text = $"{title}\nZABLOKOWANA\ngruz: {antenna.BlockersLeft}";
                color = noSignalColor;
            }
            else if (antenna.IsHeld)
            {
                text = $"{title}\nOBRÓT: {antenna.TargetYaw:0.0}°\nNACHYLENIE: {antenna.TargetPitch:0.0}°";
                color = signalColor;
            }
            else
            {
                text = $"{title}\nBRAK SYGNAŁU\nchwyć antenę kluczem";
                color = noSignalColor;
            }
            if (screens == null) return;
            foreach (var screen in screens)
            {
                if (screen == null) continue;
                if (screen.text != text) screen.text = text;
                screen.color = color;
            }
        }

        void OnGUI()
        {
            if (antenna == null || text == null || CutsceneSystem.HidesGameplayUI) return;
            var player = PlayerHealth.Local;
            if (player == null || (player.transform.position - transform.position).sqrMagnitude > readRange * readRange) return;
            var style = new GUIStyle(GUI.skin.box) { fontSize = 26, alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = color;
            GUI.Box(new Rect(Screen.width * 0.5f - 230f, Screen.height * 0.5f - 230f, 460f, 120f), text, style);
        }

        /// <summary>Editor setup.</summary>
        public void Setup(AntennaRig rig, TMP_Text[] screenTexts)
        {
            antenna = rig;
            screens = screenTexts;
        }
    }
}
