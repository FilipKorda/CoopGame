using TMPro;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Top-right HUD showing the team's shared bolt count, with a small pop when it goes up.
    /// </summary>
    public class BoltCounterUI : MonoBehaviour
    {
        [SerializeField] TextMeshProUGUI label;
        [SerializeField] string format = "Części: {0}";
        [SerializeField] float popScale = 1.3f;
        [SerializeField] float popSharpness = 12f;

        PickupSystem subscribedTo;
        int shown = -1;

        void Update()
        {
            if (subscribedTo != PickupSystem.Instance) Subscribe(PickupSystem.Instance);

            label.transform.localScale = Vector3.Lerp(label.transform.localScale, Vector3.one, 1f - Mathf.Exp(-popSharpness * Time.deltaTime));
        }

        void OnDestroy() => Subscribe(null);

        void Subscribe(PickupSystem system)
        {
            if (subscribedTo != null) subscribedTo.TeamBoltsChanged -= Show;
            subscribedTo = system;
            if (subscribedTo != null)
            {
                subscribedTo.TeamBoltsChanged += Show;
                Show(subscribedTo.TeamBolts);
            }
        }

        void Show(int total)
        {
            if (shown >= 0 && total > shown) label.transform.localScale = Vector3.one * popScale;
            shown = total;
            label.text = string.Format(format, total);
        }
    }
}
