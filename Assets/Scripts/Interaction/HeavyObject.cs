using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// A block too heavy for one wrench beam. It has several handles (Fixed <see cref="Graspable"/>s on child
    /// objects); it only moves while every handle is held, sliding in the average of the holders' steering, so the
    /// players have to pull the same way. With one holder it just strains and shakes. Server moves it; a
    /// server-authoritative NetworkTransform on this object replicates it.
    /// </summary>
    public class HeavyObject : NetworkBehaviour
    {
        [SerializeField] Graspable[] handles;
        [SerializeField] float moveSpeed = 1.6f;
        [SerializeField] float gravity = 25f;
        [SerializeField] LayerMask collisionMask = ~0;
        [Tooltip("Mesh that shakes when someone tries to move it alone.")]
        [SerializeField] Transform shakeVisual;
        [Tooltip("Lifted this much (metres) while every handle is held, so it can be carried onto things; 0 = slides along the ground.")]
        [SerializeField] float carryLift;

        BoxCollider body;
        float fallSpeed;
        Vector3 shakeRestPosition;
        bool lifted;
        float liftTargetY;

        void Awake()
        {
            body = GetComponentInChildren<BoxCollider>();
            if (shakeVisual != null) shakeRestPosition = shakeVisual.localPosition;
        }

        void Update()
        {
            if (IsServer && IsSpawned) ServerTick(Time.deltaTime);
            UpdateShake();
        }

        void ServerTick(float dt)
        {
            // Sweep from the un-shaken pose; the shake is only for show and must not let the block sink.
            if (shakeVisual != null) shakeVisual.localPosition = shakeRestPosition;

            if (HeldCount() == handles.Length)
            {
                Vector3 steer = Vector3.zero;
                foreach (var handle in handles) steer += handle.SteerWorld;
                steer /= handles.Length; // pulling against each other cancels out
                transform.position += ObjectSweep.Sweep(body, transform, steer * (moveSpeed * dt), collisionMask);

                if (carryLift > 0f)
                {
                    if (!lifted) liftTargetY = transform.position.y + carryLift;
                    lifted = true;
                    float rise = Mathf.Clamp(liftTargetY - transform.position.y, -moveSpeed * dt, moveSpeed * dt);
                    transform.position += ObjectSweep.Sweep(body, transform, Vector3.up * rise, collisionMask);
                    fallSpeed = 0f;
                    return;
                }
            }
            lifted = false;

            fallSpeed += gravity * dt;
            Vector3 fall = ObjectSweep.Sweep(body, transform, Vector3.down * (fallSpeed * dt), collisionMask);
            if (fall.sqrMagnitude < 0.000001f) fallSpeed = 0f;
            transform.position += fall;
        }

        int HeldCount()
        {
            int held = 0;
            foreach (var handle in handles)
                if (handle != null && handle.IsHeld) held++;
            return held;
        }

        void UpdateShake()
        {
            if (shakeVisual == null) return;
            int held = HeldCount();
            bool straining = held > 0 && held < handles.Length;
            shakeVisual.localPosition = shakeRestPosition + (straining ? Random.insideUnitSphere * 0.03f : Vector3.zero);
        }
    }
}
