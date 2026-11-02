using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// A barrier clamped shut by a lock: two halves, each with a handle (Fixed <see cref="Graspable"/>). One player
    /// can't move it. When both handles are held and both players pull their half away (move key away from the
    /// middle or back towards themselves), tension builds; held long enough, the lock snaps and the halves fly apart,
    /// leaving the way open for good. Letting go bleeds the tension off again.
    /// </summary>
    public class TearableBarrier : NetworkBehaviour
    {
        [SerializeField] Graspable leftHandle;
        [SerializeField] Graspable rightHandle;
        [SerializeField] Transform leftHalf;
        [SerializeField] Transform rightHalf;
        [SerializeField] Transform lockVisual;
        [Tooltip("Seconds of pulling together needed to tear it apart.")]
        [SerializeField] float tearTime = 1.5f;
        [SerializeField] float tensionDecay = 1.5f;
        [Tooltip("How far each half gives way while under tension, metres.")]
        [SerializeField] float maxStretch = 0.35f;
        [Tooltip("How much of a pull (0..1) counts.")]
        [SerializeField] float pullThreshold = 0.4f;

        readonly NetworkVariable<float> tension = new();
        readonly NetworkVariable<bool> broken = new();

        Vector3 leftRest, rightRest;
        float brokenTime = -1f;

        public bool IsBroken => broken.Value;

        void Awake()
        {
            leftRest = leftHalf.localPosition;
            rightRest = rightHalf.localPosition;
        }

        public override void OnNetworkSpawn()
        {
            broken.OnValueChanged += OnBrokenChanged;
            if (broken.Value) ApplyBroken(instant: true);
        }

        public override void OnNetworkDespawn()
        {
            broken.OnValueChanged -= OnBrokenChanged;
        }

        void Update()
        {
            if (IsServer && IsSpawned && !broken.Value) ServerTick(Time.deltaTime);
            UpdateVisuals();
        }

        void ServerTick(float dt)
        {
            bool pulling = IsPulling(leftHandle, leftHalf) && IsPulling(rightHandle, rightHalf);
            tension.Value = Mathf.Clamp01(tension.Value + (pulling ? dt / tearTime : -tensionDecay * dt));
            if (tension.Value < 1f) return;

            // Let go first: setting broken switches the handles off right away (host runs OnBrokenChanged now).
            leftHandle.ServerForceRelease();
            rightHandle.ServerForceRelease();
            broken.Value = true;
        }

        bool IsPulling(Graspable handle, Transform half)
        {
            if (handle == null || !handle.IsHeld) return false;
            Vector3 steer = handle.SteerWorld;
            Vector3 outward = Vector3.ProjectOnPlane(half.position - transform.position, Vector3.up).normalized;
            float pull = Vector3.Dot(steer, outward);

            var holder = handle.Holder;
            if (holder != null)
            {
                Vector3 towardHolder = Vector3.ProjectOnPlane(holder.transform.position - handle.SocketPosition, Vector3.up).normalized;
                pull = Mathf.Max(pull, Vector3.Dot(steer, towardHolder));
            }
            return pull >= pullThreshold;
        }

        void OnBrokenChanged(bool previous, bool current)
        {
            if (current) ApplyBroken(instant: false);
        }

        void ApplyBroken(bool instant)
        {
            brokenTime = instant ? Time.time - 10f : Time.time;
            foreach (var collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
            foreach (var handle in new[] { leftHandle, rightHandle })
            {
                if (handle == null) continue;
                if (handle.TryGetComponent(out Targetable targetable)) targetable.enabled = false;
                handle.enabled = false;
                foreach (var renderer in handle.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
            }
            if (lockVisual != null) lockVisual.gameObject.SetActive(false);
        }

        void UpdateVisuals()
        {
            Vector3 leftOut = (leftRest - rightRest).normalized;
            if (brokenTime < 0f)
            {
                // Strain: the halves give a little and rattle as tension builds.
                float t = tension.Value;
                Vector3 jitter = t > 0f ? Random.insideUnitSphere * 0.02f * t : Vector3.zero;
                leftHalf.localPosition = leftRest + leftOut * (maxStretch * t) + jitter;
                rightHalf.localPosition = rightRest - leftOut * (maxStretch * t) - jitter;
                if (lockVisual != null) lockVisual.localScale = Vector3.one * (1f + 0.25f * t);
                return;
            }

            // Torn: the halves are flung apart and sink away, then disappear.
            float age = Time.time - brokenTime;
            float fling = Mathf.Min(age, 1f);
            Vector3 drop = Vector3.down * (3f * Mathf.Max(0f, age - 0.4f) * Mathf.Max(0f, age - 0.4f));
            leftHalf.localPosition = leftRest + leftOut * (2.5f * fling) + drop;
            rightHalf.localPosition = rightRest - leftOut * (2.5f * fling) + drop;
            if (age > 1.5f)
            {
                leftHalf.gameObject.SetActive(false);
                rightHalf.gameObject.SetActive(false);
            }
        }
    }
}
