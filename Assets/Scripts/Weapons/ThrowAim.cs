using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Owner-side aiming for anything thrown in an arc (grenades, the charm robot). Picks where it should come down —
    /// the locked target, else the spot under the crosshair if it's in reach, else the furthest spot straight
    /// ahead — works out the launch velocity (longer throws get more air time, so they lob higher), and shows the
    /// arc it will fly plus a ring of the given size where it lands. The arc uses <see cref="GrenadeArc"/>, the
    /// same flight the thrown object uses, so what you see is what it does.
    /// </summary>
    public class ThrowAim
    {
        const int ArcDots = 26;

        readonly Transform owner;
        readonly PlayerTargeting targeting;
        readonly Material material;
        readonly string previewName;
        readonly float range, gravity, projectileRadius, ringRadius;
        readonly Vector2 flightTime;

        Transform previewRoot;
        Transform[] arcDots;
        Transform ring;

        public ThrowAim(Transform owner, PlayerTargeting targeting, float range, float gravity, Vector2 flightTime,
            float projectileRadius, float ringRadius, Material material, string previewName)
        {
            this.owner = owner;
            this.targeting = targeting;
            this.range = range;
            this.gravity = gravity;
            this.flightTime = flightTime;
            this.projectileRadius = projectileRadius;
            this.ringRadius = ringRadius;
            this.material = material;
            this.previewName = previewName;
        }

        /// <summary>Where it should come down: the locked target, else the crosshair spot, kept within reach.</summary>
        public Vector3 LandingSpot()
        {
            if (targeting.CurrentTarget != null) return WithinReach(targeting.CurrentTarget.AimPoint);

            Ray ray = targeting.AimRay;
            float rayLength = range + Vector3.Distance(ray.origin, owner.position) + 5f;
            if (RaycastIgnoringPlayers(ray.origin, ray.direction, rayLength, out RaycastHit hit) && PlanarDistance(hit.point) <= range)
                return hit.point;

            // Nothing in reach under the crosshair: the furthest spot straight ahead.
            Vector3 ahead = Vector3.ProjectOnPlane(ray.direction, Vector3.up);
            if (ahead.sqrMagnitude < 0.0001f) ahead = owner.forward;
            return GroundBelow(owner.position + ahead.normalized * range);
        }

        /// <summary>Velocity that lands on <paramref name="target"/>; longer throws get more air time (a higher lob).</summary>
        public Vector3 LaunchVelocity(Vector3 start, Vector3 target, out float flight)
        {
            Vector3 delta = target - start;
            float planar = new Vector2(delta.x, delta.z).magnitude;
            flight = Mathf.Lerp(flightTime.x, flightTime.y, Mathf.Clamp01(planar / range));
            return delta / flight + Vector3.up * (0.5f * gravity * flight);
        }

        Vector3 WithinReach(Vector3 spot)
        {
            Vector3 flat = Vector3.ProjectOnPlane(spot - owner.position, Vector3.up);
            if (flat.magnitude <= range) return spot;
            return GroundBelow(owner.position + flat.normalized * range);
        }

        float PlanarDistance(Vector3 spot) => Vector3.ProjectOnPlane(spot - owner.position, Vector3.up).magnitude;

        Vector3 GroundBelow(Vector3 spot)
        {
            Vector3 from = new Vector3(spot.x, owner.position.y + 20f, spot.z);
            return RaycastIgnoringPlayers(from, Vector3.down, 60f, out RaycastHit hit) ? hit.point : new Vector3(spot.x, owner.position.y, spot.z);
        }

        static bool RaycastIgnoringPlayers(Vector3 from, Vector3 direction, float distance, out RaycastHit nearest)
        {
            nearest = default;
            float best = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(from, direction, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<PlayerMovement>() != null || hit.distance >= best) continue;
                best = hit.distance;
                nearest = hit;
            }
            return best < float.MaxValue;
        }

        // ---------- Preview ----------

        /// <summary>Shows the arc from <paramref name="start"/> and the ring where it first touches something.</summary>
        public void ShowPreview(Vector3 start, Vector3 velocity, float flight)
        {
            if (previewRoot == null) BuildPreview();
            if (!previewRoot.gameObject.activeSelf) previewRoot.gameObject.SetActive(true);

            // Walk the same arc it will fly and stop at the first thing it would hit.
            float step = flight / (ArcDots - 6); // a few spare dots in case it flies past the spot
            Vector3 previous = start;
            Vector3 impact = start;
            bool hitSomething = false;
            int shown = 0;
            for (int i = 1; i <= ArcDots; i++)
            {
                Vector3 point = GrenadeArc.PositionAt(start, velocity, gravity, step * i);
                if (GrenadeArc.Sweep(previous, point, projectileRadius, out RaycastHit hit))
                {
                    impact = hit.point;
                    hitSomething = true;
                    break;
                }
                arcDots[i - 1].position = point;
                shown = i;
                previous = point;
                impact = point;
            }
            for (int i = 0; i < arcDots.Length; i++)
            {
                // The first dot sits in the hand; skip it so the arc doesn't clutter the view.
                bool visible = i > 0 && i < shown;
                if (arcDots[i].gameObject.activeSelf != visible) arcDots[i].gameObject.SetActive(visible);
            }

            ring.gameObject.SetActive(hitSomething);
            ring.position = impact + Vector3.up * 0.06f;
        }

        public void HidePreview()
        {
            if (previewRoot != null && previewRoot.gameObject.activeSelf) previewRoot.gameObject.SetActive(false);
        }

        public void Destroy()
        {
            if (previewRoot != null) Object.Destroy(previewRoot.gameObject);
        }

        void BuildPreview()
        {
            previewRoot = new GameObject(previewName).transform;
            arcDots = new Transform[ArcDots];
            for (int i = 0; i < ArcDots; i++) arcDots[i] = PreviewPiece(previewRoot, Vector3.one * 0.09f);

            // Ring of short bars at the landing spot (about one per metre of rim), plus a centre mark.
            ring = new GameObject("LandingRing").transform;
            ring.SetParent(previewRoot, false);
            int pieces = Mathf.Clamp(Mathf.RoundToInt(2f * Mathf.PI * ringRadius), 20, 72);
            float barLength = 2f * Mathf.PI * ringRadius / pieces * 0.6f;
            for (int i = 0; i < pieces; i++)
            {
                var bar = PreviewPiece(ring, new Vector3(barLength, 0.04f, 0.1f));
                bar.localRotation = Quaternion.Euler(0f, i * 360f / pieces, 0f);
                bar.localPosition = bar.localRotation * (Vector3.forward * ringRadius);
            }
            PreviewPiece(ring, new Vector3(0.35f, 0.04f, 0.35f));
            previewRoot.gameObject.SetActive(false);
        }

        Transform PreviewPiece(Transform parent, Vector3 scale)
        {
            var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(piece.GetComponent<Collider>());
            var renderer = piece.GetComponent<Renderer>();
            if (material != null) renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            piece.transform.SetParent(parent, false);
            piece.transform.localScale = scale;
            return piece.transform;
        }
    }
}
