using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Poison gas vent, an area trap. Works like the <see cref="SpikeTrap"/>: a player stepping into its circle sets it
    /// off, it hisses and blinks for <see cref="armTime"/> seconds, then a cloud of gas bursts out over the whole
    /// circle and hangs there for <see cref="gasTime"/> seconds. Anyone caught in it is poisoned
    /// (<see cref="PlayerHealth.ServerPoison"/>: 1 damage a second for 3 seconds, no knockback). Then it needs
    /// <see cref="cooldown"/> seconds from the burst before it can go off again.
    /// The server runs it and poisons; every client animates it from the replicated state and start time.
    /// </summary>
    public class GasTrap : NetworkBehaviour
    {
        enum State : byte { Ready, Armed, Fired }

        [Tooltip("Radius of the area that sets it off and that the gas fills.")]
        [SerializeField] float radius = 3f;
        [Tooltip("How high the gas reaches above the vent.")]
        [SerializeField] float height = 2.5f;

        [Header("Timing")]
        [SerializeField] float armTime = 2f;
        [Tooltip("How long the cloud hangs there poisoning whoever walks into it.")]
        [SerializeField] float gasTime = 1.5f;
        [Tooltip("From the burst until the trap can be set off again.")]
        [SerializeField] float cooldown = 5f;

        [Header("Poison")]
        [SerializeField] int damagePerTick = 1;
        [SerializeField] int ticks = 3;
        [SerializeField] float tickInterval = 1f;

        [Header("Visuals")]
        [SerializeField] Material gasMaterial;
        [SerializeField] Renderer[] lamps;
        [Tooltip("Flat disc showing the area (scaled to the radius).")]
        [SerializeField] Transform areaMarker;
        [SerializeField] int puffCount = 16;
        [SerializeField] Color readyColor = new Color(0.9f, 0.55f, 0.1f);
        [SerializeField] Color armedColor = new Color(0.45f, 1f, 0.2f);
        [SerializeField] Color cooldownColor = new Color(0.2f, 0.2f, 0.22f);

        const float SpreadTime = 0.35f;
        const float FadeTime = 0.8f;

        readonly NetworkVariable<State> state = new();
        readonly NetworkVariable<double> stateStart = new();
        readonly HashSet<PlayerHealth> poisonedThisBurst = new();

        Material[] lampMaterials;
        Material cloudMaterial;
        Color cloudColor;
        Transform cloud;
        Vector3[] puffTargets;
        float[] puffSizes;

        double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : Time.timeAsDouble;

        void Awake()
        {
            lampMaterials = new Material[lamps.Length];
            for (int i = 0; i < lamps.Length; i++) lampMaterials[i] = lamps[i].material;
            BuildCloud();
        }

        void BuildCloud()
        {
            if (gasMaterial == null) return;
            cloudMaterial = new Material(gasMaterial);
            cloudColor = cloudMaterial.HasProperty("_BaseColor") ? cloudMaterial.GetColor("_BaseColor") : cloudMaterial.color;
            cloud = new GameObject("GasCloud").transform;
            cloud.SetParent(transform, false);
            puffTargets = new Vector3[puffCount];
            puffSizes = new float[puffCount];
            // Same layout on every machine: seeded by the trap's position.
            var random = new System.Random(transform.position.GetHashCode());
            for (int i = 0; i < puffCount; i++)
            {
                float angle = (i / (float)puffCount + (float)random.NextDouble() * 0.05f) * Mathf.PI * 2f;
                float distance = radius * (i % 3 == 0 ? 0.25f : 0.55f + 0.4f * (float)random.NextDouble());
                puffTargets[i] = new Vector3(Mathf.Cos(angle) * distance, height * (0.25f + 0.45f * (float)random.NextDouble()), Mathf.Sin(angle) * distance);
                puffSizes[i] = radius * (0.55f + 0.3f * (float)random.NextDouble());
                var puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                puff.name = "Puff";
                Destroy(puff.GetComponent<Collider>());
                puff.GetComponent<Renderer>().sharedMaterial = cloudMaterial;
                puff.transform.SetParent(cloud, false);
            }
            cloud.gameObject.SetActive(false);
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
                        if (player.IsAlive && InArea(player.transform.position))
                        {
                            SetState(State.Armed);
                            break;
                        }
                    break;

                case State.Armed:
                    if (age >= armTime)
                    {
                        poisonedThisBurst.Clear();
                        SetState(State.Fired);
                    }
                    break;

                case State.Fired:
                    // The gas reaches the edge after a moment, then poisons anyone in it while it hangs there.
                    if (age >= SpreadTime * 0.5f && age <= gasTime)
                        foreach (var player in PlayerHealth.All)
                            if (player.IsAlive && !poisonedThisBurst.Contains(player) && InArea(player.transform.position))
                            {
                                poisonedThisBurst.Add(player);
                                player.ServerPoison(damagePerTick, ticks, tickInterval);
                            }
                    if (age >= Mathf.Max(cooldown, gasTime + FadeTime)) SetState(State.Ready);
                    break;
            }
        }

        void SetState(State next)
        {
            state.Value = next;
            stateStart.Value = Now;
        }

        bool InArea(Vector3 feet)
        {
            Vector3 local = feet - transform.position;
            float body = 0.35f;
            return new Vector2(local.x, local.z).magnitude <= radius + body && local.y > -0.5f && local.y < height;
        }

        // ---------- Visuals (every client) ----------

        void UpdateVisuals(float age)
        {
            Color lamp;
            float cloudAmount = 0f, spread = 0f;
            switch (state.Value)
            {
                case State.Armed:
                {
                    float t = Mathf.Clamp01(age / armTime);
                    float rate = Mathf.Lerp(3f, 14f, t);
                    lamp = Mathf.Repeat(age * rate, 1f) < 0.5f ? armedColor : Color.Lerp(armedColor, Color.black, 0.75f);
                    // A thin wisp leaking out of the vent while it builds up.
                    cloudAmount = 0.35f * t;
                    spread = 0.08f;
                    break;
                }
                case State.Fired:
                    spread = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / SpreadTime));
                    cloudAmount = age <= gasTime ? 1f : 1f - Mathf.Clamp01((age - gasTime) / FadeTime);
                    lamp = cloudAmount > 0f ? armedColor : Color.Lerp(cooldownColor, readyColor * 0.6f, Mathf.Clamp01(age / cooldown));
                    break;
                default:
                    lamp = Color.Lerp(readyColor * 0.7f, readyColor, 0.5f + 0.5f * Mathf.Sin(Time.time * 2f));
                    break;
            }
            foreach (var material in lampMaterials) material.color = lamp;

            if (cloud == null) return;
            bool show = cloudAmount > 0.001f;
            if (cloud.gameObject.activeSelf != show) cloud.gameObject.SetActive(show);
            if (!show) return;

            var color = cloudColor;
            color.a *= cloudAmount;
            if (cloudMaterial.HasProperty("_BaseColor")) cloudMaterial.SetColor("_BaseColor", color);
            else cloudMaterial.color = color;
            for (int i = 0; i < cloud.childCount; i++)
            {
                var puff = cloud.GetChild(i);
                // Swirl a little while it hangs.
                float wobble = Mathf.Sin(Time.time * 1.3f + i) * 0.15f;
                Vector3 target = puffTargets[i] + new Vector3(wobble, wobble * 0.5f, -wobble);
                puff.localPosition = Vector3.Lerp(Vector3.up * 0.3f, target, spread);
                puff.localScale = Vector3.one * Mathf.Lerp(0.4f, puffSizes[i], spread);
            }
        }

        public override void OnDestroy()
        {
            if (lampMaterials != null)
                foreach (var material in lampMaterials)
                    if (material != null) Destroy(material);
            if (cloudMaterial != null) Destroy(cloudMaterial);
            base.OnDestroy();
        }

#if UNITY_EDITOR
        // Keep the area disc as wide as the radius when it's changed in the Inspector.
        void OnValidate()
        {
            if (areaMarker == null) return;
            var marker = areaMarker;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null || marker == null) return;
                var scale = marker.localScale;
                if (Mathf.Approximately(scale.x, radius * 2f)) return;
                marker.localScale = new Vector3(radius * 2f, scale.y, radius * 2f);
            };
        }
#endif

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.45f, 1f, 0.2f);
            Vector3 center = transform.position;
            Vector3 previous = center + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= 48; i++)
            {
                float a = i / 48f * Mathf.PI * 2f;
                Vector3 next = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                Gizmos.DrawLine(previous, next);
                Gizmos.DrawLine(previous + Vector3.up * height, next + Vector3.up * height);
                previous = next;
            }
        }
    }
}
