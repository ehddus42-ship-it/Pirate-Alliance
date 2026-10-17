using UnityEngine;

namespace AcRoguelike
{
    /// <summary>
    /// Allocation-free RaycastAll / SphereCastAll for checks that run every frame (monster steering, line of sight).
    /// The hits land in one shared buffer that the next query overwrites, so read them before casting again and
    /// never cast from code that runs while a caller is still looping over <see cref="Hits"/>.
    /// </summary>
    public static class PhysicsCasts
    {
        static RaycastHit[] buffer = new RaycastHit[64];

        public static RaycastHit[] Hits => buffer;

        public static int Raycast(Vector3 origin, Vector3 direction, float distance,
            int mask = ~0, QueryTriggerInteraction triggers = QueryTriggerInteraction.Ignore)
        {
            int count;
            // A full buffer may have dropped hits: grow it and ask again, so no result is ever lost.
            while ((count = Physics.RaycastNonAlloc(origin, direction, buffer, distance, mask, triggers)) == buffer.Length)
                buffer = new RaycastHit[buffer.Length * 2];
            return count;
        }

        public static int SphereCast(Vector3 origin, float radius, Vector3 direction, float distance,
            int mask = ~0, QueryTriggerInteraction triggers = QueryTriggerInteraction.Ignore)
        {
            int count;
            while ((count = Physics.SphereCastNonAlloc(origin, radius, direction, buffer, distance, mask, triggers)) == buffer.Length)
                buffer = new RaycastHit[buffer.Length * 2];
            return count;
        }
    }
}
