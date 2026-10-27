using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>What an attack hands to whoever it hits. Extend with element, stagger strength, etc. as attacks grow.</summary>
    public readonly struct DamageInfo
    {
        public readonly int Amount;
        /// <summary>Planar direction the hit pushes the victim.</summary>
        public readonly Vector3 Direction;
        public readonly GameObject Source;

        public DamageInfo(int amount, Vector3 direction, GameObject source)
        {
            Amount = amount;
            Direction = direction;
            Source = source;
        }
    }

    /// <summary>
    /// Player hit points. The server owns the value; hits knock the owner back (movement is owner-authoritative)
    /// and grant a short invulnerability window. At zero HP the player is downed: they lie on the ground until a
    /// teammate revives them (<see cref="PlayerRevive"/>). If every player is down, the team respawns together.
    /// </summary>
    [RequireComponent(typeof(PlayerMovement))]
    public class PlayerHealth : NetworkBehaviour, IEnemyTarget
    {
        public static PlayerHealth Local { get; private set; }
        public static IReadOnlyList<PlayerHealth> All => s_All;
        static readonly List<PlayerHealth> s_All = new();
        static float s_TeamWipeRespawnAt = float.PositiveInfinity;

        [SerializeField] int maxHealth = 10;
        [SerializeField] float invulnerabilityTime = 1.2f;
        [SerializeField] float knockbackSpeed = 7f;
        [SerializeField] float knockbackUpSpeed = 5f;
        [SerializeField] float knockbackControlLock = 0.35f;
        [SerializeField] float blinkRate = 15f;
        [Tooltip("Invulnerability granted when the player starts a dodge.")]
        [SerializeField] float dodgeInvulnerability = 0.25f;

        [Header("Downed / Revive")]
        [Tooltip("Seconds a teammate has to hold Interact to revive.")]
        [SerializeField] float reviveTime = 2f;
        [SerializeField] int reviveHealth = 5;
        [Tooltip("When every player is down, wait this long and respawn the whole team.")]
        [SerializeField] float teamWipeRespawnDelay = 3f;

        [Header("Poison")]
        [Tooltip("Bubbles over a poisoned player's head (transparent green).")]
        [SerializeField] Material poisonMaterial;

        readonly NetworkVariable<int> health = new();
        readonly NetworkVariable<float> reviveProgress = new();
        readonly NetworkVariable<bool> poisoned = new();

        // Server: poison still to come.
        int poisonDamage, poisonTicksLeft;
        float poisonInterval, nextPoisonTick;
        Transform poisonFx;

        PlayerMovement movement;
        PlayerRevive revive;
        Renderer[] renderers;
        float invulnerableUntil;
        float blinkUntil;

        /// <summary>Raised on every instance with (current, max).</summary>
        public event Action<int, int> HealthChanged;

        public int Current => health.Value;
        public int Max => maxHealth;
        public bool IsAlive => health.Value > 0;
        public bool IsDowned => IsSpawned && health.Value <= 0;
        public bool IsPoisoned => poisoned.Value;
        /// <summary>0..1 while a teammate is reviving this player.</summary>
        public float ReviveProgress => reviveProgress.Value;
        public PlayerRevive Revive => revive;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_All.Clear();
            s_TeamWipeRespawnAt = float.PositiveInfinity;
            Local = null;
        }

        void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            revive = GetComponent<PlayerRevive>();
            renderers = GetComponentsInChildren<Renderer>(true);
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) health.Value = maxHealth;
            if (IsOwner) Local = this;
            s_All.Add(this);
            health.OnValueChanged += OnHealthChanged;
            HealthChanged?.Invoke(health.Value, maxHealth);
        }

        public override void OnNetworkDespawn()
        {
            health.OnValueChanged -= OnHealthChanged;
            s_All.Remove(this);
            if (Local == this) Local = null;
        }

        /// <summary>Server only. Returns true if the hit landed (not downed or invulnerable).</summary>
        public bool ApplyDamage(in DamageInfo damage)
        {
            if (!IsServer || !IsAlive || Time.time < invulnerableUntil || CutsceneSystem.ProtectsPlayers) return false;

            health.Value = Mathf.Max(0, health.Value - damage.Amount);
            invulnerableUntil = Time.time + invulnerabilityTime;

            Vector3 push = Vector3.ProjectOnPlane(damage.Direction, Vector3.up).normalized * knockbackSpeed + Vector3.up * knockbackUpSpeed;
            KnockbackRpc(push);
            return true;
        }

        /// <summary>
        /// Server only: throw a standing (not downed) player with this velocity, invulnerable or not, e.g. a trap
        /// knocking someone off a ledge. Lets go of the ledge on the owner's side.
        /// </summary>
        public void Shove(Vector3 velocity, float controlLock)
        {
            if (!IsServer || !IsAlive || CutsceneSystem.ProtectsPlayers) return;
            ShoveRpc(velocity, controlLock);
        }

        /// <summary>
        /// Server only: poison the player: <paramref name="damage"/> every <paramref name="interval"/> seconds,
        /// <paramref name="ticks"/> times, then it wears off. No knockback and no invulnerability in between (or it
        /// would swallow the next tick). A new dose restarts the count rather than stacking.
        /// </summary>
        public void ServerPoison(int damage, int ticks, float interval)
        {
            if (!IsServer || !IsAlive || ticks <= 0 || CutsceneSystem.ProtectsPlayers) return;
            poisonDamage = damage;
            poisonTicksLeft = ticks;
            poisonInterval = interval;
            nextPoisonTick = Time.time + interval;
            poisoned.Value = true;
        }

        void Update()
        {
            if (IsServer && IsSpawned)
            {
                UpdatePoison();
                UpdateReviveProgress();
                UpdateTeamWipe();
            }

            UpdateBlink();
            UpdatePoisonFx();
        }

        void UpdatePoison()
        {
            if (!poisoned.Value) return;
            if (!IsAlive || CutsceneSystem.ProtectsPlayers) // a cutscene cures it
            {
                poisoned.Value = false;
                return;
            }
            if (Time.time < nextPoisonTick) return;

            health.Value = Mathf.Max(0, health.Value - poisonDamage);
            nextPoisonTick += poisonInterval;
            if (--poisonTicksLeft <= 0) poisoned.Value = false;
        }

        // ---------- Poison look (every client) ----------

        void UpdatePoisonFx()
        {
            bool show = IsSpawned && poisoned.Value;
            if (!show)
            {
                if (poisonFx != null && poisonFx.gameObject.activeSelf) poisonFx.gameObject.SetActive(false);
                return;
            }
            if (poisonFx == null) poisonFx = BuildPoisonFx();
            if (!poisonFx.gameObject.activeSelf) poisonFx.gameObject.SetActive(true);

            // Bubbles circling over the head, each rising and shrinking away on its own loop.
            for (int i = 0; i < poisonFx.childCount; i++)
            {
                float phase = Mathf.Repeat(Time.time * 0.9f + i / (float)poisonFx.childCount, 1f);
                float angle = (Time.time * 90f + i * 72f) * Mathf.Deg2Rad;
                var bubble = poisonFx.GetChild(i);
                bubble.localPosition = new Vector3(Mathf.Cos(angle) * 0.45f, 1.7f + phase * 0.9f, Mathf.Sin(angle) * 0.45f);
                bubble.localScale = Vector3.one * Mathf.Lerp(0.3f, 0.05f, phase);
            }
        }

        Transform BuildPoisonFx()
        {
            var fx = new GameObject("PoisonFx").transform;
            fx.SetParent(transform, false);
            for (int i = 0; i < 5; i++)
            {
                var bubble = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(bubble.GetComponent<Collider>());
                if (poisonMaterial != null) bubble.GetComponent<Renderer>().sharedMaterial = poisonMaterial;
                bubble.transform.SetParent(fx, false);
            }
            return fx;
        }

        void OnGUI()
        {
            if (!IsOwner || !IsSpawned || !poisoned.Value || CutsceneSystem.HidesGameplayUI) return;
            var style = new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = new Color(0.55f, 1f, 0.35f);
            GUI.Box(new Rect(Screen.width - 330f, Screen.height - 150f, 300f, 40f), "Zatrucie!", style);
        }

        // ---------- Server ----------

        void UpdateReviveProgress()
        {
            if (!IsDowned)
            {
                reviveProgress.Value = 0f;
                return;
            }

            bool beingRevived = false;
            foreach (var other in s_All)
            {
                if (other == this || !other.IsAlive || other.revive == null || other.revive.ServerReviveTarget != this) continue;
                float range = other.revive.ServerMaxRange;
                if ((other.transform.position - transform.position).sqrMagnitude <= range * range)
                {
                    beingRevived = true;
                    break;
                }
            }

            // Progress resets if the reviver lets go or walks away.
            reviveProgress.Value = beingRevived ? Mathf.Min(1f, reviveProgress.Value + Time.deltaTime / reviveTime) : 0f;
            if (reviveProgress.Value >= 1f) Restore(reviveHealth, teleportToSpawn: false);
        }

        void UpdateTeamWipe()
        {
            bool everyoneDown = s_All.Count > 0;
            foreach (var player in s_All)
                if (!player.IsDowned) everyoneDown = false;

            if (!everyoneDown)
            {
                s_TeamWipeRespawnAt = float.PositiveInfinity;
                return;
            }

            if (float.IsPositiveInfinity(s_TeamWipeRespawnAt))
            {
                s_TeamWipeRespawnAt = Time.time + teamWipeRespawnDelay;
            }
            else if (Time.time >= s_TeamWipeRespawnAt)
            {
                s_TeamWipeRespawnAt = float.PositiveInfinity;
                foreach (var player in s_All.ToArray())
                    player.Restore(player.maxHealth, teleportToSpawn: true);
            }
        }

        void Restore(int hp, bool teleportToSpawn)
        {
            health.Value = Mathf.Clamp(hp, 1, maxHealth);
            reviveProgress.Value = 0f;
            invulnerableUntil = Time.time + invulnerabilityTime;
            if (teleportToSpawn) RespawnRpc();
        }

        // ---------- All instances ----------

        void OnHealthChanged(int previous, int current)
        {
            if (current < previous || (previous == 0 && current > 0)) blinkUntil = Time.time + invulnerabilityTime;
            HealthChanged?.Invoke(current, maxHealth);
        }

        void UpdateBlink()
        {
            bool visible = !IsSpawned || IsDowned || Time.time >= blinkUntil || Mathf.Repeat(Time.time * blinkRate, 1f) > 0.5f;
            foreach (var r in renderers)
                if (r.enabled != visible) r.enabled = visible;
        }

        /// <summary>Server only: restore HP (capped at max). Downed players can't be healed, only revived.</summary>
        public void Heal(int amount)
        {
            if (!IsServer || !IsAlive || amount <= 0) return;
            health.Value = Mathf.Min(maxHealth, health.Value + amount);
        }

        /// <summary>Owner → server: the player just dodged, ignore hits for a moment.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void RequestDodgeInvulnerabilityRpc()
        {
            if (IsAlive) invulnerableUntil = Mathf.Max(invulnerableUntil, Time.time + dodgeInvulnerability);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void KnockbackRpc(Vector3 velocity) => movement.ApplyKnockback(velocity, knockbackControlLock);

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void ShoveRpc(Vector3 velocity, float controlLock) => movement.ApplyKnockback(velocity, controlLock);

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void RespawnRpc() => movement.Respawn();
    }
}
