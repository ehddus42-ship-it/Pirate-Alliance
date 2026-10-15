using UnityEngine;

namespace AcRoguelike.Liminal
{
    /// <summary>A hurled car: it drops onto the floor line and slides along a locked straight heading.</summary>
    public sealed class TrafficLightCarProjectile : MonoBehaviour
    {
        public Vector3 halfExtents = new Vector3(1, .75f, 2.2f);
        public float lifetime = 6f, dropDistance = 3.2f, groundClearance = .2f;
        Transform owner;
        TrafficLightBoss boss;
        LiminalPlayerHealth target;
        Vector3 direction, origin;
        float speed, floorY, startHeight, travelled, age, spin;
        int damage;
        bool launched, dealtDamage;
        public static int ImpactCount { get; private set; }
        public static int HitCount { get; private set; }
        public bool Launched => launched;
        public bool DealtDamage => dealtDamage;

        public void Launch(Vector3 heading, float travelSpeed, int amount, TrafficLightBoss source, LiminalPlayerHealth player, float floorHeight)
        {
            boss = source; owner = source.transform; target = player; damage = amount;
            speed = ProjectileTuning.ScaleSpeed(travelSpeed); floorY = floorHeight;
            heading.y = 0; direction = heading.sqrMagnitude > .001f ? heading.normalized : source.transform.forward;
            origin = transform.position; startHeight = origin.y - floorHeight; launched = true;
            transform.rotation = Quaternion.LookRotation(direction);
        }

        void Update()
        {
            if (!launched || Time.deltaTime <= 0) return;
            age += Time.deltaTime;
            if (age > ProjectileTuning.ScaleFlightDuration(lifetime) || !owner || !boss.Health.IsAlive) { Destroy(gameObject); return; }
            float step = speed * Time.deltaTime; travelled += step;
            Vector3 position = origin + direction * travelled;
            float descent = Mathf.SmoothStep(0, 1, travelled / dropDistance);
            float height = Mathf.Lerp(startHeight, halfExtents.y + groundClearance, descent);
            position.y = floorY + height;
            float slope = Mathf.Atan2(-(Mathf.Lerp(startHeight, halfExtents.y + groundClearance, Mathf.SmoothStep(0, 1, (travelled + 1) / dropDistance)) - height), 1) * Mathf.Rad2Deg;
            spin = Mathf.Lerp(spin, 0, Time.deltaTime * 3) + Mathf.Lerp(260, 0, descent) * Time.deltaTime;
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction) * Quaternion.Euler(slope, 0, spin));
            if (!dealtDamage && target && target.IsAlive && Overlaps(target.transform.position))
            { dealtDamage = true; HitCount++; target.TakeDamage(damage); }
            if (!boss.ArenaContains(position, -1.5f)) { ImpactCount++; Destroy(gameObject); }
        }

        bool Overlaps(Vector3 feet)
        {
            Vector3 local = Quaternion.Inverse(Quaternion.LookRotation(direction)) * (feet + Vector3.up * .9f - transform.position);
            const float playerRadius = .4f;
            return Mathf.Abs(local.x) <= halfExtents.x + playerRadius && Mathf.Abs(local.z) <= halfExtents.z + playerRadius
                && feet.y < transform.position.y + halfExtents.y && feet.y + 1.8f > transform.position.y - halfExtents.y;
        }
    }
}
