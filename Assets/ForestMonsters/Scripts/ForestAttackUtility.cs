using AcRoguelike.Liminal;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AcRoguelike.Forest
{
    internal static class ForestAttackUtility
    {
        static readonly Collider[] sightOverlaps = new Collider[48];
        internal struct Impact
        {
            public float distance;
            public Vector3 normal, point;
            public Collider collider;
            public LiminalPlayerHealth player;
        }

        // Sweeps include initial overlaps. Enemy bodies never shield their own projectiles.
        internal static bool Sweep(PhysicsScene physics, LiminalRoom room, Vector3 origin, float radius,
            Vector3 direction, float distance, LiminalPlayerHealth target, RaycastHit[] hits,
            Collider[] overlaps, out Impact impact, bool includeHorizontalFaces = false)
        {
            impact = new Impact { distance = distance, normal = -direction };
            bool found = false;
            int count = physics.OverlapSphere(origin, radius, overlaps, ~0, QueryTriggerInteraction.Ignore);
            if (count == overlaps.Length) { impact.distance = 0; return true; }
            for (int i = 0; i < count; i++)
            {
                var other = overlaps[i];
                if (!other || other.GetComponentInParent<TrainingEnemy>()) continue;
                var player = other.GetComponentInParent<LiminalPlayerHealth>();
                if (player && player != target) continue;
                Vector3 normal = origin - other.ClosestPoint(origin);
                if (!player && normal.sqrMagnitude > .0001f &&
                    ((!includeHorizontalFaces && Mathf.Abs(normal.normalized.y) > .6f) || Vector3.Dot(normal, direction) >= 0)) continue;
                if (found && !impact.player && player) continue;
                impact = new Impact { distance = 0, normal = normal.sqrMagnitude > .001f ? normal.normalized : -direction,
                    point = other.ClosestPoint(origin), collider = other, player = player };
                found = true;
            }
            count = physics.SphereCast(origin, radius, direction, hits, distance, ~0, QueryTriggerInteraction.Ignore);
            if (count == hits.Length) { impact.distance = 0; return true; }
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (!hit.collider || hit.distance > impact.distance || hit.collider.GetComponentInParent<TrainingEnemy>()) continue;
                var player = hit.collider.GetComponentInParent<LiminalPlayerHealth>();
                if (player && player != target) continue;
                if (!player && !includeHorizontalFaces && Mathf.Abs(hit.normal.y) > .6f) continue;
                if (found && Mathf.Abs(hit.distance - impact.distance) < .001f && !impact.player && player) continue;
                impact = new Impact { distance = hit.distance, normal = hit.normal, point = hit.point, collider = hit.collider, player = player }; found = true;
            }
            if (!room) return found;
            Vector3 local = room.transform.InverseTransformPoint(origin);
            Vector3 delta = room.transform.InverseTransformVector(direction);
            Vector3 scale = room.transform.lossyScale;
            for (int axis = 0; axis <= 2; axis += 2)
            {
                if (Mathf.Abs(delta[axis]) < .0001f) continue;
                float inset = radius / Mathf.Max(.001f, Mathf.Abs(scale[axis]));
                float low = room.localBounds.min[axis] + inset, high = room.localBounds.max[axis] - inset;
                float travel = ((delta[axis] > 0 ? high : low) - local[axis]) / delta[axis];
                if (travel < 0)
                {
                    if (!((local[axis] > high && delta[axis] > 0) || (local[axis] < low && delta[axis] < 0))) continue;
                    travel = 0;
                }
                if (travel > impact.distance) continue;
                Vector3 normal = Vector3.zero; normal[axis] = delta[axis] > 0 ? -1 : 1;
                impact = new Impact { distance = travel, normal = room.transform.TransformDirection(normal).normalized }; found = true;
            }
            return found;
        }

        internal static bool HasLineOfSight(GameObject source, Vector3 origin, LiminalPlayerHealth player, RaycastHit[] buffer)
        {
            if (!player) return false;
            var physics = source.scene.GetPhysicsScene();
            int overlapCount = physics.OverlapSphere(origin, .045f, sightOverlaps, ~0, QueryTriggerInteraction.Ignore);
            if (overlapCount == sightOverlaps.Length) return false;
            for (int i = 0; i < overlapCount; i++)
            {
                var collider = sightOverlaps[i];
                if (collider && !collider.GetComponentInParent<TrainingEnemy>() && !collider.GetComponentInParent<LiminalPlayerHealth>()) return false;
            }
            Vector3 offset = player.transform.position + Vector3.up * .8f - origin;
            int count = physics.Raycast(origin, offset.normalized, buffer, offset.magnitude, ~0, QueryTriggerInteraction.Ignore);
            if (count == buffer.Length) return false;
            for (int i = 0; i < count; i++)
            {
                var collider = buffer[i].collider;
                if (!collider || collider.GetComponentInParent<TrainingEnemy>() || collider.GetComponentInParent<LiminalPlayerHealth>()) continue;
                return false;
            }
            return true;
        }

        internal static Vector3 Flat(Vector3 value) { value.y = 0; return value.sqrMagnitude > .001f ? value.normalized : Vector3.forward; }
        internal static bool Alive(TrainingEnemy owner, LiminalPlayerHealth player, LiminalRoom room, bool hadRoom)
            => owner && owner.IsAlive && owner.isActiveAndEnabled && player && player.IsAlive && player.isActiveAndEnabled && (!hadRoom || (room && room.isActiveAndEnabled));
    }
}
