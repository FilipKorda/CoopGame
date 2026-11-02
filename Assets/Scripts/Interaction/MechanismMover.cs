using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Something a <see cref="CoopMechanism"/> moves when it fires: a bridge sliding out, a gate rising.
    /// The server animates it from its start pose to the open pose; a server-authoritative NetworkTransform
    /// replicates it, and players standing on it ride along.
    /// </summary>
    public class MechanismMover : NetworkBehaviour
    {
        [Tooltip("Open position relative to the start, in the object's local axes.")]
        [SerializeField] Vector3 openOffset = new Vector3(0f, 3f, 0f);
        [Tooltip("Extra rotation when open (local euler degrees).")]
        [SerializeField] Vector3 openRotation;
        [Tooltip("Seconds from closed to open.")]
        [SerializeField] float openTime = 2f;

        Vector3 closedPosition, openPosition;
        Quaternion closedRotation, openRotationWorld;
        float progress;
        bool opening;

        void Awake()
        {
            closedPosition = transform.position;
            closedRotation = transform.rotation;
            openPosition = closedPosition + closedRotation * openOffset;
            openRotationWorld = closedRotation * Quaternion.Euler(openRotation);
        }

        /// <summary>Server only.</summary>
        public void Open() => opening = true;

        void Update()
        {
            if (!IsServer || !IsSpawned || !opening || progress >= 1f) return;
            progress = Mathf.Min(1f, progress + Time.deltaTime / Mathf.Max(0.01f, openTime));
            float eased = Mathf.SmoothStep(0f, 1f, progress);
            transform.SetPositionAndRotation(Vector3.Lerp(closedPosition, openPosition, eased),
                Quaternion.Slerp(closedRotation, openRotationWorld, eased));
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Vector3 start = Application.isPlaying ? closedPosition : transform.position;
            Vector3 end = Application.isPlaying ? openPosition : transform.position + transform.rotation * openOffset;
            Gizmos.DrawLine(start, end);
            Gizmos.DrawWireCube(end, Vector3.one * 0.4f);
        }
    }
}
