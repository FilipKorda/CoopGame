using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoopGame
{
    /// <summary>
    /// A control panel of symbol buttons (<see cref="SymbolButton"/> children) that slide platforms
    /// (<see cref="SymbolPlatform"/>) out of a wall and back in: every platform with the lit symbol is out, the rest
    /// are in. A player standing at the panel presses the nearest button with Interact (F); pressing a lit symbol
    /// turns it off.
    /// Limited (default): at most <see cref="maxLit"/> symbols lit at once; pressing another one then turns off the one
    /// lit earliest (or, with <see cref="replaceOldest"/> off, does nothing until one is turned off).
    /// Toggle: no limit, each button switches its own symbol on and off.
    /// The server keeps which symbols are lit (shared with everyone); every client slides its platforms from that.
    /// <see cref="ServerLock"/> switches it off for good (e.g. hooked to the turbine lever's On Connected).
    /// </summary>
    public class SymbolPanel : NetworkBehaviour
    {
        public enum Mode : byte { Limited, Toggle }

        [SerializeField] Mode mode = Mode.Limited;
        [Tooltip("Limited: how many symbols can be lit at once.")]
        [SerializeField, Min(1)] int maxLit = 2;
        [Tooltip("Limited: pressing another symbol when the limit is reached turns off the one lit earliest. " +
                 "Off: the press does nothing until a lit symbol is turned off.")]
        [SerializeField] bool replaceOldest = true;
        [Tooltip("The platforms it moves (each slides with its own symbol).")]
        [SerializeField] SymbolPlatform[] platforms;
        [Tooltip("How close (metres) a player's chest has to be to a button to press it.")]
        [SerializeField] float reach = 1.9f;
        [Tooltip("Shortest time between two presses (seconds).")]
        [SerializeField] float pressCooldown = 0.25f;
        [Tooltip("Show the '[F] symbol' hint near the buttons.")]
        [SerializeField] bool showHint = true;

        readonly NetworkVariable<int> lit = new();
        // Switched off for good (e.g. once the turbine is connected): dark, no presses; the platforms stay put.
        readonly NetworkVariable<bool> locked = new();

        SymbolButton[] buttons;
        SymbolButton nearest;
        InputAction interactAction;
        float nextPress, fullMessageUntil;
        // Server: lit symbols in the order they were lit (the first is turned off first when the limit is reached).
        readonly System.Collections.Generic.List<PlatformSymbol> litOrder = new();

        /// <summary>Which symbols are lit (one bit per <see cref="PlatformSymbol"/>).</summary>
        public int LitSymbols => lit.Value;
        public bool IsLit(PlatformSymbol symbol) => (lit.Value & PlatformSymbols.Bit(symbol)) != 0;
        public bool IsLocked => locked.Value;

        /// <summary>
        /// Server: switch the panel off for good: its buttons go dark and can't be pressed any more, the platforms stay
        /// where they are. Hook it to an event, e.g. the turbine lever's On Connected.
        /// </summary>
        public void ServerLock()
        {
            if (IsServer) locked.Value = true;
        }

        void Awake()
        {
            buttons = GetComponentsInChildren<SymbolButton>(true);
        }

        public override void OnNetworkSpawn()
        {
            interactAction = InputSystem.actions.FindAction("Player/Interact", true);
            lit.OnValueChanged += OnLitChanged;
            // Joining a game in progress: put the platforms where they already are.
            Apply(lit.Value, true);
        }

        public override void OnNetworkDespawn()
        {
            lit.OnValueChanged -= OnLitChanged;
        }

        void OnLitChanged(int previous, int current) => Apply(current, false);

        void Apply(int mask, bool instant)
        {
            if (platforms == null) return;
            foreach (var platform in platforms)
                if (platform != null) platform.SetExtended((mask & PlatformSymbols.Bit(platform.Symbol)) != 0, instant);
        }

        void Update()
        {
            if (!IsSpawned) return;
            nearest = locked.Value ? null : FindNearestButton();
            if (nearest != null && interactAction != null && interactAction.WasPressedThisFrame() && Time.time >= nextPress)
            {
                nextPress = Time.time + pressCooldown;
                if (WouldRefuse(nearest.Symbol)) fullMessageUntil = Time.time + 2f;
                else
                {
                    nearest.Press();
                    PressRpc((byte)nearest.Symbol);
                }
            }
            foreach (var button in buttons)
                if (button != null) button.Show(IsLit(button.Symbol), button == nearest, locked.Value);
        }

        // The button nearest the local player's chest, if within reach (and the player is able to press it).
        SymbolButton FindNearestButton()
        {
            var player = PlayerHealth.Local;
            if (player == null || !player.IsAlive || CutsceneSystem.ControlsLocked) return null;
            Vector3 chest = player.transform.position + Vector3.up * 1.2f;
            SymbolButton best = null;
            float bestDistance = reach;
            foreach (var button in buttons)
            {
                if (button == null) continue;
                float distance = Vector3.Distance(chest, button.transform.position);
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = button;
                }
            }
            return best;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void PressRpc(byte symbol, RpcParams rpcParams = default)
        {
            if (symbol >= PlatformSymbols.Count) return;
            // Only from someone standing at that button.
            SymbolButton button = null;
            foreach (var b in buttons) if (b != null && (byte)b.Symbol == symbol) button = b;
            if (button == null) return;
            foreach (var player in PlayerHealth.All)
                if (player.OwnerClientId == rpcParams.Receive.SenderClientId
                    && Vector3.Distance(player.transform.position + Vector3.up * 1.2f, button.transform.position) <= reach + 1.5f)
                {
                    if (ServerPress((PlatformSymbol)symbol)) PressedRpc(symbol);
                    return;
                }
        }

        // Everyone else sees the button sink in too.
        [Rpc(SendTo.NotServer)]
        void PressedRpc(byte symbol)
        {
            foreach (var b in buttons)
                if (b != null && (byte)b.Symbol == symbol && b != nearest) b.Press();
        }

        /// <summary>Server: press a symbol as if a player had (tests, scripted sequences). False if it did nothing.</summary>
        public bool ServerPress(PlatformSymbol symbol)
        {
            if (!IsServer || locked.Value) return false;
            int bit = PlatformSymbols.Bit(symbol);
            int mask = lit.Value;
            // Keep the lighting order in step with what's lit (it may have been set directly).
            litOrder.RemoveAll(s => (mask & PlatformSymbols.Bit(s)) == 0);
            for (int i = 0; i < PlatformSymbols.Count; i++)
                if ((mask & (1 << i)) != 0 && !litOrder.Contains((PlatformSymbol)i)) litOrder.Add((PlatformSymbol)i);

            if ((mask & bit) != 0)
            {
                mask &= ~bit;
                litOrder.Remove(symbol);
            }
            else
            {
                if (mode == Mode.Limited && litOrder.Count >= maxLit)
                {
                    if (!replaceOldest) return false;
                    while (litOrder.Count >= maxLit)
                    {
                        mask &= ~PlatformSymbols.Bit(litOrder[0]);
                        litOrder.RemoveAt(0);
                    }
                }
                mask |= bit;
                litOrder.Add(symbol);
            }
            lit.Value = mask;
            return true;
        }

        static int CountLit(int mask)
        {
            int count = 0;
            for (; mask != 0; mask &= mask - 1) count++;
            return count;
        }

        // Limited without replacing: a press that the panel would refuse right now.
        bool WouldRefuse(PlatformSymbol symbol) => mode == Mode.Limited && !replaceOldest && !IsLit(symbol) && CountLit(lit.Value) >= maxLit;

        /// <summary>Server: light exactly these symbols (tests, scripted sequences).</summary>
        public void ServerSetLit(int mask)
        {
            if (IsServer) lit.Value = mask;
        }

        /// <summary>Editor setup: the platforms it moves.</summary>
        public void SetPlatforms(SymbolPlatform[] value) => platforms = value;

        void OnGUI()
        {
            if (!showHint || nearest == null || CutsceneSystem.HidesGameplayUI) return;
            string state = IsLit(nearest.Symbol) ? "  (wysunięte)"
                : Time.time < fullMessageUntil ? $"  — najpierw wyłącz jeden (maks. {maxLit})" : "";
            var style = new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
            GUI.Box(new Rect(Screen.width * 0.5f - 230f, Screen.height - 190f, 460f, 44f),
                "[F] " + PlatformSymbols.DisplayName(nearest.Symbol) + state, style);
        }
    }
}
