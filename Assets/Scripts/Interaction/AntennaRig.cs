using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// A satellite antenna that has to be aimed exactly at its <see cref="target"/> with the wrench beam.
    /// Grabbing the antenna itself (its base handle) turns the whole antenna around its own vertical axis, 0-360°
    /// (A/D); grabbing the holder tilts the dish around the holder's X axis (W/S) within its range (default
    /// 90-180°): 0° would be straight down, 90° = level, 180° = straight up. Holding a key starts very slowly
    /// (taps nudge it by a fraction of a degree) and speeds up the longer it's held, so it can be set precisely. The player holding it sees the current angles;
    /// the right ones are only shown on the antenna's <see cref="AntennaPanel"/> while someone holds the antenna.
    /// Within <see cref="tolerance"/> of the right angles and left still for a moment, it locks: the beams drop, it
    /// can't be grabbed any more and a signal beam shoots from the dish to the target. While any of its
    /// <see cref="blockers"/> (rubble) is still there it can't be grabbed or turned at all.
    /// The server turns it and shares the angles; every client poses it from them.
    /// </summary>
    public class AntennaRig : NetworkBehaviour
    {
        [SerializeField] string displayName = "Antena";
        [Tooltip("What the dish has to point at.")]
        [SerializeField] Transform target;
        [Tooltip("The part that tilts (around its local X); the dish points along its local Y.")]
        [SerializeField] Transform holder;
        [Tooltip("Where the signal beam starts (the dish).")]
        [SerializeField] Transform dish;
        [Tooltip("Grabbing this turns the whole antenna (A/D).")]
        [SerializeField] Graspable baseHandle;
        [Tooltip("Grabbing this tilts the holder (W/S).")]
        [SerializeField] Graspable holderHandle;
        [Tooltip("Rubble jamming the antenna: it can't be grabbed or turned until every one of these is destroyed.")]
        [SerializeField] Damageable[] blockers;

        [Header("Control")]
        [Tooltip("How far the holder can tilt (degrees; 0 = down, 90 = level, 180 = up). Below level the dish would go into the ground.")]
        [SerializeField, Range(0f, 180f)] float minTilt = 90f;
        [SerializeField, Range(0f, 180f)] float maxTilt = 180f;
        [Tooltip("Degrees per second right after a key is pressed, and after holding it for Ramp Time.")]
        [SerializeField] float minSpeed = 1.5f;
        [SerializeField] float maxSpeed = 30f;
        [SerializeField] float rampTime = 2.5f;

        [Header("Aligned")]
        [Tooltip("How close (degrees, on each axis) to the right angles counts as aimed.")]
        [SerializeField] float tolerance = 0.5f;
        [Tooltip("How long it has to stay aimed without being turned before it locks (seconds).")]
        [SerializeField] float settleTime = 0.6f;
        [SerializeField] Material signalMaterial;
        [SerializeField] float signalWidth = 0.6f;

        [Header("Hints")]
        [SerializeField] float hintRange = 22f;

        readonly NetworkVariable<float> yaw = new();
        readonly NetworkVariable<float> pitch = new();
        readonly NetworkVariable<bool> aligned = new();
        readonly NetworkVariable<double> alignedAt = new();

        float targetYaw, targetPitch;
        float shownYaw, shownPitch;
        float yawHold, pitchHold, idle;
        Targetable baseTarget, holderTarget;
        LineRenderer signal;

        public string DisplayName => displayName;
        /// <summary>Turn around its own axis, 0-360 (degrees).</summary>
        public float Yaw => yaw.Value;
        /// <summary>Tilt of the holder, 0 = down, 90 = level, 180 = up (degrees).</summary>
        public float Pitch => pitch.Value;
        public float TargetYaw => targetYaw;
        public float TargetPitch => targetPitch;
        /// <summary>The right tilt is within the holder's range (otherwise the antenna can never be aimed).</summary>
        public bool CanReachTarget => targetPitch >= minTilt - tolerance && targetPitch <= maxTilt + tolerance;
        public bool IsAligned => IsSpawned && aligned.Value;
        /// <summary>Someone is holding the antenna or its holder with the beam (every client).</summary>
        public bool IsHeld => (baseHandle != null && baseHandle.IsHeld) || (holderHandle != null && holderHandle.IsHeld);
        /// <summary>How many of its blocking rubble pieces are still there (every client: destroyed ones are gone).</summary>
        public int BlockersLeft
        {
            get
            {
                int left = 0;
                if (blockers != null)
                    foreach (var blocker in blockers)
                        if (blocker != null && (!blocker.IsSpawned || !blocker.IsDead)) left++;
                return left;
            }
        }
        public int BlockerCount => blockers != null ? blockers.Length : 0;
        public bool IsBlocked => BlockersLeft > 0;

        void Awake()
        {
            if (baseHandle != null) baseTarget = baseHandle.GetComponent<Targetable>();
            if (holderHandle != null) holderTarget = holderHandle.GetComponent<Targetable>();
            ComputeSolution();
        }

        // The right angles: the dish turns with the antenna and points along the holder's Y, which for a tilt p is
        // (0, -cos p, -sin p) before the turn; so the heading is the turn + 180 and the elevation is p - 90.
        void ComputeSolution()
        {
            if (target == null || holder == null) return;
            Vector3 d = target.position - holder.position;
            float heading = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float elevation = Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            targetYaw = Mathf.Repeat(heading - 180f, 360f);
            targetPitch = Mathf.Clamp(elevation + 90f, 0f, 180f);
        }

        public override void OnNetworkSpawn()
        {
            ComputeSolution();
            if (IsServer)
            {
                yaw.Value = Mathf.Repeat(transform.eulerAngles.y, 360f);
                pitch.Value = holder != null ? Mathf.Clamp(Mathf.DeltaAngle(0f, holder.localEulerAngles.x) + 180f, minTilt, maxTilt) : Mathf.Clamp(90f, minTilt, maxTilt);
            }
            shownYaw = yaw.Value;
            shownPitch = pitch.Value;
            Pose();
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (IsServer) ServerTick(Time.deltaTime);
            if (IsServer)
            {
                shownYaw = yaw.Value;
                shownPitch = pitch.Value;
            }
            else
            {
                float k = 1f - Mathf.Exp(-20f * Time.deltaTime);
                shownYaw = Mathf.LerpAngle(shownYaw, yaw.Value, k);
                shownPitch = Mathf.Lerp(shownPitch, pitch.Value, k);
            }
            Pose();
            UpdateSignal();
            bool grabbable = !aligned.Value && !IsBlocked;
            if (baseTarget != null) baseTarget.enabled = grabbable;
            if (holderTarget != null) holderTarget.enabled = grabbable;
        }

        void ServerTick(float dt)
        {
            if (aligned.Value) return;
            if (IsBlocked)
            {
                // Jammed by rubble: nothing turns, and nobody keeps hold of it.
                if (baseHandle != null && baseHandle.IsHeld) baseHandle.ServerForceRelease();
                if (holderHandle != null && holderHandle.IsHeld) holderHandle.ServerForceRelease();
                yawHold = pitchHold = idle = 0f;
                return;
            }
            float turnInput = baseHandle != null ? baseHandle.SteerInput.x : 0f;
            float tiltInput = holderHandle != null ? holderHandle.SteerInput.y : 0f;
            if (Mathf.Abs(turnInput) > 0.1f)
            {
                yawHold += dt;
                yaw.Value = Mathf.Repeat(yaw.Value + Mathf.Sign(turnInput) * Speed(yawHold) * dt, 360f);
            }
            else yawHold = 0f;
            if (Mathf.Abs(tiltInput) > 0.1f)
            {
                pitchHold += dt;
                pitch.Value = Mathf.Clamp(pitch.Value + Mathf.Sign(tiltInput) * Speed(pitchHold) * dt, minTilt, maxTilt);
            }
            else pitchHold = 0f;

            bool turning = yawHold > 0f || pitchHold > 0f;
            idle = turning ? 0f : idle + dt;
            if (IsOnTarget() && idle >= settleTime) ServerLock();
        }

        float Speed(float held) => Mathf.Lerp(minSpeed, maxSpeed, Mathf.SmoothStep(0f, 1f, held / Mathf.Max(0.01f, rampTime)));

        public bool IsOnTarget() => Mathf.Abs(Mathf.DeltaAngle(yaw.Value, targetYaw)) <= tolerance && Mathf.Abs(pitch.Value - targetPitch) <= tolerance;

        /// <summary>Server: lock it as aimed (also tests).</summary>
        public void ServerLock()
        {
            if (!IsServer || aligned.Value) return;
            aligned.Value = true;
            alignedAt.Value = NetworkManager.ServerTime.Time;
            if (baseHandle != null)
                baseHandle.ServerForceRelease();
            if (holderHandle != null)
                holderHandle.ServerForceRelease();
        }

        /// <summary>Server: set the angles directly (tests).</summary>
        public void ServerSetAngles(float turn, float tilt)
        {
            if (!IsServer || aligned.Value) return;
            yaw.Value = Mathf.Repeat(turn, 360f);
            pitch.Value = Mathf.Clamp(tilt, minTilt, maxTilt);
            idle = 0f;
        }

        void Pose()
        {
            transform.rotation = Quaternion.Euler(0f, shownYaw, 0f);
            if (holder != null) holder.localRotation = Quaternion.Euler(shownPitch - 180f, 0f, 0f);
        }

        void UpdateSignal()
        {
            if (!aligned.Value || target == null)
            {
                if (signal != null) signal.enabled = false;
                return;
            }
            if (signal == null)
            {
                var go = new GameObject("Signal");
                go.transform.SetParent(transform, false);
                signal = go.AddComponent<LineRenderer>();
                signal.useWorldSpace = true;
                signal.positionCount = 2;
                signal.sharedMaterial = signalMaterial;
                signal.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                signal.numCapVertices = 4;
            }
            signal.enabled = true;
            float since = (float)(NetworkManager.ServerTime.Time - alignedAt.Value);
            Vector3 from = dish != null ? dish.position : holder.position;
            // The beam shoots out over half a second, then pulses gently.
            float reach = Mathf.Clamp01(since / 0.5f);
            signal.SetPosition(0, from);
            signal.SetPosition(1, Vector3.Lerp(from, target.position, reach));
            float width = signalWidth * (0.85f + 0.15f * Mathf.Sin(Time.time * 5f));
            signal.startWidth = width;
            signal.endWidth = width * 0.5f;
        }

        void OnGUI()
        {
            if (!IsSpawned || CutsceneSystem.HidesGameplayUI) return;
            var player = PlayerHealth.Local;
            if (player == null) return;
            string text = null;
            bool holdingBase = baseHandle != null && baseHandle.IsHeld && baseHandle.HolderClientId == player.OwnerClientId;
            bool holdingHolder = holderHandle != null && holderHandle.IsHeld && holderHandle.HolderClientId == player.OwnerClientId;
            if (holdingBase || holdingHolder)
            {
                string turn = $"Obrót: {yaw.Value:0.0}°" + (holdingBase ? "  [A / D]" : "");
                string tilt = $"Nachylenie: {pitch.Value:0.0}°" + (holdingHolder ? "  [W / S]" : "");
                text = displayName + "   |   " + (holdingBase ? "<b>" + turn + "</b>" : turn) + "   |   " + (holdingHolder ? "<b>" + tilt + "</b>" : tilt);
            }
            else if (!aligned.Value && (player.transform.position - transform.position).sqrMagnitude <= hintRange * hintRange)
                text = IsBlocked
                    ? $"{displayName} jest zablokowana przez gruz — zniszcz go (zostało {BlockersLeft} z {BlockerCount})"
                    : displayName + ": chwyć kluczem (PPM + LPM) — słup obraca antenę, ramię ją nachyla";
            if (text == null) return;
            var style = new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.MiddleCenter, richText = true };
            GUI.Box(new Rect(Screen.width * 0.5f - 430f, Screen.height - 240f, 860f, 44f), text, style);
        }

        /// <summary>Editor setup.</summary>
        public void Setup(string nameText, Transform targetTransform, Transform holderTransform, Transform dishTransform,
            Graspable baseGrip, Graspable holderGrip, Material signalMat)
        {
            displayName = nameText;
            target = targetTransform;
            holder = holderTransform;
            dish = dishTransform;
            baseHandle = baseGrip;
            holderHandle = holderGrip;
            signalMaterial = signalMat;
            ComputeSolution();
        }

        /// <summary>Editor setup: the rubble that jams it.</summary>
        public void SetBlockers(Damageable[] value) => blockers = value;

        void OnDrawGizmosSelected()
        {
            if (target == null || holder == null) return;
            Gizmos.color = new Color(0.3f, 1f, 0.5f);
            Gizmos.DrawLine(holder.position, target.position);
            Gizmos.color = new Color(1f, 0.6f, 0.2f);
            Gizmos.DrawRay(holder.position, holder.up * 30f);
        }
    }
}
