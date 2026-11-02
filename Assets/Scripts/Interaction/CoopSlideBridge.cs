using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// A bridge two players pull out together with their wrench beams. It slides along its track (local
    /// <see cref="slideOffset"/>, like the beam slide bridge) only while every handle is held, by the holders' steering
    /// along the track — and only outwards: it's a ratchet, it never slides back, let go and it stays where it is.
    /// Pulled all the way it locks for good: the beams drop and the handles can't be grabbed any more. With one
    /// holder it just strains and shakes. Server moves it; a server-authoritative NetworkTransform replicates it, and
    /// players standing on it ride along.
    /// </summary>
    public class CoopSlideBridge : NetworkBehaviour
    {
        [SerializeField] Graspable[] handles;
        [Tooltip("Track from the start position to fully pulled out, in the bridge's local axes.")]
        [SerializeField] Vector3 slideOffset = new Vector3(0f, 0f, 9f);
        [SerializeField] float moveSpeed = 2f;

        [Header("Visuals")]
        [Tooltip("Shakes when someone pulls alone.")]
        [SerializeField] Transform shakeVisual;
        [SerializeField] Renderer[] lamps;
        [SerializeField] Color movableColor = new Color(1f, 0.7f, 0.15f);
        [SerializeField] Color lockedColor = new Color(0.3f, 1f, 0.4f);

        readonly NetworkVariable<bool> locked = new();

        Vector3 start, end;
        float progress;
        Vector3 shakeRestPosition;
        Material[] lampMaterials;
        Targetable[] handleTargets;

        public bool IsLocked => locked.Value;

        void Awake()
        {
            start = transform.position;
            end = start + transform.rotation * slideOffset;
            if (shakeVisual != null) shakeRestPosition = shakeVisual.localPosition;
            lampMaterials = new Material[lamps.Length];
            for (int i = 0; i < lamps.Length; i++) lampMaterials[i] = lamps[i].material;
            handleTargets = new Targetable[handles.Length];
            for (int i = 0; i < handles.Length; i++) handleTargets[i] = handles[i] != null ? handles[i].GetComponent<Targetable>() : null;
        }

        void Update()
        {
            if (IsServer && IsSpawned && !locked.Value) ServerTick(Time.deltaTime);
            UpdateVisuals();
        }

        void ServerTick(float dt)
        {
            if (HeldCount() < handles.Length) return;

            Vector3 track = end - start;
            float length = track.magnitude;
            if (length < 0.001f) return;

            Vector3 steer = Vector3.zero;
            foreach (var handle in handles) steer += handle.SteerWorld;
            steer /= handles.Length; // pulling against each other cancels out
            float push = Vector3.Dot(steer, track / length);
            if (push <= 0f) return; // it only ever goes out

            progress = Mathf.Min(1f, progress + push * moveSpeed * dt / length);
            transform.position = Vector3.Lerp(start, end, progress);
            if (progress >= 1f)
            {
                locked.Value = true;
                foreach (var handle in handles)
                    if (handle != null) handle.ServerForceRelease();
            }
        }

        int HeldCount()
        {
            int held = 0;
            foreach (var handle in handles)
                if (handle != null && handle.IsHeld) held++;
            return held;
        }

        void UpdateVisuals()
        {
            bool done = locked.Value;
            foreach (var material in lampMaterials) material.color = done ? lockedColor : movableColor;
            // Nothing left to pull: the handles can't be aimed at.
            foreach (var target in handleTargets)
                if (target != null) target.enabled = !done;

            if (shakeVisual == null) return;
            int held = HeldCount();
            bool straining = !done && held > 0 && held < handles.Length;
            shakeVisual.localPosition = shakeRestPosition + (straining ? Random.insideUnitSphere * 0.03f : Vector3.zero);
        }

        public override void OnDestroy()
        {
            if (lampMaterials != null)
                foreach (var material in lampMaterials)
                    if (material != null) Destroy(material);
            base.OnDestroy();
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.8f, 0.3f, 1f);
            Vector3 from = Application.isPlaying ? start : transform.position;
            Vector3 to = Application.isPlaying ? end : transform.position + transform.rotation * slideOffset;
            Gizmos.DrawLine(from, to);
            Gizmos.DrawWireSphere(to, 0.3f);
        }
    }
}
