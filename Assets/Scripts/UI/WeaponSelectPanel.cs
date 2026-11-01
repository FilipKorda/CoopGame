using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CoopGame
{
    /// <summary>
    /// Weapon select panel: TAB opens/closes it, 8 slots from <see cref="WeaponCatalog"/>. Clicking a weapon puts it
    /// in the secondary slot (the wrench always stays) and switches to it. While it's open the cursor is free and
    /// attacks/camera are paused (<see cref="GameplayUI"/>); the player can still move.
    /// </summary>
    public class WeaponSelectPanel : MonoBehaviour
    {
        [SerializeField] GameObject panel;
        [SerializeField] Button[] slotButtons;
        [SerializeField] TextMeshProUGUI[] slotLabels;
        [SerializeField] Color emptyColor = new Color(0.2f, 0.2f, 0.22f, 0.8f);
        [SerializeField] Color availableColor = new Color(0.35f, 0.38f, 0.45f, 0.95f);
        [SerializeField] Color equippedColor = new Color(1f, 0.8f, 0.25f, 0.95f);

        InputAction menuAction;

        public bool IsOpen => panel != null && panel.activeSelf;

        void Start()
        {
            menuAction = InputSystem.actions.FindAction("Player/WeaponMenu", true);
            for (int i = 0; i < slotButtons.Length; i++)
            {
                int slotIndex = i;
                var slot = WeaponCatalog.Slots[i];
                slotLabels[i].text = $"{i + 1}\n{slot.Name}";
                slotButtons[i].interactable = !slot.IsEmpty;
                slotButtons[i].onClick.AddListener(() => Select(slotIndex));
            }
            SetOpen(false);
        }

        void OnDisable()
        {
            if (IsOpen) SetOpen(false);
        }

        void Update()
        {
            if (PlayerWeapons.Local == null)
            {
                if (IsOpen) SetOpen(false);
                return;
            }

            if (menuAction.WasPressedThisFrame()) SetOpen(!IsOpen);
            else if (IsOpen && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) SetOpen(false);

            if (IsOpen) RefreshColors();
        }

        void Select(int slotIndex)
        {
            var slot = WeaponCatalog.Slots[slotIndex];
            if (slot.IsEmpty || PlayerWeapons.Local == null) return;
            PlayerWeapons.Local.EquipSecondary(slot.Id);
            SetOpen(false);
        }

        void SetOpen(bool open)
        {
            if (panel != null) panel.SetActive(open);
            GameplayUI.IsMenuOpen = open;
            if (open) RefreshColors();
        }

        void RefreshColors()
        {
            var equipped = PlayerWeapons.Local != null ? PlayerWeapons.Local.SecondaryId : WeaponId.None;
            for (int i = 0; i < slotButtons.Length; i++)
            {
                var slot = WeaponCatalog.Slots[i];
                Color color = slot.IsEmpty ? emptyColor : slot.Id == equipped ? equippedColor : availableColor;
                ((Image)slotButtons[i].targetGraphic).color = color;
                slotLabels[i].color = slot.Id == equipped && !slot.IsEmpty ? Color.black : Color.white;
            }
        }
    }
}
