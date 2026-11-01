using System;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>What kind of hit it is; a <see cref="Damageable"/> only takes the kinds it's vulnerable to.</summary>
    [Flags]
    public enum DamageTypes
    {
        None = 0,
        /// <summary>Wrench swings and throws, shots, slams, whip lashes.</summary>
        Physical = 1 << 0,
        /// <summary>Blasts from explosive crates.</summary>
        Explosive = 1 << 1,
        /// <summary>The delayed shock from electric weapons.</summary>
        Electric = 1 << 2,
        /// <summary>The player's grenade blasts (metal crates shrug these off; only crate blasts break them).</summary>
        Grenade = 1 << 3,
        /// <summary>The player's rocket blasts (likewise no use against metal crates).</summary>
        Rocket = 1 << 4,
        /// <summary>Enemy attacks; only things on the players' side take these (the charm robot).</summary>
        Enemy = 1 << 5,
        All = ~0,
    }

    /// <summary>
    /// Something next to a <see cref="Damageable"/> that soaks up hits before they reach its health (an enemy's
    /// shield). Server only.
    /// </summary>
    public interface IDamageAbsorber
    {
        /// <summary>Takes a hit; returns how much of it still gets through to the health (0 = all absorbed).</summary>
        int Absorb(int amount, Vector3 hitDirection, DamageTypes type);
    }

    /// <summary>
    /// Server-authoritative health for anything the wrench can break (crates, enemies).
    /// Hits flash on every client; at zero health the object bursts into placeholder debris and despawns.
    /// Hits of a kind it isn't vulnerable to (e.g. a wrench on a metal crate) just throw sparks.
    /// </summary>
    public class Damageable : NetworkBehaviour
    {
        [SerializeField] int maxHealth = 1;
        [Tooltip("What can hurt this. Everything for normal crates and enemies; Explosive only for metal crates.")]
        [SerializeField] DamageTypes vulnerableTo = DamageTypes.All;

        [Header("Hit Flash")]
        [SerializeField] Renderer[] flashRenderers;
        [SerializeField] Color flashColor = Color.white;
        [SerializeField] float flashDuration = 0.1f;

        [Header("Break")]
        [SerializeField] int debrisCount = 6;
        [SerializeField] float debrisSize = 0.3f;
        [SerializeField] Color debrisColor = new Color(0.6f, 0.4f, 0.2f);
        [SerializeField] float debrisLifetime = 1.2f;

        [Header("Loot")]
        [Tooltip("Bolts that burst out when this breaks (go to the shared team wallet).")]
        [SerializeField] int boltDrop;
        [Tooltip("Health orbs that burst out when this breaks (1 HP each for whoever absorbs them).")]
        [SerializeField] int healthDrop;

        [Header("Shock (electric weapons)")]
        [SerializeField] Color shockColor = new Color(0.3f, 0.85f, 1f);

        [Header("Bleed (discs)")]
        [Tooltip("The body pulses towards this while bleeding (dark, so it shows on red enemies too).")]
        [SerializeField] Color bleedColor = new Color(0.3f, 0f, 0.03f);
        [Tooltip("Colour of the falling drops.")]
        [SerializeField] Color bloodColor = new Color(1f, 0.08f, 0.08f);

        [Header("Immune hit (sparks)")]
        [SerializeField] Color deflectColor = new Color(1f, 0.85f, 0.45f);

        readonly NetworkVariable<int> health = new();
        readonly NetworkVariable<bool> shocked = new();
        readonly NetworkVariable<bool> bleeding = new();

        Color[] baseColors;
        float flashUntil;
        int pendingShockDamage;
        float shockEndsAt;
        int pendingBleedDamage;
        float bleedEndsAt;
        float nextSparkTime;
        float nextDripTime;
        Material sparkMaterial;
        Material bleedMaterial;
        Material deflectMaterial;
        float nextDeflectTime;
        Color statusTint;
        float statusTintAmount;

        /// <summary>Raised on the server after a hit lands, with the hit direction (enemies use it for knockback).</summary>
        public event Action<Vector3> Damaged;

        /// <summary>Raised on the server when health hits zero, before the object breaks and despawns.</summary>
        public event Action<Vector3> Died;

        public bool IsDead => health.Value <= 0;
        public bool IsShocked => shocked.Value;
        public bool IsBleeding => bleeding.Value;
        public int Health => health.Value;
        public int MaxHealth => maxHealth;

        IDamageAbsorber absorber;

        public override void OnNetworkSpawn()
        {
            absorber = GetComponent<IDamageAbsorber>();
            if (IsServer) health.Value = maxHealth;
            health.OnValueChanged += OnHealthChanged;

            baseColors = new Color[flashRenderers.Length];
            for (int i = 0; i < flashRenderers.Length; i++)
                baseColors[i] = flashRenderers[i].material.color;
        }

        public override void OnNetworkDespawn()
        {
            health.OnValueChanged -= OnHealthChanged;
        }

        /// <summary>
        /// Every client: tint the body towards <paramref name="color"/> by <paramref name="amount"/> (0..1) for a
        /// status effect kept elsewhere (an enemy frozen solid or slowed); 0 clears it. Hit flashes, shocks and
        /// bleeding still show over it.
        /// </summary>
        public void SetStatusTint(Color color, float amount)
        {
            statusTint = color;
            statusTintAmount = amount;
        }

        /// <summary>Every client: the body's own colour from now on (e.g. a police enemy painted blue); hit flashes and tints still show over it.</summary>
        public void SetBaseColor(Color color)
        {
            for (int i = 0; i < flashRenderers.Length; i++)
            {
                if (flashRenderers[i] == null) continue;
                flashRenderers[i].material.color = color;
                if (baseColors != null && i < baseColors.Length) baseColors[i] = color;
            }
        }

        public bool IsVulnerableTo(DamageTypes type) => (vulnerableTo & type) != 0;

        /// <summary>
        /// Server only: a hit of the given kind. Kinds this isn't vulnerable to only throw sparks.
        /// <paramref name="hitDirection"/> is where the hit pushes; its length scales the knockback (1 = a normal hit).
        /// </summary>
        public void ApplyDamage(int amount, Vector3 hitDirection, DamageTypes type = DamageTypes.Physical)
        {
            if (!IsServer || IsDead) return;
            if (!IsVulnerableTo(type))
            {
                Deflect(hitDirection);
                return;
            }
            TakeDamage(amount, hitDirection, isHit: true, type);
        }

        /// <summary>
        /// Server only: knock it back and stun it as if hit, without any damage (e.g. its shield just broke).
        /// </summary>
        public void ServerStagger(Vector3 direction)
        {
            if (IsServer && !IsDead) Damaged?.Invoke(direction);
        }

        /// <summary>Server only: destroy it outright, past any shield (e.g. an arena clearing its leftovers).</summary>
        public void ServerKill(Vector3 hitDirection)
        {
            if (!IsServer || IsDead) return;
            TakeDamage(health.Value, hitDirection, isHit: true, DamageTypes.All, ignoreAbsorber: true);
        }

        /// <summary>
        /// Server only: electric shock. The target crackles for <paramref name="duration"/> seconds, then takes
        /// <paramref name="damage"/> extra damage (no knockback). A new shock refreshes the old one instead of stacking.
        /// </summary>
        public void ApplyShock(int damage, float duration)
        {
            if (!IsServer || IsDead || !IsVulnerableTo(DamageTypes.Electric)) return;
            pendingShockDamage = damage;
            shockEndsAt = Time.time + duration;
            shocked.Value = true;
        }

        /// <summary>
        /// Server only: a bleeding cut (discs). The target drips blood for <paramref name="duration"/> seconds, then
        /// takes <paramref name="damage"/> extra damage (no knockback). A new cut refreshes the bleed instead of stacking.
        /// Things physical hits can't hurt (metal crates) don't bleed.
        /// </summary>
        public void ApplyBleed(int damage, float duration)
        {
            if (!IsServer || IsDead || !IsVulnerableTo(DamageTypes.Physical)) return;
            pendingBleedDamage = damage;
            bleedEndsAt = Time.time + duration;
            bleeding.Value = true;
        }

        void TakeDamage(int amount, Vector3 hitDirection, bool isHit, DamageTypes type, bool ignoreAbsorber = false)
        {
            if (!IsServer || IsDead) return;
            if (absorber != null && !ignoreAbsorber)
            {
                // A shield takes it first; a fully absorbed hit doesn't stagger the enemy either.
                amount = absorber.Absorb(amount, hitDirection, type);
                if (amount <= 0) return;
            }

            health.Value = Mathf.Max(0, health.Value - amount);
            if (isHit) Damaged?.Invoke(hitDirection);

            if (IsDead)
            {
                Died?.Invoke(hitDirection);
                var pickups = PickupSystem.Instance;
                if (pickups != null)
                {
                    pickups.Drop(PickupKind.Bolt, BreakCenter(), boltDrop);
                    pickups.Drop(PickupKind.Health, BreakCenter(), healthDrop);
                }
                BreakRpc(hitDirection);
                NetworkObject.Despawn(true);
            }
        }

        void OnHealthChanged(int previous, int current)
        {
            if (current < previous) flashUntil = Time.time + flashDuration;
        }

        void Update()
        {
            if (IsServer && IsSpawned && shocked.Value && Time.time >= shockEndsAt)
            {
                shocked.Value = false;
                TakeDamage(pendingShockDamage, Vector3.zero, isHit: false, DamageTypes.Electric);
                if (!IsSpawned) return;
            }
            if (IsServer && IsSpawned && bleeding.Value && Time.time >= bleedEndsAt)
            {
                bleeding.Value = false;
                TakeDamage(pendingBleedDamage, Vector3.zero, isHit: false, DamageTypes.Physical);
                if (!IsSpawned) return;
            }

            if (baseColors == null) return;
            bool flashing = Time.time < flashUntil;
            // Shocked: flicker between the base colour and electric blue, with little sparks.
            bool shockFlicker = shocked.Value && Mathf.Repeat(Time.time * 18f, 1f) > 0.5f;
            // Bleeding: a slow pulse towards blood red, with drips.
            float bleedPulse = bleeding.Value ? 0.4f + 0.3f * Mathf.Sin(Time.time * 8f) : 0f;
            for (int i = 0; i < flashRenderers.Length; i++)
            {
                Color body = Color.Lerp(baseColors[i], statusTint, statusTintAmount);
                flashRenderers[i].material.color = flashing ? flashColor
                    : shockFlicker ? shockColor
                    : Color.Lerp(body, bleedColor, bleedPulse);
            }

            if (shocked.Value && Time.time >= nextSparkTime)
            {
                nextSparkTime = Time.time + 0.08f;
                SpawnSpark();
            }
            if (bleeding.Value && Time.time >= nextDripTime)
            {
                nextDripTime = Time.time + 0.1f;
                SpawnDrip();
            }
        }

        void SpawnSpark()
        {
            if (flashRenderers.Length == 0) return;
            if (sparkMaterial == null) sparkMaterial = new Material(flashRenderers[0].sharedMaterial) { color = shockColor };

            Bounds bounds = flashRenderers[0].bounds;
            var spark = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spark.name = "ShockSpark";
            Destroy(spark.GetComponent<Collider>());
            spark.GetComponent<Renderer>().sharedMaterial = sparkMaterial;
            Vector3 offset = Vector3.Scale(UnityEngine.Random.insideUnitSphere, bounds.extents);
            spark.transform.SetPositionAndRotation(bounds.center + offset, UnityEngine.Random.rotation);
            spark.transform.localScale = new Vector3(0.05f, 0.05f, UnityEngine.Random.Range(0.2f, 0.45f));
            Destroy(spark, 0.1f);
        }

        /// <summary>A drop of blood falling off the side of the body.</summary>
        void SpawnDrip()
        {
            if (flashRenderers.Length == 0) return;
            if (bleedMaterial == null) bleedMaterial = new Material(flashRenderers[0].sharedMaterial) { color = bloodColor };

            Bounds bounds = flashRenderers[0].bounds;
            var drop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            drop.name = "BloodDrip";
            Destroy(drop.GetComponent<Collider>());
            drop.GetComponent<Renderer>().sharedMaterial = bleedMaterial;
            Vector2 side = UnityEngine.Random.insideUnitCircle.normalized;
            Vector3 offset = new Vector3(side.x * bounds.extents.x, UnityEngine.Random.Range(-0.2f, 0.5f) * bounds.extents.y, side.y * bounds.extents.z);
            drop.transform.SetPositionAndRotation(bounds.center + offset, UnityEngine.Random.rotation);
            drop.transform.localScale = Vector3.one * UnityEngine.Random.Range(0.06f, 0.1f);
            var body = drop.AddComponent<Rigidbody>();
            body.linearVelocity = new Vector3(side.x, 0.5f, side.y) * 0.8f;
            Destroy(drop, 0.6f);
        }

        public override void OnDestroy()
        {
            if (sparkMaterial != null) Destroy(sparkMaterial);
            if (bleedMaterial != null) Destroy(bleedMaterial);
            if (deflectMaterial != null) Destroy(deflectMaterial);
            base.OnDestroy();
        }

        // ---------- Immune hits ----------

        void Deflect(Vector3 hitDirection)
        {
            if (Time.time < nextDeflectTime) return; // one clang per swing, not one per overlapping collider
            nextDeflectTime = Time.time + 0.1f;
            DeflectRpc(hitDirection);
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void DeflectRpc(Vector3 hitDirection)
        {
            flashUntil = Time.time + flashDuration;
            if (flashRenderers.Length == 0) return;
            if (deflectMaterial == null) deflectMaterial = new Material(flashRenderers[0].sharedMaterial) { color = deflectColor };

            // Sparks bounce off the face that was hit, back towards the attacker.
            Vector3 back = hitDirection.sqrMagnitude > 0.0001f ? -hitDirection.normalized : Vector3.up;
            Bounds bounds = flashRenderers[0].bounds;
            Vector3 face = bounds.center + Vector3.Scale(back, bounds.extents);
            for (int i = 0; i < 7; i++)
            {
                var spark = GameObject.CreatePrimitive(PrimitiveType.Cube);
                spark.name = "DeflectSpark";
                Destroy(spark.GetComponent<Collider>());
                spark.GetComponent<Renderer>().sharedMaterial = deflectMaterial;
                spark.transform.SetPositionAndRotation(face, UnityEngine.Random.rotation);
                spark.transform.localScale = Vector3.one * 0.07f;
                var body = spark.AddComponent<Rigidbody>();
                body.linearVelocity = (back + UnityEngine.Random.insideUnitSphere * 0.7f + Vector3.up * 0.5f) * 5f;
                Destroy(spark, 0.35f);
            }
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void BreakRpc(Vector3 hitDirection)
        {
            SpawnDebris(hitDirection);
        }

        Vector3 BreakCenter() => flashRenderers.Length > 0 ? flashRenderers[0].bounds.center : transform.position;

        void SpawnDebris(Vector3 hitDirection)
        {
            var material = new Material(flashRenderers.Length > 0 ? flashRenderers[0].sharedMaterial : null) { color = debrisColor };
            Vector3 center = BreakCenter();

            // Debris bursts upwards; it mustn't shove physics crates stacked on this one (they should just drop).
            var physicsNeighbours = new System.Collections.Generic.List<Collider>();
            foreach (var near in Physics.OverlapSphere(center, 4f, ~0, QueryTriggerInteraction.Ignore))
                if (near.attachedRigidbody != null && !near.attachedRigidbody.isKinematic && near.GetComponentInParent<Damageable>() != null)
                    physicsNeighbours.Add(near);

            for (int i = 0; i < debrisCount; i++)
            {
                var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
                piece.name = "Debris";
                piece.transform.SetPositionAndRotation(center + UnityEngine.Random.insideUnitSphere * 0.4f, UnityEngine.Random.rotation);
                piece.transform.localScale = Vector3.one * debrisSize * UnityEngine.Random.Range(0.6f, 1.2f);
                piece.GetComponent<Renderer>().sharedMaterial = material;
                var pieceCollider = piece.GetComponent<Collider>();
                foreach (var neighbour in physicsNeighbours) Physics.IgnoreCollision(pieceCollider, neighbour);

                var body = piece.AddComponent<Rigidbody>();
                body.linearVelocity = hitDirection * 3f + UnityEngine.Random.insideUnitSphere * 3f + Vector3.up * UnityEngine.Random.Range(3f, 6f);
                body.angularVelocity = UnityEngine.Random.insideUnitSphere * 10f;

                Destroy(piece, debrisLifetime * UnityEngine.Random.Range(0.8f, 1.2f));
            }
            Destroy(material, debrisLifetime * 1.3f);
        }
    }
}
