using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace CoopGame
{
    /// <summary>
    /// Fires once every listed <see cref="BeamSwitch"/> is on at the same moment (two buttons pressed together,
    /// two levers pulled together), then opens its <see cref="MechanismMover"/>s for good. Server-side logic;
    /// the movers replicate through their NetworkTransforms and the switch lamps through their own state.
    /// </summary>
    public class CoopMechanism : NetworkBehaviour
    {
        [SerializeField] BeamSwitch[] switches;
        [SerializeField] MechanismMover[] movers;
        [Tooltip("Server: also runs when it fires (e.g. a cutscene's Play).")]
        [SerializeField] UnityEvent onActivated = new();

        readonly NetworkVariable<bool> activated = new();

        public bool IsActivated => activated.Value;

        void Update()
        {
            if (!IsServer || !IsSpawned || activated.Value || switches == null || switches.Length == 0) return;

            foreach (var beamSwitch in switches)
                if (beamSwitch == null || !beamSwitch.IsOn) return;

            activated.Value = true;
            foreach (var beamSwitch in switches) beamSwitch.MarkDone();
            if (movers != null)
                foreach (var mover in movers)
                    if (mover != null) mover.Open();
            onActivated?.Invoke();
        }
    }
}
