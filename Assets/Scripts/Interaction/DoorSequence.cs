using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Opens a list of doors one after another by sliding them up (each by its own height, so the doorway is clear),
    /// once something calls <see cref="ServerOpen"/> (e.g. the turbine lever's On Connected). The doors themselves are
    /// plain scene objects: the server shares when the sequence started and every client slides them from that, so
    /// everyone sees the same, and a player joining later finds them already open.
    /// </summary>
    public class DoorSequence : NetworkBehaviour
    {
        [Tooltip("Opened in this order.")]
        [SerializeField] Transform[] doors;
        [Tooltip("How far each door goes up beyond its own height (metres).")]
        [SerializeField] float extraRaise = 0.1f;
        [Tooltip("Seconds for one door to slide all the way up.")]
        [SerializeField] float openTime = 1.5f;
        [Tooltip("Pause after one door is open before the next starts (seconds).")]
        [SerializeField] float pause = 0.5f;

        readonly NetworkVariable<bool> opened = new();
        readonly NetworkVariable<double> openedAt = new();

        Vector3[] closedPositions;
        float[] raise;

        public bool IsOpened => opened.Value;

        double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : Time.timeAsDouble;

        void Awake()
        {
            int count = doors != null ? doors.Length : 0;
            closedPositions = new Vector3[count];
            raise = new float[count];
            for (int i = 0; i < count; i++)
            {
                if (doors[i] == null) continue;
                closedPositions[i] = doors[i].position;
                var col = doors[i].GetComponent<Collider>();
                var rend = doors[i].GetComponent<Renderer>();
                float height = col != null ? col.bounds.size.y : rend != null ? rend.bounds.size.y : doors[i].lossyScale.y;
                raise[i] = height + extraRaise;
            }
        }

        /// <summary>Server: start opening the doors (only the first call counts).</summary>
        public void ServerOpen()
        {
            if (!IsServer || opened.Value) return;
            opened.Value = true;
            openedAt.Value = Now;
        }

        void Update()
        {
            if (!IsSpawned || !opened.Value || doors == null) return;
            float since = (float)(Now - openedAt.Value);
            for (int i = 0; i < doors.Length; i++)
            {
                if (doors[i] == null) continue;
                float t = Mathf.Clamp01((since - i * (openTime + pause)) / Mathf.Max(0.01f, openTime));
                doors[i].position = closedPositions[i] + Vector3.up * (raise[i] * Mathf.SmoothStep(0f, 1f, t));
            }
        }

        /// <summary>Editor setup.</summary>
        public void SetDoors(Transform[] value) => doors = value;

        void OnDrawGizmosSelected()
        {
            if (doors == null) return;
            Gizmos.color = new Color(0.3f, 1f, 0.5f);
            Vector3 previous = transform.position;
            foreach (var door in doors)
            {
                if (door == null) continue;
                Gizmos.DrawLine(previous, door.position);
                previous = door.position;
            }
        }
    }
}
