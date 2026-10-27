using TMPro;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Center HUD line for the revive flow: "you're down" for the downed player, a hold-to-revive prompt
    /// and progress for the teammate standing next to them.
    /// </summary>
    public class ReviveUI : MonoBehaviour
    {
        [SerializeField] TextMeshProUGUI label;
        [SerializeField] string keyName = "F";

        void Update()
        {
            label.text = BuildText();
        }

        string BuildText()
        {
            var health = PlayerHealth.Local;
            if (health == null) return "";

            if (health.IsDowned)
            {
                if (AllPlayersDowned()) return "Wszyscy omdleli... powrót na start";
                return health.ReviveProgress > 0f
                    ? $"Wskrzeszanie... {Percent(health.ReviveProgress)}"
                    : "Omdlałeś! Czekaj, aż partner Cię wskrzesi";
            }

            var revive = PlayerRevive.Local;
            if (revive == null || revive.TargetInRange == null) return "";
            return revive.IsReviving
                ? $"Wskrzeszanie... {Percent(revive.TargetInRange.ReviveProgress)}"
                : $"Przytrzymaj [{keyName}], aby wskrzesić partnera";
        }

        static string Percent(float progress) => Mathf.RoundToInt(progress * 100f) + "%";

        static bool AllPlayersDowned()
        {
            foreach (var player in PlayerHealth.All)
                if (!player.IsDowned) return false;
            return true;
        }
    }
}
