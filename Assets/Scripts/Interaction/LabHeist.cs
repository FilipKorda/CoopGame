using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace CoopGame
{
    /// <summary>
    /// The map heist. The vent door stays open only while someone is in the monitoring room; once a player is
    /// inside the vent it slams shut for good. The operator (at the monitoring panel) opens the vent's
    /// symbol doors with the buttons on the panel (caught with the wrench beam), one symbol at a time, and types the 5-symbol code that lifts the glass dome. The thief takes
    /// the map (F), which switches on the jump pad back to the monitoring room. The server keeps the state; every
    /// machine moves the doors from it.
    /// </summary>
    public class LabHeist : NetworkBehaviour
    {
        [Header("Monitoring room")]
        [SerializeField] Transform monitoringPanel;
        [Tooltip("A player this close to the panel counts as being in the monitoring room.")]
        [SerializeField] float roomRadius = 5f;
        [SerializeField] float consoleReach = 5f;

        [Header("Vent entrance")]
        [SerializeField] Transform ventDoor;
        [SerializeField] float ventDoorLift = 3.3f;
        [Tooltip("The first stretch of the vent behind the door (local box): a player in it shuts the door for good.")]
        [SerializeField] Transform ventInside;
        [SerializeField] Vector3 ventInsideSize = new Vector3(7f, 5f, 6f);

        [Header("Symbol doors in the vent")]
        [Tooltip("Names of the symbols, as the operator's console lists them. The first ones are on the vent doors, the rest only in the code.")]
        [SerializeField] string[] symbolNames;
        [SerializeField] Transform[] doors;
        [Tooltip("Symbol (index into Symbol Names) of each door.")]
        [SerializeField] int[] doorSymbols;
        [SerializeField] float doorLift = 4.6f;

        [Header("Glass dome")]
        [Tooltip("The code, in the order of Symbol 1..5 in front of the dome (indexes into Symbol Names).")]
        [SerializeField] int[] code;
        [SerializeField] Transform dome;
        [SerializeField] float domeLift = 2.6f;
        [SerializeField] Transform map;
        [SerializeField] float mapReach = 2.4f;
        [Tooltip("Switched on when the map is taken (takes the thief back to the monitoring room).")]
        [SerializeField] Behaviour exitPad;

        [Header("Buttons on the monitoring panel")]
        [Tooltip("One per door symbol (index = symbol): opens every vent door with it.")]
        [SerializeField] Transform[] doorButtons = new Transform[0];
        [Tooltip("The dome code keypad (index = symbol).")]
        [SerializeField] Transform[] codeButtons = new Transform[0];
        [SerializeField] float buttonDepth = 0.08f;

        [SerializeField] float moveSpeed = 3f;

        public UnityEvent onMapTakenServer = new();

        readonly NetworkVariable<bool> operatorPresent = new();
        readonly NetworkVariable<bool> ventLocked = new();
        readonly NetworkVariable<sbyte> openSymbol = new(-1);
        readonly NetworkVariable<byte> codeProgress = new();
        readonly NetworkVariable<bool> domeOpen = new();
        readonly NetworkVariable<bool> mapTaken = new();

        Vector3 ventRest, domeRest; Vector3[] doorRest;
        InputAction interactAction;
        bool nearPanel, nearMap;
        int aimedButton = -1;
        Vector3[] buttonRest; float[] pressedAt = new float[0];
        Graspable[] grips; bool[] gripHeld;
        float nextPress, messageUntil;
        string message;

        public bool VentOpen => operatorPresent.Value && !ventLocked.Value;
        public bool VentLocked => ventLocked.Value;
        public int OpenSymbol => openSymbol.Value;
        public int CodeProgress => codeProgress.Value;
        public bool DomeOpen => domeOpen.Value;
        public bool MapTaken => mapTaken.Value;

        void Awake()
        {
            if (ventDoor != null) ventRest = ventDoor.position;
            if (dome != null) domeRest = dome.position;
            doorRest = new Vector3[doors != null ? doors.Length : 0];
            for (int i = 0; i < doorRest.Length; i++) if (doors[i] != null) doorRest[i] = doors[i].position;
        }

        public override void OnNetworkSpawn()
        {
            interactAction = InputSystem.actions.FindAction("Player/Interact", true);
            mapTaken.OnValueChanged += (_, _) => ApplyMap();
            ApplyMap();
        }

        void ApplyMap()
        {
            if (map != null) map.gameObject.SetActive(!mapTaken.Value);
            if (exitPad != null) exitPad.enabled = mapTaken.Value;
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (IsServer) ServerTick();
            MoveParts(Time.deltaTime);
            LocalInput();
        }

        // ---------- Server ----------

        void ServerTick()
        {
            bool present = false, inVent = false;
            foreach (var p in PlayerHealth.All)
            {
                if (p == null || !p.IsAlive) continue;
                Vector3 pos = p.transform.position;
                if (monitoringPanel != null && Vector3.Distance(pos + Vector3.up, monitoringPanel.position) <= roomRadius) present = true;
                if (ventInside != null)
                {
                    Vector3 local = ventInside.InverseTransformPoint(pos + Vector3.up);
                    if (Mathf.Abs(local.x) <= ventInsideSize.x * 0.5f && Mathf.Abs(local.y) <= ventInsideSize.y * 0.5f && Mathf.Abs(local.z) <= ventInsideSize.z * 0.5f) inVent = true;
                }
            }
            if (operatorPresent.Value != present) operatorPresent.Value = present;
            if (inVent && !ventLocked.Value) { ventLocked.Value = true; SayRpc("Wentylacja zatrzasnęła się za wami na stałe!"); }
            ServerButtons();
        }

        // A panel button is pressed by catching it with the wrench beam: the moment the beam latches on it counts
        // once, and the beam lets go so it works like a click.
        void ServerButtons()
        {
            if (grips == null || grips.Length != ButtonCount)
            {
                grips = new Graspable[ButtonCount]; gripHeld = new bool[ButtonCount];
                for (int i = 0; i < ButtonCount; i++) if (Button(i) != null) grips[i] = Button(i).GetComponent<Graspable>();
            }
            for (int i = 0; i < grips.Length; i++)
            {
                if (grips[i] == null) continue;
                bool held = grips[i].IsHeld;
                if (held && !gripHeld[i])
                {
                    ServerPress(i);
                    grips[i].ServerForceRelease();
                }
                gripHeld[i] = held;
            }
        }

        /// <summary>Server: what panel button <paramref name="index"/> does (door buttons first, then the keypad).</summary>
        public void ServerPress(int index)
        {
            if (index < 0 || index >= ButtonCount) return;
            int symbol = ButtonSymbol(index);
            if (index < doorButtons.Length) ServerOpenDoors(symbol == openSymbol.Value ? -1 : symbol);
            else if (!domeOpen.Value) ServerEnterCode(symbol);
            PressedRpc(index);
        }

        [Rpc(SendTo.Everyone)]
        void PressedRpc(int index) { if (index >= 0 && index < pressedAt.Length) pressedAt[index] = Time.time; }


        /// <summary>Server: open every vent door with this symbol (the others close). -1 closes all.</summary>
        public void ServerOpenDoors(int symbol) => openSymbol.Value = (sbyte)Mathf.Clamp(symbol, -1, symbolNames.Length - 1);

        /// <summary>Server: the operator presses a symbol of the dome code.</summary>
        public void ServerEnterCode(int symbol)
        {
            if (domeOpen.Value || code == null || code.Length == 0) return;
            if (symbol == code[codeProgress.Value])
            {
                codeProgress.Value++;
                if (codeProgress.Value >= code.Length) { domeOpen.Value = true; SayRpc("Kod przyjęty. Szklana kopuła się podnosi!"); }
            }
            else { codeProgress.Value = 0; SayRpc("Błędny kod! System się zresetował, zacznijcie od początku."); }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void TakeMapRpc(RpcParams rpcParams = default)
        {
            foreach (var p in PlayerHealth.All)
                if (p.OwnerClientId == rpcParams.Receive.SenderClientId && p.IsAlive && Vector3.Distance(p.transform.position + Vector3.up, map.position) <= mapReach + 2f) { ServerTakeMap(); return; }
        }

        /// <summary>Server: the thief takes the map (only once the dome is up).</summary>
        public bool ServerTakeMap()
        {
            if (!domeOpen.Value || mapTaken.Value) return false;
            mapTaken.Value = true;
            SayRpc("Mapa zdobyta! Wskakuj na wyrzutnię, zabierze cię do operatora.");
            onMapTakenServer.Invoke();
            return true;
        }

        [Rpc(SendTo.Everyone)]
        void SayRpc(string text) { message = text; messageUntil = Time.time + 4.5f; }

        // ---------- Every machine ----------

        void MoveParts(float dt)
        {
            float step = moveSpeed * dt;
            if (ventDoor != null) ventDoor.position = Vector3.MoveTowards(ventDoor.position, ventRest + Vector3.up * (VentOpen ? ventDoorLift : 0f), step * 1.5f);
            for (int i = 0; i < doorRest.Length; i++)
                if (doors[i] != null) doors[i].position = Vector3.MoveTowards(doors[i].position, doorRest[i] + Vector3.up * (doorSymbols[i] == openSymbol.Value ? doorLift : 0f), step);
            if (dome != null) dome.position = Vector3.MoveTowards(dome.position, domeRest + Vector3.up * (domeOpen.Value ? domeLift : 0f), step * 0.5f);
        }

        void LocalInput()
        {
            nearPanel = nearMap = false; aimedButton = -1;
            var player = PlayerHealth.Local;
            if (player == null || !player.IsAlive || CutsceneSystem.ControlsLocked) return;
            Vector3 chest = player.transform.position + Vector3.up;
            nearPanel = monitoringPanel != null && Vector3.Distance(chest, monitoringPanel.position) <= consoleReach;
            nearMap = map != null && domeOpen.Value && !mapTaken.Value && Vector3.Distance(chest, map.position) <= mapReach;
            if (nearPanel) aimedButton = AimedButton(chest);
            if (interactAction == null || !interactAction.WasPressedThisFrame() || Time.time < nextPress) return;
            nextPress = Time.time + 0.25f;
            if (nearMap) TakeMapRpc();
        }

        // Buttons: door buttons first (index = symbol), then the keypad (index - door count = symbol).
        int ButtonCount => doorButtons.Length + codeButtons.Length;
        Transform Button(int i) => i < doorButtons.Length ? doorButtons[i] : codeButtons[i - doorButtons.Length];
        int ButtonSymbol(int i) => i < doorButtons.Length ? i : i - doorButtons.Length;

        // The button the camera looks at (the one nearest the middle of the screen, within a hand's width of the ray).
        int AimedButton(Vector3 chest)
        {
            var cam = Camera.main; if (cam == null) return -1;
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            int best = -1; float bestDist = 0.5f;
            for (int i = 0; i < ButtonCount; i++)
            {
                var b = Button(i); if (b == null) continue;
                Vector3 to = b.position - ray.origin; float along = Vector3.Dot(to, ray.direction);
                if (along < 0.2f || along > 12f) continue;
                float off = Vector3.Cross(ray.direction, to).magnitude;
                if (off < bestDist) { bestDist = off; best = i; }
            }
            if (best < 0)
            {
                // No exact aim: the button nearest the chest, if it's right in front.
                float near = 0.9f;
                for (int i = 0; i < ButtonCount; i++) { var b = Button(i); if (b == null) continue; float d = Vector3.Distance(chest, b.position); if (d < near) { near = d; best = i; } }
            }
            return best;
        }

        void LateUpdate()
        {
            if (buttonRest == null || buttonRest.Length != ButtonCount)
            {
                buttonRest = new Vector3[ButtonCount]; pressedAt = new float[ButtonCount];
                for (int i = 0; i < ButtonCount; i++) { if (Button(i) != null) buttonRest[i] = Button(i).localPosition; pressedAt[i] = -10f; }
            }
            for (int i = 0; i < ButtonCount; i++)
            {
                var b = Button(i); if (b == null) continue;
                bool held = (i < doorButtons.Length && ButtonSymbol(i) == openSymbol.Value) || Time.time - pressedAt[i] < 0.2f;
                // Pushed in along its local +z (into the panel) while pressed or while its doors are open.
                b.localPosition = buttonRest[i] + b.localRotation * Vector3.forward * (held ? buttonDepth : 0f);
                b.localScale = Vector3.one * (i == aimedButton ? 1.12f : 1f);
            }
        }

        void OnGUI()
        {
            if (!IsSpawned || CutsceneSystem.HidesGameplayUI) return;
            var box = new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.MiddleCenter, richText = true, wordWrap = true };
            if (Time.time < messageUntil && !string.IsNullOrEmpty(message))
                GUI.Box(new Rect(Screen.width * 0.5f - 420f, Screen.height - 250f, 840f, 52f), message, box);
            if (nearPanel)
            {
                string vent = ventLocked.Value ? "<color=#ff7766>zatrzaśnięta</color>" : VentOpen ? "<color=#77ee77>otwarta</color>" : "zamknięta";
                string open = openSymbol.Value >= 0 ? symbolNames[openSymbol.Value] : "—";
                string dome = domeOpen.Value ? "<color=#77ee77>otwarta</color>" : $"kod {codeProgress.Value}/{code.Length}";
                GUI.Box(new Rect(Screen.width * 0.5f - 380f, 96f, 760f, 44f), $"Wentylacja: {vent}   |   Otwarte drzwi: {open}   |   Kopuła: {dome}", box);
            }
            string hint = nearMap ? "[F] Zabierz mapę"
                : aimedButton >= 0 && aimedButton < doorButtons.Length ? $"Promień: drzwi {symbolNames[ButtonSymbol(aimedButton)]}" + (ButtonSymbol(aimedButton) == openSymbol.Value ? " (zamknij)" : "")
                : aimedButton >= 0 && !domeOpen.Value ? $"Promień: kod kopuły {symbolNames[ButtonSymbol(aimedButton)]}" : null;
            if (hint != null) GUI.Box(new Rect(Screen.width * 0.5f - 230f, Screen.height - 190f, 460f, 44f), hint, box);
        }

        public void Setup(Transform panel, Transform vent, Transform ventInsideBox, string[] names, Transform[] ventDoors, int[] ventDoorSymbols,
            int[] domeCode, Transform domeTransform, Transform mapTransform, Behaviour pad)
        {
            monitoringPanel = panel; ventDoor = vent; ventInside = ventInsideBox; symbolNames = names; doors = ventDoors; doorSymbols = ventDoorSymbols;
            code = domeCode; dome = domeTransform; map = mapTransform; exitPad = pad;
        }

        public void SetupButtons(Transform[] doorPanelButtons, Transform[] codePanelButtons)
        {
            doorButtons = doorPanelButtons; codeButtons = codePanelButtons;
        }
    }
}
