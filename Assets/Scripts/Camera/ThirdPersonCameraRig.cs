using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoopGame
{
    /// <summary>
    /// Orbit pivot for the local player's camera. A Cinemachine camera with Third Person Follow
    /// tracks this transform, so its rotation is the camera's rotation and its position is the
    /// point the camera orbits. Lives in the level scene (not networked); the local player
    /// registers itself on spawn.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class ThirdPersonCameraRig : MonoBehaviour
    {
        public static ThirdPersonCameraRig Instance { get; private set; }

        [Header("Follow")]
        [SerializeField] float pivotHeight = 1.5f;
        [Tooltip("How fast the pivot catches up horizontally.")]
        [SerializeField] float horizontalSharpness = 25f;
        [Tooltip("Lower than horizontal so jumps don't jerk the camera.")]
        [SerializeField] float verticalSharpness = 6f;

        [Header("Look")]
        [Tooltip("Degrees per pixel of mouse movement.")]
        [SerializeField] float mouseSensitivity = 0.12f;
        [Tooltip("Degrees per second at full stick deflection.")]
        [SerializeField] float gamepadSensitivity = 200f;
        [SerializeField] bool invertY;
        [SerializeField] float minPitch = -35f;
        [SerializeField] float maxPitch = 70f;
        [SerializeField] float defaultPitch = 12f;

        [Header("Auto Recenter")]
        [Tooltip("Drift behind the player while running and not touching the look input.")]
        [SerializeField] bool autoRecenter = true;
        [SerializeField] float recenterDelay = 1.2f;
        [SerializeField] float recenterSpeed = 70f;
        [Tooltip("Don't swing around when the player runs towards the camera.")]
        [SerializeField] float recenterMaxAngle = 110f;

        [Header("Aim Camera")]
        [Tooltip("Third Person Follow on the player camera; found automatically if empty.")]
        [SerializeField] CinemachineThirdPersonFollow follow;
        [Tooltip("Over-the-right-shoulder offset while aiming, so the player doesn't cover the crosshair.")]
        [SerializeField] float aimShoulderX = 1.3f;
        [SerializeField] float aimDistance = 4.5f;
        [SerializeField] float aimBlendSpeed = 6f;

        [Header("Zipline Camera")]
        [Tooltip("Riding a zipline the camera moves this far to the right and down (metres), so the cable above the " +
                 "player runs across the top of the view instead of straight through the middle.")]
        [SerializeField] float ziplineShoulderX = 1.4f;
        [SerializeField] float ziplineDrop = 1.1f;
        [SerializeField] float ziplineDistance = 7f;

        PlayerMovement target;
        bool menuWasOpen;
        float aimBlend, ziplineBlend;
        float baseShoulderX, baseShoulderY;
        float baseDistance;
        InputAction lookAction;
        float yaw;
        float pitch;
        float lastLookInputTime;
        Vector3 followPosition;
        float shakeStrength;
        float shakeDuration;
        float shakeUntil;

        public PlayerMovement Target => target;
        public Vector3 PlanarForward => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        public Vector3 PlanarRight => Quaternion.Euler(0f, yaw, 0f) * Vector3.right;

        void Awake()
        {
            Instance = this;
            yaw = transform.eulerAngles.y;
            pitch = defaultPitch;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void Start()
        {
            lookAction = InputSystem.actions.FindAction("Player/Look", true);
            if (follow == null) follow = FindFirstObjectByType<CinemachineThirdPersonFollow>();
            if (follow != null)
            {
                // Cinemachine mirrors the shoulder offset by CameraSide (0.5 cancels it out entirely); at 1 the
                // offset's x is simply metres to the right, which the aim blend below relies on.
                follow.CameraSide = 1f;
                baseShoulderX = follow.ShoulderOffset.x;
                baseShoulderY = follow.ShoulderOffset.y;
                baseDistance = follow.CameraDistance;
            }
        }

        void UpdateAimCamera(float dt)
        {
            if (follow == null) return;
            bool aiming = PlayerTargeting.Local != null && PlayerTargeting.Local.IsAiming;
            aimBlend = Mathf.MoveTowards(aimBlend, aiming ? 1f : 0f, aimBlendSpeed * dt);

            bool ziplining = target != null && target.IsZiplining;
            ziplineBlend = Mathf.MoveTowards(ziplineBlend, ziplining ? 1f : 0f, 1.5f * dt);
            float zip = Mathf.SmoothStep(0f, 1f, ziplineBlend);

            Vector3 shoulder = follow.ShoulderOffset;
            shoulder.x = Mathf.Lerp(Mathf.Lerp(baseShoulderX, aimShoulderX, aimBlend), ziplineShoulderX, zip);
            shoulder.y = baseShoulderY - ziplineDrop * zip;
            follow.ShoulderOffset = shoulder;
            follow.CameraDistance = Mathf.Lerp(Mathf.Lerp(baseDistance, aimDistance, aimBlend), ziplineDistance, zip);
        }

        public void SetTarget(PlayerMovement player)
        {
            target = player;
            SnapBehindTarget();
            SetCursorLocked(true);
        }

        public void ClearTarget(PlayerMovement player)
        {
            if (target == player) target = null;
        }

        public void SnapBehindTarget()
        {
            if (target == null) return;
            yaw = target.transform.eulerAngles.y;
            pitch = defaultPitch;
            followPosition = PivotPosition();
            transform.SetPositionAndRotation(followPosition, Quaternion.Euler(pitch, yaw, 0f));
        }

        /// <summary>Short random jolt of the orbit pivot (e.g. a slam impact). Stronger calls override weaker ones.</summary>
        public void AddShake(float strength, float duration)
        {
            if (Time.time < shakeUntil && strength < CurrentShake()) return;
            shakeStrength = strength;
            shakeDuration = duration;
            shakeUntil = Time.time + duration;
        }

        float CurrentShake() => Time.time < shakeUntil ? shakeStrength * (shakeUntil - Time.time) / shakeDuration : 0f;

        void Update()
        {
            UpdateCursorLock();
        }

        void LateUpdate()
        {
            if (target == null) return;
            float dt = Time.deltaTime;

            ReadLookInput(dt);
            AutoRecenter(dt);
            UpdateAimCamera(dt);

            Vector3 desired = PivotPosition();
            float h = 1f - Mathf.Exp(-horizontalSharpness * dt);
            float v = 1f - Mathf.Exp(-verticalSharpness * dt);
            followPosition = new Vector3(
                Mathf.Lerp(followPosition.x, desired.x, h),
                Mathf.Lerp(followPosition.y, desired.y, v),
                Mathf.Lerp(followPosition.z, desired.z, h));

            // Shake sits on top of the smoothed follow position so it never drifts the camera.
            Vector3 shake = Random.insideUnitSphere * CurrentShake();
            transform.SetPositionAndRotation(followPosition + shake, Quaternion.Euler(pitch, yaw, 0f));
        }

        Vector3 PivotPosition() => target.transform.position + Vector3.up * pivotHeight;

        void ReadLookInput(float dt)
        {
            if (lookAction == null || GameplayUI.IsMenuOpen) return;
            Vector2 look = lookAction.ReadValue<Vector2>();
            if (look.sqrMagnitude < 0.0001f) return;

            bool fromPointer = lookAction.activeControl?.device is Pointer;
            if (fromPointer)
            {
                // Mouse look only while the cursor is captured, so UI clicks don't spin the camera.
                if (Cursor.lockState != CursorLockMode.Locked) return;
                look *= mouseSensitivity;
            }
            else
            {
                look *= gamepadSensitivity * dt;
            }

            yaw += look.x;
            pitch += invertY ? look.y : -look.y;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            lastLookInputTime = Time.time;
        }

        void AutoRecenter(float dt)
        {
            if (!autoRecenter || Time.time - lastLookInputTime < recenterDelay) return;
            // While aiming the player strafes; swinging the camera after the movement would ruin the aim.
            if (PlayerTargeting.Local != null && PlayerTargeting.Local.IsAiming) return;

            Vector3 velocity = target.PlanarVelocity;
            if (velocity.sqrMagnitude < 1f) return;

            float targetYaw = Quaternion.LookRotation(velocity, Vector3.up).eulerAngles.y;
            if (Mathf.Abs(Mathf.DeltaAngle(yaw, targetYaw)) > recenterMaxAngle) return;

            yaw = Mathf.MoveTowardsAngle(yaw, targetYaw, recenterSpeed * dt);
        }

        void UpdateCursorLock()
        {
            if (target == null) return;

            // A menu needs a free cursor; take it back once the menu closes.
            if (GameplayUI.IsMenuOpen)
            {
                if (Cursor.lockState != CursorLockMode.None) SetCursorLocked(false);
                menuWasOpen = true;
                return;
            }
            if (menuWasOpen)
            {
                menuWasOpen = false;
                SetCursorLocked(true);
                return;
            }

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                SetCursorLocked(false);
            else if (Cursor.lockState != CursorLockMode.Locked && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                SetCursorLocked(true);
        }

        static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
