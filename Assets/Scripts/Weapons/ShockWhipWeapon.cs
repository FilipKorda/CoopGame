using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoopGame
{
    /// <summary>
    /// Secondary melee weapon: an electric whip with a long reach. Attack swings it in a wide arc in front of the
    /// player (alternately right-to-left and left-to-right); everything the lash sweeps over takes 2 damage, and every
    /// hit shocks the target (<see cref="Damageable.ApplyShock"/>), which deals 1 extra damage a moment later.
    /// Walls stop the lash. No aiming, no throw, no slam. The owner swings it and detects hits as the lash passes
    /// them; the server validates and applies damage. The lash itself is a chain of thin glowing links (plus a
    /// fading trail behind the tip), animated here on every client.
    /// </summary>
    public class ShockWhipWeapon : PlayerWeapon
    {
        [Header("Attack")]
        [SerializeField] int damage = 2;
        [SerializeField] int shockDamage = 1;
        [Tooltip("Seconds until the shock deals its extra damage.")]
        [SerializeField] float shockDuration = 1f;
        [SerializeField] float cooldown = 0.5f;
        [Tooltip("How far the lash reaches.")]
        [SerializeField] float range = 8f;
        [Tooltip("Width of the swing in degrees: everything in this slice in front of the player gets hit.")]
        [SerializeField] float arcAngle = 150f;
        [Tooltip("Height above the feet the lash sweeps at (and checks line of sight from).")]
        [SerializeField] float lashHeight = 1f;
        [Tooltip("How far below / above the lash something can be and still get hit.")]
        [SerializeField] float reachBelow = 1.2f;
        [SerializeField] float reachAbove = 1.8f;
        [SerializeField] float serverReachTolerance = 2.5f;
        [SerializeField, Range(0f, 1f)] float moveSpeedWhileLashing = 0.3f;

        [Header("Lash Visual")]
        [Tooltip("The handle in the hand; swung around its pivot.")]
        [SerializeField] Transform handle;
        [Tooltip("End of the handle, where the lash comes out.")]
        [SerializeField] Transform lashRoot;
        [SerializeField] Material lashMaterial;
        [SerializeField] int links = 16;
        [SerializeField] float lashWidth = 0.1f;
        [SerializeField] float tipWidth = 0.045f;
        [Tooltip("Seconds for the lash to fly out to the side, sweep across (it hits as it passes), and roll back in.")]
        [SerializeField] float unfurlTime = 0.07f;
        [SerializeField] float sweepTime = 0.22f;
        [SerializeField] float retractTime = 0.2f;
        [Tooltip("Degrees the tip trails behind the hand while sweeping.")]
        [SerializeField] float tipLag = 30f;
        [Tooltip("How long the lash hangs from the handle when idle.")]
        [SerializeField] float hangLength = 0.9f;
        [Tooltip("How far the electric crackle shakes the lash.")]
        [SerializeField] float crackle = 0.05f;
        [Tooltip("Seconds the glowing streak behind the tip lasts.")]
        [SerializeField] float trailLife = 0.15f;
        [SerializeField] float trailWidth = 0.09f;
        [SerializeField] Vector3 idleEuler = new Vector3(30f, 10f, 0f);
        [SerializeField] float swingPitch = 10f;

        const int TrailLinks = 24;

        PlayerMovement movement;
        float lashStartTime = float.NegativeInfinity;
        float lashYaw;
        float sweepSign = 1f;     // +1: from the right to the left, -1: the other way round
        bool nextFromRight = true;
        bool sweeping;
        bool cracked = true;
        float lashLength;
        readonly HashSet<Damageable> hitThisLash = new();
        Transform linkRoot;
        Transform[] linkTransforms;
        Transform[] trailTransforms;
        Vector3[] points;
        readonly List<(Vector3 point, float time)> trail = new();

        public override WeaponId Id => WeaponId.ShockWhip;
        public override bool IsMelee => true;

        Vector3 LashOrigin => transform.position + Vector3.up * lashHeight;
        float StartAngle => sweepSign * arcAngle * 0.5f;

        void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            BuildLinks();
        }

        // ---------- Owner: swinging and hits ----------

        public override void MeleeAttack()
        {
            if (Time.time - lashStartTime < cooldown) return;

            float sign = nextFromRight ? 1f : -1f;
            nextFromRight = !nextFromRight;
            BeginLash(transform.eulerAngles.y, sign);
            hitThisLash.Clear();
            sweeping = true;
            if (movement.IsGrounded) movement.SlowDown(moveSpeedWhileLashing, unfurlTime + sweepTime + retractTime);
            SwingRpc(lashYaw, sign);
        }

        void Update()
        {
            if (!IsSpawned || !IsOwner || !sweeping) return;
            float t = Time.time - lashStartTime;
            if (t < unfurlTime) return; // nothing gets hit until the swing starts
            CheckHits(SweepAngle(t));
            if (t >= unfurlTime + sweepTime) sweeping = false;
        }

        /// <summary>Where the tip is in the swing, in degrees from straight ahead (positive = to the right).</summary>
        float SweepAngle(float t)
        {
            float u = Mathf.Clamp01((t - unfurlTime) / sweepTime);
            return Mathf.Lerp(StartAngle, -StartAngle, u * u * (3f - 2f * u));
        }

        Vector3 SwingDirection(float angle) => Quaternion.Euler(0f, lashYaw + angle, 0f) * Vector3.forward;

        /// <summary>Hits everything the lash has swept over so far (from the start of the swing to <paramref name="angle"/>).</summary>
        void CheckHits(float angle)
        {
            Vector3 origin = LashOrigin;
            Vector3 forward = SwingDirection(0f);
            float from = Mathf.Min(StartAngle, angle);
            float to = Mathf.Max(StartAngle, angle);
            foreach (var collider in Physics.OverlapSphere(origin, range, ~0, QueryTriggerInteraction.Ignore))
            {
                var target = collider.GetComponentInParent<Damageable>();
                if (target == null || !target.IsSpawned || target.IsDead || hitThisLash.Contains(target)) continue;
                if (target.TryGetComponent(out IEnemyTarget _)) continue; // our own side (the charm robot)

                Bounds bounds = collider.bounds;
                Vector3 offset = bounds.ClosestPoint(origin) - origin;
                if (offset.y < -reachBelow || offset.y > reachAbove) continue;
                if (new Vector3(offset.x, 0f, offset.z).magnitude > range) continue;

                Vector3 toCenter = bounds.center - origin;
                toCenter.y = 0f;
                float centerDistance = toCenter.magnitude;
                if (centerDistance > 0.05f)
                {
                    // Hit if the swept slice touches any part of it, not just its middle.
                    float targetAngle = Vector3.SignedAngle(forward, toCenter, Vector3.up);
                    float halfWidth = Mathf.Rad2Deg * Mathf.Asin(Mathf.Min(1f, new Vector2(bounds.extents.x, bounds.extents.z).magnitude / centerDistance));
                    if (targetAngle + halfWidth < from || targetAngle - halfWidth > to) continue;
                    if (offset.sqrMagnitude > 0.0025f && ClipToWalls(origin, offset.normalized, offset.magnitude) < offset.magnitude - 0.05f) continue;
                }

                hitThisLash.Add(target);
                HitRpc(target.NetworkObject, centerDistance > 0.05f ? toCenter / centerDistance : forward);
            }
        }

        /// <summary>How far the lash gets before a wall stops it (things it can hit and players don't stop it).</summary>
        static float ClipToWalls(Vector3 origin, Vector3 direction, float length)
        {
            foreach (var hit in Physics.RaycastAll(origin, direction, length, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<Damageable>() != null || hit.collider.GetComponentInParent<PlayerMovement>() != null) continue;
                length = Mathf.Min(length, hit.distance);
            }
            return length;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void HitRpc(NetworkObjectReference targetRef, Vector3 direction)
        {
            if (!targetRef.TryGet(out NetworkObject targetObject) || !targetObject.TryGetComponent(out Damageable target)) return;
            if (Vector3.Distance(targetObject.transform.position, transform.position) > range + serverReachTolerance) return;

            target.ApplyDamage(damage, direction);
            if (!target.IsDead) target.ApplyShock(shockDamage, shockDuration);
        }

        [Rpc(SendTo.NotMe, InvokePermission = RpcInvokePermission.Owner)]
        void SwingRpc(float yaw, float sign) => BeginLash(yaw, sign);

        void BeginLash(float yaw, float sign)
        {
            lashStartTime = Time.time;
            lashYaw = yaw;
            sweepSign = sign;
            cracked = false;
            trail.Clear();
        }

        // ---------- Lash visual (every client) ----------

        void BuildLinks()
        {
            linkRoot = new GameObject("WhipLash").transform;
            linkRoot.SetParent(transform, false);
            linkTransforms = CreateLinks(Mathf.Max(2, links));
            trailTransforms = CreateLinks(TrailLinks);
            points = new Vector3[linkTransforms.Length + 1];
            linkRoot.gameObject.SetActive(false);
        }

        Transform[] CreateLinks(int count)
        {
            var created = new Transform[count];
            for (int i = 0; i < count; i++)
            {
                var link = GameObject.CreatePrimitive(PrimitiveType.Cube);
                link.name = "WhipLink";
                // Gone right away, not at the end of the frame: it would sit inside the player's own collider.
                DestroyImmediate(link.GetComponent<Collider>());
                var renderer = link.GetComponent<Renderer>();
                if (lashMaterial != null) renderer.sharedMaterial = lashMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                link.transform.SetParent(linkRoot, false);
                link.transform.localScale = Vector3.zero;
                created[i] = link.transform;
            }
            return created;
        }

        void LateUpdate()
        {
            // The lash shows whenever the handle does (in the hand, not on the back).
            bool shown = handle != null && lashRoot != null && handle.gameObject.activeInHierarchy;
            if (linkRoot.gameObject.activeSelf != shown) linkRoot.gameObject.SetActive(shown);
            if (!shown) return;

            float t = Time.time - lashStartTime;
            UpdateHandle(t);
            UpdateLashPoints(t);
            for (int i = 0; i < linkTransforms.Length; i++)
                PlaceLink(linkTransforms[i], points[i], points[i + 1], Mathf.Lerp(lashWidth, tipWidth, (float)i / (linkTransforms.Length - 1)));
            UpdateTrail(t);

            if (!cracked && t >= unfurlTime + sweepTime)
            {
                cracked = true;
                CrackSparks(points[^1], lashMaterial);
            }
        }

        void UpdateHandle(float t)
        {
            if (t < unfurlTime + sweepTime)
            {
                // Swing the handle with the lash (the swing is fixed in the world; the player may turn meanwhile).
                float localYaw = lashYaw + SweepAngle(t) - transform.eulerAngles.y;
                var swing = Quaternion.Euler(swingPitch, localYaw, 0f);
                handle.localRotation = t < unfurlTime ? Quaternion.Slerp(Quaternion.Euler(idleEuler), swing, EaseOut(t / unfurlTime)) : swing;
            }
            else
            {
                handle.localRotation = Quaternion.Slerp(handle.localRotation, Quaternion.Euler(idleEuler), 1f - Mathf.Exp(-12f * Time.deltaTime));
            }
        }

        /// <summary>
        /// Idle, the lash hangs from the handle and sways. Swinging, it flies out to one side, sweeps across the front
        /// with the tip trailing behind the hand, then sags and rolls back into the hanging shape. It crackles all the while.
        /// </summary>
        void UpdateLashPoints(float t)
        {
            Vector3 root = lashRoot.position;
            Vector3 origin = LashOrigin;
            Vector3 handleDirection = lashRoot.forward;
            Vector3 planar = Vector3.ProjectOnPlane(handleDirection, Vector3.up);
            planar = planar.sqrMagnitude > 0.0001f ? planar.normalized : transform.forward;
            Vector3 hangBend = root + handleDirection * (0.35f * hangLength);
            Vector3 hangEnd = root + planar * (0.2f * hangLength) + Vector3.down * hangLength
                + transform.right * (Mathf.Sin(Time.time * 2.3f) * 0.06f);

            float swingEnd = unfurlTime + sweepTime;
            float tipAngle = SweepAngle(Mathf.Min(t, swingEnd));
            if (t < swingEnd) lashLength = ClipToWalls(origin, SwingDirection(tipAngle), range);

            float energy;
            if (t < swingEnd) energy = 1f;
            else if (t < swingEnd + retractTime) energy = 1f - (t - swingEnd) / retractTime;
            else energy = 0.25f;

            int count = points.Length - 1;
            for (int i = 0; i <= count; i++)
            {
                float s = (float)i / count;
                float u = 1f - s;
                Vector3 hanging = u * u * root + 2f * u * s * hangBend + s * s * hangEnd;
                // The part near the hand leads the swing; the tip trails behind it.
                float angle = tipAngle - sweepSign * tipLag * (1f - s);
                Vector3 swung = Vector3.Lerp(root, origin + SwingDirection(angle) * lashLength, s);
                float bulge = Mathf.Sin(Mathf.PI * s);

                Vector3 point;
                if (t < unfurlTime)
                {
                    float k = EaseOut(t / unfurlTime);
                    point = Vector3.Lerp(hanging, swung, k) + Vector3.up * (bulge * 0.15f * lashLength * (1f - k));
                }
                else if (t < swingEnd)
                {
                    point = swung;
                }
                else if (t < swingEnd + retractTime)
                {
                    float r = (t - swingEnd) / retractTime;
                    point = Vector3.Lerp(swung, hanging, r * r * (3f - 2f * r))
                        + Vector3.down * (bulge * 0.2f * lashLength * Mathf.Sin(Mathf.PI * r));
                }
                else
                {
                    point = hanging;
                }
                points[i] = point + Random.insideUnitSphere * (crackle * energy * bulge);
            }
        }

        /// <summary>A glowing streak along the path of the tip while it sweeps, so the whole slice it covers shows.</summary>
        void UpdateTrail(float t)
        {
            if (t >= unfurlTime && t < unfurlTime + sweepTime) trail.Add((points[^1], Time.time));
            while (trail.Count > 0 && (Time.time - trail[0].time > trailLife || trail.Count > TrailLinks + 1)) trail.RemoveAt(0);

            for (int i = 0; i < trailTransforms.Length; i++)
            {
                if (i + 1 >= trail.Count)
                {
                    trailTransforms[i].localScale = Vector3.zero;
                    continue;
                }
                float age = (Time.time - trail[i].time) / trailLife;
                PlaceLink(trailTransforms[i], trail[i].point, trail[i + 1].point, trailWidth * (1f - age));
            }
        }

        static void PlaceLink(Transform link, Vector3 a, Vector3 b, float width)
        {
            Vector3 delta = b - a;
            float length = delta.magnitude;
            if (length < 0.0001f || width <= 0f)
            {
                link.localScale = Vector3.zero;
                return;
            }
            link.SetPositionAndRotation((a + b) * 0.5f, Quaternion.LookRotation(delta));
            // A little longer than the gap, so the chain has no holes at the bends.
            link.localScale = new Vector3(width, width, length + width);
        }

        static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

        static void CrackSparks(Vector3 point, Material material)
        {
            for (int i = 0; i < 8; i++)
            {
                var spark = GameObject.CreatePrimitive(PrimitiveType.Cube);
                spark.name = "WhipSpark";
                Destroy(spark.GetComponent<Collider>());
                var renderer = spark.GetComponent<Renderer>();
                if (material != null) renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                spark.transform.SetPositionAndRotation(point, Random.rotation);
                spark.transform.localScale = Vector3.one * 0.07f;
                var body = spark.AddComponent<Rigidbody>();
                body.useGravity = false;
                body.linearVelocity = Random.onUnitSphere * 5f;
                Destroy(spark, 0.25f);
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.5f);
            Vector3 origin = transform.position + Vector3.up * lashHeight;
            Vector3 previous = origin;
            for (int i = 0; i <= 12; i++)
            {
                float angle = Mathf.Lerp(-arcAngle * 0.5f, arcAngle * 0.5f, i / 12f);
                Vector3 point = origin + Quaternion.Euler(0f, angle, 0f) * transform.forward * range;
                Gizmos.DrawLine(previous, point);
                previous = point;
            }
            Gizmos.DrawLine(previous, origin);
        }
    }
}
