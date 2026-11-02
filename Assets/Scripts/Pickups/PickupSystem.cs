using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoopGame
{
    public enum PickupKind : byte
    {
        /// <summary>Goes to the shared team wallet.</summary>
        Bolt,
        /// <summary>Restores 1 HP of whoever absorbs it.</summary>
        Health,
    }

    /// <summary>
    /// Every loose pickup in the level (bolts, health) plus the shared team bolt wallet.
    /// The server owns the list of uncollected pickups and is the only one that can claim them, so each
    /// pickup is used exactly once no matter how many players touch it. Clients only render pickups
    /// (<see cref="PickupVisual"/>) and play the fly-to-player animation the server tells them about.
    /// Lives on the game session object (not in a level), so the team wallet carries over from level to level;
    /// pickups still lying around when a level is left go with it.
    /// </summary>
    public class PickupSystem : NetworkBehaviour
    {
        public static PickupSystem Instance { get; private set; }

        [Header("Drop")]
        [SerializeField] float scatterMinRadius = 0.6f;
        [SerializeField] float scatterMaxRadius = 2.2f;
        [SerializeField] LayerMask groundMask = ~0;

        [Header("Pickup")]
        [Tooltip("Players closer than this pull pickups in.")]
        [SerializeField] float magnetRadius = 3.5f;
        [Tooltip("Pickups can't be collected right after they burst out.")]
        [SerializeField] float pickupDelay = 0.35f;
        [Tooltip("Height on the player the pickups are measured from and fly to.")]
        [SerializeField] float playerCenterHeight = 1f;

        [Header("Visual")]
        [SerializeField] Material boltMaterial;
        [SerializeField] Material healthMaterial;

        readonly NetworkVariable<int> teamBolts = new();

        // Server only: pickups that are still lying around.
        readonly Dictionary<int, ServerPickup> serverPickups = new();
        // Every instance: what is rendered.
        readonly Dictionary<int, PickupVisual> visuals = new();
        readonly List<int> claimBuffer = new();
        readonly List<int> idBuffer = new();
        readonly List<byte> kindBuffer = new();
        readonly List<Vector3> positionBuffer = new();
        int nextPickupId;

        struct ServerPickup
        {
            public PickupKind Kind;
            public Vector3 Origin;
            public Vector3 Landing;
            public float SpawnTime;
        }

        public int TeamBolts => teamBolts.Value;
        public event System.Action<int> TeamBoltsChanged;

        void Awake() => Instance = this;

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            teamBolts.OnValueChanged += OnTeamBoltsChanged;
            TeamBoltsChanged?.Invoke(teamBolts.Value);
            SceneManager.activeSceneChanged += OnLevelChanged;

            // A client that joins mid-game asks for the pickups already lying around.
            if (!IsServer) RequestSyncRpc();
        }

        public override void OnNetworkDespawn()
        {
            teamBolts.OnValueChanged -= OnTeamBoltsChanged;
            SceneManager.activeSceneChanged -= OnLevelChanged;
            foreach (var visual in visuals.Values)
                if (visual != null) Destroy(visual.gameObject);
            visuals.Clear();
            serverPickups.Clear();
        }

        void OnTeamBoltsChanged(int previous, int current) => TeamBoltsChanged?.Invoke(current);

        /// <summary>Server: pay <paramref name="amount"/> bolts from the team's wallet. False (nothing paid) if there aren't enough.</summary>
        public bool ServerSpendBolts(int amount)
        {
            if (!IsServer || amount < 0 || teamBolts.Value < amount) return false;
            teamBolts.Value -= amount;
            return true;
        }

        // A new level (the wallet lives on): whatever was still lying around the old one is gone with it.
        void OnLevelChanged(Scene previous, Scene next)
        {
            foreach (var visual in visuals.Values)
                if (visual != null) Destroy(visual.gameObject);
            visuals.Clear();
            serverPickups.Clear();
        }

        // ---------- Server ----------

        /// <summary>Server only: burst <paramref name="count"/> pickups of one kind out of <paramref name="origin"/>.</summary>
        public void Drop(PickupKind kind, Vector3 origin, int count)
        {
            if (!IsServer || count <= 0) return;

            int firstId = nextPickupId;
            var landings = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                landings[i] = PickLandingSpot(origin);
                serverPickups.Add(nextPickupId++, new ServerPickup { Kind = kind, Origin = origin, Landing = landings[i], SpawnTime = Time.time });
            }

            SpawnDropRpc((byte)kind, firstId, origin, landings);
        }

        Vector3 PickLandingSpot(Vector3 origin)
        {
            Vector2 offset = Random.insideUnitCircle.normalized * Random.Range(scatterMinRadius, scatterMaxRadius);
            Vector3 top = new Vector3(origin.x + offset.x, origin.y + 1.5f, origin.z + offset.y);

            // Land on level geometry, not on players or things that can break.
            var hits = Physics.RaycastAll(top, Vector3.down, 20f, groundMask, QueryTriggerInteraction.Ignore);
            float bestDistance = float.MaxValue;
            Vector3 landing = new Vector3(top.x, origin.y - 0.5f, top.z);
            foreach (var hit in hits)
            {
                if (hit.collider is CharacterController || hit.collider.GetComponentInParent<Damageable>() != null) continue;
                if (hit.distance < bestDistance)
                {
                    bestDistance = hit.distance;
                    landing = hit.point;
                }
            }
            return landing;
        }

        void Update()
        {
            if (!IsServer || serverPickups.Count == 0) return;

            foreach (var client in NetworkManager.ConnectedClientsList)
            {
                var player = client.PlayerObject;
                if (player == null || !player.TryGetComponent(out PlayerHealth playerHealth) || !playerHealth.IsAlive) continue;
                Vector3 center = player.transform.position + Vector3.up * playerCenterHeight;

                // Health only flies to players who are missing HP, and never more than they can use,
                // so a teammate in need can still get the rest.
                int healthRoom = playerHealth.Max - playerHealth.Current;
                int bolts = 0, heal = 0;

                claimBuffer.Clear();
                foreach (var pair in serverPickups)
                {
                    var pickup = pair.Value;
                    float age = Time.time - pickup.SpawnTime;
                    if (age < pickupDelay) continue;
                    if (pickup.Kind == PickupKind.Health && heal >= healthRoom) continue;

                    Vector3 position = PickupVisual.EvaluateArc(pickup.Origin, pickup.Landing, age);
                    if ((position - center).sqrMagnitude > magnetRadius * magnetRadius) continue;

                    claimBuffer.Add(pair.Key);
                    if (pickup.Kind == PickupKind.Bolt) bolts++;
                    else heal++;
                }

                if (claimBuffer.Count == 0) continue;

                // Removing on claim is what makes double counting impossible.
                foreach (int id in claimBuffer) serverPickups.Remove(id);
                if (bolts > 0) teamBolts.Value += bolts;
                if (heal > 0) playerHealth.Heal(heal);
                CollectRpc(claimBuffer.ToArray(), player.NetworkObjectId);
            }
        }

        [Rpc(SendTo.Server)]
        void RequestSyncRpc(RpcParams rpcParams = default)
        {
            idBuffer.Clear();
            kindBuffer.Clear();
            positionBuffer.Clear();
            foreach (var pair in serverPickups)
            {
                idBuffer.Add(pair.Key);
                kindBuffer.Add((byte)pair.Value.Kind);
                positionBuffer.Add(pair.Value.Landing);
            }

            SyncRpc(idBuffer.ToArray(), kindBuffer.ToArray(), positionBuffer.ToArray(),
                RpcTarget.Single(rpcParams.Receive.SenderClientId, RpcTargetUse.Temp));
        }

        // ---------- All instances ----------

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void SpawnDropRpc(byte kind, int firstId, Vector3 origin, Vector3[] landings)
        {
            for (int i = 0; i < landings.Length; i++)
                CreateVisual(firstId + i, (PickupKind)kind, origin, landings[i], alreadyLanded: false);
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void SyncRpc(int[] ids, byte[] kinds, Vector3[] landings, RpcParams rpcParams)
        {
            for (int i = 0; i < ids.Length; i++)
                if (!visuals.ContainsKey(ids[i]))
                    CreateVisual(ids[i], (PickupKind)kinds[i], landings[i], landings[i], alreadyLanded: true);
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void CollectRpc(int[] ids, ulong collectorObjectId)
        {
            NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(collectorObjectId, out var collector);
            foreach (int id in ids)
            {
                if (!visuals.Remove(id, out var visual) || visual == null) continue;
                if (collector != null) visual.FlyTo(collector.transform, playerCenterHeight);
                else Destroy(visual.gameObject);
            }
        }

        void CreateVisual(int id, PickupKind kind, Vector3 origin, Vector3 landing, bool alreadyLanded)
        {
            visuals[id] = PickupVisual.Create(kind, kind == PickupKind.Health ? healthMaterial : boltMaterial, origin, landing, alreadyLanded);
        }
    }
}
