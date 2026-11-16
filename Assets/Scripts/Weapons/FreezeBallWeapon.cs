using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Freeze ball launcher. Hold Aim to see the ball's whole path: the arc to the spot under the crosshair (in
    /// reach; otherwise the furthest spot straight ahead — no lock-on), then its bounces. Attack fires it. The ball
    /// bounces off the surroundings 3 times in nice arcs and shatters on the next touch; enemies don't stop it — it
    /// flies through them, and each one it passes takes 1 damage and is frozen solid for a second, then slowed to a
    /// crawl (80% slower) for 2 more (<see cref="EnemyController.ServerFreeze"/>).
    /// 5 balls, one comes back every few seconds (<see cref="RegeneratingAmmo"/>).
    /// Every client flies its own copy of each ball (<see cref="FreezeBallProjectile"/>, all on the same
    /// <see cref="BounceArc"/> path); the server's copy decides whom it froze.
    /// </summary>
    public class FreezeBallWeapon : PlayerWeapon
    {
        const int PreviewDots = 80;
        const int StepsPerDot = 3;

        [SerializeField] RegeneratingAmmo balls = new RegeneratingAmmo(5, 3f);
        [SerializeField] int damage = 1;
        [Tooltip("Seconds a hit enemy is frozen solid.")]
        [SerializeField] float freezeTime = 1f;
        [Tooltip("Seconds it stays slowed after thawing.")]
        [SerializeField] float slowTime = 2f;
        [Tooltip("Share of its normal speed while slowed (0.2 = 80% slower).")]
        [SerializeField, Range(0f, 1f)] float slowFactor = 0.2f;
        [Tooltip("Furthest spot the first arc comes down on, horizontal distance from the player.")]
        [SerializeField] float range = 15f;
        [Tooltip("Flight time of the first arc for the nearest / furthest shot: far shots lob higher.")]
        [SerializeField] Vector2 flightTime = new Vector2(0.45f, 0.9f);
        [SerializeField] float fireInterval = 0.5f;
        [SerializeField] BounceSettings flight = new BounceSettings
        {
            gravity = 22f, radius = 0.18f, bounces = 3, restitution = 0.7f, friction = 0.9f, minHop = 1f, maxLifetime = 6f,
        };
        [Tooltip("Where the ball leaves the launcher.")]
        [SerializeField] Transform muzzle;

        [Header("Visuals")]
        [SerializeField] Material ballMaterial;
        [SerializeField] Material previewMaterial;

        PlayerTargeting targeting;
        float nextFireTime;
        bool aimedThisFrame;
        ThrowAim aim; // owner, made on first use; only picks the spot and the launch velocity
        Transform previewRoot;
        Transform[] previewDots;
        Transform[] previewMarks;

        public override WeaponId Id => WeaponId.Freezer;
        public override string StatusText => balls.StatusText;

        Vector3 Muzzle => muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.3f;

        void Awake() => targeting = GetComponent<PlayerTargeting>();

        void Update()
        {
            if (IsSpawned && IsOwner) balls.Tick(Time.deltaTime);
        }

        public override void OnNetworkDespawn()
        {
            aim?.Destroy();
            if (previewRoot != null) Destroy(previewRoot.gameObject);
        }

        // ---------- Owner: aiming and firing ----------

        public override void TickAiming(bool firePressed, bool fireHeld)
        {
            if (balls.IsEmpty) return; // nothing to fire: no path either (the HUD says so)

            aimedThisFrame = true;
            aim ??= new ThrowAim(transform, targeting, range, flight.gravity, flightTime, flight.radius, 0.5f, previewMaterial, "FreezeAim");
            Vector3 start = Muzzle;
            Vector3 velocity = aim.LaunchVelocity(start, aim.LandingSpot(), out _);
            ShowPreview(start, velocity);

            if (firePressed && Time.time >= nextFireTime && balls.TryUse())
            {
                nextFireTime = Time.time + fireInterval;
                FireRpc(start, velocity);
            }
        }

        void LateUpdate()
        {
            if (!IsOwner) return;
            if (!aimedThisFrame && previewRoot != null && previewRoot.gameObject.activeSelf) previewRoot.gameObject.SetActive(false);
            aimedThisFrame = false;
        }

        /// <summary>Dots along the whole path the ball will fly, a mark on every bounce and a bigger one where it shatters.</summary>
        void ShowPreview(Vector3 start, Vector3 velocity)
        {
            if (previewRoot == null) BuildPreview();
            if (!previewRoot.gameObject.activeSelf) previewRoot.gameObject.SetActive(true);

            var ball = new BallFlight { Position = start, Velocity = velocity };
            int dots = 0, marks = 0, steps = 0;
            while (!ball.Ended && dots < previewDots.Length)
            {
                bool touched = BounceArc.Advance(ref ball, flight, null, out Vector3 contact);
                steps++;
                if (touched && marks < previewMarks.Length)
                {
                    var mark = previewMarks[marks++];
                    mark.position = contact;
                    mark.localScale = Vector3.one * (ball.Ended ? 0.4f : 0.22f);
                }
                // The first dots sit in the launcher; skip them so the path doesn't clutter the view.
                if (steps % StepsPerDot == 0 && steps > StepsPerDot) previewDots[dots++].position = ball.Position;
            }
            for (int i = 0; i < previewDots.Length; i++) SetVisible(previewDots[i], i < dots);
            for (int i = 0; i < previewMarks.Length; i++) SetVisible(previewMarks[i], i < marks);
        }

        static void SetVisible(Transform piece, bool visible)
        {
            if (piece.gameObject.activeSelf != visible) piece.gameObject.SetActive(visible);
        }

        void BuildPreview()
        {
            previewRoot = new GameObject("FreezeBallPreview").transform;
            previewDots = new Transform[PreviewDots];
            for (int i = 0; i < previewDots.Length; i++) previewDots[i] = PreviewPiece(Vector3.one * 0.08f);
            previewMarks = new Transform[flight.bounces + 1];
            for (int i = 0; i < previewMarks.Length; i++) previewMarks[i] = PreviewPiece(Vector3.one * 0.22f);
            previewRoot.gameObject.SetActive(false);
        }

        Transform PreviewPiece(Vector3 scale)
        {
            var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(piece.GetComponent<Collider>());
            var renderer = piece.GetComponent<Renderer>();
            if (previewMaterial != null) renderer.sharedMaterial = previewMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            piece.transform.SetParent(previewRoot, false);
            piece.transform.localScale = scale;
            piece.transform.localRotation = Quaternion.Euler(45f, 45f, 0f); // diamonds: read the same from any side
            return piece.transform;
        }

        // ---------- Flight (every client) and freezing (server) ----------

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
        void FireRpc(Vector3 start, Vector3 velocity)
        {
            // The server only lets balls from the player's own hand through (slack for latency).
            if (IsServer && !IsOwner && ((start - transform.position).sqrMagnitude > 9f || velocity.sqrMagnitude > 60f * 60f)) return;
            FreezeBallProjectile.Spawn(this, start, velocity, flight, ballMaterial);
        }

        /// <summary>Server: this weapon's ball flew through <paramref name="enemy"/> (once per ball per enemy).</summary>
        public void OnBallPassedEnemy(EnemyController enemy)
        {
            if (!IsServer || enemy == null || !enemy.IsAlive) return;
            // Freeze first, so the hit doesn't knock the block of ice about.
            enemy.ServerFreeze(freezeTime, slowTime, slowFactor);
            enemy.Damageable.ApplyDamage(damage, Vector3.zero);
        }
    }
}
