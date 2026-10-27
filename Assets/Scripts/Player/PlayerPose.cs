using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// The one place that bends the placeholder body: squashed when crouching, tipped over when downed,
    /// lying flat while swimming. Reads only replicated state, so every client shows the same pose.
    /// </summary>
    [RequireComponent(typeof(PlayerMovement))]
    public class PlayerPose : MonoBehaviour
    {
        [Tooltip("Root of the placeholder meshes (pivot at the feet).")]
        [SerializeField] Transform visualRoot;
        [SerializeField] float crouchScale = 0.6f;
        [SerializeField] float blendSpeed = 4f;

        [Header("Downed")]
        [SerializeField] float downedTilt = 90f;
        [SerializeField] float downedLift = 0.45f;

        [Header("Swimming")]
        [Tooltip("Body angle while floating on the surface (head up a little).")]
        [SerializeField] float surfaceSwimTilt = 70f;
        [SerializeField] float surfaceSwimLift = 1.15f;
        [SerializeField] float underwaterTilt = 85f;
        [SerializeField] float underwaterLift = 1f;

        PlayerMovement movement;
        PlayerHealth health;
        float tilt;
        float lift;

        void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            health = GetComponent<PlayerHealth>();
        }

        void Update()
        {
            if (visualRoot == null) return;

            float targetTilt = 0f, targetLift = 0f;
            if (health != null && health.IsDowned)
            {
                targetTilt = downedTilt;
                targetLift = downedLift;
            }
            else
            {
                switch (movement.SwimStateForVisuals)
                {
                    case PlayerMovement.SwimMode.Surface:
                        targetTilt = surfaceSwimTilt;
                        targetLift = surfaceSwimLift;
                        break;
                    case PlayerMovement.SwimMode.Underwater:
                        targetTilt = underwaterTilt;
                        targetLift = underwaterLift;
                        break;
                }
            }

            tilt = Mathf.MoveTowards(tilt, targetTilt, 90f * blendSpeed * Time.deltaTime);
            lift = Mathf.MoveTowards(lift, targetLift, blendSpeed * Time.deltaTime);
            visualRoot.localRotation = Quaternion.Euler(tilt, 0f, 0f);
            visualRoot.localPosition = Vector3.up * lift;

            Vector3 scale = visualRoot.localScale;
            scale.y = Mathf.MoveTowards(scale.y, movement.IsCrouchingForVisuals ? crouchScale : 1f, 6f * Time.deltaTime);
            visualRoot.localScale = scale;
        }
    }
}
