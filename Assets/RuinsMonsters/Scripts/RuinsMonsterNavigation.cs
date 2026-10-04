using AcRoguelike.Liminal;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AcRoguelike.Ruins
{
    /// <summary>A room-owned route around wrecks and barricades, shared by its ruins creatures.</summary>
    [DisallowMultipleComponent]
    public sealed class RuinsMonsterNavigation : MonoBehaviour
    {
        const float Cell = 1.05f, MinimumClearance = .67f;
        LiminalRoom room;
        Vector3 minimum;
        int width, depth, targetCell = -1;
        bool[] walkable;
        int[] distance, queue;
        float lastFlood = -1;

        public static RuinsMonsterNavigation ForRoom(LiminalRoom owner)
        {
            if (!owner) return null;
            return owner.GetComponent<RuinsMonsterNavigation>() ?? owner.gameObject.AddComponent<RuinsMonsterNavigation>();
        }

        void Initialize()
        {
            if (walkable != null) return;
            room = GetComponent<LiminalRoom>();
            if (!room) return;
            minimum = room.localBounds.min;
            minimum.y = 0;
            minimum += new Vector3(1, 0, 1);
            width = Mathf.Max(1, Mathf.FloorToInt((room.localBounds.size.x - 2) / Cell));
            depth = Mathf.Max(1, Mathf.FloorToInt((room.localBounds.size.z - 2) / Cell));
            walkable = new bool[width * depth];
            distance = new int[walkable.Length];
            queue = new int[walkable.Length];
            var overlaps = new Collider[64];
            float clearance = MinimumClearance;
            // The first route query runs after the room's roster has spawned. Reserve enough room for
            // its broadest controller, rather than steering the bulwark into a drone-sized opening.
            foreach (var monster in room.GetComponentsInChildren<RuinsMonster>(true))
            {
                var controller = monster.GetComponent<CharacterController>();
                if (!controller) continue;
                Vector3 scale = controller.transform.lossyScale;
                clearance = Mathf.Max(clearance, controller.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)) + .05f);
            }
            Physics.SyncTransforms();
            var physics = room.gameObject.scene.GetPhysicsScene();
            for (int i = 0; i < walkable.Length; i++)
            {
                Vector3 p = World(i);
                int count = physics.OverlapCapsule(p + Vector3.up * .75f, p + Vector3.up * 1.35f,
                    clearance, overlaps, ~0, QueryTriggerInteraction.Ignore);
                bool clear = count < overlaps.Length;
                for (int n = 0; n < count && clear; n++)
                {
                    var hit = overlaps[n];
                    if (!hit || hit.GetComponentInParent<TrainingEnemy>() || hit.GetComponentInParent<LiminalPlayerHealth>()) continue;
                    // Small walkable floor lips are handled by the character controller's step offset.
                    if (hit.bounds.max.y <= p.y + .22f) continue;
                    clear = false;
                }
                walkable[i] = clear;
            }
        }

        Vector3 World(int index) => room.transform.TransformPoint(minimum +
            new Vector3((index % width + .5f) * Cell, .05f, (index / width + .5f) * Cell));

        int CellAt(Vector3 position)
        {
            Vector3 p = room.transform.InverseTransformPoint(position) - minimum;
            return Mathf.Clamp(Mathf.FloorToInt(p.z / Cell), 0, depth - 1) * width +
                Mathf.Clamp(Mathf.FloorToInt(p.x / Cell), 0, width - 1);
        }

        int Nearest(int source)
        {
            if (walkable[source]) return source;
            int x = source % width, z = source / width, best = -1, bestCost = int.MaxValue;
            for (int dz = -4; dz <= 4; dz++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int nx = x + dx, nz = z + dz, cost = dx * dx + dz * dz;
                    if (nx < 0 || nx >= width || nz < 0 || nz >= depth || cost >= bestCost) continue;
                    int next = nz * width + nx;
                    if (walkable[next]) { best = next; bestCost = cost; }
                }
            return best;
        }

        bool CornerClear(int x, int z, int nx, int nz) => x == nx || z == nz ||
            (walkable[z * width + nx] && walkable[nz * width + x]);

        void Flood(int target)
        {
            for (int i = 0; i < distance.Length; i++) distance[i] = int.MaxValue;
            int start = 0, end = 0;
            queue[end++] = target; distance[target] = 0;
            while (start < end)
            {
                int current = queue[start++], x = current % width, z = current / width;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, nz = z + dz;
                        if ((dx == 0 && dz == 0) || nx < 0 || nx >= width || nz < 0 || nz >= depth) continue;
                        int next = nz * width + nx;
                        if (!walkable[next] || distance[next] != int.MaxValue || !CornerClear(x, z, nx, nz)) continue;
                        distance[next] = distance[current] + 1;
                        queue[end++] = next;
                    }
            }
            targetCell = target; lastFlood = Time.time;
        }

        public Vector3 Direction(Vector3 position, Vector3 destination)
        {
            Initialize();
            if (!room || walkable == null) return Vector3.zero;
            int target = Nearest(CellAt(destination));
            if (target < 0) return Vector3.zero;
            if (targetCell < 0 || (targetCell != target && Time.time - lastFlood >= .3f)) Flood(target);
            int source = Nearest(CellAt(position));
            if (source < 0 || distance[source] == int.MaxValue) return Vector3.zero;
            if (source == targetCell) return Vector3.ProjectOnPlane(destination - position, Vector3.up).normalized;
            int x = source % width, z = source / width, best = source;
            float bestScore = distance[source] + 1;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, nz = z + dz;
                    if ((dx == 0 && dz == 0) || nx < 0 || nx >= width || nz < 0 || nz >= depth) continue;
                    int next = nz * width + nx;
                    if (!walkable[next] || distance[next] == int.MaxValue || !CornerClear(x, z, nx, nz)) continue;
                    float score = distance[next] + Vector3.Distance(World(next), position) * .1f;
                    if (score < bestScore) { best = next; bestScore = score; }
                }
            return Vector3.ProjectOnPlane(World(best) - position, Vector3.up).normalized;
        }
    }
}
