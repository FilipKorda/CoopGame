using TMPro;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// The end of the game, on the final screen of the last cutscene. While it's in the level, leaving goes to the
    /// title screen. End Game (the cutscene's On Finished) keeps the screen fully shown after the Timeline stops and
    /// rolls the credits (bottom to top, with buttons to speed them up and to stop), then leaves the game.
    /// Each machine watches its own credits; the host leaving ends the game for both.
    /// </summary>
    public class GameOver : MonoBehaviour
    {
        [Tooltip("The credits text: it scrolls from under the bottom of the screen up past its top. Empty = leave at once.")]
        [SerializeField] RectTransform credits;
        [Tooltip("Switched on with the credits (the text and the buttons).")]
        [SerializeField] GameObject creditsScreen;
        [SerializeField] TMP_Text speedLabel;
        [Tooltip("Canvas units per second at normal speed.")]
        [SerializeField] float scrollSpeed = 90f;

        static readonly int[] Speeds = { 1, 2, 4 };
        int speedIndex;
        bool rolling, left;

        void OnEnable() => NetworkBootstrap.LeaveToMainMenu = true;

        void OnDisable()
        {
            NetworkBootstrap.LeaveToMainMenu = false;
            if (rolling) GameplayUI.IsMenuOpen = false;
        }

        public void EndGame()
        {
            foreach (var group in GetComponentsInChildren<CanvasGroup>(true)) group.alpha = 1f;
            if (credits == null) { Leave(); return; }
            creditsScreen.SetActive(true);
            credits.anchoredPosition = Vector2.zero;
            rolling = true;
            GameplayUI.IsMenuOpen = true;   // a free cursor for the buttons, no attacks
            ShowSpeed();
        }

        /// <summary>Button: the next speed (x1, x2, x4, then x1 again).</summary>
        public void SpeedUp()
        {
            speedIndex = (speedIndex + 1) % Speeds.Length;
            ShowSpeed();
        }

        /// <summary>Button: stop watching and leave.</summary>
        public void Close() => Leave();

        void ShowSpeed()
        {
            if (speedLabel != null) speedLabel.text = "Przyspiesz  x" + Speeds[speedIndex];
        }

        void Update()
        {
            if (!rolling || left) return;
            credits.anchoredPosition += Vector2.up * (scrollSpeed * Speeds[speedIndex] * Time.unscaledDeltaTime);
            // Anchored at the bottom with its top edge: done once the bottom edge is past the top of the screen.
            if (credits.anchoredPosition.y > credits.rect.height + ((RectTransform)credits.parent).rect.height) Leave();
        }

        void Leave()
        {
            if (left) return;
            left = true;
            NetworkBootstrap.LeaveGame();
        }
    }
}
