using AcRoguelike.Liminal;
using UnityEngine;

namespace AcRoguelike.GameTheme
{
    /// <summary>A swept pixel bolt. Walls stop it, dashes evade it, and room cleanup owns its lifetime.</summary>
    public sealed class GamePixelBolt : MonoBehaviour
    {
        const float Radius = .18f;
        TrainingEnemy owner;
        LiminalPlayerHealth target;
        LiminalRoom room;
        Vector3 direction;
        float remaining;
        int damage;

        public static void Fire(Vector3 position, Vector3 direction, TrainingEnemy owner,
            LiminalPlayerHealth target, LiminalRoom room, int damage)
        {
            var library = GameVoxelMonsterLibrary.Instance;
            if (!library || !library.projectileMesh || !library.paletteMaterial) return;
            var go = new GameObject("Pixel Bolt");
            go.transform.SetParent(room ? room.transform : owner.transform.parent, true);
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));
            go.AddComponent<MeshFilter>().sharedMesh = library.projectileMesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = library.paletteMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var bolt = go.AddComponent<GamePixelBolt>();
            bolt.owner = owner;
            bolt.target = target;
            bolt.room = room;
            bolt.direction = direction.normalized;
            bolt.remaining = 15;
            bolt.damage = damage;
        }

        void Update()
        {
            if (!owner || !owner.IsAlive || !target || !target.IsAlive) { Destroy(gameObject); return; }
            float step = Mathf.Min(remaining, 8.5f * Time.deltaTime);
            if (step <= 0) return;
            RaycastHit? closest = null;
            foreach (var hit in Physics.SphereCastAll(transform.position, Radius, direction, step, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<TrainingEnemy>()) continue;
                if (!closest.HasValue || hit.distance < closest.Value.distance) closest = hit;
            }
            if (closest.HasValue)
            {
                var hit = closest.Value;
                var player = hit.collider.GetComponentInParent<LiminalPlayerHealth>();
                if (player) player.TakeDamage(damage);
                HitFeedback.Sparks(hit.point, -direction, 5, .4f);
                Destroy(gameObject);
                return;
            }
            transform.position += direction * step;
            remaining -= step;
            if (remaining <= 0 || (room && !room.Contains(transform.position, .2f))) Destroy(gameObject);
        }
    }
}
