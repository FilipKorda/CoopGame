using UnityEngine;
using UnityEngine.AI;

namespace CoopGame
{
    /// <summary>
    /// Cosmetic island life: the NPC walks (NavMeshAgent) to random spots inside its <see cref="NpcSector"/> and
    /// stands there for a while (IDLE). Sits on the NPC's root (the old marker cube); <see cref="NpcProceduralGait"/>
    /// on the model child reads how fast the root moves and switches IDLE/WALK by itself.
    ///
    /// Networking: purely local and cosmetic, run the same way on the host and on every client, no NetworkObject
    /// and no RPCs. Each NPC's random choices are seeded from its name, so all machines start the same walk, but
    /// players may see an NPC a few steps apart later on. Nothing in gameplay depends on where these NPCs stand.
    ///
    /// Cutscenes: while one plays (<see cref="CutsceneSystem.IsPlaying"/>) the wanderer lets go of the root, so a
    /// timeline can move it (A2_Middle moves Island President (1)); afterwards it carries on from wherever the root
    /// is and walks back into its sector. The agent only plans the path (updatePosition off); this script moves the
    /// root and puts the feet on the ground colliders.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class NpcWanderer : MonoBehaviour
    {
        public NpcSector sector;
        [Tooltip("Walking speed in m/s.")]
        public float walkSpeed = 1.0f;
        [Tooltip("Seconds of standing (IDLE) between walks, random in this range.")]
        public Vector2 idleSeconds = new Vector2(3f, 9f);
        [Tooltip("Don't pick spots closer than this to the sector's edge.")]
        public float edgeMargin = 0.6f;
        [Tooltip("Degrees per second when turning.")]
        public float turnSpeed = 220f;
        [Tooltip("Layers the feet stand on.")]
        public LayerMask groundMask = 1;

        NavMeshAgent agent;
        Collider[] own;
        System.Random rng;
        float rootAboveFeet;
        float idleUntil;
        bool walking, ready, paused;
        NavMeshPath path;
        readonly RaycastHit[] hits = new RaycastHit[8];

        public bool IsWalking => walking;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            agent.enabled = false;
            own = GetComponentsInChildren<Collider>(true);
            rng = new System.Random(Hash(gameObject.name));
            path = new NavMeshPath();
        }

        void Start()
        {
            float ground = GroundY(transform.position + Vector3.up * 0.5f, 4f, transform.position.y - 1f);
            rootAboveFeet = transform.position.y - ground;
            TryAttach();
            idleUntil = Time.time + Mathf.Lerp(0.5f, idleSeconds.y, (float)rng.NextDouble());
        }

        static int Hash(string s)
        {
            unchecked { int h = 23; foreach (char c in s) h = h * 31 + c; return h; }
        }

        Vector3 Feet => transform.position - Vector3.up * rootAboveFeet;

        bool TryAttach()
        {
            if (!NavMesh.SamplePosition(Feet, out var hit, 2.5f, NavMesh.AllAreas)) return ready = false;
            // Put the agent on the mesh without moving the visible root.
            Vector3 keep = transform.position;
            transform.position = hit.position;
            agent.enabled = true;
            transform.position = keep;
            agent.updatePosition = false;
            agent.updateRotation = false;
            agent.speed = walkSpeed;
            agent.Warp(hit.position);
            walking = false;
            return ready = agent.isOnNavMesh;
        }

        void OnDisable()
        {
            if (agent != null && agent.enabled) agent.enabled = false;
            ready = false;
            walking = false;
        }

        void OnEnable()
        {
            // Re-attach on the next frame (also after the GameObject was switched off and on by a cutscene).
            ready = false;
        }

        void Update()
        {
            if (CutsceneSystem.IsPlaying)
            {
                if (!paused) { paused = true; if (agent.enabled) agent.enabled = false; walking = false; ready = false; }
                return;
            }
            if (paused) { paused = false; idleUntil = Time.time + 1f; }
            if (!ready)
            {
                if (Time.frameCount % 15 == 0) TryAttach();
                return;
            }

            if (walking)
            {
                if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.05f)
                {
                    walking = false;
                    agent.ResetPath();
                    idleUntil = Time.time + Mathf.Lerp(idleSeconds.x, idleSeconds.y, (float)rng.NextDouble());
                }
            }
            else if (Time.time >= idleUntil)
            {
                walking = PickDestination();
                if (!walking) idleUntil = Time.time + 1f;
            }

            // Follow the agent: x/z from the agent, height from the ground under it.
            Vector3 next = agent.nextPosition;
            Vector3 feet = new Vector3(next.x, GroundY(next + Vector3.up * 1.2f, 3f, next.y), next.z);
            transform.position = feet + Vector3.up * rootAboveFeet;

            Vector3 flat = walking ? agent.desiredVelocity : Vector3.zero;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.04f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat), turnSpeed * Time.deltaTime);
        }

        bool PickDestination()
        {
            if (sector == null) return false;
            Vector3 feet = Feet;
            for (int i = 0; i < 12; i++)
            {
                Vector3 c = sector.RandomPoint(rng);
                c.y = feet.y;
                if (!NavMesh.SamplePosition(c, out var hit, 3f, NavMesh.AllAreas)) continue;
                if (!sector.Contains(hit.position, edgeMargin)) continue;
                if ((hit.position - feet).sqrMagnitude < 2.25f) continue;
                if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                // Outside its sector (after a cutscene) it may walk any way back in; inside, the path must stay inside.
                if (sector.Contains(feet) && !PathInside(path)) continue;
                agent.SetPath(path);
                return true;
            }
            return false;
        }

        bool PathInside(NavMeshPath p)
        {
            var corners = p.corners;
            for (int i = 1; i < corners.Length; i++)
                for (int k = 1; k <= 4; k++)
                    if (!sector.Contains(Vector3.Lerp(corners[i - 1], corners[i], k / 4f))) return false;
            return true;
        }

        float GroundY(Vector3 from, float down, float fallback)
        {
            int n = Physics.RaycastNonAlloc(from, Vector3.down, hits, down, groundMask, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                if (System.Array.IndexOf(own, hits[i].collider) >= 0) continue;
                if (hits[i].collider.GetComponentInParent<NpcWanderer>() != null) continue;
                if (hits[i].point.y > best) best = hits[i].point.y;
            }
            return best > float.NegativeInfinity ? best : fallback;
        }

        void OnDrawGizmosSelected()
        {
            if (sector == null) return;
            Gizmos.color = sector.gizmoColor;
            Gizmos.DrawLine(transform.position, sector.transform.position);
        }
    }
}
