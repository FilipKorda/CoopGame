using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Swimming. Water is dense: speed bleeds off fast, and normal swimming is a breaststroke where each kick
    /// gives a burst of speed (a stepped, surging motion). Holding Dodge (Shift) switches to a steady crawl.
    /// On the surface the player floats and moves flat; Crouch (C) dives, and underwater the player swims
    /// wherever the camera points. Holding Crouch again underwater swims up to the surface.
    /// Jump at the surface climbs out over a pool edge. No attacks or dodges in water.
    /// </summary>
    public partial class PlayerMovement
    {
        public enum SwimMode : byte { None, Surface, Underwater }

        [Header("Swimming")]
        [Tooltip("Height above the feet that decides whether the player is in water.")]
        [SerializeField] float chestHeight = 1.2f;
        [Tooltip("How deep the feet hang below the surface while floating on top.")]
        [SerializeField] float surfaceFeetDepth = 1.5f;
        [Tooltip("Water resistance: how quickly speed bleeds off between kicks.")]
        [SerializeField] float waterDrag = 2.5f;
        [Tooltip("Breaststroke: speed added by each kick.")]
        [SerializeField] float strokeImpulse = 4f;
        [SerializeField] float strokeInterval = 0.85f;
        [Tooltip("Crawl (holding Dodge): steady speed, no kicks.")]
        [SerializeField] float crawlSpeed = 5f;
        [SerializeField] float crawlAcceleration = 8f;
        [SerializeField] float diveSpeed = 4f;
        [SerializeField] float ascendSpeed = 3f;
        [SerializeField] float buoyancySpring = 20f;
        [SerializeField] float buoyancyDamping = 6f;
        [SerializeField] float swimTurnSpeed = 360f;
        [Tooltip("Jump out of the water at the surface, e.g. onto a pool edge.")]
        [SerializeField] float waterJumpHeight = 2.2f;
        [Tooltip("Chest this far above the surface counts as out of the water (stops flickering in and out).")]
        [SerializeField] float waterExitMargin = 0.4f;

        readonly NetworkVariable<byte> swimState = new(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        SwimMode swimMode;
        WaterVolume water;
        Vector3 swimVelocity;
        float lastStrokeTime = float.NegativeInfinity;
        bool ascendArmed;
        float waterReentryTime;

        /// <summary>Owner: currently swimming (surface or underwater).</summary>
        public bool IsSwimming => swimMode != SwimMode.None;
        /// <summary>Replicated swim state, for visuals on every client.</summary>
        public SwimMode SwimStateForVisuals => (SwimMode)swimState.Value;

        bool TryEnterWater()
        {
            if (Time.time < waterReentryTime) return false;
            var found = WaterVolume.Find(transform.position + Vector3.up * chestHeight);
            if (found == null) return false;

            water = found;
            ReleaseLedge();
            slamPhase = SlamPhase.None;
            dodgeStartTime = float.NegativeInfinity;
            // Splash in: keep some of the momentum, buoyancy brings the player back up to the surface.
            swimVelocity = planarVelocity * 0.5f + Vector3.up * (verticalVelocity * 0.4f);
            lastStrokeTime = float.NegativeInfinity;
            SetSwimMode(SwimMode.Surface);
            if (isCrouching.Value) isCrouching.Value = false;
            return true;
        }

        void ExitWater()
        {
            SetSwimMode(SwimMode.None);
            water = null;
            planarVelocity = Vector3.ProjectOnPlane(swimVelocity, Vector3.up);
            verticalVelocity = swimVelocity.y;
            jumpsUsed = 1;
            doubleJumped = false;
            lastGroundedTime = float.NegativeInfinity;
        }

        void SetSwimMode(SwimMode mode)
        {
            swimMode = mode;
            if (swimState.Value != (byte)mode) swimState.Value = (byte)mode;
        }

        void SimulateSwim(float dt, bool hasControl, Vector2 moveInput, bool jumpPressed)
        {
            Vector3 chest = transform.position + Vector3.up * chestHeight;
            if (water == null || !water.ContainsHorizontally(chest) || chest.y > water.SurfaceY + waterExitMargin)
            {
                // Walked out of a shallow end or got carried over the edge.
                ExitWater();
                return;
            }

            bool alive = health == null || health.IsAlive;
            bool crawling = hasControl && dodgeAction.IsPressed();
            bool diveHeld = hasControl && crouchAction.IsPressed();
            bool divePressed = hasControl && crouchAction.WasPressedThisFrame();

            // Flat steering on the surface, full camera direction (including looking up/down) underwater.
            Vector3 forward, right;
            if (swimMode == SwimMode.Underwater) GetCameraBasis3D(out forward, out right);
            else GetCameraBasis(out forward, out right);
            Vector3 wish = Vector3.ClampMagnitude(forward * moveInput.y + right * moveInput.x, 1f);
            bool wantsToMove = wish.sqrMagnitude > 0.01f;

            if (crawling && wantsToMove)
            {
                // Crawl: steady, even pull through the water.
                Vector3 target = wish * crawlSpeed;
                if (swimMode == SwimMode.Surface) target.y = swimVelocity.y;
                swimVelocity = Vector3.MoveTowards(swimVelocity, target, crawlAcceleration * dt);
            }
            else
            {
                // Dense water eats the speed; breaststroke kicks put it back in bursts.
                swimVelocity *= Mathf.Exp(-waterDrag * dt);
                if (wantsToMove && Time.time - lastStrokeTime >= strokeInterval)
                {
                    Vector3 kick = wish * strokeImpulse;
                    if (swimMode == SwimMode.Surface) kick.y = 0f;
                    swimVelocity += kick;
                    lastStrokeTime = Time.time;
                }
            }

            float floatY = water.SurfaceY - surfaceFeetDepth;
            if (swimMode == SwimMode.Surface)
            {
                swimVelocity.y += ((floatY - transform.position.y) * buoyancySpring - swimVelocity.y * buoyancyDamping) * dt;

                if (divePressed)
                {
                    SetSwimMode(SwimMode.Underwater);
                    swimVelocity.y = -diveSpeed;
                    ascendArmed = false;
                }
                else if (jumpPressed)
                {
                    ExitWater();
                    verticalVelocity = JumpVelocity(waterJumpHeight);
                    jumpsUsed = MaxJumps;
                    waterReentryTime = Time.time + 0.4f;
                    return;
                }
            }
            else
            {
                // The press that dived doesn't count; let go and hold again to swim up.
                if (!diveHeld) ascendArmed = true;
                bool ascending = (ascendArmed && diveHeld) || !alive;
                if (ascending) swimVelocity.y = Mathf.MoveTowards(swimVelocity.y, ascendSpeed, 12f * dt);

                if (transform.position.y >= floatY && swimVelocity.y >= 0f) SetSwimMode(SwimMode.Surface);
            }

            CollisionFlags flags = controller.Move(swimVelocity * dt);
            if ((flags & CollisionFlags.Below) != 0 && swimVelocity.y < 0f) swimVelocity.y = 0f;
            if ((flags & CollisionFlags.Above) != 0 && swimVelocity.y > 0f) swimVelocity.y = 0f;

            planarVelocity = Vector3.ProjectOnPlane(swimVelocity, Vector3.up);
            verticalVelocity = swimVelocity.y;

            Vector3 face = Vector3.ProjectOnPlane(wantsToMove ? wish : swimVelocity, Vector3.up);
            if (face.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(face, Vector3.up), swimTurnSpeed * dt);
        }

        void GetCameraBasis3D(out Vector3 forward, out Vector3 right)
        {
            var rig = ThirdPersonCameraRig.Instance;
            Transform view = rig != null ? rig.transform : (Camera.main != null ? Camera.main.transform : transform);
            forward = view.forward;
            right = view.right;
        }
    }
}
