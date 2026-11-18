using UnityEngine;

namespace CoopGame
{
    /// <summary>Keeps turning its transform around a local axis (turbine rotors, fans). Purely visual, not networked.</summary>
    public class Spinner : MonoBehaviour
    {
        [SerializeField] Vector3 axis = Vector3.right;
        [SerializeField] float degreesPerSecond = 90f;
        [Tooltip("Turn from the start (e.g. a water wheel) instead of waiting for StartRotate().")]
        [SerializeField] bool spinOnStart;
        public bool IsWingSpinning { get; private set; } = false;

        public void Setup(Vector3 localAxis, float speed, bool spinFromStart = false) { axis = localAxis; degreesPerSecond = speed; spinOnStart = spinFromStart; }

        void Start() { if (spinOnStart) IsWingSpinning = true; }

        void Update()
        {
            if (!IsWingSpinning) return;
            transform.Rotate(axis, degreesPerSecond * Time.deltaTime, Space.Self);
        }


        public void StartRotate()
        {
            IsWingSpinning = true;
        }
    }
}
