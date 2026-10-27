using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoopGame
{
    /// <summary>
    /// Test keys for one person checking co-op obstacles with two game windows (editor and development builds only).
    /// Keyboard and mouse only reach the focused window, so these let one player keep doing their part while you
    /// switch to the other window:
    /// - F9: freeze the current wrench-beam grab (it keeps holding and steering, see <see cref="WrenchGrab.ToggleLatch"/>).
    /// - F10: long press window for beam buttons, for every player (on while any player has it on).
    /// </summary>
    public class PlayerDevTools : NetworkBehaviour
    {
        static readonly List<PlayerDevTools> all = new();

        readonly NetworkVariable<bool> longButtonWindow = new(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        WrenchGrab grab;
        string notice;
        float noticeUntil;
        GUIStyle style;

        /// <summary>True while any player has the long button window on (read by the server).</summary>
        public static bool LongButtonWindow
        {
            get
            {
                foreach (var tools in all)
                    if (tools.IsSpawned && tools.longButtonWindow.Value) return true;
                return false;
            }
        }

        void Awake() => grab = GetComponent<WrenchGrab>();

        public override void OnNetworkSpawn() => all.Add(this);

        public override void OnNetworkDespawn() => all.Remove(this);

        void Update()
        {
            if (!IsSpawned || !IsOwner || !Debug.isDebugBuild) return;
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.f9Key.wasPressedThisFrame && grab != null)
            {
                bool wasLatched = grab.IsLatched;
                if (!grab.ToggleLatch()) Notice("F9: najpierw złap coś wiązką (PPM + przytrzymaj LPM)");
                else if (wasLatched) Notice("Chwyt odmrożony");
                else Notice("Chwyt zamrożony - przełącz się na drugie okno");
            }

            if (keyboard.f10Key.wasPressedThisFrame)
                longButtonWindow.Value = !longButtonWindow.Value;
        }

        void Notice(string text)
        {
            notice = text;
            noticeUntil = Time.time + 2.5f;
        }

        void OnGUI()
        {
            if (!IsSpawned || !IsOwner || !Debug.isDebugBuild || CutsceneSystem.HidesGameplayUI) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 15, fontStyle = FontStyle.Bold };
                style.normal.textColor = Color.white;
            }

            bool latched = grab != null && grab.IsLatched;
            bool longWindow = LongButtonWindow;
            string windowState = longButtonWindow.Value ? "WŁ" : longWindow ? "WŁ (drugi gracz)" : "wył";
            string text = $"TEST   F9 zamroź chwyt: {(latched ? "ZAMROŻONY" : "-")}   |   F10 długie okno przycisków: {windowState}";
            if (Time.time < noticeUntil) text += "\n" + notice;

            const float width = 720f;
            float height = Time.time < noticeUntil ? 50f : 28f;
            var previous = GUI.backgroundColor;
            if (latched) GUI.backgroundColor = new Color(1f, 0.2f, 1f);
            GUI.Box(new Rect((Screen.width - width) * 0.5f, 6f, width, height), text, style);
            GUI.backgroundColor = previous;
        }
    }
}
