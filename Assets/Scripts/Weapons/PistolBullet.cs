using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// One client's copy of a pistol bullet (see <see cref="PistolWeapon"/>): a glowing slug with a streak behind
    /// it, flying straight until it touches anything but a player or reaches its range. Only the shooter's copy
    /// counts; the others are stopped where the shooter's stopped.
    /// </summary>
    public class PistolBullet : MonoBehaviour
    {
        PistolWeapon owner;
        int id;
        Vector3 direction;
        float speed, radius, range, travelled;
        Material material;
        float nextPuffTime;

        public static PistolBullet Spawn(PistolWeapon owner, int id, Vector3 start, Vector3 direction, float speed, float radius, float range, Material material)
        {
            var root = new GameObject("PistolBullet");
            root.transform.SetPositionAndRotation(start, Quaternion.LookRotation(direction));
            var bullet = root.AddComponent<PistolBullet>();
            bullet.owner = owner;
            bullet.id = id;
            bullet.direction = direction;
            bullet.speed = speed;
            bullet.radius = radius;
            bullet.range = range;
            bullet.material = material;

            Piece(PrimitiveType.Sphere, root.transform, Vector3.zero, new Vector3(0.4f, 0.4f, 0.7f), material);
            Piece(PrimitiveType.Cube, root.transform, new Vector3(0f, 0f, -0.8f), new Vector3(0.15f, 0.15f, 1.3f), material); // streak
            return bullet;
        }

        static void Piece(PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 scale, Material material)
        {
            var piece = GameObject.CreatePrimitive(type);
            Destroy(piece.GetComponent<Collider>());
            var renderer = piece.GetComponent<Renderer>();
            if (material != null) renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            piece.transform.SetParent(parent, false);
            piece.transform.localPosition = localPosition;
            piece.transform.localScale = scale;
        }

        void Update()
        {
            if (owner == null)
            {
                Destroy(gameObject);
                return;
            }

            float step = Mathf.Min(speed * Time.deltaTime, range - travelled);
            Vector3 position = transform.position;
            RaycastHit nearest = default;
            float best = float.MaxValue;
            foreach (var hit in Physics.SphereCastAll(position, radius, direction, step, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.distance >= best || hit.collider.GetComponentInParent<PlayerMovement>() != null) continue;
                best = hit.distance;
                nearest = hit;
            }

            if (best < float.MaxValue)
            {
                // Touching it from the start (point is zero then): it is right here.
                Vector3 point = best > 0f || nearest.point != Vector3.zero ? nearest.point : position;
                End(point, nearest.collider);
                return;
            }

            transform.position = position + direction * step;
            travelled += step;
            if (travelled >= range)
            {
                End(transform.position, null);
                return;
            }
            if (Time.time >= nextPuffTime)
            {
                nextPuffTime = Time.time + 0.03f;
                SmokePuff.Spawn(position, material);
            }
        }

        void End(Vector3 point, Collider hitCollider)
        {
            owner.OnBulletEnd(id, point, hitCollider, direction);
            Destroy(gameObject);
        }
    }
}
