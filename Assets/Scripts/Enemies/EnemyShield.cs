using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// A shield with its own hit points in front of an enemy. While it lasts it takes every hit (of any kind)
    /// and the enemy loses no health, isn't knocked back and can't be interrupted; the hit that breaks it is spent
    /// on the shield too. When it breaks the enemy staggers, the shield bursts into pieces and is gone for good.
    /// The server keeps the shield's hit points; every screen shows the shield taking damage (it flashes, sparks
    /// fly and it darkens as it wears down) and a bar over the enemy's head: shield on top, health under it.
    /// </summary>
    [RequireComponent(typeof(Damageable))]
    public class EnemyShield : NetworkBehaviour, IDamageAbsorber
    {
        [SerializeField, Min(1)] int maxShield = 30;
        [Tooltip("The shield model (switched off once it breaks).")]
        [SerializeField] Transform shieldVisual;
        [Tooltip("Parts of the shield that flash and darken as it takes damage.")]
        [SerializeField] Renderer[] shieldRenderers;
        [SerializeField] Color flashColor = Color.white;
        [Tooltip("The shield darkens towards this as it wears down.")]
        [SerializeField] Color wornColor = new Color(0.25f, 0.22f, 0.2f);
        [SerializeField] Color sparkColor = new Color(1f, 0.85f, 0.45f);
        [Tooltip("How hard the enemy is knocked back when the shield breaks (1 = a normal hit).")]
        [SerializeField] float breakStagger = 1.3f;

        [Header("Bars over the head")]
        [SerializeField] bool showBars = true;
        [SerializeField] float barHeight = 2.95f;
        [SerializeField] float barWidth = 1.1f;
        [SerializeField] Color shieldBarColor = new Color(0.35f, 0.75f, 1f);
        [SerializeField] Color healthBarColor = new Color(1f, 0.3f, 0.25f);

        readonly NetworkVariable<int> shield = new();

        Damageable damageable;
        Color[] baseColors;
        Material[] materials;
        float flashUntil;
        Material sparkMaterial;
        Transform bars, shieldFill, healthFill;

        public bool HasShield => shield.Value > 0;
        public int Shield => shield.Value;
        public int MaxShield => maxShield;

        void Awake() => damageable = GetComponent<Damageable>();

        public override void OnNetworkSpawn()
        {
            if (IsServer) shield.Value = maxShield;
            shield.OnValueChanged += OnShieldChanged;

            materials = new Material[shieldRenderers.Length];
            baseColors = new Color[shieldRenderers.Length];
            for (int i = 0; i < shieldRenderers.Length; i++)
            {
                materials[i] = shieldRenderers[i].material;
                baseColors[i] = materials[i].color;
            }
            if (shieldVisual != null) shieldVisual.gameObject.SetActive(shield.Value > 0);
            if (showBars) BuildBars();
        }

        public override void OnNetworkDespawn()
        {
            shield.OnValueChanged -= OnShieldChanged;
        }

        // ---------- Server ----------

        public int Absorb(int amount, Vector3 hitDirection, DamageTypes type)
        {
            if (!IsServer || shield.Value <= 0) return amount;
            shield.Value = Mathf.Max(0, shield.Value - amount);
            if (shield.Value == 0)
            {
                // Broken: the enemy reels back (which also cuts short whatever it was doing).
                Vector3 push = Vector3.ProjectOnPlane(hitDirection, Vector3.up);
                if (push.sqrMagnitude < 0.01f) push = -transform.forward;
                damageable.ServerStagger(push.normalized * breakStagger);
            }
            return 0;
        }

        // ---------- Every machine ----------

        void OnShieldChanged(int previous, int current)
        {
            if (current >= previous) return;
            flashUntil = Time.time + 0.1f;
            if (shieldVisual == null || !shieldVisual.gameObject.activeSelf) return;
            if (current > 0)
            {
                Sparks(6, 0.07f, 5f);
                return;
            }
            // Broken: chunks of it fly off and the shield is gone.
            Sparks(10, 0.09f, 7f);
            Chunks();
            shieldVisual.gameObject.SetActive(false);
        }

        void Update()
        {
            if (!IsSpawned || materials == null) return;
            bool flashing = Time.time < flashUntil;
            float worn = 1f - shield.Value / (float)maxShield;
            for (int i = 0; i < materials.Length; i++)
                materials[i].color = flashing ? flashColor : Color.Lerp(baseColors[i], wornColor, worn * 0.75f);
        }

        void LateUpdate()
        {
            if (bars == null) return;
            bool show = IsSpawned && !damageable.IsDead;
            if (bars.gameObject.activeSelf != show) bars.gameObject.SetActive(show);
            if (!show) return;

            float shieldPart = shield.Value / (float)maxShield;
            float healthPart = damageable.MaxHealth > 0 ? damageable.Health / (float)damageable.MaxHealth : 0f;
            SetFill(shieldFill, shieldPart);
            SetFill(healthFill, healthPart);
            var cam = Camera.main;
            if (cam != null) bars.rotation = Quaternion.LookRotation(bars.position - cam.transform.position);
        }

        void SetFill(Transform fill, float part)
        {
            fill.localScale = new Vector3(barWidth * part, fill.localScale.y, fill.localScale.z);
            fill.localPosition = new Vector3(-barWidth * (1f - part) * 0.5f, fill.localPosition.y, fill.localPosition.z);
            fill.gameObject.SetActive(part > 0f);
        }

        void BuildBars()
        {
            bars = new GameObject("ShieldAndHealthBars").transform;
            bars.SetParent(transform, false);
            bars.localPosition = Vector3.up * barHeight;
            var back = Piece(bars, new Color(0.05f, 0.05f, 0.07f), new Vector3(0f, -0.03f, 0.01f), new Vector3(barWidth + 0.06f, 0.24f, 0.02f));
            back.name = "Back";
            shieldFill = Piece(bars, shieldBarColor, new Vector3(0f, 0.03f, 0f), new Vector3(barWidth, 0.1f, 0.02f));
            shieldFill.name = "Shield";
            healthFill = Piece(bars, healthBarColor, new Vector3(0f, -0.09f, 0f), new Vector3(barWidth, 0.06f, 0.02f));
            healthFill.name = "Health";
        }

        Transform Piece(Transform parent, Color color, Vector3 position, Vector3 scale)
        {
            var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(piece.GetComponent<Collider>());
            var renderer = piece.GetComponent<Renderer>();
            renderer.material.color = color;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            piece.transform.SetParent(parent, false);
            piece.transform.localPosition = position;
            piece.transform.localScale = scale;
            return piece.transform;
        }

        void Sparks(int count, float size, float speed)
        {
            if (shieldRenderers.Length == 0) return;
            if (sparkMaterial == null) sparkMaterial = new Material(shieldRenderers[0].sharedMaterial) { color = sparkColor };
            Bounds bounds = shieldRenderers[0].bounds;
            for (int i = 0; i < count; i++)
            {
                var spark = GameObject.CreatePrimitive(PrimitiveType.Cube);
                spark.name = "ShieldSpark";
                Destroy(spark.GetComponent<Collider>());
                spark.GetComponent<Renderer>().sharedMaterial = sparkMaterial;
                spark.transform.SetPositionAndRotation(bounds.center + Vector3.Scale(Random.insideUnitSphere, bounds.extents), Random.rotation);
                spark.transform.localScale = Vector3.one * size;
                var body = spark.AddComponent<Rigidbody>();
                body.linearVelocity = (transform.forward + Random.insideUnitSphere * 0.8f + Vector3.up * 0.5f) * speed;
                Destroy(spark, 0.35f);
            }
        }

        void Chunks()
        {
            foreach (var renderer in shieldRenderers)
            {
                if (renderer == null) continue;
                Bounds bounds = renderer.bounds;
                for (int i = 0; i < 4; i++)
                {
                    var chunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    chunk.name = "ShieldChunk";
                    chunk.GetComponent<Renderer>().sharedMaterial = renderer.sharedMaterial;
                    chunk.transform.SetPositionAndRotation(bounds.center + Vector3.Scale(Random.insideUnitSphere, bounds.extents), Random.rotation);
                    chunk.transform.localScale = Vector3.Scale(bounds.size, new Vector3(0.45f, 0.45f, 0.45f)) + Vector3.one * 0.05f;
                    var body = chunk.AddComponent<Rigidbody>();
                    body.linearVelocity = (transform.forward * 0.6f + Random.insideUnitSphere + Vector3.up) * Random.Range(3f, 6f);
                    body.angularVelocity = Random.insideUnitSphere * 8f;
                    Destroy(chunk, Random.Range(2f, 3f));
                }
            }
        }

        public override void OnDestroy()
        {
            if (materials != null)
                foreach (var material in materials)
                    if (material != null) Destroy(material);
            if (sparkMaterial != null) Destroy(sparkMaterial);
            base.OnDestroy();
        }
    }
}
