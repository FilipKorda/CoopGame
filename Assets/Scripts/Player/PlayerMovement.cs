using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoopGame
{
    /// <summary>
    /// Ratchet &amp; Clank style third-person movement: camera-relative running, snappy turning,
    /// jump + double jump, a directional dodge (direction + Dodge), ledge grabbing and riding ziplines.
    /// Only the owning client simulates; the owner-authoritative NetworkTransform replicates the result.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(NetworkTransform))]
    public partial class PlayerMovement : NetworkBehaviour
    {
        [Header("Run")]
        [SerializeField] float moveSpeed = 8f;
        [SerializeField] float crouchSpeed = 3f;
        [SerializeField] float groundAcceleration = 60f;
        [SerializeField] float groundDeceleration = 70f;
        [SerializeField] float airAcceleration = 25f;
        [SerializeField] float turnSpeed = 900f;

        [Header("Jump")]
        [SerializeField] float jumpHeight = 1.6f;
        [SerializeField] float doubleJumpHeight = 1.3f;
        [SerializeField] float gravity = 32f;
        [SerializeField] float fallGravityMultiplier = 1.5f;
        [Tooltip("Upward velocity is multiplied by this when jump is released early (variable jump height).")]
        [SerializeField, Range(0f, 1f)] float jumpCutMultiplier = 0.5f;
        [SerializeField] float maxFallSpeed = 30f;
        [SerializeField] float coyoteTime = 0.12f;
        [SerializeField] float jumpBufferTime = 0.12f;

        [Header("Dodge")]
        [Tooltip("Distance covered by one dodge.")]
        [SerializeField] float dodgeDistance = 4f;
        [SerializeField] float dodgeDuration = 0.25f;
        [Tooltip("Wait after a dodge ends before the next one.")]
        [SerializeField] float dodgeCooldown = 0.35f;

        [Header("Slam (attack after double jump)")]
        [Tooltip("Hang time in the air while the wrench is raised.")]
        [SerializeField] float slamWindupTime = 0.12f;
        [SerializeField] float slamFallSpeed = 30f;
        [Tooltip("No input for this long after the impact.")]
        [SerializeField] float slamLandingLock = 0.3f;

        [Header("Ledge Grab")]
        [Tooltip("Height of the (invisible) hands above the feet; this is what snaps to the ledge.")]
        [SerializeField] float handHeight = 1.95f;
        [Tooltip("Distance from the ledge line out to the player's center while hanging.")]
        [SerializeField] float hangWallOffset = 0.55f;
        [SerializeField] float grabVerticalTolerance = 0.45f;
        [Tooltip("How far out from the wall the hands may be and still grab.")]
        [SerializeField] float grabReach = 0.9f;
        [SerializeField] float grabBehindTolerance = 0.25f;
        [Tooltip("Only grab when falling or near the top of a jump, not while flying up past a ledge.")]
        [SerializeField] float grabMaxRiseSpeed = 2f;
        [SerializeField] float ledgeEndMargin = 0.3f;
        [SerializeField] float shimmySpeed = 2.5f;
        [SerializeField] float hangSnapSharpness = 15f;
        [SerializeField] float hangJumpHeight = 2.6f;
        [Tooltip("Push towards the wall after jumping up from a ledge, so the player climbs over the top.")]
        [SerializeField] float climbAssistSpeed = 2.5f;
        [SerializeField] float climbAssistTime = 0.6f;
        [Tooltip("The ledge just let go of can't be grabbed again for this long.")]
        [SerializeField] float regrabDelay = 0.35f;

        [Header("Visuals")]
        [SerializeField] Renderer[] tintedRenderers;
        [SerializeField] Color[] playerColors = { new Color(0.2f, 0.55f, 1f), new Color(1f, 0.55f, 0.15f) };
        [Tooltip("How strongly the player colour shows on the tinted parts (1 = full colour, lower = paler, towards white).")]
        [SerializeField, Range(0f, 1f)] float tintAmount = 1f;

        [Header("Respawn")]
        [SerializeField] float killHeight = -30f;

        const float GroundStickVelocity = -2f;
        const int MaxJumps = 2;
        // Right after a launch the controller still reports the ground it left; ignore it this long.
        const float LaunchGroundGrace = 0.15f;

        readonly NetworkVariable<bool> isCrouching = new(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        // Hanging from a ledge, so every client can pose the figure the moment it grabs on.
        readonly NetworkVariable<bool> isHanging = new(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        // The last double jump, so every client can start the flip the moment one happens: count * 32 + how it was done
        // (0 = not aiming; aiming: 11 + the sideways input -10..10, + = right). One value, so both always arrive together.
        readonly NetworkVariable<int> doubleJumpInfo = new(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        int localDoubleJumpInfo;
        // Riding a zipline (owner): the line, how far down it (metres) and how fast; shared as a flag for the pose.
        Zipline zipline;
        float zipDistance, zipSpeed, zipGrabAllowedAt;
        readonly NetworkVariable<bool> isZiplining = new(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        PlayerTargeting targeting;

        CharacterController controller;
        NetworkTransform networkTransform;
        PlayerHealth health;
        InputAction moveAction, jumpAction, crouchAction, dodgeAction;
        float controlLockedUntil;

        enum SlamPhase { None, Windup, Falling }
        SlamPhase slamPhase;
        float slamPhaseStart;
        bool doubleJumped;

        PlayerSpawnPoints currentLevel;
        Vector3 planarVelocity;
        float verticalVelocity;
        float lastGroundedTime = float.NegativeInfinity;
        float lastJumpPressedTime = float.NegativeInfinity;
        int jumpsUsed;
        bool canCutJump;
        Vector3 dodgeDirection;
        float dodgeStartTime = float.NegativeInfinity;
        float launchTime = float.NegativeInfinity;

        // Aimed launch (directed jump pads): follows the arc exactly until it lands or bumps into something.
        bool flying;
        JumpArc flight;
        float flightTime;

        Ledge hangLedge;
        float hangOffset;
        Ledge releasedLedge;
        float releasedAt = float.NegativeInfinity;
        Vector3 climbAssistVelocity;
        float climbAssistUntil;

        public Vector3 PlanarVelocity => planarVelocity;
        public bool IsGrounded => controller != null && controller.enabled && controller.isGrounded;
        /// <summary>Replicated crouch state, for visuals on every client.</summary>
        public bool IsCrouchingForVisuals => isCrouching.Value;
        public bool IsHanging => hangLedge != null;
        /// <summary>Hanging from a ledge, on every client (the owner knows at once, the others get it shared).</summary>
        public bool IsHangingForVisuals => IsOwner ? hangLedge != null : isHanging.Value;
        /// <summary>Owner: riding a zipline right now.</summary>
        public bool IsZiplining => zipline != null;
        /// <summary>Riding a zipline, on every client.</summary>
        public bool IsZipliningForVisuals => IsOwner ? zipline != null : isZiplining.Value;
        int DoubleJumpInfo => IsOwner ? localDoubleJumpInfo : doubleJumpInfo.Value;
        /// <summary>How many double jumps so far, on every client (goes up by one with each, e.g. to start a flip).</summary>
        public int DoubleJumpCountForVisuals => DoubleJumpInfo >> 5;
        /// <summary>Whether the last double jump was made while aiming, on every client.</summary>
        public bool DoubleJumpAimedForVisuals => (DoubleJumpInfo & 31) != 0;
        /// <summary>Sideways input at the last double jump if it was made while aiming (-1 = left .. 1 = right), else 0.</summary>
        public float DoubleJumpSidewaysForVisuals => DoubleJumpAimedForVisuals ? ((DoubleJumpInfo & 31) - 11) / 10f : 0f;
        /// <summary>While hanging the hands are this high above the feet and this far in front of them (metres).</summary>
        public float HandHeight => handHeight;
        public float HangWallOffset => hangWallOffset;
        public bool IsDodging => Time.time - dodgeStartTime < dodgeDuration;
        public bool IsSlamming => slamPhase != SlamPhase.None;
        /// <summary>Owner: flying an aimed jump pad's arc (<see cref="LaunchTo"/>).</summary>
        public bool IsFlying => flying;

        /// <summary>Owner: standing on solid ground and free to be shot up by a jump pad.</summary>
        public bool CanLaunch => controller.isGrounded && Time.time - launchTime > LaunchGroundGrace && !IsSwimming
            && hangLedge == null && !IsInputBlocked && (health == null || health.IsAlive);

        /// <summary>Owner: true in the air after a double jump, when Attack turns into a slam.</summary>
        public bool CanSlam => doubleJumped && slamPhase == SlamPhase.None && hangLedge == null && !controller.isGrounded
            && Time.time >= controlLockedUntil && (health == null || health.IsAlive);

        /// <summary>Owner: raised with the impact position when a slam hits the ground.</summary>
        public event System.Action<Vector3> SlamLanded;

        float slowFactor = 1f;
        float slowUntil;

        /// <summary>Owner: scale run speed for a moment, e.g. while swinging a melee weapon.</summary>
        public void SlowDown(float factor, float duration)
        {
            slowFactor = factor;
            slowUntil = Time.time + duration;
        }

        float SpeedMultiplier => Time.time < slowUntil ? slowFactor : 1f;

        readonly System.Collections.Generic.HashSet<object> inputBlockers = new();

        /// <summary>
        /// Ignore move/jump/crouch input (gravity still applies) while any system asks for it — reviving a teammate,
        /// holding something with the wrench beam... Each caller passes itself so they don't undo each other.
        /// </summary>
        public void SetInputBlock(object source, bool blocked)
        {
            if (blocked) inputBlockers.Add(source);
            else inputBlockers.Remove(source);
        }

        public bool IsInputBlocked => inputBlockers.Count > 0;

        /// <summary>When set (non-zero), the player faces this way instead of the move direction — strafing while aiming.</summary>
        public Vector3 FacingOverride { get; set; }

        /// <summary>When set (non-zero), face the object held with the wrench beam; wins over <see cref="FacingOverride"/>.</summary>
        public Vector3 GrabFacing { get; set; }

        // Riding moving objects (wrench-grabbed bridges, lifts...): where the thing under our feet was last frame.
        Transform rideSurface;
        Vector3 rideLastPosition;
        Quaternion rideLastRotation;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            networkTransform = GetComponent<NetworkTransform>();
            health = GetComponent<PlayerHealth>();
            targeting = GetComponent<PlayerTargeting>();
        }

        /// <summary>Owner only: launch the player (e.g. when hit) and take away control for a moment.</summary>
        public void ApplyKnockback(Vector3 velocity, float controlLock)
        {
            if (IsSwimming)
            {
                // Water soaks up most of the hit.
                swimVelocity += velocity * 0.4f;
                controlLockedUntil = Time.time + controlLock;
                return;
            }

            // On a zipline the player holds on to the end; hits don't throw them off.
            if (zipline != null) return;
            ReleaseLedge();
            flying = false;
            planarVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
            verticalVelocity = velocity.y;
            dodgeStartTime = float.NegativeInfinity;
            slamPhase = SlamPhase.None;
            doubleJumped = false;
            canCutJump = false;
            jumpsUsed = MaxJumps;
            lastGroundedTime = float.NegativeInfinity;
            controlLockedUntil = Time.time + controlLock;
        }

        /// <summary>
        /// Owner only: shoot straight up to <paramref name="height"/> metres (jump pads). Air steering stays, and so
        /// does the double jump; letting go of Jump doesn't cut it short.
        /// </summary>
        public void Launch(float height)
        {
            if (IsSwimming || hangLedge != null || zipline != null) return;
            flying = false;
            verticalVelocity = JumpVelocity(height);
            dodgeStartTime = float.NegativeInfinity;
            slamPhase = SlamPhase.None;
            doubleJumped = false;
            canCutJump = false;
            jumpsUsed = 1;
            lastGroundedTime = float.NegativeInfinity;
            lastJumpPressedTime = float.NegativeInfinity;
            launchTime = Time.time;
        }

        /// <summary>
        /// Owner only: fly a parabola that lands the feet on <paramref name="target"/>, peaking
        /// <paramref name="arcHeight"/> metres above the higher end (aimed jump pads). No steering or jumping on the
        /// way, so it always arrives; hitting something on the way ends it and the player just falls from there.
        /// </summary>
        public void LaunchTo(Vector3 target, float arcHeight)
        {
            if (IsSwimming || hangLedge != null || zipline != null) return;
            flight = new JumpArc(transform.position, target, arcHeight, gravity, gravity * fallGravityMultiplier);
            flightTime = 0f;
            flying = true;
            planarVelocity = flight.PlanarVelocity;
            verticalVelocity = flight.VerticalVelocityAt(0f);
            dodgeStartTime = float.NegativeInfinity;
            slamPhase = SlamPhase.None;
            doubleJumped = false;
            canCutJump = false;
            jumpsUsed = MaxJumps;
            lastGroundedTime = float.NegativeInfinity;
            lastJumpPressedTime = float.NegativeInfinity;
            launchTime = Time.time;
        }

        public override void OnNetworkSpawn()
        {
            ApplyPlayerColor();
            if (!IsOwner) return;

            moveAction = InputSystem.actions.FindAction("Player/Move", true);
            jumpAction = InputSystem.actions.FindAction("Player/Jump", true);
            crouchAction = InputSystem.actions.FindAction("Player/Crouch", true);
            dodgeAction = InputSystem.actions.FindAction("Player/Dodge", true);
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner && ThirdPersonCameraRig.Instance != null)
                ThirdPersonCameraRig.Instance.ClearTarget(this);
        }

        void Update()
        {
            if (!IsSpawned || !IsOwner) return;

            // Players spawn in the bootstrap scene; wait for a level before simulating gravity.
            if (PlayerSpawnPoints.Instance == null) return;
            if (currentLevel != PlayerSpawnPoints.Instance) EnterLevel(PlayerSpawnPoints.Instance);

            Simulate(Time.deltaTime);
            bool hangingNow = hangLedge != null;
            if (isHanging.Value != hangingNow) isHanging.Value = hangingNow;
            bool ziplineNow = zipline != null;
            if (isZiplining.Value != ziplineNow) isZiplining.Value = ziplineNow;

            if (transform.position.y < killHeight) Respawn();
        }

        void Simulate(float dt)
        {
            // No input while knocked back, downed or busy (reviving).
            bool hasControl = Time.time >= controlLockedUntil && !IsInputBlocked && (health == null || health.IsAlive);
            Vector2 moveInput = hasControl ? Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f) : Vector2.zero;
            bool jumpPressed = hasControl && jumpAction.WasPressedThisFrame();
            bool jumpReleased = hasControl && jumpAction.WasReleasedThisFrame();
            bool crouchHeld = hasControl && crouchAction.IsPressed();

            GetCameraBasis(out Vector3 camForward, out Vector3 camRight);
            Vector3 wishDir = camForward * moveInput.y + camRight * moveInput.x;

            if (zipline != null)
            {
                SimulateZipline(dt);
                return;
            }

            if (swimMode != SwimMode.None)
            {
                SimulateSwim(dt, hasControl, moveInput, jumpPressed);
                return;
            }

            if (hangLedge != null)
            {
                SimulateHang(dt, wishDir, jumpPressed, hasControl && crouchAction.WasPressedThisFrame());
                return;
            }

            if (slamPhase != SlamPhase.None)
            {
                SimulateSlam(dt);
                return;
            }

            if (flying)
            {
                SimulateFlight(dt);
                return;
            }

            bool grounded = controller.isGrounded && Time.time - launchTime > LaunchGroundGrace;
            if (grounded) RideSurface();
            rideSurface = null; // re-detected by this frame's Move (OnControllerColliderHit)

            if (grounded)
            {
                lastGroundedTime = Time.time;
                jumpsUsed = 0;
                doubleJumped = false;
                if (verticalVelocity < 0f) verticalVelocity = GroundStickVelocity;
            }
            else if (jumpsUsed == 0 && Time.time - lastGroundedTime > coyoteTime)
            {
                // Walked off a ledge: only the air jump is left.
                jumpsUsed = 1;
            }

            // Dodge: a quick burst in the held direction (facing direction if none), ground only.
            bool dodgeReady = Time.time - dodgeStartTime >= dodgeDuration + dodgeCooldown;
            if (hasControl && grounded && dodgeReady && dodgeAction.WasPressedThisFrame())
                StartDodge(wishDir);

            bool crouching = grounded && crouchHeld && !IsDodging;

            // Planar movement
            float targetSpeed = (crouching ? crouchSpeed : moveSpeed) * SpeedMultiplier;
            Vector3 targetVelocity = wishDir * targetSpeed;
            float accel;
            if (!grounded && !hasControl) accel = 0f; // keep knockback momentum until landing
            else if (!grounded) accel = airAcceleration;
            else accel = wishDir.sqrMagnitude > 0.0001f ? groundAcceleration : groundDeceleration;

            if (IsDodging)
                planarVelocity = dodgeDirection * DodgeSpeed((Time.time - dodgeStartTime) / dodgeDuration);
            else if (Time.time < climbAssistUntil && wishDir.sqrMagnitude < 0.0001f)
                planarVelocity = climbAssistVelocity;
            else
                planarVelocity = Vector3.MoveTowards(planarVelocity, targetVelocity, accel * dt);

            // Jumping
            if (jumpPressed) lastJumpPressedTime = Time.time;
            bool jumpBuffered = Time.time - lastJumpPressedTime <= jumpBufferTime;
            bool canGroundJump = jumpsUsed == 0 && Time.time - lastGroundedTime <= coyoteTime;

            if (jumpBuffered && canGroundJump)
            {
                verticalVelocity = JumpVelocity(jumpHeight);
                jumpsUsed = 1;
                canCutJump = true;
                crouching = false;
                // Jumping out of a dodge keeps its momentum but hands control back to air steering.
                dodgeStartTime = float.NegativeInfinity;
                lastJumpPressedTime = float.NegativeInfinity;
                lastGroundedTime = float.NegativeInfinity;
            }
            else if (jumpPressed && !grounded && jumpsUsed < MaxJumps)
            {
                // Max: a double jump never slows a faster climb (the launch from a jump pad).
                verticalVelocity = Mathf.Max(verticalVelocity, JumpVelocity(doubleJumpHeight));
                jumpsUsed = MaxJumps;
                doubleJumped = true;
                canCutJump = true;
                lastJumpPressedTime = float.NegativeInfinity;
                // Remember whether it was made while aiming and which way the stick pushed compared with the facing.
                int how = 0;
                if (targeting != null && targeting.IsAiming)
                {
                    float sideways = wishDir.sqrMagnitude > 0.01f ? Vector3.Dot(wishDir.normalized, transform.right) : 0f;
                    how = 11 + Mathf.RoundToInt(Mathf.Clamp(sideways, -1f, 1f) * 10f);
                }
                localDoubleJumpInfo = ((localDoubleJumpInfo >> 5) + 1) * 32 + how;
                doubleJumpInfo.Value = localDoubleJumpInfo;
            }

            if (jumpReleased && canCutJump && verticalVelocity > 0f)
            {
                verticalVelocity *= jumpCutMultiplier;
                canCutJump = false;
            }

            // Gravity. Moving by the average of old and new velocity keeps jump height frame-rate independent.
            float startVertical = verticalVelocity;
            if (!grounded || verticalVelocity > 0f)
            {
                float g = verticalVelocity < 0f ? gravity * fallGravityMultiplier : gravity;
                verticalVelocity = Mathf.Max(verticalVelocity - g * dt, -maxFallSpeed);
            }
            float verticalStep = (startVertical + verticalVelocity) * 0.5f * dt;
            float previousHandsY = transform.position.y + handHeight;

            CollisionFlags flags = controller.Move(planarVelocity * dt + Vector3.up * verticalStep);
            if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f) verticalVelocity = 0f;
            // Hitting a wall mid-air kills the momentum into it.
            if (!grounded && (flags & CollisionFlags.Sides) != 0) planarVelocity = Vector3.ProjectOnPlane(controller.velocity, Vector3.up);

            // Face the movement direction (a dodge faces where it goes, the wrench beam faces what it holds,
            // aiming faces the crosshair).
            Vector3 faceDir = IsDodging ? dodgeDirection
                : GrabFacing.sqrMagnitude > 0.0001f ? GrabFacing
                : FacingOverride.sqrMagnitude > 0.0001f ? FacingOverride
                : wishDir;
            if (faceDir.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(faceDir, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeed * dt);
            }

            if (isCrouching.Value != crouching) isCrouching.Value = crouching;

            if (rideSurface != null)
            {
                rideLastPosition = rideSurface.position;
                rideLastRotation = rideSurface.rotation;
            }

            if (TryEnterWater()) return;
            if (!controller.isGrounded && hasControl && verticalVelocity <= grabMaxRiseSpeed) TryGrabLedge(previousHandsY);
        }

        float JumpVelocity(float height) => Mathf.Sqrt(2f * gravity * height);

        // ---------- Zipline ----------

        /// <summary>
        /// Owner only: grab <paramref name="line"/> at its top and ride it down (it only goes down). Hands on the
        /// cable, the player hangs under it and slides, speeding up, braking near the end, and lets go only there.
        /// No collisions on the way (the line is meant to be clear). False if the player can't grab it right now.
        /// </summary>
        public bool StartZipline(Zipline line)
        {
            if (line == null || zipline != null || Time.time < zipGrabAllowedAt || IsSwimming || hangLedge != null
                || slamPhase != SlamPhase.None || IsInputBlocked || (health != null && !health.IsAlive)) return false;
            flying = false;
            zipline = line;
            zipDistance = line.StartDistance;
            zipSpeed = line.StartSpeed;
            planarVelocity = Vector3.zero;
            verticalVelocity = 0f;
            dodgeStartTime = float.NegativeInfinity;
            doubleJumped = false;
            canCutJump = false;
            // CharacterController overrides transform writes while enabled.
            controller.enabled = false;
            return true;
        }

        void SimulateZipline(float dt)
        {
            if (health != null && !health.IsAlive)
            {
                LeaveZipline();
                return;
            }
            zipSpeed = Mathf.Min(zipline.SpeedLimitAt(zipDistance), zipSpeed + zipline.Acceleration * dt);
            zipDistance = Mathf.Min(zipDistance + zipSpeed * dt, zipline.EndDistance);
            transform.position = zipline.PointAt(zipDistance) + Vector3.down * zipline.HangDepth;
            Vector3 flat = Vector3.ProjectOnPlane(zipline.Direction, Vector3.up);
            if (flat.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat, Vector3.up), turnSpeed * dt);

            // No letting go on the way: the ride always ends at the bottom.
            if (zipDistance >= zipline.EndDistance) LeaveZipline();
        }

        // Off the zipline (at the bottom, or downed): keeps the speed it had; the air jump is still there.
        void LeaveZipline()
        {
            var line = zipline;
            zipline = null;
            controller.enabled = true;
            if (line == null) return;
            Vector3 velocity = line.Direction * zipSpeed;
            planarVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
            verticalVelocity = Mathf.Min(0f, velocity.y);
            jumpsUsed = 1;
            lastGroundedTime = float.NegativeInfinity;
            lastJumpPressedTime = float.NegativeInfinity;
            zipGrabAllowedAt = Time.time + 0.5f;
        }

        void SimulateFlight(float dt)
        {
            flightTime += dt;
            Vector3 wanted = flight.PositionAt(flightTime);
            controller.Move(wanted - transform.position);
            rideSurface = null;
            if (isCrouching.Value) isCrouching.Value = false;

            Vector3 heading = flight.PlanarVelocity;
            if (heading.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(heading, Vector3.up), turnSpeed * dt);

            bool landed = flightTime >= flight.Duration;
            bool blocked = (wanted - transform.position).sqrMagnitude > 0.35f * 0.35f;
            if (landed || blocked)
            {
                // Back to normal movement: a clean landing stops dead on the target, a bump just drops from there.
                flying = false;
                planarVelocity = Vector3.zero;
                verticalVelocity = Mathf.Max(Mathf.Min(flight.VerticalVelocityAt(flightTime), 0f), -maxFallSpeed);
                launchTime = Time.time - LaunchGroundGrace;
            }
            if (TryEnterWater()) flying = false;
        }

        // ---------- Riding moving objects ----------

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            // Standing on something that moves over the network (beam-moved bridges, mechanism bridges and gates,
            // heavy blocks...): remember it so we move along with it.
            if (hit.normal.y < 0.6f) return;
            // ...or a symbol platform, which every client slides itself.
            Transform mover = null;
            var networked = hit.collider.GetComponentInParent<NetworkTransform>();
            if (networked != null) mover = networked.transform;
            else
            {
                var platform = hit.collider.GetComponentInParent<SymbolPlatform>();
                if (platform != null) mover = platform.transform;
            }
            if (mover == null || mover == transform) return;
            if (rideSurface != mover)
            {
                rideSurface = mover;
                rideLastPosition = rideSurface.position;
                rideLastRotation = rideSurface.rotation;
            }
        }

        /// <summary>Carry the player by however much the surface under their feet moved/turned since last frame.</summary>
        void RideSurface()
        {
            if (rideSurface == null) return;
            Vector3 local = Quaternion.Inverse(rideLastRotation) * (transform.position - rideLastPosition);
            Vector3 carried = rideSurface.position + rideSurface.rotation * local;
            Vector3 delta = carried - transform.position;
            if (delta.sqrMagnitude > 0.0000001f) controller.Move(delta);

            float yaw = (rideSurface.rotation * Quaternion.Inverse(rideLastRotation)).eulerAngles.y;
            if (Mathf.Abs(Mathf.DeltaAngle(0f, yaw)) > 0.01f) transform.rotation = Quaternion.Euler(0f, yaw, 0f) * transform.rotation;
        }

        // ---------- Dodge ----------

        void StartDodge(Vector3 wishDir)
        {
            dodgeDirection = wishDir.sqrMagnitude > 0.01f ? wishDir.normalized : Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            dodgeStartTime = Time.time;
            planarVelocity = dodgeDirection * DodgeSpeed(0f);
            if (health != null) health.RequestDodgeInvulnerabilityRpc();
        }

        /// <summary>
        /// Speed falls linearly from a burst down to run speed over the dodge, covering exactly
        /// <see cref="dodgeDistance"/>: distance = duration * (peak + run) / 2.
        /// </summary>
        float DodgeSpeed(float t)
        {
            float peak = 2f * dodgeDistance / dodgeDuration - moveSpeed;
            return Mathf.Lerp(peak, moveSpeed, Mathf.Clamp01(t));
        }

        // ---------- Slam ----------

        /// <summary>Owner only: freeze in the air for the windup, then drive straight down until impact.</summary>
        public void StartSlam()
        {
            if (!CanSlam) return;
            slamPhase = SlamPhase.Windup;
            slamPhaseStart = Time.time;
            dodgeStartTime = float.NegativeInfinity;
            planarVelocity = Vector3.zero;
            verticalVelocity = 0f;
            canCutJump = false;
        }

        void SimulateSlam(float dt)
        {
            if (health != null && !health.IsAlive)
            {
                slamPhase = SlamPhase.None;
                return;
            }

            planarVelocity = Vector3.zero;
            if (slamPhase == SlamPhase.Windup)
            {
                verticalVelocity = 0f;
                if (Time.time - slamPhaseStart >= slamWindupTime)
                {
                    slamPhase = SlamPhase.Falling;
                    slamPhaseStart = Time.time;
                }
                return;
            }

            verticalVelocity = -slamFallSpeed;
            CollisionFlags flags = controller.Move(Vector3.down * (slamFallSpeed * dt));
            if (TryEnterWater()) return; // slamming into water just splashes in, no impact
            if ((flags & CollisionFlags.Below) == 0 && !controller.isGrounded) return;

            slamPhase = SlamPhase.None;
            doubleJumped = false;
            jumpsUsed = 0;
            verticalVelocity = GroundStickVelocity;
            lastGroundedTime = Time.time;
            controlLockedUntil = Time.time + slamLandingLock;
            SlamLanded?.Invoke(transform.position);
        }

        // ---------- Ledge grab ----------

        void TryGrabLedge(float previousHandsY)
        {
            Vector3 hands = transform.position + Vector3.up * handHeight;
            // Test the whole vertical sweep of the hands this frame, so a fast fall can't skip past a ledge.
            float sweepLow = Mathf.Min(previousHandsY, hands.y) - grabVerticalTolerance;
            float sweepHigh = Mathf.Max(previousHandsY, hands.y) + grabVerticalTolerance;
            Ledge best = null;
            float bestScore = float.MaxValue;
            float bestOffset = 0f;

            foreach (var ledge in Ledge.All)
            {
                if (ledge == releasedLedge && Time.time - releasedAt < regrabDelay) continue;
                float limit = ledge.HalfLength - ledgeEndMargin;
                if (limit <= 0f) continue;

                float along = ledge.Project(hands);
                if (Mathf.Abs(along) > ledge.HalfLength) continue;

                Vector3 edgePoint = ledge.PointAt(along);
                if (edgePoint.y < sweepLow || edgePoint.y > sweepHigh) continue;
                float outward = Vector3.Dot(hands - edgePoint, ledge.Outward);
                if (outward < -grabBehindTolerance || outward > grabReach) continue;

                float score = Mathf.Abs(hands.y - edgePoint.y) + Mathf.Abs(outward);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = ledge;
                    bestOffset = Mathf.Clamp(along, -limit, limit);
                }
            }

            if (best != null) StartHang(best, bestOffset);
        }

        void StartHang(Ledge ledge, float offset)
        {
            hangLedge = ledge;
            hangOffset = offset;
            planarVelocity = Vector3.zero;
            verticalVelocity = 0f;
            dodgeStartTime = float.NegativeInfinity;
            doubleJumped = false;
            canCutJump = false;
            jumpsUsed = 0;
            climbAssistUntil = 0f;
        }

        void SimulateHang(float dt, Vector3 wishDir, bool jumpPressed, bool dropPressed)
        {
            if (health != null && !health.IsAlive)
            {
                ReleaseLedge();
                return;
            }

            // Shimmy along the edge with the camera-relative stick direction.
            float limit = hangLedge.HalfLength - ledgeEndMargin;
            hangOffset = Mathf.Clamp(hangOffset + Vector3.Dot(wishDir, hangLedge.Along) * shimmySpeed * dt, -limit, limit);

            Vector3 target = hangLedge.PointAt(hangOffset) + hangLedge.Outward * hangWallOffset - Vector3.up * handHeight;
            Vector3 next = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-hangSnapSharpness * dt));
            controller.Move(next - transform.position);

            Quaternion faceWall = Quaternion.LookRotation(-hangLedge.Outward, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, faceWall, turnSpeed * dt);
            if (isCrouching.Value) isCrouching.Value = false;

            if (jumpPressed)
            {
                // Jump straight up: reaches the next ledge above, or climbs over the top thanks to the assist push.
                Vector3 intoWall = -hangLedge.Outward;
                ReleaseLedge();
                verticalVelocity = JumpVelocity(hangJumpHeight);
                planarVelocity = Vector3.zero;
                climbAssistVelocity = intoWall * climbAssistSpeed;
                climbAssistUntil = Time.time + climbAssistTime;
                jumpsUsed = 1;
            }
            else if (dropPressed)
            {
                // Let go; a ledge further down can still be caught on the way.
                ReleaseLedge();
                verticalVelocity = 0f;
                planarVelocity = Vector3.zero;
                jumpsUsed = 1;
            }
        }

        void ReleaseLedge()
        {
            if (hangLedge == null) return;
            releasedLedge = hangLedge;
            releasedAt = Time.time;
            hangLedge = null;
            lastGroundedTime = float.NegativeInfinity;
            lastJumpPressedTime = float.NegativeInfinity;
        }

        void GetCameraBasis(out Vector3 forward, out Vector3 right)
        {
            var rig = ThirdPersonCameraRig.Instance;
            if (rig != null)
            {
                forward = rig.PlanarForward;
                right = rig.PlanarRight;
                return;
            }

            Transform cam = Camera.main != null ? Camera.main.transform : transform;
            forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            right = Vector3.Cross(Vector3.up, forward);
        }

        void EnterLevel(PlayerSpawnPoints level)
        {
            currentLevel = level;
            Respawn();
            if (ThirdPersonCameraRig.Instance != null) ThirdPersonCameraRig.Instance.SetTarget(this);
        }

        /// <summary>Owner only: teleport back to this player's spawn point.</summary>
        public void Respawn()
        {
            if (currentLevel == null) return;
            TeleportTo(currentLevel.GetSpawnPose(OwnerClientId));
        }

        /// <summary>Owner only: put the player at <paramref name="spawn"/>, standing still (respawns, the end of a cutscene).</summary>
        public void TeleportTo(Pose spawn)
        {
            ReleaseLedge();
            zipline = null;
            flying = false;
            planarVelocity = Vector3.zero;
            verticalVelocity = 0f;
            jumpsUsed = 0;
            dodgeStartTime = float.NegativeInfinity;
            slamPhase = SlamPhase.None;
            doubleJumped = false;
            if (IsSwimming) SetSwimMode(SwimMode.None);
            water = null;

            // CharacterController overrides transform writes while enabled.
            controller.enabled = false;
            networkTransform.Teleport(spawn.position, spawn.rotation, transform.localScale);
            controller.enabled = true;

            if (ThirdPersonCameraRig.Instance != null) ThirdPersonCameraRig.Instance.SnapBehindTarget();
        }

        void ApplyPlayerColor()
        {
            if (tintedRenderers == null || playerColors.Length == 0) return;
            Color color = Color.Lerp(Color.white, playerColors[(int)(OwnerClientId % (ulong)playerColors.Length)], tintAmount);
            foreach (var r in tintedRenderers)
                if (r != null) r.material.color = color;
        }
    }
}
