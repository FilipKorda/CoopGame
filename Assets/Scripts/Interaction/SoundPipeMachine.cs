using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace CoopGame
{
    /// <summary>
    /// The merchant's broken pipe machine. F at the button blows air through the pipes one after another (left to
    /// right as the players see them); a sound object standing in front of an outlet plays its note, an empty pipe
    /// only hisses. F at the merchant: he hums his melody. Objects in the melody's order -> solved.
    /// The server decides what stands where; every machine plays the sounds.
    /// </summary>
    public class SoundPipeMachine : NetworkBehaviour
    {
        [Tooltip("The pipes, left to right as the players see them.")]
        [SerializeField] Transform[] pipes;
        [Tooltip("The objects that make the notes (carried in front of the pipes).")]
        [SerializeField] Transform[] soundObjects;
        [Tooltip("The note of each sound object, in Hz.")]
        [SerializeField] float[] notes;
        [Tooltip("The merchant's melody: which sound object belongs in front of each pipe, left to right.")]
        [SerializeField] int[] melody;
        [SerializeField] Transform button;
        [SerializeField] Transform merchant;
        [Tooltip("The way the air comes out of the pipes (world).")]
        [SerializeField] Vector3 outletDirection = Vector3.right;
        [Tooltip("An object counts for a pipe when it stands within Slot Radius of the point this far out of the outlet.")]
        [SerializeField] float slotDistance = 1.3f;
        [SerializeField] float slotRadius = 1.1f;
        [SerializeField] float reach = 2.4f;
        [SerializeField] float noteGap = 0.75f;
        [SerializeField] Material puffMaterial;

        [Header("Texts")]
        [SerializeField] string merchantName = "Kupiec";
        [SerializeField] string humLine = "(nuci) To moja melodia. Zagrajcie mi ją na tej maszynie, to porozmawiamy.";
        [SerializeField] string wrongLine = "Nie, nie... To nie jest moja melodia.";
        [SerializeField] string emptyLine = "Coś tu syczy, ale melodii brak. Brakuje dźwięków.";
        [SerializeField] string solvedLine = "Tak! To jest moja melodia! Dobrze... Porozmawiajmy o tym statku.";

        [Header("The flying ship (F at the merchant once it's solved)")]
        [Tooltip("Share of the team's bolts the ship costs.")]
        [SerializeField, Range(0f, 1f)] float shipPriceShare = 0.8f;
        [SerializeField] string boughtLine = "Umowa stoi! Latający statek jest wasz. Zapłaciliście {0} części.";

        [Tooltip("Server, once: the ship was bought.")]
        public UnityEvent onShipBoughtServer = new();
        [Tooltip("Server, once: the melody was played right.")]
        public UnityEvent onSolvedServer = new();
        [Tooltip("Every machine (also a player who joins later): the melody was played right.")]
        public UnityEvent onSolved = new();

        readonly NetworkVariable<bool> solved = new();
        readonly NetworkVariable<bool> shipBought = new();
        InputAction interactAction;
        float nextPress, busyUntil, lineUntil;
        string line;
        bool nearButton, nearMerchant, solvedApplied;
        static readonly Dictionary<int, AudioClip> s_Clips = new();
        static AudioClip s_Hiss;

        public bool IsSolved => solved.Value;
        public bool ShipBought => shipBought.Value;
        /// <summary>What the ship costs right now: a share of the team's bolts.</summary>
        public int ShipPrice => PickupSystem.Instance != null ? Mathf.RoundToInt(PickupSystem.Instance.TeamBolts * shipPriceShare) : 0;

        public override void OnNetworkSpawn()
        {
            interactAction = InputSystem.actions.FindAction("Player/Interact", true);
            solved.OnValueChanged += (_, now) => { if (now) ApplySolved(); };
            if (solved.Value) ApplySolved();
        }

        void ApplySolved()
        {
            if (solvedApplied) return;
            solvedApplied = true;
            onSolved.Invoke();
        }

        void Update()
        {
            if (!IsSpawned) return;
            nearButton = nearMerchant = false;
            var player = PlayerHealth.Local;
            if (player == null || !player.IsAlive || CutsceneSystem.ControlsLocked) return;
            Vector3 chest = player.transform.position + Vector3.up * 1.2f;
            nearButton = !solved.Value && button != null && Vector3.Distance(chest, button.position) <= reach;
            nearMerchant = !nearButton && !shipBought.Value && merchant != null && Vector3.Distance(chest, merchant.position) <= reach + 0.6f;
            if (interactAction == null || !interactAction.WasPressedThisFrame() || Time.time < nextPress || Time.time < busyUntil) return;
            if (nearButton) { nextPress = Time.time + 0.3f; BlowRpc(); }
            else if (nearMerchant && solved.Value) { nextPress = Time.time + 0.3f; BuyRpc(); }
            else if (nearMerchant) { nextPress = Time.time + 0.3f; HumRpc(); }
        }

        bool SenderNear(ulong clientId, Transform point)
        {
            foreach (var p in PlayerHealth.All)
                if (p.OwnerClientId == clientId && p.IsAlive && Vector3.Distance(p.transform.position + Vector3.up * 1.2f, point.position) <= reach + 2f) return true;
            return false;
        }

        // ---------- Server ----------

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void BlowRpc(RpcParams rpcParams = default)
        {
            if (solved.Value || Time.time < busyUntil || !SenderNear(rpcParams.Receive.SenderClientId, button)) return;
            ServerBlow();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void HumRpc(RpcParams rpcParams = default)
        {
            if (Time.time < busyUntil || !SenderNear(rpcParams.Receive.SenderClientId, merchant)) return;
            busyUntil = Time.time + melody.Length * noteGap + 0.5f;
            PlayRpc(MelodySlots(), true);
        }

        /// <summary>Server: what stands in front of each pipe (sound object index, -1 = nothing).</summary>
        public int[] ServerSlots()
        {
            var slots = new int[pipes.Length];
            var outlet = outletDirection.normalized;
            for (int i = 0; i < pipes.Length; i++)
            {
                slots[i] = -1;
                Vector3 spot = pipes[i].position + outlet * slotDistance;
                float best = slotRadius;
                for (int j = 0; j < soundObjects.Length; j++)
                {
                    Vector3 d = soundObjects[j].position - spot;
                    if (Mathf.Abs(d.y) > 1.8f) continue;
                    d.y = 0f;
                    if (d.magnitude <= best) { best = d.magnitude; slots[i] = j; }
                }
            }
            return slots;
        }

        int[] MelodySlots() => (int[])melody.Clone();

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void BuyRpc(RpcParams rpcParams = default)
        {
            if (!SenderNear(rpcParams.Receive.SenderClientId, merchant)) return;
            ServerBuyShip();
        }

        /// <summary>Server: buy the ship (once the melody is solved) for its share of the team's bolts.</summary>
        public bool ServerBuyShip()
        {
            if (!solved.Value || shipBought.Value || PickupSystem.Instance == null) return false;
            int price = ShipPrice;
            if (!PickupSystem.Instance.ServerSpendBolts(price)) return false;
            shipBought.Value = true;
            BoughtRpc(price);
            onShipBoughtServer.Invoke();
            return true;
        }

        [Rpc(SendTo.Everyone)]
        void BoughtRpc(int price) => Say(string.Format(boughtLine, price), 5f);

        /// <summary>Server: blow through the pipes now (as if a player pressed F at the button).</summary>
        public void ServerBlow()
        {
            var slots = ServerSlots();
            busyUntil = Time.time + pipes.Length * noteGap + 0.8f;
            PlayRpc(slots, false);
            StartCoroutine(ServerJudge(slots, pipes.Length * noteGap + 0.3f));
        }

        IEnumerator ServerJudge(int[] slots, float delay)
        {
            yield return new WaitForSeconds(delay);
            bool any = false, right = slots.Length == melody.Length;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] >= 0) any = true;
                if (i >= melody.Length || slots[i] != melody[i]) right = false;
            }
            if (right && !solved.Value) { solved.Value = true; onSolvedServer.Invoke(); }
            LineRpc(right ? 2 : any ? 1 : 0);
        }

        // ---------- Every machine ----------

        [Rpc(SendTo.Everyone)]
        void PlayRpc(int[] slots, bool hum)
        {
            busyUntil = Time.time + slots.Length * noteGap + 0.5f;
            if (hum) Say(humLine, slots.Length * noteGap + 2.5f);
            StartCoroutine(Play(slots, hum));
        }

        [Rpc(SendTo.Everyone)]
        void LineRpc(int result) => Say(result == 2 ? solvedLine : result == 1 ? wrongLine : emptyLine, 4.5f);

        void Say(string text, float seconds) { line = text; lineUntil = Time.time + seconds; }

        IEnumerator Play(int[] slots, bool hum)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                int obj = slots[i];
                Vector3 at = hum ? merchant.position + Vector3.up * 0.8f : pipes[Mathf.Min(i, pipes.Length - 1)].position;
                if (!hum) StartCoroutine(Puff(at));
                if (obj >= 0 && obj < notes.Length) AudioSource.PlayClipAtPoint(Note(notes[obj]), at, hum ? 0.6f : 0.9f);
                else AudioSource.PlayClipAtPoint(Hiss(), at, 0.5f);
                yield return new WaitForSeconds(noteGap);
            }
        }

        IEnumerator Puff(Vector3 from)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            if (puffMaterial != null) go.GetComponent<Renderer>().sharedMaterial = puffMaterial;
            var dir = outletDirection.normalized;
            for (float t = 0f; t < 0.5f; t += Time.deltaTime)
            {
                float k = t / 0.5f;
                go.transform.position = from + dir * (0.5f + k * 1.6f);
                go.transform.localScale = Vector3.one * Mathf.Lerp(0.3f, 1.1f, k) * (1f - k * 0.6f);
                yield return null;
            }
            Destroy(go);
        }

        // A plucked, organ-ish tone (fundamental + two overtones), made once per pitch.
        static AudioClip Note(float hz)
        {
            int key = Mathf.RoundToInt(hz);
            if (s_Clips.TryGetValue(key, out var clip)) return clip;
            const int rate = 44100; int n = (int)(rate * 0.7f);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Min(1f, t / 0.02f) * Mathf.Exp(-t * 4f);
                float w = 2f * Mathf.PI * hz * t;
                data[i] = env * 0.45f * (Mathf.Sin(w) + 0.35f * Mathf.Sin(2f * w) + 0.15f * Mathf.Sin(3f * w));
            }
            clip = AudioClip.Create("Note_" + key, n, 1, rate, false);
            clip.SetData(data, 0);
            s_Clips[key] = clip;
            return clip;
        }

        static AudioClip Hiss()
        {
            if (s_Hiss != null) return s_Hiss;
            const int rate = 44100; int n = (int)(rate * 0.45f);
            var data = new float[n]; var rnd = new System.Random(7); float low = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                low += 0.25f * ((float)rnd.NextDouble() * 2f - 1f - low);
                data[i] = low * 0.6f * Mathf.Min(1f, t / 0.03f) * Mathf.Exp(-t * 6f);
            }
            s_Hiss = AudioClip.Create("Hiss", n, 1, rate, false);
            s_Hiss.SetData(data, 0);
            return s_Hiss;
        }

        void OnGUI()
        {
            if (!IsSpawned || CutsceneSystem.HidesGameplayUI) return;
            var box = new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.MiddleCenter, richText = true, wordWrap = true };
            if (Time.time < lineUntil && !string.IsNullOrEmpty(line))
                GUI.Box(new Rect(Screen.width * 0.5f - 420f, Screen.height - 250f, 840f, 52f), $"<color=#ffd966><b>{merchantName}:</b></color> {line}", box);
            string hint = nearButton ? "[F] Uruchom maszynę"
                : nearMerchant && solved.Value ? $"[F] Kup statek od kupca ({ShipPrice} części)"
                : nearMerchant ? "[F] Posłuchaj melodii kupca" : null;
            if (hint != null && Time.time >= busyUntil)
                GUI.Box(new Rect(Screen.width * 0.5f - 230f, Screen.height - 190f, 460f, 44f), hint, box);
        }

        public void Setup(Transform[] pipesLeftToRight, Transform[] objects, float[] objectNotes, int[] tune, Transform startButton, Transform merchantTransform, Vector3 outlet, Material puff)
        {
            pipes = pipesLeftToRight; soundObjects = objects; notes = objectNotes; melody = tune;
            button = startButton; merchant = merchantTransform; outletDirection = outlet; puffMaterial = puff;
        }
    }
}
