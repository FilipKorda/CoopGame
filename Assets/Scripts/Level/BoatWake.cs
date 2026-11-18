using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Foam behind (and at the bow of) a moving boat: the particle systems emit by how fast this object moves, so a
    /// boat lying still makes none. A jump (a cutscene starting, or skipped to its end) isn't speed.
    /// </summary>
    public class BoatWake : MonoBehaviour
    {
        [Tooltip("Foam systems (world simulation space); each emits Per Metre particles for every metre travelled.")]
        [SerializeField] ParticleSystem[] foam;
        [SerializeField] float perMetre = 6f;
        [Tooltip("Slower than this (m/s) leaves no foam.")]
        [SerializeField] float minSpeed = 0.4f;

        Vector3 lastPosition;
        bool started;

        void OnEnable() => started = false;

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Vector3 position = transform.position;
            float speed = started ? Vector3.ProjectOnPlane(position - lastPosition, Vector3.up).magnitude / dt : 0f;
            lastPosition = position;
            started = true;
            if (speed > 40f || speed < minSpeed) speed = 0f;
            foreach (var system in foam)
            {
                if (system == null) continue;
                var emission = system.emission;
                emission.rateOverTime = speed * perMetre;
            }
        }
    }
}
