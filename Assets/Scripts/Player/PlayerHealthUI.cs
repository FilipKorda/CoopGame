using TMPro;
using UnityEngine;

namespace CoopGame
{
    /// <summary>Bottom-right HUD showing the local player's HP; pops green when healed.</summary>
    public class PlayerHealthUI : MonoBehaviour
    {
        [SerializeField] TextMeshProUGUI label;
        [SerializeField] string format = "HP: {0}/{1}";
        [SerializeField] Color normalColor = Color.white;
        [SerializeField] Color lowColor = new Color(1f, 0.3f, 0.3f);
        [SerializeField] Color healColor = new Color(0.4f, 1f, 0.5f);
        [SerializeField] int lowThreshold = 3;
        [SerializeField] float popScale = 1.25f;
        [SerializeField] float popSharpness = 12f;
        [SerializeField] float healFlashTime = 0.4f;

        PlayerHealth subscribedTo;
        int shown = -1;
        float healFlashUntil;

        void Update()
        {
            if (subscribedTo != PlayerHealth.Local) Subscribe(PlayerHealth.Local);

            label.transform.localScale = Vector3.Lerp(label.transform.localScale, Vector3.one, 1f - Mathf.Exp(-popSharpness * Time.deltaTime));
            if (subscribedTo != null) label.color = CurrentColor(subscribedTo.Current);
        }

        void OnDestroy() => Subscribe(null);

        void Subscribe(PlayerHealth health)
        {
            if (subscribedTo != null) subscribedTo.HealthChanged -= Show;
            subscribedTo = health;
            shown = -1;
            if (subscribedTo != null)
            {
                subscribedTo.HealthChanged += Show;
                Show(subscribedTo.Current, subscribedTo.Max);
            }
            else
            {
                label.text = "";
            }
        }

        void Show(int current, int max)
        {
            if (shown > 0 && current > shown)
            {
                label.transform.localScale = Vector3.one * popScale;
                healFlashUntil = Time.time + healFlashTime;
            }
            shown = current;
            label.text = string.Format(format, current, max);
        }

        Color CurrentColor(int current)
        {
            if (Time.time < healFlashUntil) return healColor;
            return current <= lowThreshold ? lowColor : normalColor;
        }
    }
}
