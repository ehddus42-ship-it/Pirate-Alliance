using AcRoguelike.Liminal;
using UnityEngine;

namespace AcRoguelike.GameTheme
{
    /// <summary>
    /// A room-sized shared flow grid prevents melee enemies orbiting U-shaped puzzle walls forever.
    /// Static occupancy is sampled once; a player-centred flood is shared by the whole pack.
    /// Local character-controller steering still handles moving actors and the final approach.
    /// </summary>
    internal sealed class GameThemeNavigation
    {
        const float Cell = 1.1f;
        readonly LiminalRoom room;
        readonly int width, depth;
        readonly Vector3 minimum;
        readonly bool[] walkable;
        readonly int[] distance, queue;
        int destinationCell = -1;
        float refreshedAt = -10;

        public GameThemeNavigation(LiminalRoom owner)
        {
            room = owner;
            minimum = owner.localBounds.min;
            minimum.y = 0;
            minimum += new Vector3(1, 0, 1);
            width = Mathf.Max(1, Mathf.FloorToInt((owner.localBounds.size.x - 2) / Cell));
            depth = Mathf.Max(1, Mathf.FloorToInt((owner.localBounds.size.z - 2) / Cell));
            walkable = new bool[width * depth];
            distance = new int[walkable.Length];
            queue = new int[walkable.Length];
            var hits = new Collider[32];
            Physics.SyncTransforms();
            for (int i = 0; i < walkable.Length; i++)
            {
                Vector3 p = World(i);
                int count = Physics.OverlapSphereNonAlloc(p + Vector3.up * .9f, .76f, hits, ~0, QueryTriggerInteraction.Ignore);
                bool clear = count < hits.Length;
                for (int n = 0; n < count && clear; n++)
                {
                    if (hits[n].GetComponentInParent<TrainingEnemy>() || hits[n].GetComponentInParent<LiminalPlayerHealth>()) continue;
                    clear = false;
                }
                walkable[i] = clear;
            }
        }

        Vector3 World(int index)
        {
            return room.transform.TransformPoint(minimum + new Vector3((index % width + .5f) * Cell, .05f, (index / width + .5f) * Cell));
        }

        int CellAt(Vector3 world)
        {
            Vector3 p = room.transform.InverseTransformPoint(world) - minimum;
            int x = Mathf.Clamp(Mathf.FloorToInt(p.x / Cell), 0, width - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt(p.z / Cell), 0, depth - 1);
            return z * width + x;
        }

        int NearestWalkable(int from)
        {
            if (walkable[from]) return from;
            int x = from % width, z = from / width, best = -1, score = int.MaxValue;
            for (int dz = -3; dz <= 3; dz++)
                for (int dx = -3; dx <= 3; dx++)
                {
                    int nx = x + dx, nz = z + dz, cost = dx * dx + dz * dz;
                    if (nx < 0 || nx >= width || nz < 0 || nz >= depth || cost >= score) continue;
                    int index = nz * width + nx;
                    if (!walkable[index]) continue;
                    best = index; score = cost;
                }
            return best;
        }

        void Flood(int target)
        {
            for (int i = 0; i < distance.Length; i++) distance[i] = int.MaxValue;
            int start = 0, end = 0;
            queue[end++] = target;
            distance[target] = 0;
            while (start < end)
            {
                int current = queue[start++], x = current % width, z = current / width;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int nx = x + dx, nz = z + dz;
                        if (nx < 0 || nx >= width || nz < 0 || nz >= depth) continue;
                        int next = nz * width + nx;
                        if (!walkable[next] || distance[next] != int.MaxValue) continue;
                        if (dx != 0 && dz != 0 && (!walkable[z * width + nx] || !walkable[nz * width + x])) continue;
                        distance[next] = distance[current] + 1;
                        queue[end++] = next;
                    }
            }
        }

        public Vector3 Direction(Vector3 position, Vector3 destination)
        {
            int target = NearestWalkable(CellAt(destination));
            if (target < 0) return Vector3.zero;
            if (target != destinationCell && Time.time - refreshedAt >= .35f)
            {
                Flood(target);
                destinationCell = target;
                refreshedAt = Time.time;
            }
            int current = NearestWalkable(CellAt(position));
            if (current < 0 || distance[current] == int.MaxValue) return Vector3.zero;
            if (current == destinationCell) return Vector3.ProjectOnPlane(destination - position, Vector3.up).normalized;
            int x = current % width, z = current / width, best = current;
            float bestScore = distance[current] + 1;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, nz = z + dz;
                    if ((dx == 0 && dz == 0) || nx < 0 || nx >= width || nz < 0 || nz >= depth) continue;
                    int next = nz * width + nx;
                    if (!walkable[next] || distance[next] == int.MaxValue) continue;
                    if (dx != 0 && dz != 0 && (!walkable[z * width + nx] || !walkable[nz * width + x])) continue;
                    float score = distance[next] + Vector3.Distance(World(next), position) * .1f;
                    if (score < bestScore) { best = next; bestScore = score; }
                }
            return Vector3.ProjectOnPlane(World(best) - position, Vector3.up).normalized;
        }

#if UNITY_EDITOR
        public string Diagnose(Vector3 position, Vector3 destination)
        {
            Vector3 direction = Direction(position, destination);
            int source = CellAt(position), nearest = NearestWalkable(source);
            int target = NearestWalkable(CellAt(destination));
            return $"source={source} sourceClear={walkable[source]} sourceWorld={World(source):F2} nearest={nearest} nearestWorld={(nearest >= 0 ? World(nearest).ToString("F2") : "none")} distance={(nearest >= 0 ? distance[nearest] : -1)} target={target} targetWorld={(target >= 0 ? World(target).ToString("F2") : "none")} direction={direction:F3}";
        }
#endif
    }
}
