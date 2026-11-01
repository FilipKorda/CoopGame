using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CoopGame
{
    /// <summary>
    /// Bottom-left HUD: the two equipped weapons (wrench on Q, secondary on E), with the active one highlighted
    /// and the secondary's status (ammo / reloading).
    /// </summary>
    public class LoadoutHUD : MonoBehaviour
    {
        [SerializeField] Image mainBox;
        [SerializeField] TextMeshProUGUI mainLabel;
        [SerializeField] Image secondaryBox;
        [SerializeField] TextMeshProUGUI secondaryLabel;
        [SerializeField] Color activeColor = new Color(1f, 0.8f, 0.25f, 0.85f);
        [SerializeField] Color inactiveColor = new Color(0.1f, 0.1f, 0.12f, 0.6f);

        void LateUpdate()
        {
            var weapons = PlayerWeapons.Local;
            bool visible = weapons != null;
            mainBox.gameObject.SetActive(visible);
            secondaryBox.gameObject.SetActive(visible);
            if (!visible) return;

            var thrower = weapons.GetComponent<WrenchThrow>();
            bool wrenchAway = thrower != null && !thrower.HasWrench;
            mainLabel.text = $"[Q] {WeaponCatalog.MainWeaponName}" + (wrenchAway ? "  (w locie)" : "");

            var secondary = weapons.Secondary;
            secondaryLabel.text = secondary != null
                ? $"[E] {WeaponCatalog.NameOf(secondary.Id)}  {secondary.StatusText}"
                : "[E] —  (TAB)";

            mainBox.color = weapons.MainActive ? activeColor : inactiveColor;
            secondaryBox.color = !weapons.MainActive ? activeColor : inactiveColor;
            mainLabel.color = weapons.MainActive ? Color.black : Color.white;
            secondaryLabel.color = !weapons.MainActive ? Color.black : Color.white;
        }
    }
}
