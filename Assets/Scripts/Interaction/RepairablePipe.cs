using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// A broken pipe that two players put back with the wrench's magnetic beam ("magic"). It has two handles
    /// (Fixed <see cref="Graspable"/>s on separate objects that follow the pipe); it only moves while both are held,
    /// floating in the average of the holders' steering, so the players have to push the same way. The closer it
    /// gets to its slot, the more it straightens out; near the slot it is pulled in, snaps into place and counts as
    /// repaired for good (<see cref="TaskObjective"/> waits for it). A see-through ghost shows the slot until then.
    /// Server moves the pipe and its handles; their server-authoritative NetworkTransforms replicate them.
    /// </summary>
    public class RepairablePipe : NetworkBehaviour
    {
        static readonly List<RepairablePipe> s_All = new();

        [Tooltip("The two beam handles; both must be held for the pipe to move.")]
        [SerializeField] Graspable[] handles;
        [Tooltip("World pose of the pipe once repaired (where it snaps to).")]
        [SerializeField] Vector3 repairedPosition;
        [SerializeField] Quaternion repairedRotation = Quaternion.identity;
        [Tooltip("Floating speed while both handles are held, m/s.")]
        [SerializeField] float moveSpeed = 1.3f;
        [Tooltip("Within this distance of the slot the beam pulls the pipe in by itself.")]
        [SerializeField] float assistDistance = 1.2f;
        [SerializeField] float assistSpeed = 0.8f;
        [Tooltip("Snaps into place at this distance from the slot.")]
        [SerializeField] float snapDistance = 0.35f;
        [Tooltip("Can't be pushed farther than this from the slot (the start counts as at least this far).")]
        [SerializeField] float maxDistance = 5f;

        [Header("Visuals")]
        [Tooltip("See-through material of the ghost pipe marking the slot.")]
        [SerializeField] Material ghostMaterial;
        [Tooltip("Glow shell around the pipe while the beam holds it.")]
        [SerializeField] Material glowMaterial;
        [SerializeField] Color ghostColor = new Color(0.3f, 0.9f, 1f, 0.28f);
        [SerializeField] Color glowColor = new Color(0.75f, 0.35f, 1f, 0.45f);
        [SerializeField] Color repairedFlashColor = new Color(0.4f, 1f, 0.6f, 0.7f);

        readonly NetworkVariable<bool> repaired = new();

        // Server state
        Vector3 brokenPosition;
        Quaternion brokenRotation;
        float brokenDistance;
        Vector3[] handleOffsets;
        Quaternion[] handleRotations;

        // Visuals (every client)
        Renderer ghost, glow;
        MaterialPropertyBlock block;
        float repairedAt = -100f;
        float messageUntil = -100f;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        public bool IsRepaired => repaired.Value;

        void Awake()
        {
            brokenPosition = transform.position;
            brokenRotation = transform.rotation;
            brokenDistance = Mathf.Max(FlatDistance(brokenPosition, repairedPosition), snapDistance + 0.01f);

            int count = handles != null ? handles.Length : 0;
            handleOffsets = new Vector3[count];
            handleRotations = new Quaternion[count];
            Quaternion inverse = Quaternion.Inverse(transform.rotation);
            for (int i = 0; i < count; i++)
            {
                if (handles[i] == null) continue;
                handleOffsets[i] = inverse * (handles[i].transform.position - transform.position);
                handleRotations[i] = inverse * handles[i].transform.rotation;
            }

            block = new MaterialPropertyBlock();
        }

        void Start()
        {
            ghost = CreateCopy("Pipe Slot Ghost", ghostMaterial, null, 1.02f);
            if (ghost != null) ghost.transform.SetPositionAndRotation(repairedPosition, repairedRotation);
            glow = CreateCopy("Pipe Magic Glow", glowMaterial, transform, 1.08f);
        }

        void OnEnable() => s_All.Add(this);
        void OnDisable() => s_All.Remove(this);

        public override void OnNetworkSpawn()
        {
            repaired.OnValueChanged += OnRepairedChanged;
            if (repaired.Value) ApplyRepaired(instant: true);
        }

        public override void OnNetworkDespawn()
        {
            repaired.OnValueChanged -= OnRepairedChanged;
        }

        void OnRepairedChanged(bool previous, bool current)
        {
            if (current) ApplyRepaired(instant: false);
        }

        void ApplyRepaired(bool instant)
        {
            // The handles' job is over: nobody can grab them any more (their lamps stay lit, see BeamSwitch.MarkDone).
            if (handles != null)
                foreach (var handle in handles)
                {
                    if (handle == null) continue;
                    handle.enabled = false;
                    if (handle.TryGetComponent(out Targetable targetable)) targetable.enabled = false;
                }
            if (!instant)
            {
                repairedAt = Time.time;
                messageUntil = Time.time + 4f;
            }
        }

        void Update()
        {
            if (IsServer && IsSpawned && !repaired.Value) ServerTick(Time.deltaTime);
            UpdateVisuals();
        }

        // ---------- Server ----------

        void ServerTick(float dt)
        {
            if (HeldCount() == handles.Length && handles.Length > 0)
            {
                Vector3 steer = Vector3.zero;
                foreach (var handle in handles) steer += handle.SteerWorld;
                steer /= handles.Length; // pushing against each other cancels out
                steer.y = 0f;

                Vector3 position = transform.position + steer * (moveSpeed * dt);

                // Keep it near its slot.
                Vector3 flat = position - repairedPosition;
                flat.y = 0f;
                float limit = Mathf.Max(maxDistance, brokenDistance);
                if (flat.magnitude > limit) position -= flat - flat.normalized * limit;

                // Near the slot the beam pulls it in.
                float distance = FlatDistance(position, repairedPosition);
                if (distance < assistDistance)
                {
                    Vector3 pull = repairedPosition - position;
                    pull.y = 0f;
                    position += Vector3.ClampMagnitude(pull, assistSpeed * dt);
                    distance = FlatDistance(position, repairedPosition);
                }

                // Straighten and level out as it gets closer.
                float progress = 1f - Mathf.Clamp01(distance / brokenDistance);
                float eased = Mathf.SmoothStep(0f, 1f, progress);
                position.y = Mathf.Lerp(brokenPosition.y, repairedPosition.y, eased);
                Quaternion rotation = Quaternion.Slerp(brokenRotation, repairedRotation, eased);

                if (distance <= snapDistance)
                {
                    ServerSnap();
                    return;
                }

                transform.SetPositionAndRotation(position, rotation);
            }

            MoveHandles();
        }

        void ServerSnap()
        {
            transform.SetPositionAndRotation(repairedPosition, repairedRotation);
            MoveHandles();
            repaired.Value = true;
            foreach (var handle in handles)
            {
                if (handle == null) continue;
                handle.ServerForceRelease();
                if (handle.TryGetComponent(out BeamSwitch beamSwitch)) beamSwitch.MarkDone();
            }
        }

        void MoveHandles()
        {
            for (int i = 0; i < handles.Length; i++)
            {
                if (handles[i] == null) continue;
                handles[i].transform.SetPositionAndRotation(transform.position + transform.rotation * handleOffsets[i],
                    transform.rotation * handleRotations[i]);
            }
        }

        int HeldCount()
        {
            int held = 0;
            if (handles != null)
                foreach (var handle in handles)
                    if (handle != null && handle.IsHeld) held++;
            return held;
        }

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        // ---------- Visuals (every client) ----------

        Renderer CreateCopy(string name, Material material, Transform parent, float scale)
        {
            if (material == null || !TryGetComponent(out MeshFilter source) || source.sharedMesh == null) return null;
            var go = new GameObject(name);
            if (parent != null)
            {
                // Grow around the mesh centre, not the pivot (the pipe's pivot is at one end).
                go.transform.SetParent(parent, false);
                go.transform.localScale = Vector3.one * scale;
                go.transform.localPosition = -source.sharedMesh.bounds.center * (scale - 1f);
            }
            else
            {
                go.transform.localScale = transform.lossyScale * scale;
            }
            go.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.SetActive(false);
            return renderer;
        }

        void UpdateVisuals()
        {
            bool isRepaired = repaired.Value;

            if (ghost != null)
            {
                bool show = !isRepaired;
                if (ghost.gameObject.activeSelf != show) ghost.gameObject.SetActive(show);
                if (show) SetColor(ghost, ghostColor, 0.55f + 0.45f * Mathf.Sin(Time.time * 3f));
            }

            if (glow != null)
            {
                int held = handles != null ? HeldCount() : 0;
                float flash = Mathf.Clamp01(1f - (Time.time - repairedAt) / 1.2f);
                bool show = (!isRepaired && held > 0) || flash > 0f;
                if (glow.gameObject.activeSelf != show) glow.gameObject.SetActive(show);
                if (show)
                {
                    if (flash > 0f)
                        SetColor(glow, repairedFlashColor, flash);
                    else if (held == handles.Length)
                        SetColor(glow, glowColor, 0.8f + 0.2f * Mathf.Sin(Time.time * 12f));
                    else // one player alone: weak flicker, it won't budge
                        SetColor(glow, glowColor, 0.25f + 0.2f * Mathf.PerlinNoise(Time.time * 9f, 0.3f));
                }
            }
        }

        void SetColor(Renderer renderer, Color color, float alphaScale)
        {
            color.a *= Mathf.Clamp01(alphaScale);
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            renderer.SetPropertyBlock(block);
        }

        void OnGUI()
        {
            if (CutsceneSystem.HidesGameplayUI) return;
            var style = new GUIStyle(GUI.skin.box) { fontSize = 20, alignment = TextAnchor.MiddleCenter };

            if (Time.time < messageUntil)
            {
                int done = 0;
                foreach (var pipe in s_All)
                    if (pipe.IsRepaired) done++;
                GUI.Box(new Rect(Screen.width * 0.5f - 220f, 180f, 440f, 40f), $"Rura naprawiona ({done}/{s_All.Count})", style);
                return;
            }

            // Holding one handle alone: tell the player they need a partner.
            if (repaired.Value || handles == null || NetworkManager == null) return;
            bool mine = false;
            foreach (var handle in handles)
                if (handle != null && handle.IsHeld && handle.HolderClientId == NetworkManager.LocalClientId) mine = true;
            if (mine && HeldCount() < handles.Length)
                GUI.Box(new Rect(Screen.width * 0.5f - 260f, 180f, 520f, 40f), "Za ciężka! Drugi gracz musi chwycić drugi uchwyt", style);
        }

        public override void OnDestroy()
        {
            if (ghost != null) Destroy(ghost.gameObject);
            base.OnDestroy();
        }

        /// <summary>Editor setup.</summary>
        public void Setup(Graspable[] pipeHandles, Vector3 slotPosition, Quaternion slotRotation, Material ghostMat, Material glowMat)
        {
            handles = pipeHandles;
            repairedPosition = slotPosition;
            repairedRotation = slotRotation;
            ghostMaterial = ghostMat;
            glowMaterial = glowMat;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.9f, 1f);
            Gizmos.DrawLine(transform.position, repairedPosition);
            Gizmos.DrawWireSphere(repairedPosition, snapDistance);
        }
    }
}
