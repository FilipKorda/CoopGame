using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Gentle rocking on the water (a boat): bobs and tilts this object a little around where it sits under its
    /// parent, so whatever moves the parent (a cutscene) still decides where it is. Put what floats (hull, deck,
    /// spots to stand on) under this object.
    /// </summary>
    public class FloatingBob : MonoBehaviour
    {
        [Tooltip("Metres up and down.")]
        [SerializeField] float height = 0.06f;
        [Tooltip("Seconds for one bob.")]
        [SerializeField] float period = 2.8f;
        [Tooltip("Degrees of side-to-side roll.")]
        [SerializeField] float roll = 1.6f;
        [Tooltip("Degrees of front-to-back pitch.")]
        [SerializeField] float pitch = 0.8f;

        Vector3 basePosition;
        Quaternion baseRotation;

        void Awake()
        {
            basePosition = transform.localPosition;
            baseRotation = transform.localRotation;
        }

        void Update()
        {
            float t = Time.time * Mathf.PI * 2f / Mathf.Max(0.1f, period);
            transform.localPosition = basePosition + Vector3.up * (Mathf.Sin(t) * height);
            transform.localRotation = baseRotation
                * Quaternion.Euler(Mathf.Sin(t * 0.77f + 1.3f) * pitch, 0f, Mathf.Sin(t * 0.61f) * roll);
        }
    }
}
