using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Classic spike trap, on the floor or on a wall. A player in its line of fire (the box in front of the plate,
    /// as deep as the spikes are long) sets it off; it rattles and blinks for <see cref="armTime"/> seconds, then the
    /// spikes shoot out, hurting and throwing whoever is in the line (off a ledge too) and stay out a moment. Then it
    /// needs <see cref="cooldown"/> seconds (counted from the shot) before anything can set it off again — step out of
    /// the line, let it fire, walk through.
    /// The server runs it and does the hits; every client animates it from the replicated state and start time.
    /// </summary>
    public class SpikeTrap : NetworkBehaviour
    {
        enum State : byte { Ready, Armed, Fired }

        [Tooltip("Plate and spikes live here: its local Y is the way the spikes shoot, the plate lies in its X/Z plane. Must not be scaled. Empty = this transform.")]
        [SerializeField] Transform frame;
        [Tooltip("Plate size along the frame's local X and Z.")]
        [SerializeField] Vector2 size = new Vector2(4f, 2f);
        [Tooltip("How far the spikes reach out of the plate; also the depth of the line that sets the trap off.")]
        [SerializeField] float spikeLength = 1.3f;

        [Header("Timing")]
        [SerializeField] float armTime = 2f;
        [Tooltip("From the shot until the trap can be set off again.")]
        [SerializeField] float cooldown = 5f;
        [SerializeField] float extendTime = 0.07f;
        [SerializeField] float holdTime = 0.5f;
        [SerializeField] float retractTime = 0.4f;

        [Header("Hit")]
        [SerializeField] int damage = 2;
        [Tooltip("Floor trap: thrown sideways out of the line. Wall trap: thrown away from the wall.")]
        [SerializeField] float pushSpeed = 8f;
        [SerializeField] float liftSpeed = 7f;
        [SerializeField] float controlLock = 0.45f;

        [Header("Visuals")]
        [Tooltip("Scaled along its local Y: flat when retracted, 1 when the spikes are all the way out.")]
        [SerializeField] Transform spikes;
        [SerializeField] Renderer[] lamps;
        [SerializeField] Color readyColor = new Color(0.9f, 0.55f, 0.1f);
        [SerializeField] Color armedColor = new Color(1f, 0.1f, 0.05f);
        [SerializeField] Color cooldownColor = new Color(0.2f, 0.2f, 0.22f);

        const float RetractedScale = 0.03f;
        // Points up the player's body tested against the line, and how fat the body is around them.
        static readonly float[] BodySamples = { 0.3f, 1f, 1.7f };
        const float BodyRadius = 0.35f;

        readonly NetworkVariable<State> state = new();
        readonly NetworkVariable<double> stateStart = new();
        readonly HashSet<PlayerHealth> hitThisShot = new();
        Material[] lampMaterials;

        Transform Frame => frame != null ? frame : transform;
        double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : Time.timeAsDouble;

        void Awake()
        {
            lampMaterials = new Material[lamps.Length];
            for (int i = 0; i < lamps.Length; i++) lampMaterials[i] = lamps[i].material;
        }

        void Update()
        {
            if (!IsSpawned) return;
            float age = (float)(Now - stateStart.Value);
            if (IsServer) ServerTick(age);
            UpdateVisuals(age);
        }

        // ---------- Server ----------

        void ServerTick(float age)
        {
            switch (state.Value)
            {
                case State.Ready:
                    foreach (var player in PlayerHealth.All)
                        if (player.IsAlive && InLine(player.transform.position))
                        {
                            SetState(State.Armed);
                            break;
                        }
                    break;

                case State.Armed:
                    if (age >= armTime)
                    {
                        hitThisShot.Clear();
                        SetState(State.Fired);
                    }
                    break;

                case State.Fired:
                    // Spikes are out (from halfway through the thrust until they start pulling back): anyone in the line gets it.
                    if (age >= extendTime * 0.5f && age <= extendTime + holdTime) HitPlayersInLine();
                    if (age >= Mathf.Max(cooldown, extendTime + holdTime + retractTime)) SetState(State.Ready);
                    break;
            }
        }

        void SetState(State next)
        {
            state.Value = next;
            stateStart.Value = Now;
        }

        void HitPlayersInLine()
        {
            foreach (var player in PlayerHealth.All)
            {
                if (!player.IsAlive || hitThisShot.Contains(player) || !InLine(player.transform.position)) continue;
                hitThisShot.Add(player);
                Vector3 push = PushFor(player.transform.position);
                player.ApplyDamage(new DamageInfo(damage, push, gameObject));
                // Thrown even while still invulnerable from an earlier hit: nobody hangs on in front of the spikes.
                player.Shove(push, controlLock);
            }
        }

        Vector3 PushFor(Vector3 feet)
        {
            var f = Frame;
            Vector3 shoot = f.up;
            Vector3 away;
            if (Mathf.Abs(shoot.y) < 0.7f)
            {
                // Wall: straight out from the wall.
                away = Vector3.ProjectOnPlane(shoot, Vector3.up).normalized;
            }
            else
            {
                // Floor (or ceiling): out over the nearest edge of the plate.
                Vector3 local = f.InverseTransformPoint(feet);
                float toSideX = size.x * 0.5f - Mathf.Abs(local.x);
                float toSideZ = size.y * 0.5f - Mathf.Abs(local.z);
                away = toSideZ <= toSideX ? f.forward * (local.z >= 0f ? 1f : -1f) : f.right * (local.x >= 0f ? 1f : -1f);
                away = Vector3.ProjectOnPlane(away, Vector3.up).normalized;
            }
            return away * pushSpeed + Vector3.up * liftSpeed;
        }

        /// <summary>Is a player standing with their feet here anywhere in the line of fire?</summary>
        bool InLine(Vector3 feet)
        {
            var f = Frame;
            foreach (float height in BodySamples)
            {
                Vector3 local = f.InverseTransformPoint(feet + Vector3.up * height);
                if (Mathf.Abs(local.x) <= size.x * 0.5f + BodyRadius && Mathf.Abs(local.z) <= size.y * 0.5f + BodyRadius
                    && local.y >= -0.2f && local.y <= spikeLength + BodyRadius)
                    return true;
            }
            return false;
        }

        // ---------- Visuals (every client) ----------

        void UpdateVisuals(float age)
        {
            float extension;
            Color color;
            switch (state.Value)
            {
                case State.Armed:
                {
                    // Tips poke out and rattle, the lamps blink faster and faster.
                    float t = Mathf.Clamp01(age / armTime);
                    extension = RetractedScale + 0.1f * t + Random.Range(0f, 0.03f);
                    float rate = Mathf.Lerp(3f, 14f, t);
                    color = Mathf.Repeat(age * rate, 1f) < 0.5f ? armedColor : Color.Lerp(armedColor, Color.black, 0.75f);
                    break;
                }
                case State.Fired:
                {
                    float e = age < extendTime ? age / extendTime
                        : age < extendTime + holdTime ? 1f
                        : 1f - Mathf.Clamp01((age - extendTime - holdTime) / retractTime);
                    extension = Mathf.Lerp(RetractedScale, 1f, e);
                    color = e > 0f ? armedColor : Color.Lerp(cooldownColor, readyColor * 0.6f, Mathf.Clamp01(age / cooldown));
                    break;
                }
                default:
                    extension = RetractedScale;
                    color = Color.Lerp(readyColor * 0.7f, readyColor, 0.5f + 0.5f * Mathf.Sin(Time.time * 2f));
                    break;
            }

            if (spikes != null) spikes.localScale = new Vector3(1f, extension, 1f);
            foreach (var material in lampMaterials) material.color = color;
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
            Gizmos.color = new Color(1f, 0.2f, 0.1f);
            Gizmos.matrix = Frame.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.up * (spikeLength * 0.5f), new Vector3(size.x, spikeLength, size.y));
        }
    }
}
