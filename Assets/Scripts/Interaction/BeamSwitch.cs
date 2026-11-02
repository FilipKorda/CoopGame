using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// A switch operated with the wrench beam, input for a <see cref="CoopMechanism"/>.
    /// - Button (Fixed graspable): latching the beam on presses it, but it only counts for a short moment
    ///   (<see cref="pressWindow"/>), so two players have to hit their buttons at the same time.
    /// - Lever (Hinge graspable that springs back): counts while it's pulled all the way; letting go returns it,
    ///   so two levers can only be "on" together if two players hold them.
    /// The lamp shows the state on every client: off, on, too late (button held without the partner), done.
    /// </summary>
    [RequireComponent(typeof(Graspable))]
    public class BeamSwitch : NetworkBehaviour
    {
        /// <summary>Hold: on for as long as the beam holds it (e.g. two players holding two things at once).</summary>
        public enum Kind { Button, Lever, Hold }
        enum State : byte { Off, On, TooLate, Done }

        [SerializeField] Kind kind = Kind.Button;
        [Tooltip("Button: how long a press counts as 'now' after the beam latches on.")]
        [SerializeField] float pressWindow = 0.8f;
        [Tooltip("Button: the window used instead while a player has the test option on (F10), so one person can press both buttons in two windows.")]
        [SerializeField] float testPressWindow = 10f;
        [Tooltip("Lever: how far it must be pulled (0..1 of its swing) to count.")]
        [SerializeField, Range(0.5f, 1f)] float leverThreshold = 0.95f;

        [Header("Visuals")]
        [SerializeField] Renderer lamp;
        [Tooltip("Button cap that sinks in while the beam holds it.")]
        [SerializeField] Transform buttonCap;
        [SerializeField] Vector3 pressedOffset = new Vector3(0f, 0f, 0.12f);
        [SerializeField] Color offColor = new Color(0.25f, 0.25f, 0.3f);
        [SerializeField] Color onColor = new Color(0.3f, 1f, 0.4f);
        [SerializeField] Color tooLateColor = new Color(1f, 0.25f, 0.2f);
        [SerializeField] Color doneColor = new Color(0.3f, 0.8f, 1f);

        readonly NetworkVariable<byte> state = new((byte)State.Off);

        Graspable graspable;
        bool wasHeld;
        float pressStartTime;
        Vector3 capRestPosition;
        Material lampMaterial;

        public bool IsOn => state.Value == (byte)State.On;
        /// <summary>Marked done (<see cref="MarkDone"/>): its job is over and it stays lit.</summary>
        public bool IsDone => state.Value == (byte)State.Done;

        void Awake()
        {
            graspable = GetComponent<Graspable>();
            if (buttonCap != null) capRestPosition = buttonCap.localPosition;
            if (lamp != null) lampMaterial = lamp.material;
        }

        /// <summary>Server: the mechanism fired; stay lit for good.</summary>
        public void MarkDone()
        {
            if (IsServer) state.Value = (byte)State.Done;
        }

        void Update()
        {
            if (IsServer && IsSpawned) ServerTick();
            UpdateVisuals();
        }

        void ServerTick()
        {
            if (state.Value == (byte)State.Done) return;

            bool held = graspable.IsHeld;
            if (held && !wasHeld) pressStartTime = Time.time;
            wasHeld = held;

            State next;
            if (kind == Kind.Button)
            {
                float window = PlayerDevTools.LongButtonWindow ? testPressWindow : pressWindow;
                next = !held ? State.Off : Time.time - pressStartTime <= window ? State.On : State.TooLate;
            }
            else if (kind == Kind.Hold)
                next = held ? State.On : State.Off;
            else
                next = graspable.HingeProgress >= leverThreshold ? State.On : State.Off;

            if (state.Value != (byte)next) state.Value = (byte)next;
        }

        void UpdateVisuals()
        {
            if (lampMaterial != null)
            {
                lampMaterial.color = (State)state.Value switch
                {
                    State.On => onColor,
                    State.TooLate => tooLateColor,
                    State.Done => doneColor,
                    _ => offColor,
                };
            }

            if (buttonCap != null)
            {
                Vector3 target = capRestPosition + (graspable.IsHeld ? pressedOffset : Vector3.zero);
                buttonCap.localPosition = Vector3.MoveTowards(buttonCap.localPosition, target, 2f * Time.deltaTime);
            }
        }

        public override void OnDestroy()
        {
            if (lampMaterial != null) Destroy(lampMaterial);
            base.OnDestroy();
        }
    }
}
