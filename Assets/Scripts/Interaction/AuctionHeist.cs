using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace CoopGame
{
    /// <summary>
    /// The weapon auction. Disguises from two stalls (F): auction guest and technical staff, one player each.
    /// Guard checkpoints only let the right disguise through (the guards' line has an invisible wall that only an
    /// allowed player walks through). The guest sits down (F at a chair) and bids (F) to keep the auction from
    /// ending; only while the guest sits does the backstage guard let the staff in. Backstage the staff sets off
    /// the alarm (F) so the guards look away, then steals the weapon (F).
    /// </summary>
    public class AuctionHeist : NetworkBehaviour
    {
        public enum Disguise : byte { None, Guest, Staff }
        public enum Rule : byte { GuestOnly, StaffWhileGuestSeated, StaffOnly }

        [Serializable]
        public class Checkpoint
        {
            public string name;
            public Rule rule;
            [Tooltip("The guards and the wall between them.")]
            public Collider[] colliders;
            public Transform center;
        }

        [Header("Disguises")]
        [SerializeField] Collider guestShop;
        [SerializeField] Collider staffShop;
        [SerializeField] float shopReach = 3f;

        [Header("Guards")]
        [SerializeField] Checkpoint[] checkpoints;
        [Tooltip("Turn away while the alarm is on.")]
        [SerializeField] Transform[] guards;

        [Header("Auction")]
        [SerializeField] Transform[] chairs;
        [SerializeField] float chairReach = 1.8f;
        [SerializeField] float auctionTime = 30f;
        [SerializeField] float bidBonus = 8f;
        [SerializeField] float maxRemaining = 25f;
        [SerializeField] int startPrice = 1000;

        [Header("Backstage")]
        [SerializeField] Transform alarmButton;
        [SerializeField] Transform weapon;
        [SerializeField] float reach = 2.4f;

        public UnityEvent onStolenServer = new();

        const ulong NoClient = ulong.MaxValue;
        readonly NetworkVariable<ulong> guestClient = new(NoClient);
        readonly NetworkVariable<ulong> staffClient = new(NoClient);
        readonly NetworkVariable<bool> guestSeated = new();
        readonly NetworkVariable<bool> auctionRunning = new();
        readonly NetworkVariable<double> soldAt = new();
        readonly NetworkVariable<int> price = new();
        readonly NetworkVariable<bool> alarm = new();
        readonly NetworkVariable<bool> stolen = new();

        InputAction interactAction, jumpAction;
        float nextPress, messageUntil, nextRivalBid;
        string message;
        bool inputBlocked;
        CharacterController ignoringFor;
        bool[] ignoring;
        Quaternion[] guardRest;
        GameObject guestHat, staffHat;

        double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : Time.timeAsDouble;
        public bool GuestSeated => guestSeated.Value;
        public bool AlarmOn => alarm.Value;
        public bool Stolen => stolen.Value;
        public bool AuctionRunning => auctionRunning.Value;
        public int Price => price.Value;
        public float TimeLeft => auctionRunning.Value ? Mathf.Max(0f, (float)(soldAt.Value - Now)) : auctionTime;
        public Disguise DisguiseOf(ulong client) => client == guestClient.Value ? Disguise.Guest : client == staffClient.Value ? Disguise.Staff : Disguise.None;

        void Awake()
        {
            guardRest = new Quaternion[guards != null ? guards.Length : 0];
            for (int i = 0; i < guardRest.Length; i++) if (guards[i] != null) guardRest[i] = guards[i].rotation;
        }

        public override void OnNetworkSpawn()
        {
            interactAction = InputSystem.actions.FindAction("Player/Interact", true);
            jumpAction = InputSystem.actions.FindAction("Player/Jump", false);
            if (IsServer) price.Value = startPrice;
            stolen.OnValueChanged += (_, _) => ApplyStolen();
            ApplyStolen();
        }

        public override void OnNetworkDespawn() => SetInputBlock(false);

        void ApplyStolen() { if (weapon != null) weapon.gameObject.SetActive(!stolen.Value); }

        void Update()
        {
            if (!IsSpawned) return;
            if (IsServer) ServerTick();
            UpdateGuards();
            UpdateHats();
            LocalTick();
        }

        // ---------- Server ----------

        void ServerTick()
        {
            if (!auctionRunning.Value || stolen.Value) return;
            if (Time.time >= nextRivalBid) { nextRivalBid = Time.time + UnityEngine.Random.Range(4f, 7f); price.Value += 150 + 50 * UnityEngine.Random.Range(0, 4); }
            if (Now >= soldAt.Value)
            {
                SayRpc("Broń sprzedana innemu kupcowi! Aukcja zaczyna się od nowa, spróbujcie jeszcze raz.");
                price.Value = startPrice;
                soldAt.Value = Now + auctionTime;
            }
        }

        bool Near(ulong client, Vector3 point, float distance)
        {
            foreach (var p in PlayerHealth.All)
                if (p.OwnerClientId == client && p.IsAlive && Vector3.Distance(p.transform.position + Vector3.up, point) <= distance + 1.5f) return true;
            return false;
        }

        bool NearShop(ulong client, Collider shop)
        {
            foreach (var p in PlayerHealth.All)
                if (p.OwnerClientId == client && p.IsAlive && Vector3.Distance(shop.ClosestPoint(p.transform.position + Vector3.up), p.transform.position + Vector3.up) <= shopReach + 1.5f) return true;
            return false;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void BuyRpc(byte kind, RpcParams rpcParams = default)
        {
            ulong c = rpcParams.Receive.SenderClientId;
            if (NearShop(c, kind == (byte)Disguise.Guest ? guestShop : staffShop)) ServerBuy(c, (Disguise)kind);
        }

        /// <summary>Server: <paramref name="client"/> buys a disguise. Once bought it's theirs for good: no second one, and nobody else gets the same one.</summary>
        public bool ServerBuy(ulong client, Disguise kind)
        {
            var to = RpcTarget.Single(client, RpcTargetUse.Temp);
            if (DisguiseOf(client) != Disguise.None) { SayToRpc("Masz już przebranie. Innego nie kupisz.", to); return false; }
            if ((kind == Disguise.Guest ? guestClient.Value : staffClient.Value) != NoClient) { SayToRpc("To przebranie ktoś już kupił.", to); return false; }
            if (kind == Disguise.Guest) guestClient.Value = client;
            else if (kind == Disguise.Staff) staffClient.Value = client;
            else return false;
            SayToRpc(kind == Disguise.Guest ? "Masz przebranie gościa aukcji. Strażnik przy wejściu na aukcję cię wpuści." : "Masz strój technicznego personelu. Wpuszczą cię na zaplecze.", to);
            return true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void SitRpc(int chair, RpcParams rpcParams = default)
        {
            ulong c = rpcParams.Receive.SenderClientId;
            if (chair >= 0 && chair < chairs.Length && Near(c, chairs[chair].position, chairReach)) ServerSit(c, chair);
        }

        /// <summary>Server: the guest sits down on chair <paramref name="chair"/> (the auction starts the first time).</summary>
        public bool ServerSit(ulong client, int chair)
        {
            if (client != guestClient.Value || guestSeated.Value || stolen.Value) return false;
            guestSeated.Value = true;
            if (!auctionRunning.Value) { auctionRunning.Value = true; soldAt.Value = Now + auctionTime; price.Value = startPrice; nextRivalBid = Time.time + 5f; }
            SeatRpc(chair, RpcTarget.Single(client, RpcTargetUse.Temp));
            SayRpc("Gość aukcji usiadł. Licytacja ruszyła, a personel techniczny może wejść na zaplecze.");
            return true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void StandRpc(RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == guestClient.Value) guestSeated.Value = false;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void BidRpc(RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == guestClient.Value && guestSeated.Value) ServerBid();
        }

        /// <summary>Server: the guest bids: more time before the hammer falls.</summary>
        public void ServerBid()
        {
            if (!auctionRunning.Value || stolen.Value) return;
            soldAt.Value = Math.Min(Now + maxRemaining, Math.Max(soldAt.Value, Now) + bidBonus);
            price.Value += 250;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void AlarmRpc(RpcParams rpcParams = default)
        {
            if (Near(rpcParams.Receive.SenderClientId, alarmButton.position, reach)) ServerAlarm();
        }

        public void ServerAlarm()
        {
            if (alarm.Value || stolen.Value) return;
            alarm.Value = true;
            SayRpc("ALARM! Strażnicy odwrócili się od broni!");
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void StealRpc(RpcParams rpcParams = default)
        {
            if (Near(rpcParams.Receive.SenderClientId, weapon.position, reach)) ServerSteal();
        }

        /// <summary>Server: take the weapon (only while the alarm keeps the guards looking away).</summary>
        public bool ServerSteal()
        {
            if (!alarm.Value || stolen.Value) return false;
            stolen.Value = true; alarm.Value = false; auctionRunning.Value = false;
            SayRpc("Broń skradziona! Uciekajcie stąd, zanim ktoś się zorientuje!");
            onStolenServer.Invoke();
            return true;
        }

        [Rpc(SendTo.Everyone)]
        void SayRpc(string text) => Say(text);

        [Rpc(SendTo.SpecifiedInParams)]
        void SayToRpc(string text, RpcParams rpcParams) => Say(text);

        // Owner: sit on the chair (feet on its seat, facing the stage).
        [Rpc(SendTo.SpecifiedInParams)]
        void SeatRpc(int chair, RpcParams rpcParams)
        {
            var player = PlayerHealth.Local; if (player == null) return;
            var movement = player.GetComponent<PlayerMovement>();
            var c = chairs[chair]; var b = c.GetComponent<Collider>() != null ? c.GetComponent<Collider>().bounds : new Bounds(c.position, Vector3.one * 0.5f);
            Vector3 face = weapon != null ? weapon.position - c.position : Vector3.forward; face.y = 0f;
            movement.TeleportTo(new Pose(new Vector3(b.center.x, b.max.y, b.center.z), Quaternion.LookRotation(face.sqrMagnitude > 0.01f ? face : Vector3.forward)));
        }

        void Say(string text) { message = text; messageUntil = Time.time + 4.5f; }

        // ---------- Every machine ----------

        void UpdateGuards()
        {
            for (int i = 0; i < guardRest.Length; i++)
            {
                if (guards[i] == null) continue;
                var want = alarm.Value ? guardRest[i] * Quaternion.Euler(0f, 180f, 0f) : guardRest[i];
                guards[i].rotation = Quaternion.RotateTowards(guards[i].rotation, want, 360f * Time.deltaTime);
            }
        }

        void UpdateHats()
        {
            Hat(ref guestHat, guestClient.Value, new Color(0.08f, 0.08f, 0.1f), new Vector3(0.55f, 0.45f, 0.55f), "GuestHat");
            Hat(ref staffHat, staffClient.Value, new Color(1f, 0.55f, 0.1f), new Vector3(0.6f, 0.2f, 0.6f), "StaffCap");
        }

        void Hat(ref GameObject hat, ulong client, Color color, Vector3 size, string name)
        {
            PlayerHealth owner = null;
            if (client != NoClient) foreach (var p in PlayerHealth.All) if (p != null && p.OwnerClientId == client) owner = p;
            if (owner == null) { if (hat != null) hat.SetActive(false); return; }
            if (hat == null)
            {
                hat = GameObject.CreatePrimitive(PrimitiveType.Cube); hat.name = name; Destroy(hat.GetComponent<Collider>());
                var r = hat.GetComponent<Renderer>(); r.material.color = color;
            }
            if (hat.transform.parent != owner.transform) { hat.transform.SetParent(owner.transform, false); hat.transform.localPosition = new Vector3(0f, 2.35f, 0f); hat.transform.localScale = size; }
            hat.SetActive(true);
        }

        bool Allowed(Rule rule, ulong client)
        {
            var d = DisguiseOf(client);
            return rule switch
            {
                Rule.GuestOnly => d == Disguise.Guest,
                Rule.StaffOnly => d == Disguise.Staff,
                _ => d == Disguise.Staff && guestSeated.Value,
            };
        }

        string Denied(Rule rule) => rule switch
        {
            Rule.GuestOnly => "Strażnik: Wstęp tylko dla gości aukcji.",
            Rule.StaffOnly => "Strażnik: Dalej wolno tylko personelowi technicznemu.",
            _ => DisguiseOf(NetworkManager.LocalClientId) == Disguise.Staff ? "Strażnik: Zaczekaj, aukcja jeszcze się nie zaczęła." : "Strażnik: Zaplecze tylko dla personelu technicznego.",
        };

        void SetInputBlock(bool blocked)
        {
            if (inputBlocked == blocked) return;
            inputBlocked = blocked;
            var player = PlayerHealth.Local;
            if (player != null && player.TryGetComponent(out PlayerMovement movement)) movement.SetInputBlock(this, blocked);
        }

        void LocalTick()
        {
            var player = PlayerHealth.Local;
            ulong me = NetworkManager.LocalClientId;
            bool seated = guestSeated.Value && guestClient.Value == me;
            SetInputBlock(seated);
            if (player == null) return;

            // The guards' lines: walk through only where this player is allowed.
            var cc = player.GetComponent<CharacterController>();
            if (cc != null && checkpoints != null)
            {
                if (ignoringFor != cc || ignoring == null) { ignoringFor = cc; ignoring = new bool[checkpoints.Length]; for (int i = 0; i < ignoring.Length; i++) SetIgnore(i, false, true); }
                for (int i = 0; i < checkpoints.Length; i++) { bool ok = Allowed(checkpoints[i].rule, me); if (ok != ignoring[i]) SetIgnore(i, ok, false); }
            }

            if (!player.IsAlive || CutsceneSystem.ControlsLocked) return;
            Vector3 chest = player.transform.position + Vector3.up;
            if (seated)
            {
                if (jumpAction != null && jumpAction.WasPressedThisFrame()) StandRpc();
                else if (Pressed()) BidRpc();
                return;
            }
            if (!Pressed()) return;
            if (guestShop != null && Vector3.Distance(guestShop.ClosestPoint(chest), chest) <= shopReach) { BuyRpc((byte)Disguise.Guest); return; }
            if (staffShop != null && Vector3.Distance(staffShop.ClosestPoint(chest), chest) <= shopReach) { BuyRpc((byte)Disguise.Staff); return; }
            if (weapon != null && !stolen.Value && Vector3.Distance(chest, weapon.position) <= reach) { if (alarm.Value) StealRpc(); else Say("Strażnicy patrzą prosto na broń. Najpierw włącz alarm!"); return; }
            if (alarmButton != null && !alarm.Value && !stolen.Value && Vector3.Distance(chest, alarmButton.position) <= reach) { AlarmRpc(); return; }
            int chair = NearestChair(chest);
            if (chair >= 0 && guestClient.Value == me) SitRpc(chair);
            else if (chair >= 0) Say("Na aukcji siadają tylko goście w odpowiednim stroju.");
        }

        bool Pressed()
        {
            if (interactAction == null || !interactAction.WasPressedThisFrame() || Time.time < nextPress) return false;
            nextPress = Time.time + 0.25f;
            return true;
        }

        int NearestChair(Vector3 chest)
        {
            int best = -1; float bestDist = chairReach;
            for (int i = 0; i < chairs.Length; i++) { if (chairs[i] == null) continue; float d = Vector3.Distance(chest, chairs[i].position + Vector3.up * 0.6f); if (d < bestDist) { bestDist = d; best = i; } }
            return best;
        }

        void SetIgnore(int i, bool ignore, bool force)
        {
            if (!force && ignoring[i] == ignore) return;
            ignoring[i] = ignore;
            foreach (var c in checkpoints[i].colliders) if (c != null && ignoringFor != null) Physics.IgnoreCollision(c, ignoringFor, ignore);
        }

        void OnGUI()
        {
            if (!IsSpawned || CutsceneSystem.HidesGameplayUI) return;
            var box = new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.MiddleCenter, richText = true, wordWrap = true };
            if (Time.time < messageUntil && !string.IsNullOrEmpty(message))
                GUI.Box(new Rect(Screen.width * 0.5f - 420f, Screen.height - 250f, 840f, 52f), message, box);
            var player = PlayerHealth.Local; if (player == null) return;
            ulong me = NetworkManager.LocalClientId;
            Vector3 chest = player.transform.position + Vector3.up;
            bool seated = guestSeated.Value && guestClient.Value == me;
            int left = Mathf.CeilToInt(TimeLeft);
            if (auctionRunning.Value && !stolen.Value)
            {
                string clock = left <= 8 ? $"<color=#ff5544>0:{left:00}</color>" : $"0:{left:00}";
                GUI.Box(new Rect(Screen.width * 0.5f - 300f, 150f, 600f, 44f), $"AUKCJA BRONI   cena {price.Value} części   młotek za {clock}" + (alarm.Value ? "   <color=#ff5544>ALARM</color>" : ""), box);
            }
            string hint = null;
            if (seated) hint = "[F] Licytuj (więcej czasu)   [Spacja] Wstań";
            else if (guestShop != null && Vector3.Distance(guestShop.ClosestPoint(chest), chest) <= shopReach) hint = ShopHint(me, Disguise.Guest, guestClient.Value, "gość aukcji");
            else if (staffShop != null && Vector3.Distance(staffShop.ClosestPoint(chest), chest) <= shopReach) hint = ShopHint(me, Disguise.Staff, staffClient.Value, "techniczny personel");
            else if (weapon != null && !stolen.Value && Vector3.Distance(chest, weapon.position) <= reach) hint = alarm.Value ? "[F] Ukradnij broń" : "Strażnicy patrzą na broń: najpierw alarm";
            else if (alarmButton != null && !alarm.Value && !stolen.Value && Vector3.Distance(chest, alarmButton.position) <= reach) hint = "[F] Włącz alarm";
            else if (NearestChair(chest) >= 0 && guestClient.Value == me && !guestSeated.Value) hint = "[F] Usiądź na aukcji";
            else
                foreach (var cp in checkpoints)
                    if (cp.center != null && Vector3.Distance(chest, cp.center.position) <= 3.5f && !Allowed(cp.rule, me)) { hint = Denied(cp.rule); break; }
            if (hint != null) GUI.Box(new Rect(Screen.width * 0.5f - 300f, Screen.height - 190f, 600f, 44f), hint, box);
        }

        string ShopHint(ulong me, Disguise kind, ulong owner, string label) =>
            DisguiseOf(me) == kind ? "Masz już to przebranie"
            : DisguiseOf(me) != Disguise.None ? "Masz już przebranie, innego nie kupisz"
            : owner != NoClient ? "To przebranie ktoś już kupił"
            : "[F] Kup przebranie: " + label;

        public void Setup(Collider guestStall, Collider staffStall, Checkpoint[] guardLines, Transform[] allGuards, Transform[] auctionChairs, Transform alarmPoint, Transform weaponProp)
        {
            guestShop = guestStall; staffShop = staffStall; checkpoints = guardLines; guards = allGuards; chairs = auctionChairs; alarmButton = alarmPoint; weapon = weaponProp;
        }
    }
}
