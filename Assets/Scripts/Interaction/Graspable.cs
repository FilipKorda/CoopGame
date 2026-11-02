using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// An object the wrench's magnetic beam can grab (only objects with this component can be grabbed).
    /// The grabbing player steers it with the move keys and it follows its constraint:
    /// - Slide: along a straight track (bridges, lifts, sliding doors)
    /// - Hinge: rotates around an axis between two angles (drawbridges, gates)
    /// - Carry: free on the ground, can be set down anywhere (blocks to climb on)
    /// - Fixed: doesn't move itself; a latch point that just reports who holds it and how they steer
    ///   (buttons, and the handles of co-op objects like <see cref="HeavyObject"/> or <see cref="TearableBarrier"/>)
    /// On release it either stays or returns to its start (e.g. a bridge one player must hold while the other crosses).
    /// The server owns the movement and lets only one player hold it at a time; a server-authoritative
    /// NetworkTransform replicates it. Players standing on it ride along (<see cref="PlayerMovement"/>).
    /// </summary>
    public class Graspable : NetworkBehaviour
    {
        public enum Motion { Slide, Hinge, Carry, Fixed }
        public enum ReleaseBehaviour { Stay, ReturnToStart }

        const ulong NoHolder = ulong.MaxValue;

        static readonly List<Graspable> s_All = new();
        public static IReadOnlyList<Graspable> All => s_All;

        [SerializeField] Motion motion = Motion.Slide;
        [SerializeField] ReleaseBehaviour onRelease = ReleaseBehaviour.Stay;
        [Tooltip("Where the beam attaches; also the aim point (put a Targetable's Aim Override on it).")]
        [SerializeField] Transform socket;
        [Tooltip("Optional: more places the beam can attach (e.g. one on each side of a block). A grab takes whichever " +
                 "socket, this list or the one above, is nearest the player grabbing it.")]
        [SerializeField] Transform[] extraSockets;
        [Tooltip("Speed of the socket when steered, m/s.")]
        [SerializeField] float moveSpeed = 3f;
        [SerializeField] float returnSpeed = 3f;
        [Tooltip("The holder lets go if they end up farther than this from the socket.")]
        [SerializeField] float maxHoldDistance = 18f;

        [Header("Slide")]
        [Tooltip("Track from the start position, in the object's local axes.")]
        [SerializeField] Vector3 slideOffset = new Vector3(0f, 0f, 5f);

        [Header("Hinge")]
        [Tooltip("Rotation axis in the object's local space; the object's pivot is the hinge.")]
        [SerializeField] Vector3 hingeAxis = Vector3.right;
        [SerializeField] float minAngle;
        [SerializeField] float maxAngle = 90f;

        [Header("Carry")]
        [Tooltip("Lifted this much off the ground while held.")]
        [SerializeField] float carryLift = 0.4f;
        [SerializeField] float gravity = 25f;
        [SerializeField] LayerMask carryCollisionMask = ~0;
        [Tooltip("How far it can be carried from where it started, metres along its own X (left/right) and Z (forward/back). 0 = no limit on that axis.")]
        [SerializeField] Vector2 carryRange;

        [Header("Beam")]
        [SerializeField] Material beamMaterial;
        [SerializeField] float beamWidth = 0.08f;

        readonly NetworkVariable<ulong> holder = new(NoHolder);
        // Which socket the current grab holds: 0 = Socket, 1.. = Extra Sockets.
        readonly NetworkVariable<byte> heldSocket = new(0);

        // Server state
        Vector3 slideStart, slideEnd;
        float slideProgress;
        Quaternion baseRotation;
        Vector3 worldHingeAxis;
        float hingeAngle;
        Vector2 steerInput;
        float steerYaw;
        Vector3 controlAxis;
        float carryTargetY;
        float fallSpeed;
        BoxCollider carryCollider;

        Transform beam;

        public bool IsHeld => holder.Value != NoHolder;
        public ulong HolderClientId => holder.Value;
        /// <summary>Where the beam attaches: the socket the current grab took (the main one when nobody holds it).</summary>
        public Vector3 SocketPosition => SocketAt(IsHeld ? heldSocket.Value : 0);

        Vector3 SocketAt(int index)
        {
            Transform t = index == 0 || extraSockets == null || index > extraSockets.Length ? socket : extraSockets[index - 1];
            if (t == null) t = socket;
            return t != null ? t.position : transform.position;
        }

        int NearestSocket(Vector3 from)
        {
            int best = 0;
            float bestDistance = (SocketAt(0) - from).sqrMagnitude;
            int count = extraSockets != null ? Mathf.Min(extraSockets.Length, byte.MaxValue) : 0;
            for (int i = 1; i <= count; i++)
            {
                if (extraSockets[i - 1] == null) continue;
                float distance = (SocketAt(i) - from).sqrMagnitude;
                if (distance < bestDistance) { best = i; bestDistance = distance; }
            }
            return best;
        }

        /// <summary>The socket a grab from <paramref name="from"/> would take (for range checks before grabbing).</summary>
        public Vector3 NearestSocketPosition(Vector3 from) => SocketAt(NearestSocket(from));

        /// <summary>Server: the holder's move input as a flat world direction (camera-relative, like walking), length 0..1.</summary>
        public Vector3 SteerWorld
        {
            get
            {
                if (!IsHeld) return Vector3.zero;
                Quaternion yaw = Quaternion.Euler(0f, steerYaw, 0f);
                return yaw * new Vector3(steerInput.x, 0f, steerInput.y);
            }
        }

        /// <summary>Server: the holder's move keys as they are (x = A/D, y = W/S), length 0..1; zero when nobody holds it.</summary>
        public Vector2 SteerInput => IsHeld ? steerInput : Vector2.zero;

        /// <summary>Server, Hinge: 0 at the start angle, 1 at the far end (levers use this).</summary>
        public float HingeProgress => Mathf.Approximately(maxAngle, minAngle) ? 0f : Mathf.InverseLerp(minAngle, maxAngle, hingeAngle);

        /// <summary>Server: the player holding this, or null.</summary>
        public PlayerHealth Holder
        {
            get
            {
                foreach (var player in PlayerHealth.All)
                    if (player.OwnerClientId == holder.Value) return player;
                return null;
            }
        }

        void Awake()
        {
            slideStart = transform.position;
            slideEnd = slideStart + transform.rotation * slideOffset;
            baseRotation = transform.rotation;
            worldHingeAxis = (baseRotation * hingeAxis).normalized;
            hingeAngle = Mathf.Clamp(0f, minAngle, maxAngle);
            carryCollider = GetComponentInChildren<BoxCollider>();
        }

        void OnEnable() => s_All.Add(this);

        void OnDisable()
        {
            s_All.Remove(this);
            // Switched off (e.g. the handle of a torn-down barrier): Update stops, so drop the holder and hide the
            // beam here, or it would hang in the air where it was last drawn.
            if (beam != null) beam.gameObject.SetActive(false);
            if (IsServer && IsSpawned && IsHeld) ServerRelease(holder.Value);
        }

        // ---------- Server API (called from WrenchGrab RPCs) ----------

        public bool ServerTryGrab(ulong clientId)
        {
            if (!IsServer || IsHeld) return false;
            byte chosen = 0;
            foreach (var player in PlayerHealth.All)
                if (player.OwnerClientId == clientId) chosen = (byte)NearestSocket(player.transform.position);
            heldSocket.Value = chosen;
            holder.Value = clientId;
            steerInput = Vector2.zero;
            if (motion == Motion.Carry) carryTargetY = transform.position.y + carryLift;
            controlAxis = ComputeControlAxis();
            return true;
        }

        /// <summary>
        /// The direction the move keys push the object for the whole grab, taken from which way the socket starts
        /// moving: mostly sideways/forward → push it with the stick like on the ground; mostly vertical → up/down
        /// on the stick. Fixed per grab, so a drawbridge keeps lowering with the same key even as it tips over.
        /// </summary>
        Vector3 ComputeControlAxis()
        {
            Vector3 tangent = motion == Motion.Slide
                ? slideEnd - slideStart
                : Vector3.Cross(worldHingeAxis, Vector3.ProjectOnPlane(SocketPosition - transform.position, worldHingeAxis));
            if (tangent.sqrMagnitude < 0.000001f) return Vector3.zero;
            tangent.Normalize();

            Vector3 flat = Vector3.ProjectOnPlane(tangent, Vector3.up);
            return flat.magnitude >= 0.3f ? flat.normalized : Vector3.up * Mathf.Sign(tangent.y);
        }

        /// <summary>How far along the constraint (+1 = forwards) the current input pushes, -1..1.</summary>
        float ControlAmount(Vector3 right, Vector3 forward)
        {
            if (!IsHeld || controlAxis == Vector3.zero) return 0f;
            bool vertical = Mathf.Abs(controlAxis.y) > 0.5f;
            Vector3 input = vertical ? Vector3.up * steerInput.y : right * steerInput.x + forward * steerInput.y;
            return Mathf.Clamp(Vector3.Dot(input, controlAxis), -1f, 1f);
        }

        public void ServerRelease(ulong clientId)
        {
            if (!IsServer || holder.Value != clientId) return;
            holder.Value = NoHolder;
            steerInput = Vector2.zero;
            fallSpeed = 0f;
        }

        /// <summary>Move input (stick/WASD) plus the holder's camera yaw, so "right" means the holder's screen right.</summary>
        public void ServerSetInput(ulong clientId, Vector2 move, float cameraYaw)
        {
            if (!IsServer || holder.Value != clientId) return;
            steerInput = Vector2.ClampMagnitude(move, 1f);
            steerYaw = cameraYaw;
        }

        // ---------- Simulation ----------

        void Update()
        {
            if (IsServer && IsSpawned) ServerTick(Time.deltaTime);
            UpdateBeam();
        }

        void ServerTick(float dt)
        {
            if (IsHeld && !HolderStillValid()) ServerRelease(holder.Value);

            // Steering is relative to the holder's camera, like walking.
            Quaternion yaw = Quaternion.Euler(0f, steerYaw, 0f);
            Vector3 right = yaw * Vector3.right;
            Vector3 forward = yaw * Vector3.forward;

            switch (motion)
            {
                case Motion.Slide: TickSlide(ControlAmount(right, forward), dt); break;
                case Motion.Hinge: TickHinge(ControlAmount(right, forward), dt); break;
                case Motion.Carry: TickCarry(right * steerInput.x + forward * steerInput.y, dt); break;
            }
        }

        void TickSlide(float push, float dt)
        {
            Vector3 track = slideEnd - slideStart;
            float length = track.magnitude;
            if (length < 0.001f) return;

            if (IsHeld) slideProgress += push * moveSpeed * dt / length;
            else if (onRelease == ReleaseBehaviour.ReturnToStart) slideProgress = Mathf.MoveTowards(slideProgress, 0f, returnSpeed * dt / length);

            slideProgress = Mathf.Clamp01(slideProgress);
            transform.position = Vector3.Lerp(slideStart, slideEnd, slideProgress);
        }

        void TickHinge(float push, float dt)
        {
            Vector3 arm = Vector3.ProjectOnPlane(SocketPosition - transform.position, worldHingeAxis);
            float radius = Mathf.Max(0.5f, arm.magnitude);
            float degreesPerMeter = Mathf.Rad2Deg / radius;

            if (IsHeld)
            {
                hingeAngle += push * moveSpeed * degreesPerMeter * dt;
            }
            else if (onRelease == ReleaseBehaviour.ReturnToStart)
            {
                hingeAngle = Mathf.MoveTowards(hingeAngle, Mathf.Clamp(0f, minAngle, maxAngle), returnSpeed * degreesPerMeter * dt);
            }

            hingeAngle = Mathf.Clamp(hingeAngle, minAngle, maxAngle);
            transform.rotation = Quaternion.AngleAxis(hingeAngle, worldHingeAxis) * baseRotation;
        }

        void TickCarry(Vector3 flatSteer, float dt)
        {
            if (IsHeld)
            {
                Vector3 step = ClampToCarryRange(flatSteer * (moveSpeed * dt));
                step.y = Mathf.Clamp(carryTargetY - transform.position.y, -moveSpeed * dt, moveSpeed * dt);
                transform.position += Sweep(step);
            }
            else if (onRelease == ReleaseBehaviour.ReturnToStart && (transform.position - slideStart).sqrMagnitude > 0.0001f)
            {
                transform.position = Vector3.MoveTowards(transform.position, slideStart, returnSpeed * dt);
            }
            else
            {
                // Dropped: fall until something is below.
                fallSpeed += gravity * dt;
                Vector3 fall = Sweep(Vector3.down * (fallSpeed * dt));
                if (fall.sqrMagnitude < 0.000001f) fallSpeed = 0f;
                transform.position += fall;
            }
        }

        Vector3 Sweep(Vector3 step) => ObjectSweep.Sweep(carryCollider, transform, step, carryCollisionMask);

        /// <summary>Shortens a flat carry step so it doesn't leave <see cref="carryRange"/> around the start.</summary>
        Vector3 ClampToCarryRange(Vector3 step)
        {
            if (carryRange.x <= 0f && carryRange.y <= 0f) return step;
            Vector3 local = Quaternion.Inverse(baseRotation) * (transform.position + step - slideStart);
            if (carryRange.x > 0f) local.x = Mathf.Clamp(local.x, -carryRange.x, carryRange.x);
            if (carryRange.y > 0f) local.z = Mathf.Clamp(local.z, -carryRange.y, carryRange.y);
            Vector3 clamped = slideStart + baseRotation * local - transform.position;
            clamped.y = step.y;
            return clamped;
        }

        /// <summary>Server: drop whoever holds this (e.g. a co-op obstacle that just got torn apart).</summary>
        public void ServerForceRelease()
        {
            if (!IsServer || !IsHeld) return;
            ServerRelease(holder.Value);
        }

        bool HolderStillValid()
        {
            foreach (var player in PlayerHealth.All)
            {
                if (player.OwnerClientId != holder.Value) continue;
                return player.IsAlive && (player.transform.position - SocketPosition).sqrMagnitude <= maxHoldDistance * maxHoldDistance;
            }
            return false; // holder left the game
        }

        // ---------- Beam (every client) ----------

        void UpdateBeam()
        {
            Transform wrench = IsHeld ? FindHolderWrench() : null;
            if (wrench == null)
            {
                if (beam != null) beam.gameObject.SetActive(false);
                return;
            }

            if (beam == null) beam = CreateBeam();
            beam.gameObject.SetActive(true);

            Vector3 from = wrench.position + wrench.forward * 0.95f;
            Vector3 to = SocketPosition;
            Vector3 delta = to - from;
            float width = beamWidth * (1f + 0.35f * Mathf.Sin(Time.time * 25f));
            beam.SetPositionAndRotation(from + delta * 0.5f, Quaternion.LookRotation(delta.sqrMagnitude > 0.0001f ? delta : Vector3.forward));
            beam.localScale = new Vector3(width, width, delta.magnitude);
        }

        Transform FindHolderWrench()
        {
            foreach (var player in PlayerHealth.All)
                if (player.OwnerClientId == holder.Value && player.TryGetComponent(out PlayerMelee melee))
                    return melee.WeaponPivot;
            return null;
        }

        Transform CreateBeam()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "GrabBeam";
            Destroy(go.GetComponent<Collider>());
            var renderer = go.GetComponent<Renderer>();
            if (beamMaterial != null) renderer.sharedMaterial = beamMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        public override void OnDestroy()
        {
            if (beam != null) Destroy(beam.gameObject);
            base.OnDestroy();
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.8f, 0.3f, 1f);
            if (motion == Motion.Slide)
            {
                Vector3 start = Application.isPlaying ? slideStart : transform.position;
                Vector3 end = Application.isPlaying ? slideEnd : transform.position + transform.rotation * slideOffset;
                Gizmos.DrawLine(start, end);
                Gizmos.DrawWireSphere(end, 0.3f);
            }
            else if (motion == Motion.Hinge)
            {
                Vector3 axis = transform.rotation * hingeAxis.normalized;
                Gizmos.DrawLine(transform.position - axis * 2f, transform.position + axis * 2f);
            }
            else if (motion == Motion.Carry && (carryRange.x > 0f || carryRange.y > 0f))
            {
                // Where its centre can go.
                Vector3 start = Application.isPlaying ? slideStart : transform.position;
                Gizmos.matrix = Matrix4x4.TRS(start, Application.isPlaying ? baseRotation : transform.rotation, Vector3.one);
                Gizmos.DrawWireCube(Vector3.zero, new Vector3(carryRange.x * 2f, 0.05f, carryRange.y * 2f));
            }
        }
    }
}
