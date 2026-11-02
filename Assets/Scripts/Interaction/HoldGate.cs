using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// A heavy gate held up by a lever: it rises while the lever (a <see cref="BeamSwitch"/> lever, pulled all the
    /// way with the wrench beam) is held and stays up as long as someone keeps holding it. Let go and the gate drops
    /// under gravity and slams shut with a small bounce — one player holds, the other runs through.
    /// Anyone still under it when it comes down is knocked out of the doorway to their side (and hurt a little), so
    /// nobody gets stuck inside the gate.
    /// The server moves it; a server-authoritative NetworkTransform replicates it. The slam effect plays everywhere.
    /// </summary>
    public class HoldGate : NetworkBehaviour
    {
        [SerializeField] BeamSwitch[] levers;
        [Tooltip("How high the gate lifts, metres.")]
        [SerializeField] float openHeight = 3.6f;
        [SerializeField] float riseSpeed = 2.5f;
        [Tooltip("Downward acceleration once let go.")]
        [SerializeField] float fallGravity = 30f;
        [Tooltip("Speed kept (upwards) when it hits the ground; bounces slower than 2 m/s just stop.")]
        [SerializeField, Range(0f, 0.6f)] float bounce = 0.2f;
        [Tooltip("Gate size: X across the doorway, Y height, Z thickness (local).")]
        [SerializeField] Vector3 size = new Vector3(3f, 3.5f, 0.5f);

        [Header("Under the gate")]
        [SerializeField] int crushDamage = 1;
        [SerializeField] float crushPush = 7f;
        [Tooltip("How far above a player's head the falling gate already throws them out.")]
        [SerializeField] float crushWarning = 1f;

        [Header("Visuals")]
        [SerializeField] Renderer lamp;
        [SerializeField] Color closedColor = new Color(1f, 0.2f, 0.1f);
        [SerializeField] Color openColor = new Color(0.3f, 1f, 0.4f);
        [SerializeField] Material slamSparkMaterial;
        [SerializeField] float slamShake = 0.35f;

        readonly NetworkVariable<bool> held = new();
        readonly NetworkVariable<bool> sealedShut = new();

        readonly System.Collections.Generic.HashSet<PlayerHealth> pushedThisFall = new();
        Vector3 closedPosition;
        float lift, velocity;
        Material lampMaterial;

        public bool IsHeldOpen => held.Value;
        /// <summary>Shut for good (<see cref="ServerSeal"/>): the levers don't open it any more.</summary>
        public bool IsSealed => sealedShut.Value;
        /// <summary>0 shut .. 1 fully open (server).</summary>
        public float Openness => lift / openHeight;
        /// <summary>Where the gate stands when shut (its doorway), on every machine.</summary>
        public Vector3 ClosedPosition => closedPosition;
        /// <summary>Gate size: X across the doorway, Y height, Z thickness (local).</summary>
        public Vector3 Size => size;

        /// <summary>
        /// Server: shut the gate for good. It slams down at once (starting at <paramref name="slamSpeed"/>, m/s) and no
        /// lever lifts it again; anyone under it is thrown out to their side as usual.
        /// </summary>
        public void ServerSeal(float slamSpeed)
        {
            if (!IsServer || sealedShut.Value) return;
            sealedShut.Value = true;
            held.Value = false;
            if (lift > 0f) velocity = Mathf.Min(velocity, -slamSpeed);
        }

        void Awake()
        {
            closedPosition = transform.position;
            if (lamp != null) lampMaterial = lamp.material;
        }

        void Update()
        {
            if (IsServer && IsSpawned) ServerTick(Time.deltaTime);
            if (lampMaterial != null) lampMaterial.color = held.Value ? openColor : closedColor;
        }

        void ServerTick(float dt)
        {
            bool on = !sealedShut.Value && levers != null && levers.Length > 0;
            if (on)
                foreach (var lever in levers)
                    if (lever == null || !lever.IsOn) on = false;
            if (held.Value != on) held.Value = on;

            float before = lift;
            if (on)
            {
                velocity = 0f;
                lift = Mathf.MoveTowards(lift, openHeight, riseSpeed * dt);
                pushedThisFall.Clear();
            }
            else if (lift > 0f || velocity > 0f)
            {
                velocity -= fallGravity * dt;
                lift += velocity * dt;
                if (lift <= 0f)
                {
                    float impact = -velocity;
                    lift = 0f;
                    velocity = impact * bounce > 2f ? impact * bounce : 0f;
                    if (impact > 3f) SlamRpc(Mathf.Clamp01(impact / 15f));
                }
            }
            if (lift < before) PushOutFromUnder();
            if (!Mathf.Approximately(lift, before)) transform.position = closedPosition + Vector3.up * lift;
        }

        /// <summary>Players standing in the doorway while the gate comes down get thrown out to their side.</summary>
        void PushOutFromUnder()
        {
            float bottom = transform.position.y - size.y * 0.5f;
            foreach (var player in PlayerHealth.All)
            {
                if (!player.IsAlive) continue;
                Vector3 feet = player.transform.position;
                Vector3 local = Quaternion.Inverse(transform.rotation) * (feet - closedPosition);
                const float radius = 0.4f, height = 1.9f;
                if (Mathf.Abs(local.x) > size.x * 0.5f || Mathf.Abs(local.z) > size.z * 0.5f + radius) continue;
                // Push while the gate is still a little above their head: once it's inside the capsule they'd stick.
                if (bottom > feet.y + height + crushWarning || bottom < feet.y - 0.2f) continue;
                if (!pushedThisFall.Add(player)) continue;

                Vector3 side = transform.forward * (local.z >= 0f ? 1f : -1f);
                Vector3 push = side * crushPush + Vector3.up * 4f;
                player.ApplyDamage(new DamageInfo(crushDamage, side, gameObject));
                player.Shove(push, 0.4f);
            }
        }

        [Rpc(SendTo.Everyone)]
        void SlamRpc(float strength)
        {
            Vector3 bottom = closedPosition + Vector3.down * (size.y * 0.5f);
            if (slamSparkMaterial != null)
                for (int i = -1; i <= 1; i++)
                    ShotEffects.Sparks(bottom + transform.right * (size.x * 0.45f * i), slamSparkMaterial, 6);
            var local = PlayerHealth.Local;
            if (local != null && ThirdPersonCameraRig.Instance != null)
            {
                float distance = Vector3.Distance(local.transform.position, bottom);
                if (distance < 15f) ThirdPersonCameraRig.Instance.AddShake(slamShake * strength * (1f - distance / 15f), 0.3f);
            }
        }

        public override void OnDestroy()
        {
            if (lampMaterial != null) Destroy(lampMaterial);
            base.OnDestroy();
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Vector3 start = Application.isPlaying ? closedPosition : transform.position;
            Gizmos.matrix = Matrix4x4.TRS(start + Vector3.up * openHeight, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, size);
        }
    }
}
