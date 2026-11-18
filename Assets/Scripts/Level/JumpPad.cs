using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// A launch pad that shoots the players standing on it straight up.
    /// - Automatic (no cranks): goes off as soon as a player steps on, then recharges by itself.
    /// - Wound up (one crank): someone winds it first with the wrench beam (latch onto the crank and hold any move
    ///   key); it fires once, then needs winding again. Let go halfway and the spring slowly slips back.
    /// - Co-op (several cranks): only winds while every crank is turned at the same time; one player alone just
    ///   strains against it.
    /// Any of these can be aimed instead: with a landing target set it throws the players along a parabola onto that
    /// point (<see cref="PlayerMovement.LaunchTo"/>) rather than straight up.
    /// The server owns the charge. Movement is owner-simulated, so every client launches its own player: standing on
    /// a charged pad it goes up at once and tells the server, which spends the charge; whoever else stands on the pad
    /// when it fires goes up with them.
    /// </summary>
    public class JumpPad : NetworkBehaviour
    {
        [SerializeField] float launchHeight = 9f;
        [Tooltip("Top surface players stand on, local space (the pad's root must not be scaled).")]
        [SerializeField] Vector3 topCenter = new Vector3(0f, 0.3f, 0f);
        [SerializeField] Vector2 topSize = new Vector2(3f, 3f);
        [Tooltip("Players already standing on the pad when someone else sets it off still go up within this time.")]
        [SerializeField] float fireGrace = 0.3f;

        [Header("Aimed launch (leave empty to go straight up)")]
        [Tooltip("Where the players land: they fly a parabola onto this point instead of going straight up. Move it anywhere in the scene.")]
        [SerializeField] Transform landingTarget;
        [Tooltip("How far above the higher of the pad and the target the arc peaks.")]
        [SerializeField] float arcHeight = 4f;
        [Tooltip("Turned (around Y) to point at the landing target.")]
        [SerializeField] Transform aimArrow;

        [Header("Automatic (no cranks)")]
        [SerializeField] float rechargeTime = 1.5f;

        [Header("Crank (leave empty for an automatic pad)")]
        [SerializeField] Graspable[] cranks;
        [Tooltip("Seconds of cranking (all cranks at once) for a full charge.")]
        [SerializeField] float windTime = 2.5f;
        [Tooltip("Charge lost per second while not fully wound and not being cranked.")]
        [SerializeField] float unwindSpeed = 0.25f;
        [Tooltip("How hard (0..1) the move key has to be held to count as cranking.")]
        [SerializeField, Range(0.1f, 1f)] float crankThreshold = 0.4f;

        [Header("Visuals")]
        [SerializeField] Transform plate;
        [SerializeField] Renderer[] lamps;
        [Tooltip("Crank arms, spun around their local Z as the charge builds.")]
        [SerializeField] Transform[] crankArms;
        [Tooltip("Gauge bars, scaled on Y with the charge.")]
        [SerializeField] Transform[] gauges;
        [SerializeField] float crankTurns = 3f;
        [SerializeField] Color emptyColor = new Color(0.35f, 0.12f, 0.1f);
        [SerializeField] Color chargingColor = new Color(1f, 0.75f, 0.15f);
        [SerializeField] Color readyColor = new Color(0.25f, 1f, 0.45f);

        readonly NetworkVariable<float> charge = new();
        readonly NetworkVariable<int> fireCount = new();

        Vector3 plateRest;
        float popTime = float.NegativeInfinity;
        Material[] lampMaterials;
        Targetable[] crankTargets;

        // Local player
        PlayerHealth localPlayer;
        PlayerMovement localMovement;
        float lastFireSeen = float.NegativeInfinity;
        int launchedForFire = -1;
        float retriggerTime;

        bool IsAutomatic => cranks == null || cranks.Length == 0;
        public bool IsCharged => charge.Value >= 1f;

        void Awake()
        {
            if (plate != null) plateRest = plate.localPosition;
            lampMaterials = new Material[lamps.Length];
            for (int i = 0; i < lamps.Length; i++) lampMaterials[i] = lamps[i].material;
            crankTargets = new Targetable[IsAutomatic ? 0 : cranks.Length];
            for (int i = 0; i < crankTargets.Length; i++) crankTargets[i] = cranks[i].GetComponent<Targetable>();
        }

        public override void OnNetworkSpawn()
        {
            // An automatic pad starts ready; a crank pad starts unwound.
            if (IsServer) charge.Value = IsAutomatic ? 1f : 0f;
            fireCount.OnValueChanged += OnFired;
        }

        public override void OnNetworkDespawn()
        {
            fireCount.OnValueChanged -= OnFired;
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (IsServer) ServerTick(Time.deltaTime);
            LocalPlayerTick();
            UpdateVisuals();
        }

        // ---------- Server ----------

        void ServerTick(float dt)
        {
            float current = charge.Value;
            if (current >= 1f)
            {
                // Wound: nothing left to crank.
                ReleaseCranks();
                return;
            }

            float next = IsAutomatic ? current + dt / rechargeTime
                : AllCranking() ? current + dt / windTime
                : current - unwindSpeed * dt;
            next = Mathf.Clamp01(next);
            if (next >= 1f) ReleaseCranks(); // the crank clicks into place and lets go of the beam
            if (next != current) charge.Value = next;
        }

        bool AllCranking()
        {
            foreach (var crank in cranks)
                if (crank == null || !crank.IsHeld || crank.SteerWorld.magnitude < crankThreshold) return false;
            return true;
        }

        void ReleaseCranks()
        {
            if (IsAutomatic) return;
            foreach (var crank in cranks)
                if (crank != null) crank.ServerForceRelease();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void FireRpc()
        {
            if (charge.Value < 1f) return; // someone else already used it
            charge.Value = 0f;
            fireCount.Value++;
        }

        // ---------- Local player (every client) ----------

        void LocalPlayerTick()
        {
            var player = PlayerHealth.Local;
            if (player == null) return;
            if (player != localPlayer)
            {
                localPlayer = player;
                localMovement = player.GetComponent<PlayerMovement>();
            }
            if (localMovement == null || !localMovement.CanLaunch || !IsOnTop(player.transform.position)) return;

            if (IsCharged && Time.time >= retriggerTime)
            {
                // Charged and we're on it: go now rather than wait for the server, and spend the charge.
                LaunchLocal();
                launchedForFire = fireCount.Value + 1;
                retriggerTime = Time.time + 0.5f;
                FireRpc();
            }
            else if (Time.time - lastFireSeen <= fireGrace && launchedForFire != fireCount.Value)
            {
                // The other player set it off while we stand on it.
                LaunchLocal();
                launchedForFire = fireCount.Value;
            }
        }

        void LaunchLocal()
        {
            if (landingTarget != null) localMovement.LaunchTo(landingTarget.position, arcHeight);
            else localMovement.Launch(launchHeight);
        }

        bool IsOnTop(Vector3 feet)
        {
            const float margin = 0.3f; // the capsule hangs over the edge a little
            Vector3 local = transform.InverseTransformPoint(feet) - topCenter;
            return Mathf.Abs(local.x) <= topSize.x * 0.5f + margin && Mathf.Abs(local.z) <= topSize.y * 0.5f + margin
                && local.y > -0.3f && local.y < 0.6f;
        }

        void OnFired(int previous, int current)
        {
            lastFireSeen = Time.time;
            popTime = Time.time;
        }

        // ---------- Visuals (every client) ----------

        void PointArrow()
        {
            if (aimArrow == null || landingTarget == null) return;
            Vector3 flat = Vector3.ProjectOnPlane(landingTarget.position - aimArrow.position, Vector3.up);
            if (flat.sqrMagnitude > 0.0001f) aimArrow.rotation = Quaternion.LookRotation(flat, Vector3.up);
        }

        void UpdateVisuals()
        {
            PointArrow();
            float c = charge.Value;
            Color color = c >= 1f
                ? Color.Lerp(readyColor, Color.white, 0.25f + 0.25f * Mathf.Sin(Time.time * 6f))
                : Color.Lerp(emptyColor, chargingColor, c);
            foreach (var material in lampMaterials) material.color = color;

            if (plate != null)
            {
                // Kick up fast when it fires, then settle back.
                float age = Time.time - popTime;
                float pop = age < 0.08f ? age / 0.08f : Mathf.Clamp01(1f - (age - 0.08f) / 0.35f);
                plate.localPosition = plateRest + Vector3.up * (0.45f * pop);
            }

            int held = 0;
            if (!IsAutomatic)
                foreach (var crank in cranks)
                    if (crank != null && crank.IsHeld) held++;
            bool straining = held > 0 && held < cranks.Length && c < 1f;

            foreach (var arm in crankArms)
            {
                if (arm == null) continue;
                float jitter = straining ? Random.Range(-8f, 8f) : 0f;
                arm.localRotation = Quaternion.Euler(0f, 0f, -c * crankTurns * 360f + jitter);
            }
            foreach (var gauge in gauges)
                if (gauge != null) gauge.localScale = new Vector3(1f, Mathf.Max(0.001f, c), 1f);

            // A wound crank can't be aimed at: there's nothing left to do with it.
            foreach (var target in crankTargets)
                if (target != null) target.enabled = c < 1f;
        }

        public override void OnDestroy()
        {
            if (lampMaterials != null)
                foreach (var material in lampMaterials)
                    if (material != null) Destroy(material);
            base.OnDestroy();
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 1f, 0.5f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(topCenter + Vector3.up * 0.15f, new Vector3(topSize.x, 0.3f, topSize.y));
            if (landingTarget == null)
            {
                Gizmos.DrawLine(topCenter, topCenter + Vector3.up * launchHeight);
                return;
            }

            // The arc the players will fly, from the middle of the pad onto the target.
            Gizmos.matrix = Matrix4x4.identity;
            var arc = new JumpArc(transform.TransformPoint(topCenter), landingTarget.position, arcHeight);
            Vector3 previous = arc.Start;
            for (int i = 1; i <= 40; i++)
            {
                Vector3 next = arc.PositionAt(arc.Duration * i / 40f);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
            Gizmos.DrawWireSphere(landingTarget.position, 0.5f);
        }
    }
}
