using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoopGame
{
    /// <summary>
    /// Placeholder connection menu living on the NetworkManager object in the bootstrap scene.
    /// Starts a game either directly over UnityTransport (LAN / Multiplayer Play Mode)
    /// or through a Multiplayer Services session with Relay, then loads the level for everyone.
    /// The host picks where the game starts: the test scene or the start of any act in the Build Settings
    /// ("A1_Start", "A2_Start"...). The server also spawns the
    /// game session object (<see cref="sessionPrefab"/>: team bolts, level transitions), which lives on across
    /// every level.
    /// In the editor, pressing Play with a level scene open goes straight into that scene as host
    /// (see the editor's play-from-scene hook).
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class NetworkBootstrap : MonoBehaviour
    {
        /// <summary>SessionState key the editor sets to the scene that was open when Play was pressed.</summary>
        public const string PlayFromSceneKey = "CoopGame.PlayFromScene";
        /// <summary>SessionState key the editor sets in a Multiplayer Play Mode virtual player: join the local host.</summary>
        public const string JoinLocalKey = "CoopGame.JoinLocal";
        const string StartChoiceKey = "CoopGame.StartSceneName";
        /// <summary>Start scene of an act: "A1_Start", "A2_Start"... (the number is the act).</summary>
        static readonly Regex ActStartScene = new(@"^A(\d+)_Start$");

        [Tooltip("The test scene; also the fallback when the chosen scene can't be loaded.")]
        [SerializeField] string gameSceneName = "Game";
        [SerializeField] string bootstrapSceneName = "Bootstrap";
        [Tooltip("Title screen the lobby's \"Wróć\" button goes back to.")]
        [SerializeField] string mainMenuSceneName = "MainMenu";
        [Tooltip("Spawned by the server when a game starts and kept across level loads (team bolts, transitions).")]
        [SerializeField] GameObject sessionPrefab;
        [SerializeField] int maxPlayers = 2;
        [SerializeField] ushort directPort = 7777;

        NetworkManager networkManager;
        ISession session;
        string address = "127.0.0.1";
        string joinCode = "";
        string status = "";
        bool busy;
        bool returnToBootstrap;
        string sceneOverride;
        // Host: the session came from the lobby menu, so wait for "Play" instead of loading the level at once.
        bool holdInLobby;
        // F1 in the menu: the old developer panel (direct LAN host/join, quick session).
        bool showDevMenu;

        // Where the host can start: the test scene and each act's start scene that's in the Build Settings.
        string[] startScenes, startLabels;
        int startChoice;

        string StartScene => !string.IsNullOrEmpty(sceneOverride) ? sceneOverride
            : startScenes.Length > 0 ? startScenes[startChoice] : gameSceneName;

        void Awake()
        {
            networkManager = GetComponent<NetworkManager>();
            networkManager.OnServerStarted += OnServerStarted;
            networkManager.OnClientStopped += OnClientStopped;
            FindStartScenes();
        }

        /// <summary>
        /// Lists the start choices from the Build Settings: the test scene first ("Scena testowa"), then every
        /// "A{n}_Start" scene as "Akt n" in act order — adding an act's start scene to the build adds it here.
        /// Picks the last choice made on this machine, else Act 1.
        /// </summary>
        void FindStartScenes()
        {
            var found = new List<(int order, string scene, string label)>();
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                string scene = Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(i));
                if (scene == gameSceneName) found.Add((0, scene, "Scena testowa"));
                else if (ActStartScene.Match(scene) is { Success: true } act)
                {
                    int number = int.Parse(act.Groups[1].Value);
                    found.Add((number, scene, $"Akt {number}"));
                }
            }
            found.Sort((a, b) => a.order.CompareTo(b.order));
            startScenes = found.Select(f => f.scene).ToArray();
            startLabels = found.Select(f => f.label).ToArray();

            startChoice = Array.IndexOf(startScenes, PlayerPrefs.GetString(StartChoiceKey, ""));
            if (startChoice < 0) startChoice = Math.Max(0, found.FindIndex(f => f.order == 1));
        }

        void Start()
        {
#if UNITY_EDITOR
            // Play pressed with a level open in the editor: host straight into it.
            string playFrom = UnityEditor.SessionState.GetString(PlayFromSceneKey, "");
            UnityEditor.SessionState.EraseString(PlayFromSceneKey);
            if (!string.IsNullOrEmpty(playFrom))
            {
                sceneOverride = playFrom;
                StartDirectHost();
            }

            // A Multiplayer Play Mode virtual player: join the main editor's host (the transport keeps trying for a
            // while, so it doesn't matter which editor starts first).
            if (UnityEditor.SessionState.GetBool(JoinLocalKey, false))
            {
                UnityEditor.SessionState.EraseBool(JoinLocalKey);
                address = "127.0.0.1";
                StartDirectClient();
            }
#endif
            // The main menu (create / join a lobby). It lives in this scene, so it goes away when the level loads.
            LobbyMenu.Create(this);
        }

        void OnDestroy()
        {
            if (networkManager == null) return;
            networkManager.OnServerStarted -= OnServerStarted;
            networkManager.OnClientStopped -= OnClientStopped;
        }

        void Update()
        {
            if (IsInMenu && UnityEngine.InputSystem.Keyboard.current is { f1Key: { wasPressedThisFrame: true } })
                showDevMenu = !showDevMenu;

            if (!returnToBootstrap) return;
            returnToBootstrap = false;

            // The bootstrap scene creates a fresh NetworkManager, so drop this persistent one.
            Destroy(gameObject);
            SceneManager.LoadScene(LeaveToMainMenu ? mainMenuSceneName : bootstrapSceneName);
            LeaveToMainMenu = false;
        }

        /// <summary>Set while the game's last level plays (<see cref="GameOver"/>): leaving goes to the title screen, not the lobby.</summary>
        public static bool LeaveToMainMenu;

        /// <summary>This machine: leave the game (the host leaving ends it for both).</summary>
        public static void LeaveGame()
        {
            var bootstrap = FindAnyObjectByType<NetworkBootstrap>();
            if (bootstrap != null) bootstrap.Disconnect();
        }

        void OnServerStarted()
        {
            if (!holdInLobby) LoadStartScene();
        }

        void LoadStartScene()
        {
            if (sessionPrefab != null)
            {
                // Not tied to any level: it moves along to every scene the game loads.
                var sessionObject = Instantiate(sessionPrefab).GetComponent<NetworkObject>();
                sessionObject.Spawn(destroyWithScene: false);
            }

            string scene = StartScene;
            if (!Application.CanStreamedLevelBeLoaded(scene))
            {
                Debug.LogWarning($"[NetworkBootstrap] Scene '{scene}' isn't in the Build Settings; starting the test scene instead.");
                scene = gameSceneName;
            }
            networkManager.SceneManager.LoadScene(scene, LoadSceneMode.Single);
        }

        void OnClientStopped(bool wasHost)
        {
            session = null;
            returnToBootstrap = true;
        }

        // ---------- Lobby (used by LobbyMenu) ----------

        /// <summary>The Multiplayer Services session (lobby) this player is in, or null.</summary>
        public ISession Session => session;
        public NetworkManager Network => networkManager;
        public bool IsBusy => busy;
        public string Status => status;
        /// <summary>In the menu scene with a lobby joined: waiting for both players to be ready and the host's "Play".</summary>
        public bool InLobby => session != null && SceneManager.GetActiveScene().name == bootstrapSceneName;
        /// <summary>The main menu or the lobby is on screen (not in a level, not connecting straight to a game).</summary>
        public bool IsInMenu => InLobby || (!networkManager.IsListening && session == null);
        /// <summary>Where the host's game starts ("Akt 1"...).</summary>
        public string StartLabel => startLabels.Length > 0 ? startLabels[startChoice] : gameSceneName;

        /// <summary>Host: pick the previous / next start scene.</summary>
        public void CycleStartScene(int step)
        {
            if (startScenes.Length == 0) return;
            startChoice = (startChoice + step + startScenes.Length) % startScenes.Length;
            PlayerPrefs.SetString(StartChoiceKey, startScenes[startChoice]);
        }

        /// <summary>Create a 2-player lobby with Relay; the host waits in the menu until <see cref="StartGameFromLobby"/>.</summary>
        public async Task CreateLobby()
        {
            holdInLobby = true;
            await RunServiceCall("Tworzenie lobby...", async () =>
            {
                await EnsurePlayerName();
                var options = new SessionOptions { MaxPlayers = maxPlayers }.WithRelayNetwork().WithPlayerName();
                session = await MultiplayerService.Instance.CreateSessionAsync(options);
            });
            if (session == null) holdInLobby = false;
        }

        /// <summary>Join the lobby with this code (the invitation from the host).</summary>
        public async Task JoinLobby(string code)
        {
            code = (code ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(code))
            {
                status = "Wpisz kod lobby.";
                return;
            }
            await RunServiceCall("Dołączanie do lobby...", async () =>
            {
                await EnsurePlayerName();
                session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code, new JoinSessionOptions().WithPlayerName());
            });
        }

        /// <summary>Host: close the lobby to newcomers and take both players into the chosen level.</summary>
        public async void StartGameFromLobby()
        {
            if (!networkManager.IsServer || !holdInLobby) return;
            holdInLobby = false;
            LoadStartScene();

            if (session == null || !session.IsHost) return;
            try
            {
                session.AsHost().IsLocked = true;
                await session.AsHost().SavePropertiesAsync();
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>Leave the lobby (the host leaving ends it for both) and go back to the menu.</summary>
        public void LeaveLobby() => Disconnect();

        /// <summary>
        /// Back to the title screen (MainMenu scene). Only from the menu, not in a lobby or a game. The NetworkManager
        /// persists across scenes, so drop it: the bootstrap scene makes a fresh one when the player comes back.
        /// </summary>
        public void BackToMainMenu()
        {
            if (busy || session != null || networkManager.IsListening) return;
            Destroy(gameObject);
            SceneManager.LoadScene(mainMenuSceneName);
        }

        public void ClearStatus() => status = "";

        // ---------- Direct connection (UnityTransport) ----------

        void StartDirectHost()
        {
            Transport.SetConnectionData("127.0.0.1", directPort, "0.0.0.0");
            status = networkManager.StartHost() ? "" : "Failed to start host.";
        }

        void StartDirectClient()
        {
            Transport.SetConnectionData(address, directPort);
            status = networkManager.StartClient() ? $"Connecting to {address}:{directPort}..." : "Failed to start client.";
        }

        UnityTransport Transport => (UnityTransport)networkManager.NetworkConfig.NetworkTransport;

        // ---------- Multiplayer Services session (Relay) ----------

        async void CreateSession()
        {
            await RunServiceCall("Creating session...", async () =>
            {
                var options = new SessionOptions { MaxPlayers = maxPlayers }.WithRelayNetwork();
                session = await MultiplayerService.Instance.CreateSessionAsync(options);
            });
        }

        async void JoinSession()
        {
            string code = joinCode.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(code))
            {
                status = "Enter a join code.";
                return;
            }

            await RunServiceCall("Joining session...", async () =>
            {
                session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
            });
        }

        async Task RunServiceCall(string message, Func<Task> call)
        {
            busy = true;
            status = message;
            try
            {
                await EnsureSignedIn();
                await call();
                status = "";
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                status = $"Error: {e.Message}";
            }
            finally
            {
                busy = false;
            }
        }

        const string PlayerNameKey = "CoopGame.PlayerName";

        /// <summary>The name this player picked in the menu (saved on this machine; empty = a random one).</summary>
        public string DesiredPlayerName
        {
            get => PlayerPrefs.GetString(PlayerNameKey, "");
            set => PlayerPrefs.SetString(PlayerNameKey, CleanPlayerName(value));
        }

        /// <summary>Unity player names can't have spaces and are at most 50 characters.</summary>
        public static string CleanPlayerName(string name)
        {
            name = new string((name ?? "").Trim().Select(c => char.IsWhiteSpace(c) ? '_' : c).ToArray());
            return name.Length > 30 ? name.Substring(0, 30) : name;
        }

        /// <summary>
        /// Lobbies carry the player's name (<c>WithPlayerName</c>), which comes from the Authentication account, so it
        /// has to exist first: the name typed in the menu, or a generated one ("Name#1234").
        /// </summary>
        async Task EnsurePlayerName()
        {
            var auth = AuthenticationService.Instance;
            string wanted = DesiredPlayerName;
            string current = auth.PlayerName ?? "";
            int hash = current.IndexOf('#');
            string currentBase = hash >= 0 ? current.Substring(0, hash) : current;

            if (!string.IsNullOrEmpty(wanted) && wanted != currentBase)
                await auth.UpdatePlayerNameAsync(wanted);
            else if (string.IsNullOrEmpty(current))
                await auth.GetPlayerNameAsync(autoGenerate: true);
        }

        static async Task EnsureSignedIn()
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                var options = new InitializationOptions();
#if UNITY_EDITOR
                // Multiplayer Play Mode players share one machine; give each its own anonymous identity.
                options.SetProfile("mppm" + System.Diagnostics.Process.GetCurrentProcess().Id);
#endif
                await UnityServices.InitializeAsync(options);
            }

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        async void Disconnect()
        {
            if (session != null)
            {
                try { await session.LeaveAsync(); }
                catch (Exception e) { Debug.LogException(e); }
                session = null;
            }

            if (networkManager.IsListening) networkManager.Shutdown();
            returnToBootstrap = true;
        }

        // ---------- Placeholder UI ----------

        void OnGUI()
        {
            // The box would sit on the cutscene's black bar; it's back as soon as the cutscene ends.
            if (networkManager.IsListening && CutsceneSystem.HidesGameplayUI) return;
            // The lobby menu covers the menu scene; F1 brings back this developer panel.
            if (IsInMenu && !showDevMenu) return;
            GUILayout.BeginArea(new Rect(10, 10, 280, 400), GUI.skin.box);

            if (!networkManager.IsListening && session == null)
                DrawConnectMenu();
            else
                DrawSessionInfo();

            if (!string.IsNullOrEmpty(status)) GUILayout.Label(status);
            GUILayout.EndArea();
        }

        void DrawConnectMenu()
        {
            GUI.enabled = !busy;

            GUILayout.Label("Start (host)");
            // The test scene (always first) gets a row of its own, the acts three to a row under it.
            int choice = startChoice;
            int firstAct = startScenes.Length > 0 && startScenes[0] == gameSceneName ? 1 : 0;
            if (firstAct == 1 && GUILayout.Toggle(choice == 0, startLabels[0], GUI.skin.button)) choice = 0;
            for (int row = firstAct; row < startScenes.Length; row += 3)
            {
                GUILayout.BeginHorizontal();
                for (int i = row; i < Mathf.Min(row + 3, startScenes.Length); i++)
                    if (GUILayout.Toggle(i == choice, startLabels[i], GUI.skin.button)) choice = i;
                GUILayout.EndHorizontal();
            }
            if (choice != startChoice)
            {
                startChoice = choice;
                PlayerPrefs.SetString(StartChoiceKey, startScenes[choice]);
            }

            GUILayout.Space(6);
            GUILayout.Label("Direct (LAN / Play Mode)");
            if (GUILayout.Button("Host")) StartDirectHost();
            GUILayout.BeginHorizontal();
            address = GUILayout.TextField(address, GUILayout.Width(150));
            if (GUILayout.Button("Join IP")) StartDirectClient();
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.Label("Online session (Relay)");
            if (GUILayout.Button("Create Session")) CreateSession();
            GUILayout.BeginHorizontal();
            joinCode = GUILayout.TextField(joinCode, GUILayout.Width(150));
            if (GUILayout.Button("Join Code")) JoinSession();
            GUILayout.EndHorizontal();

            GUI.enabled = true;
        }

        void DrawSessionInfo()
        {
            string role = networkManager.IsHost ? "Host" : networkManager.IsServer ? "Server" : "Client";
            GUILayout.Label($"{role} | players: {(networkManager.IsServer ? networkManager.ConnectedClientsIds.Count.ToString() : "-")}");
            if (session != null)
            {
                GUILayout.Label("Join code:");
                GUILayout.TextField(session.Code ?? "");
            }
            GUILayout.Label("Esc = free cursor");
            if (GUILayout.Button("Disconnect")) Disconnect();
        }
    }
}
