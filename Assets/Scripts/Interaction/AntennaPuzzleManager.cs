using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace CoopGame
{
    public class AntennaPuzzleManager : NetworkBehaviour
    {
        [Header("Antennas")]
        [SerializeField] AntennaRig[] antennas;
        [SerializeField] Spinner[] spinner;
        [Tooltip("Server, once: every antenna points at the tower (e.g. play a cutscene).")]
        [SerializeField] UnityEvent onAntennasAligned;
        bool spinnerStarted;

        [Header("Power Levers")]
        [SerializeField] PowerLever[] powerLevers;

        [Header("Objects")]
        [SerializeField] GameObject[] objectsToShow;

        readonly NetworkVariable<bool> puzzleCompleted = new();

        public override void OnNetworkSpawn()
        {
            puzzleCompleted.OnValueChanged += OnPuzzleCompletedChanged;

            if (IsServer)
                CheckPuzzle();

            UpdateObjectVisibility(puzzleCompleted.Value);
        }

        public override void OnNetworkDespawn()
        {
            puzzleCompleted.OnValueChanged -= OnPuzzleCompletedChanged;
        }

        void Update()
        {
            if (!IsServer || puzzleCompleted.Value)
                return;

            CheckPuzzle();
        }

        void CheckPuzzle()
        {
            if (antennas != null)
            {
                foreach (var antenna in antennas)
                {
                    if (antenna == null || !antenna.IsAligned)
                        return;
                }

                if (!spinnerStarted)
                {
                    foreach (var spin in spinner)
                    {
                        spin.StartRotate();
                    }

                    spinnerStarted = true;
                    onAntennasAligned?.Invoke();
                }
            }

            if (powerLevers != null)
            {
                foreach (var powerLever in powerLevers)
                {
                    if (powerLever == null || !powerLever.IsConnected)
                        return;
                }
            }

            puzzleCompleted.Value = true;
        }

        void OnPuzzleCompletedChanged(bool previous, bool current)
        {
            UpdateObjectVisibility(current);
        }

        void UpdateObjectVisibility(bool visible)
        {
            if (objectsToShow == null)
                return;

            foreach (var obj in objectsToShow)
            {
                if (obj != null)
                    obj.SetActive(visible);
            }
        }
    }
}