using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Placeholder aiming HUD for the local player: a crosshair while aiming and a lock marker over every locked
    /// target (copies of the marker are made as needed for multi-lock weapons). Weapon-agnostic, it only reads
    /// <see cref="PlayerTargeting"/>.
    /// </summary>
    public class TargetingHUD : MonoBehaviour
    {
        [SerializeField] TextMeshProUGUI crosshair;
        [SerializeField] TextMeshProUGUI lockMarker;
        [SerializeField] Color crosshairColor = Color.white;
        [SerializeField] Color crosshairLockedColor = new Color(1f, 0.35f, 0.25f);
        [Tooltip("Lock marker colour on something the wrench beam can grab.")]
        [SerializeField] Color grabbableColor = new Color(0.85f, 0.4f, 1f);
        [SerializeField] Color enemyMarkerColor = new Color(1f, 0.35f, 0.25f);
        [SerializeField] float markerSpinSpeed = 120f;

        readonly List<TextMeshProUGUI> markers = new();

        void LateUpdate()
        {
            if (markers.Count == 0) markers.Add(lockMarker);

            var targeting = PlayerTargeting.Local;
            bool aiming = targeting != null && targeting.IsAiming;
            Targetable latest = aiming ? targeting.CurrentTarget : null;

            bool latestGrabbable = latest != null && latest.Graspable != null;
            crosshair.enabled = aiming;
            crosshair.color = latest == null ? crosshairColor : latestGrabbable ? grabbableColor : crosshairLockedColor;

            int shown = 0;
            var cam = Camera.main;
            if (aiming && cam != null)
            {
                foreach (var target in targeting.LockedTargets)
                {
                    if (target == null) continue;
                    Vector3 screen = cam.WorldToScreenPoint(target.AimPoint);
                    if (screen.z <= 0f) continue;

                    if (shown == markers.Count) markers.Add(Instantiate(lockMarker, lockMarker.transform.parent));
                    var marker = markers[shown++];
                    marker.color = target.Graspable != null ? grabbableColor : enemyMarkerColor;
                    // Screen-space overlay canvas: world position of a UI element is in screen pixels.
                    marker.rectTransform.position = new Vector3(screen.x, screen.y, 0f);
                    marker.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Time.time * markerSpinSpeed);
                }
            }
            for (int i = 0; i < markers.Count; i++) markers[i].enabled = i < shown;
        }
    }
}
