using System;
using System.Linq;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace CoopGame
{
    /// <summary>
    /// Main menu of the bootstrap scene, built in code (uGUI). Flow for two players:
    /// A clicks "Utwórz lobby" and gets the lobby code to send to B (the invitation) and a "Graj" button.
    /// B clicks "Dołącz kodem", pastes the code and accepts the invitation, which joins the lobby (Relay).
    /// In the lobby each player toggles "Gotowy"; the host's "Graj" works only once both are ready and connected,
    /// then the host loads the level for both (see <see cref="NetworkBootstrap.StartGameFromLobby"/>).
    /// Lives in the bootstrap scene, so it disappears with it when the level loads.
    /// </summary>
    public class LobbyMenu : MonoBehaviour
    {
        const string ReadyKey = "ready";

        NetworkBootstrap bootstrap;
        Font font;
        GameObject canvasObject, mainPanel, joinPanel, invitePanel, lobbyPanel;
        Text statusText, inviteText, codeText, playersText, startText, waitText, readyLabel;
        InputField codeInput, nameInput;
        Button createButton, joinOpenButton, backButton, nextButton, acceptButton, readyButton, playButton;
        GameObject hostOnly;
        string pendingCode;
        bool savingReady;

        public static LobbyMenu Create(NetworkBootstrap bootstrap)
        {
            var menu = new GameObject("LobbyMenu").AddComponent<LobbyMenu>();
            menu.bootstrap = bootstrap;
            menu.Build();
            return menu;
        }

        void OnDestroy()
        {
            if (canvasObject != null) Destroy(canvasObject);
        }

        // ---------- State ----------

        ISession Session => bootstrap != null ? bootstrap.Session : null;

        void Update()
        {
            if (bootstrap == null) { Destroy(gameObject); return; }

            bool inMenu = bootstrap.IsInMenu;
            canvasObject.SetActive(inMenu);
            if (!inMenu) return;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            bool busy = bootstrap.IsBusy;
            bool inLobby = bootstrap.InLobby;
            if (inLobby)
            {
                Show(lobbyPanel);
                RefreshLobby();
            }
            else if (lobbyPanel.activeSelf)
                Show(mainPanel); // left the lobby

            createButton.interactable = !busy;
            joinOpenButton.interactable = !busy;
            backButton.interactable = !busy;
            nextButton.interactable = !busy && CleanCode(codeInput.text).Length > 0;
            acceptButton.interactable = !busy;
            statusText.text = bootstrap.Status;
        }

        void RefreshLobby()
        {
            var session = Session;
            codeText.text = session.Code ?? "...";

            var players = session.Players;
            var lines = new System.Text.StringBuilder();
            for (int i = 0; i < session.MaxPlayers; i++)
            {
                if (i >= players.Count)
                {
                    lines.AppendLine($"{i + 1}.  <color=#888888>czekamy na gracza...</color>");
                    continue;
                }
                var player = players[i];
                string tags = (player.Id == session.Host ? "  [Host]" : "") + (player.Id == session.CurrentPlayer?.Id ? "  (Ty)" : "");
                string ready = IsReady(player) ? "<color=#5BD16A>GOTOWY</color>" : "<color=#E0A040>nie gotowy</color>";
                lines.AppendLine($"{i + 1}.  {DisplayName(player, i)}{tags}   —   {ready}");
            }
            playersText.text = lines.ToString().TrimEnd();

            bool meReady = session.CurrentPlayer != null && IsReady(session.CurrentPlayer);
            readyLabel.text = meReady ? "Nie gotowy" : "Gotowy";
            readyButton.interactable = !savingReady;

            bool isHost = session.IsHost;
            hostOnly.SetActive(isHost);
            playButton.gameObject.SetActive(isHost);
            startText.text = $"Start: {bootstrap.StartLabel}";

            bool full = players.Count >= session.MaxPlayers;
            bool allReady = full && players.All(IsReady);
            // Relay: the other player must also be connected to the host's game before the level loads.
            bool connected = bootstrap.Network.IsServer && bootstrap.Network.ConnectedClientsIds.Count >= players.Count;
            playButton.interactable = allReady && connected;

            waitText.gameObject.SetActive(!isHost || !playButton.interactable);
            waitText.text = !full ? "Czekamy, aż drugi gracz dołączy."
                : !allReady ? "Obaj gracze muszą być gotowi."
                : isHost ? "Łączenie z drugim graczem..."
                : "Czekamy, aż host kliknie „Graj”.";
        }

        static bool IsReady(IReadOnlyPlayer player) =>
            player.Properties != null && player.Properties.TryGetValue(ReadyKey, out var p) && p?.Value == "1";

        static string DisplayName(IReadOnlyPlayer player, int index)
        {
            string name = player.GetPlayerName();
            if (string.IsNullOrEmpty(name)) return $"Gracz {index + 1}";
            int hash = name.IndexOf('#');
            return hash > 0 ? name.Substring(0, hash) : name;
        }

        static string CleanCode(string code) => new string((code ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

        // ---------- Actions ----------

        void SaveName(string name)
        {
            bootstrap.DesiredPlayerName = name;
            nameInput.SetTextWithoutNotify(bootstrap.DesiredPlayerName);
        }

        async void OnCreateLobby()
        {
            SaveName(nameInput.text);
            bootstrap.ClearStatus();
            await bootstrap.CreateLobby();
        }

        void OnOpenJoin()
        {
            SaveName(nameInput.text);
            bootstrap.ClearStatus();
            codeInput.text = "";
            Show(joinPanel);
            codeInput.ActivateInputField();
        }

        void OnPaste() => codeInput.text = CleanCode(GUIUtility.systemCopyBuffer);

        void OnNext()
        {
            pendingCode = CleanCode(codeInput.text);
            if (pendingCode.Length == 0) return;
            inviteText.text = $"Zaproszenie do lobby\n<b><size=44>{pendingCode}</size></b>\n\nCzy chcesz dołączyć?";
            Show(invitePanel);
        }

        async void OnAccept()
        {
            await bootstrap.JoinLobby(pendingCode);
            if (Session == null) Show(joinPanel); // failed: the status says why
        }

        void OnDecline()
        {
            pendingCode = null;
            bootstrap.ClearStatus();
            Show(mainPanel);
        }

        void OnCopyCode()
        {
            if (Session?.Code != null) GUIUtility.systemCopyBuffer = Session.Code;
        }

        async void OnToggleReady()
        {
            var session = Session;
            if (session?.CurrentPlayer == null || savingReady) return;
            bool ready = !IsReady(session.CurrentPlayer);
            savingReady = true;
            try
            {
                session.CurrentPlayer.SetProperty(ReadyKey, new PlayerProperty(ready ? "1" : "0", VisibilityPropertyOptions.Member));
                await session.SaveCurrentPlayerDataAsync();
            }
            catch (Exception e) { Debug.LogException(e); }
            finally { savingReady = false; }
        }

        void OnPlay()
        {
            if (playButton.interactable) bootstrap.StartGameFromLobby();
        }

        void OnLeave()
        {
            bootstrap.LeaveLobby();
            Show(mainPanel);
        }

        void Show(GameObject panel)
        {
            mainPanel.SetActive(panel == mainPanel);
            joinPanel.SetActive(panel == joinPanel);
            invitePanel.SetActive(panel == invitePanel);
            lobbyPanel.SetActive(panel == lobbyPanel);
        }

        // ---------- UI building ----------

        void Build()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform);
            }

            canvasObject = new GameObject("LobbyCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var root = (RectTransform)canvasObject.transform;
            var background = NewRect("Background", root);
            Stretch(background);
            background.gameObject.AddComponent<Image>().color = new Color(0.06f, 0.07f, 0.1f, 0.85f);

            // Main
            mainPanel = Panel(root, "Main");
            MakeLabel(mainPanel.transform, "COOP GAME", 64, FontStyle.Bold, 90);
            Space(mainPanel.transform, 20);
            MakeLabel(mainPanel.transform, "Twoja nazwa:", 22, FontStyle.Normal, 30);
            nameInput = MakeField(mainPanel.transform, "losowa", 30, InputField.ContentType.Standard, null);
            nameInput.text = bootstrap.DesiredPlayerName;
            nameInput.onEndEdit.AddListener(SaveName);
            Space(mainPanel.transform, 10);
            createButton = MakeButton(mainPanel.transform, "Utwórz lobby", OnCreateLobby);
            joinOpenButton = MakeButton(mainPanel.transform, "Dołącz kodem", OnOpenJoin);
            backButton = MakeButton(mainPanel.transform, "Wróć", bootstrap.BackToMainMenu);
            Space(mainPanel.transform, 10);
            MakeLabel(mainPanel.transform, "F1 – panel deweloperski (LAN / wybór aktu)", 18, FontStyle.Italic, 30).color = new Color(1, 1, 1, 0.45f);

            // Join: enter the code from the invitation
            joinPanel = Panel(root, "Join");
            MakeLabel(joinPanel.transform, "Dołącz do lobby", 44, FontStyle.Bold, 70);
            MakeLabel(joinPanel.transform, "Wpisz kod, który wysłał Ci host:", 24, FontStyle.Normal, 40);
            var codeRow = Row(joinPanel.transform, 64);
            codeInput = MakeField(codeRow, "KOD LOBBY", 12, InputField.ContentType.Alphanumeric, OnNext);
            MakeButton(codeRow, "Wklej", OnPaste, 140);
            nextButton = MakeButton(joinPanel.transform, "Dalej", OnNext);
            MakeButton(joinPanel.transform, "Wstecz", () => Show(mainPanel));

            // Invitation: accept / decline
            invitePanel = Panel(root, "Invite");
            inviteText = MakeLabel(invitePanel.transform, "", 30, FontStyle.Normal, 200);
            var inviteRow = Row(invitePanel.transform, 64);
            acceptButton = MakeButton(inviteRow, "Akceptuj", OnAccept);
            MakeButton(inviteRow, "Odrzuć", OnDecline);

            // Lobby
            lobbyPanel = Panel(root, "Lobby", 760);
            MakeLabel(lobbyPanel.transform, "Lobby", 48, FontStyle.Bold, 70);
            hostOnly = new GameObject("HostInvite", typeof(RectTransform));
            hostOnly.transform.SetParent(lobbyPanel.transform, false);
            var hostLayout = hostOnly.AddComponent<VerticalLayoutGroup>();
            hostLayout.spacing = 8;
            hostLayout.childControlHeight = hostLayout.childControlWidth = true;
            hostLayout.childForceExpandHeight = false;
            MakeLabel(hostOnly.transform, "Zaproś drugiego gracza – wyślij mu ten kod:", 24, FontStyle.Normal, 36);
            var inviteCodeRow = Row(hostOnly.transform, 64);
            codeText = MakeLabel(inviteCodeRow, "...", 44, FontStyle.Bold, 64);
            MakeButton(inviteCodeRow, "Kopiuj", OnCopyCode, 160);
            var startRow = Row(hostOnly.transform, 50);
            MakeButton(startRow, "<", () => bootstrap.CycleStartScene(-1), 60);
            startText = MakeLabel(startRow, "", 26, FontStyle.Normal, 50);
            MakeButton(startRow, ">", () => bootstrap.CycleStartScene(1), 60);

            Space(lobbyPanel.transform, 10);
            MakeLabel(lobbyPanel.transform, "Gracze", 26, FontStyle.Bold, 36);
            playersText = MakeLabel(lobbyPanel.transform, "", 26, FontStyle.Normal, 90);
            playersText.alignment = TextAnchor.MiddleLeft;
            Space(lobbyPanel.transform, 10);
            readyButton = MakeButton(lobbyPanel.transform, "Gotowy", OnToggleReady);
            readyLabel = readyButton.GetComponentInChildren<Text>();
            playButton = MakeButton(lobbyPanel.transform, "Graj", OnPlay);
            waitText = MakeLabel(lobbyPanel.transform, "", 22, FontStyle.Italic, 34);
            MakeButton(lobbyPanel.transform, "Opuść lobby", OnLeave);

            // Status (errors, "Tworzenie lobby...") under everything
            var status = NewRect("Status", root);
            status.anchorMin = new Vector2(0, 0);
            status.anchorMax = new Vector2(1, 0);
            status.pivot = new Vector2(0.5f, 0);
            status.anchoredPosition = new Vector2(0, 40);
            status.sizeDelta = new Vector2(-80, 60);
            statusText = AddText(status.gameObject, "", 24, FontStyle.Normal);
            statusText.color = new Color(1f, 0.85f, 0.5f);

            Show(mainPanel);
        }

        RectTransform NewRect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        GameObject Panel(Transform parent, string name, float width = 560)
        {
            var rect = NewRect(name, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, 0);
            rect.gameObject.AddComponent<Image>().color = new Color(0.13f, 0.15f, 0.2f, 0.95f);
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(40, 40, 30, 30);
            layout.spacing = 12;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = rect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rect.gameObject;
        }

        Transform Row(Transform parent, float height)
        {
            var rect = NewRect("Row", parent);
            rect.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            return rect;
        }

        void Space(Transform parent, float height) =>
            NewRect("Space", parent).gameObject.AddComponent<LayoutElement>().preferredHeight = height;

        Text AddText(GameObject target, string text, int size, FontStyle style)
        {
            var label = target.AddComponent<Text>();
            label.font = font;
            label.text = text;
            label.fontSize = size;
            label.fontStyle = style;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.supportRichText = true;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }

        Text MakeLabel(Transform parent, string text, int size, FontStyle style, float height)
        {
            var rect = NewRect("Label", parent);
            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            element.flexibleWidth = 1;
            return AddText(rect.gameObject, text, size, style);
        }

        Button MakeButton(Transform parent, string text, Action onClick, float width = -1)
        {
            var rect = NewRect(text, parent);
            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = 60;
            if (width > 0) element.preferredWidth = element.minWidth = width;
            else element.flexibleWidth = 1;

            var image = rect.gameObject.AddComponent<Image>();
            image.color = Color.white;
            var button = rect.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = new Color(0.25f, 0.45f, 0.75f);
            colors.highlightedColor = new Color(0.33f, 0.55f, 0.88f);
            colors.pressedColor = new Color(0.18f, 0.35f, 0.6f);
            colors.selectedColor = colors.normalColor;
            colors.disabledColor = new Color(0.3f, 0.3f, 0.33f, 0.6f);
            button.colors = colors;
            button.onClick.AddListener(() => onClick());

            var labelRect = NewRect("Text", rect);
            Stretch(labelRect);
            AddText(labelRect.gameObject, text, 28, FontStyle.Bold);
            return button;
        }

        InputField MakeField(Transform parent, string placeholder, int limit, InputField.ContentType type, Action onSubmit)
        {
            var rect = NewRect("Field", parent);
            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.flexibleWidth = 1;
            element.preferredHeight = 60;
            rect.gameObject.AddComponent<Image>().color = new Color(0.08f, 0.09f, 0.12f);

            var textRect = NewRect("Text", rect);
            Stretch(textRect);
            textRect.offsetMin = new Vector2(16, 0);
            textRect.offsetMax = new Vector2(-16, 0);
            var text = AddText(textRect.gameObject, "", 34, FontStyle.Bold);
            text.supportRichText = false;

            var placeholderRect = NewRect("Placeholder", rect);
            Stretch(placeholderRect);
            var hint = AddText(placeholderRect.gameObject, placeholder, 28, FontStyle.Italic);
            hint.color = new Color(1, 1, 1, 0.3f);

            var field = rect.gameObject.AddComponent<InputField>();
            field.textComponent = text;
            field.placeholder = hint;
            field.characterLimit = limit;
            field.contentType = type;
            if (onSubmit != null) field.onSubmit.AddListener(_ => onSubmit());
            return field;
        }
    }
}
