using UnityEngine;
using UnityEngine.InputSystem;

namespace CoopGame
{
    /// <summary>
    /// A zipline from its top end (<see cref="from"/>, by the start stand) down to its bottom end (<see cref="to"/>,
    /// by the end stand). A player standing at the top presses Interact (F) to grab the cable and slides down it
    /// (<see cref="PlayerMovement.StartZipline"/>): speeding up, braking over the last metres, letting go at the end
    /// (there's no letting go on the way). It only goes down: at the bottom it just says so. Each player rides on
    /// their own machine and the movement is shared like any other, so nothing here is networked.
    /// </summary>
    public class Zipline : MonoBehaviour
    {
        [Tooltip("The cable's top end (start) and bottom end (finish), in world space.")]
        [SerializeField] Vector3 from;
        [SerializeField] Vector3 to;
        [Tooltip("How far under the cable the player's feet hang (metres; hands on the cable).")]
        [SerializeField] float hangDepth = 2.25f;
        [Tooltip("Metres skipped at the top and the bottom, so the rider clears the stands.")]
        [SerializeField] float startOffset = 1.5f;
        [SerializeField] float endOffset = 1.5f;

        [Header("Speed")]
        [SerializeField] float startSpeed = 3f;
        [SerializeField] float acceleration = 4f;
        [SerializeField] float maxSpeed = 18f;
        [Tooltip("Over the last metres it slows down to Arrive Speed.")]
        [SerializeField] float brakeDistance = 12f;
        [SerializeField] float arriveSpeed = 3f;

        [Header("Grabbing")]
        [Tooltip("How close (metres) the player has to be to the top end to grab the cable.")]
        [SerializeField] float grabRange = 3.5f;
        [SerializeField] string grabHint = "[F] Zjedź tyrolką";
        [SerializeField] string bottomHint = "Tyrolka działa tylko w dół — zjazd zaczyna się na górze";

        InputAction interactAction;

        public Vector3 Direction => (to - from).normalized;
        public float Length => Vector3.Distance(from, to);
        public float StartDistance => Mathf.Min(startOffset, Length * 0.5f);
        public float EndDistance => Mathf.Max(StartDistance, Length - endOffset);
        public float HangDepth => hangDepth;
        public float StartSpeed => startSpeed;
        public float Acceleration => acceleration;

        public Vector3 PointAt(float distance) => from + Direction * distance;

        /// <summary>Top speed at this point: full speed, easing down to the arrival speed near the end.</summary>
        public float SpeedLimitAt(float distance) =>
            Mathf.Lerp(arriveSpeed, maxSpeed, Mathf.Clamp01((EndDistance - distance) / Mathf.Max(0.01f, brakeDistance)));

        void OnEnable()
        {
            interactAction = InputSystem.actions.FindAction("Player/Interact", true);
        }

        PlayerMovement LocalMovement
        {
            get
            {
                var player = PlayerHealth.Local;
                return player != null && player.IsAlive ? player.GetComponent<PlayerMovement>() : null;
            }
        }

        bool Near(PlayerMovement movement, Vector3 point) =>
            Vector3.Distance(movement.transform.position + Vector3.up * 1.2f, point) <= grabRange + 1.2f;

        void Update()
        {
            var movement = LocalMovement;
            if (movement == null || movement.IsZiplining || CutsceneSystem.ControlsLocked) return;
            if (interactAction != null && interactAction.WasPressedThisFrame() && Near(movement, PointAt(StartDistance)))
                movement.StartZipline(this);
        }

        void OnGUI()
        {
            if (CutsceneSystem.HidesGameplayUI) return;
            var movement = LocalMovement;
            if (movement == null) return;
            string text = null;
            if (movement.IsZiplining) return;
            if (Near(movement, PointAt(StartDistance))) text = grabHint;
            else if (Near(movement, PointAt(EndDistance))) text = bottomHint;
            if (text == null) return;
            var style = new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
            GUI.Box(new Rect(Screen.width * 0.5f - 330f, Screen.height - 190f, 660f, 44f), text, style);
        }

        /// <summary>Editor setup: the cable's top and bottom ends.</summary>
        public void SetEnds(Vector3 top, Vector3 bottom)
        {
            from = top;
            to = bottom;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 1f, 0.5f);
            Gizmos.DrawLine(from, to);
            Gizmos.DrawWireSphere(PointAt(StartDistance), grabRange);
            Gizmos.color = new Color(1f, 0.6f, 0.2f);
            Gizmos.DrawLine(PointAt(StartDistance) + Vector3.down * hangDepth, PointAt(EndDistance) + Vector3.down * hangDepth);
        }
    }
}
