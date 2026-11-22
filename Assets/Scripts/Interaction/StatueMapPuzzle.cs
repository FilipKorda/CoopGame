using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Hidden Room 2 (A2_Start): the statue map. Nine heavy statues (<see cref="HeavyObject"/>, two handles: both players)
    /// have to stand on the floor panels that the wall map shows for them. The map sits behind a cover that slides open
    /// only while both map buttons (<see cref="BeamSwitch"/> Hold) are held at the same time.
    /// Server: counts the statues standing on their own panel (the locked antenna panel shows "x/9"), and glides a
    /// statue that was let go of near a free panel onto it (any panel, so that gives nothing away). All placed = solved
    /// for good: the antenna panel unlocks, the cover closes, and the statues and map buttons switch off on every machine.
    /// </summary>
    public class StatueMapPuzzle : NetworkBehaviour
    {
        [SerializeField] HeavyObject[] statues;
        [Tooltip("Per statue: the floor panel it belongs on (all of them are also the panels statues glide onto).")]
        [SerializeField] Transform[] spots;
        [Tooltip("Counts as placed this close (metres, flat) to its own panel's centre, while nobody holds it.")]
        [SerializeField] float placedTolerance = 0.3f;
        [Tooltip("A statue let go of this close (metres, flat) to a free panel glides onto its centre.")]
        [SerializeField] float snapRadius = 0.7f;
        [SerializeField] float snapSpeed = 1.2f;
        [Tooltip("Seconds all statues must stay placed before it counts.")]
        [SerializeField] float settleTime = 0.5f;

        [Header("Map")]
        [SerializeField] BeamSwitch[] mapButtons;
        [SerializeField] SlideLeaves mapCover;

        readonly NetworkVariable<int> placed = new();
        readonly NetworkVariable<bool> mapOpen = new();
        readonly NetworkVariable<bool> solved = new();

        Graspable[][] handles;
        BoxCollider[] bodies;
        float solvedSince = -1f;
        bool switchedOff;

        public bool IsSolved => solved.Value;
        /// <summary>Statues on their own panel (every machine).</summary>
        public int Placed => placed.Value;
        public int Count => statues != null ? statues.Length : 0;
        public bool MapOpen => mapOpen.Value;
        /// <summary>Short state for the locked antenna panel's screen.</summary>
        public string ProgressText => $"POSĄGI NA MIEJSCU: {placed.Value}/{Count}";

        void Awake()
        {
            int n = Count;
            handles = new Graspable[n][];
            bodies = new BoxCollider[n];
            for (int i = 0; i < n; i++)
            {
                if (statues[i] == null) { handles[i] = new Graspable[0]; continue; }
                handles[i] = statues[i].GetComponentsInChildren<Graspable>(true);
                bodies[i] = statues[i].GetComponent<BoxCollider>();
            }
        }

        public override void OnNetworkSpawn()
        {
            solved.OnValueChanged += OnSolvedChanged;
            if (solved.Value) SwitchOff();
        }

        public override void OnNetworkDespawn()
        {
            solved.OnValueChanged -= OnSolvedChanged;
        }

        void OnSolvedChanged(bool previous, bool current)
        {
            if (current) SwitchOff();
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (IsServer) ServerTick(Time.deltaTime);
            if (mapCover != null) mapCover.SetOpen(mapOpen.Value);
        }

        // ---------- Server ----------

        void ServerTick(float dt)
        {
            if (solved.Value) return;

            bool open = mapButtons != null && mapButtons.Length > 0;
            if (open)
                foreach (var button in mapButtons)
                    if (button == null || !button.IsOn) { open = false; break; }
            if (mapOpen.Value != open) mapOpen.Value = open;

            int count = 0;
            for (int i = 0; i < Count; i++)
            {
                if (statues[i] == null || spots == null || i >= spots.Length || spots[i] == null || Held(i)) continue;
                Snap(i, dt);
                if (Flat(statues[i].transform.position - spots[i].position) <= placedTolerance) count++;
            }
            if (placed.Value != count) placed.Value = count;

            if (count < Count || Count == 0)
            {
                solvedSince = -1f;
                return;
            }
            if (solvedSince < 0f) solvedSince = Time.time;
            if (Time.time - solvedSince < settleTime) return;

            solved.Value = true;
            mapOpen.Value = false;
            if (mapButtons != null)
                foreach (var button in mapButtons)
                    if (button != null) button.MarkDone();
        }

        bool Held(int i)
        {
            foreach (var h in handles[i])
                if (h != null && h.IsHeld) return true;
            return false;
        }

        /// <summary>Let go of near a free panel: glide onto its centre.</summary>
        void Snap(int i, float dt)
        {
            Transform t = statues[i].transform;
            Transform best = null;
            float bestDistance = snapRadius;
            foreach (var spot in spots)
            {
                if (spot == null) continue;
                float d = Flat(t.position - spot.position);
                if (d > bestDistance || Taken(spot, i)) continue;
                best = spot;
                bestDistance = d;
            }
            if (best == null || bestDistance < 0.003f) return;

            Vector3 to = best.position - t.position;
            to.y = 0f;
            Vector3 step = to.normalized * Mathf.Min(to.magnitude, snapSpeed * dt);
            BoxCollider body = bodies[i];
            if (body == null) { t.position += step; return; }
            // Up over the panel's lip, across, back down (HeavyObject's gravity settles it).
            Vector3 up = ObjectSweep.Sweep(body, t, Vector3.up * StepUp, ~0);
            t.position += up;
            t.position += ObjectSweep.Sweep(body, t, step, ~0);
            t.position += ObjectSweep.Sweep(body, t, Vector3.down * up.y, ~0);
        }

        const float StepUp = 0.08f;

        /// <summary>Another statue already stands on (or right next to) this panel.</summary>
        bool Taken(Transform spot, int except)
        {
            for (int k = 0; k < Count; k++)
                if (k != except && statues[k] != null && Flat(statues[k].transform.position - spot.position) < 0.9f) return true;
            return false;
        }

        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        // ---------- Every machine ----------

        /// <summary>Solved: statue handles and map buttons can't be grabbed any more (switching a grip off drops its holder).</summary>
        void SwitchOff()
        {
            if (switchedOff) return;
            switchedOff = true;
            if (handles != null)
                foreach (var set in handles)
                    foreach (var grip in set)
                    {
                        if (grip == null) continue;
                        grip.enabled = false;
                        var target = grip.GetComponent<Targetable>();
                        if (target != null) target.enabled = false;
                        foreach (var r in grip.GetComponentsInChildren<Renderer>()) r.enabled = false; // the socket cube
                    }
            if (mapButtons != null)
                foreach (var button in mapButtons)
                {
                    if (button == null) continue;
                    var grip = button.GetComponent<Graspable>();
                    if (grip != null) grip.enabled = false;
                    var target = button.GetComponent<Targetable>();
                    if (target != null) target.enabled = false;
                }
            if (mapCover != null) mapCover.SetOpen(false);
        }
    }
}
