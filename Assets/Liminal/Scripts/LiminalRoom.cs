using UnityEngine;

namespace AcRoguelike.Liminal
{
    public enum LiminalRoomKind { Arrival, Exploration, Combat, Threshold, Boss }

    /// <summary>An authored room. Socket forward vectors both point along the route.</summary>
    [DisallowMultipleComponent]
    public sealed class LiminalRoom : MonoBehaviour
    {
        public string roomId;
        public string displayName;
        public LiminalRoomKind kind;
        public Transform entry;
        public Transform exit;
        public Transform playerSpawn;
        public Transform[] enemySpawns = new Transform[0];
        public Bounds localBounds = new Bounds(new Vector3(0, 2, 14), new Vector3(20, 8, 28));
        public GameObject entranceGate;
        public GameObject exitGate;
        [TextArea(2, 5)] public string designNotes;

        public bool Contains(Vector3 worldPosition, float inset = 0)
        {
            Vector3 p = transform.InverseTransformPoint(worldPosition);
            return p.x >= localBounds.min.x + inset && p.x <= localBounds.max.x - inset
                && p.z >= localBounds.min.z + inset && p.z <= localBounds.max.z - inset;
        }

        public void SetGates(bool entranceClosed, bool exitClosed)
        {
            if (entranceGate) entranceGate.SetActive(entranceClosed);
            if (exitGate) exitGate.SetActive(exitClosed);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(.3f, .9f, .85f, .8f);
            Gizmos.DrawWireCube(localBounds.center, localBounds.size);
            Gizmos.matrix = Matrix4x4.identity;
            DrawSocket(entry, Color.cyan);
            DrawSocket(exit, Color.yellow);
            if (playerSpawn) { Gizmos.color = Color.green; Gizmos.DrawWireSphere(playerSpawn.position, .5f); }
            if (enemySpawns != null) foreach (var marker in enemySpawns)
            {
                if (!marker) continue;
                Gizmos.color = new Color(1, .35f, .25f);
                Gizmos.DrawWireSphere(marker.position + Vector3.up, .6f);
            }
        }

        static void DrawSocket(Transform socket, Color color)
        {
            if (!socket) return;
            Gizmos.color = color;
            Gizmos.DrawWireCube(socket.position + Vector3.up, new Vector3(3, 2, .1f));
            Gizmos.DrawRay(socket.position + Vector3.up, socket.forward * 2);
        }
    }
}
