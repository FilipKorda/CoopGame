using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// The heavy sphere of the <see cref="MazeRoomPuzzle"/>. It has two handles (Fixed <see cref="Graspable"/>s on its
    /// yoke) and only rolls while both are held, in the average of the holders' steering, so both players have to push
    /// the same way. Let go near a dock and it rolls into it; lying in dock N it opens the bolts of colour N in the
    /// mazes and glows in that colour. Once the room is solved it locks in its dock for good.
    /// The server rolls it; a server-authoritative NetworkTransform replicates the position and every machine spins
    /// the ball from its own movement.
    /// </summary>
    public class DockSphere : NetworkBehaviour
    {
        [SerializeField] MazeRoomPuzzle room;
        [Tooltip("Both must be held for the sphere to roll.")]
        [SerializeField] Graspable[] handles;
        [Tooltip("Dock N is element N-1; the sphere counts as in a dock while it rests on its centre.")]
        [SerializeField] Transform[] docks;
        [Tooltip("The ball itself: spins as the sphere rolls (the yoke with the handles doesn't).")]
        [SerializeField] Transform model;
        [SerializeField] float radius = 0.625f;
        [SerializeField] float moveSpeed = 1.5f;
        [Tooltip("Not pushed within this distance (metres, flat) of a dock, it rolls into it by itself.")]
        [SerializeField] float snapRadius = 0.9f;
        [SerializeField] float snapSpeed = 1.2f;
        [Tooltip("Where its centre may go (world X and Z).")]
        [SerializeField] Vector2 areaMin;
        [SerializeField] Vector2 areaMax;
        [SerializeField] LayerMask collisionMask = ~0;

        [Header("Visuals")]
        [Tooltip("Glow bands on the ball: the colour of the dock it lies in.")]
        [SerializeField] Renderer[] glowRenderers;
        [Tooltip("Per dock (element N-1 = dock N): dim in its colour, bright while the sphere lies in it.")]
        [SerializeField] Renderer[] dockLights;

        readonly NetworkVariable<int> dock = new();
        readonly NetworkVariable<bool> locked = new();

        float restY;
        Vector3 lastPosition;
        Vector3 modelRest;
        Targetable[] targets;
        GUIStyle hintStyle;

        /// <summary>Dock (1-based) the sphere lies in; 0 while it's between docks.</summary>
        public int DockNumber => dock.Value;
        public bool IsLocked => locked.Value;
        public Graspable[] Handles => handles;

        void Awake()
        {
            restY = transform.position.y;
            lastPosition = transform.position;
            if (model != null) modelRest = model.localPosition;
            int count = handles != null ? handles.Length : 0;
            targets = new Targetable[count];
            for (int i = 0; i < count; i++) targets[i] = handles[i] != null ? handles[i].GetComponent<Targetable>() : null;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) dock.Value = DockAt(transform.position, 0.05f);
            lastPosition = transform.position;
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (IsServer) ServerTick(Time.deltaTime);
            UpdateVisuals();
        }

        // ---------- Server ----------

        void ServerTick(float dt)
        {
            if (locked.Value) return;

            Vector3 position = transform.position;
            Vector3 steer = Vector3.zero;
            if (HeldCount() == handles.Length && handles.Length > 0)
            {
                foreach (var handle in handles) steer += handle.SteerWorld;
                steer /= handles.Length; // pushing against each other cancels out
                steer.y = 0f;
            }

            if (steer.magnitude > 0.2f)
            {
                position += Sweep(position, steer * (moveSpeed * dt));
            }
            else
            {
                int near = DockAt(position, snapRadius);
                if (near > 0)
                {
                    Vector3 centre = docks[near - 1].position;
                    centre.y = position.y;
                    position = Vector3.MoveTowards(position, centre, snapSpeed * dt);
                }
            }

            position.x = Mathf.Clamp(position.x, areaMin.x, areaMax.x);
            position.z = Mathf.Clamp(position.z, areaMin.y, areaMax.y);
            position.y = restY;
            if ((transform.position - position).sqrMagnitude > 1e-10f) transform.position = position;

            int now = DockAt(position, 0.03f);
            if (dock.Value != now) dock.Value = now;
        }

        /// <summary>Nearest dock (1-based) within <paramref name="reach"/> metres (flat) of <paramref name="position"/>, or 0.</summary>
        int DockAt(Vector3 position, float reach)
        {
            int best = 0;
            float bestDistance = reach * reach;
            if (docks == null) return 0;
            for (int i = 0; i < docks.Length; i++)
            {
                if (docks[i] == null) continue;
                Vector3 d = docks[i].position - position;
                d.y = 0f;
                if (d.sqrMagnitude <= bestDistance) { bestDistance = d.sqrMagnitude; best = i + 1; }
            }
            return best;
        }

        /// <summary>How much of <paramref name="step"/> the ball can roll before touching something (players don't stop it).</summary>
        Vector3 Sweep(Vector3 from, Vector3 step)
        {
            float distance = step.magnitude;
            if (distance < 0.00001f) return step;
            Vector3 direction = step / distance;
            float allowed = distance;
            foreach (var hit in Physics.SphereCastAll(from, radius * 0.95f, direction, distance, collisionMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<PlayerMovement>() != null) continue;
                if (hit.distance <= 0f)
                {
                    Vector3 toward = hit.collider.ClosestPoint(from) - from;
                    if (toward.sqrMagnitude > 0.000001f && Vector3.Dot(toward.normalized, direction) > 0.3f) return Vector3.zero;
                    continue;
                }
                allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - 0.02f));
            }
            return direction * allowed;
        }

        int HeldCount()
        {
            int held = 0;
            if (handles != null)
                foreach (var handle in handles)
                    if (handle != null && handle.IsHeld) held++;
            return held;
        }

        /// <summary>Server: the room is solved; the sphere stays where it is for good and its handles let go.</summary>
        public void ServerLock()
        {
            if (!IsServer || locked.Value) return;
            locked.Value = true;
            if (handles != null)
                foreach (var handle in handles)
                    if (handle != null) handle.ServerForceRelease();
        }

        /// <summary>Server, for tests: put the sphere straight into dock <paramref name="number"/> (1-based).</summary>
        public void ServerPlaceInDock(int number)
        {
            if (!IsServer || locked.Value || docks == null || number < 1 || number > docks.Length) return;
            Vector3 centre = docks[number - 1].position;
            centre.y = restY;
            transform.position = centre;
            dock.Value = number;
        }

        // ---------- Every machine ----------

        void UpdateVisuals()
        {
            bool done = locked.Value;
            for (int i = 0; i < targets.Length; i++)
            {
                var handle = handles[i];
                if (handle == null) continue;
                if (done && handle.enabled) handle.enabled = false; // drops the holder
                if (targets[i] != null)
                {
                    bool aimable = !done && handle.enabled && !handle.IsHeld;
                    if (targets[i].enabled != aimable) targets[i].enabled = aimable;
                }
            }

            // Spin the ball the way it rolls.
            Vector3 delta = transform.position - lastPosition;
            delta.y = 0f;
            lastPosition = transform.position;
            if (model != null)
            {
                if (delta.sqrMagnitude > 1e-8f && delta.sqrMagnitude < 4f)
                {
                    Vector3 axis = Vector3.Cross(Vector3.up, delta).normalized;
                    model.rotation = Quaternion.AngleAxis(delta.magnitude / radius * Mathf.Rad2Deg, axis) * model.rotation;
                }
                int held = HeldCount();
                bool straining = !done && held > 0 && held < handles.Length;
                model.localPosition = modelRest + (straining ? Random.insideUnitSphere * 0.025f : Vector3.zero);
            }

            if (room == null) return;
            int current = dock.Value;
            Material glow = current > 0 ? room.DockGlow(current) : room.DockDim(room.FinalDock);
            if (glowRenderers != null)
                foreach (var r in glowRenderers) MazeRoomPuzzle.SetMaterial(r, glow);
            if (dockLights != null)
                for (int i = 0; i < dockLights.Length; i++)
                    MazeRoomPuzzle.SetMaterial(dockLights[i], i + 1 == current ? room.DockGlow(i + 1) : room.DockDim(i + 1));
        }

        void OnGUI()
        {
            if (!IsSpawned || locked.Value || CutsceneSystem.HidesGameplayUI || NetworkManager == null || handles == null) return;
            ulong me = NetworkManager.LocalClientId;
            bool mine = false;
            foreach (var handle in handles)
                if (handle != null && handle.IsHeld && handle.HolderClientId == me) mine = true;
            if (!mine) return;

            string text = HeldCount() < handles.Length
                ? "Kula jest za ciężka — drugi gracz musi złapać drugi uchwyt."
                : "Pchajcie kulę razem w tę samą stronę. Puśćcie ją przy docku, to sama się do niego wtoczy.";
            hintStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 20, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            var content = new GUIContent(text);
            float width = Mathf.Min(900f, Screen.width - 40f);
            float height = Mathf.Max(48f, hintStyle.CalcHeight(content, width) + 10f);
            GUI.Box(new Rect((Screen.width - width) * 0.5f, Screen.height * 0.74f, width, height), content, hintStyle);
        }
    }
}
