using AcRoguelike.Liminal;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AcRoguelike.GameTheme
{
    /// <summary>A committed soft-drop shot or an I bar that expires on its fourth wall contact.</summary>
    public sealed class TetrominoProjectile : MonoBehaviour
    {
        public const float SlowSpeed = 2.2f * ProjectileTuning.SpeedMultiplier, FastSpeed = 13.2f * ProjectileTuning.SpeedMultiplier;
        public const float SlowDuration = .65f, AccelerationDuration = .45f;
        public const float RicochetSpeed = 11f * ProjectileTuning.SpeedMultiplier;
        const float BaseSoftDropLifetime = 5.5f, RicochetLifetime = 18f / ProjectileTuning.SpeedMultiplier;
        // Acceleration timing stays fixed. Extend the fast tail by the distance lost to slower travel.
        static readonly float SoftDropLifetime = BaseSoftDropLifetime
            + SoftDropDistance(BaseSoftDropLifetime) * (1f / ProjectileTuning.SpeedMultiplier - 1f) / FastSpeed;
        public float Speed { get; private set; }
        public int BounceCount { get; private set; }
        public int Damage { get; private set; }
        public int DamageAttempts { get; private set; }
        public bool HitPlayer { get; private set; }
        public bool Finished { get; private set; }
        public bool Ricochet { get; private set; }
        public Vector3 Direction => direction;
        public float CollisionRadius => radius;

        TrainingEnemy owner;
        LiminalPlayerHealth target;
        LiminalRoom room;
        bool hadRoom;
        Vector3 direction;
        Transform visual;
        float age, radius;
        readonly RaycastHit[] hits = new RaycastHit[128];
        readonly Collider[] overlaps = new Collider[128];

        public static TetrominoProjectile Fire(Vector3 position, Vector3 direction, TrainingEnemy owner,
            LiminalPlayerHealth target, LiminalRoom room, int damage, TetrominoShape shape, bool ricochet)
        {
            var go = new GameObject(ricochet ? "Ricochet I Block" : "Soft Drop " + shape);
            go.transform.SetParent(room ? room.transform : owner ? owner.transform.parent : null, true);
            go.transform.position = position;
            var shot = go.AddComponent<TetrominoProjectile>();
            shot.owner = owner; shot.target = target; shot.room = room; shot.hadRoom = room;
            direction.y = 0;
            shot.direction = direction.sqrMagnitude > .0001f ? direction.normalized : Vector3.forward;
            shot.Damage = Mathf.Max(1, damage); shot.Ricochet = ricochet;
            shot.radius = ricochet ? .82f : .4f;
            shot.Speed = ricochet ? RicochetSpeed : SlowSpeed;
            shot.visual = TetrominoVisual.Create(go.transform, ricochet ? TetrominoShape.I : shape, ricochet ? .42f : .26f);
            if (owner) owner.Defeated += shot.OwnerDefeated;
            if (target) target.Died += shot.Finish;
            return shot;
        }

        void Update()
        {
            if (Finished) return;
            if (!owner || !owner.IsAlive || !target || !target.IsAlive || (hadRoom && !room)
                || (room && !room.Contains(transform.position))) { Finish(); return; }
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            float lifetime = Ricochet ? RicochetLifetime : SoftDropLifetime;
            float nextAge = Mathf.Min(age + dt, lifetime);
            float distance = Ricochet ? RicochetSpeed * (nextAge - age) : SoftDropDistance(nextAge) - SoftDropDistance(age);
            age = nextAge;
            Speed = Ricochet ? RicochetSpeed : Mathf.Lerp(SlowSpeed, FastSpeed,
                Mathf.SmoothStep(0, 1, Mathf.InverseLerp(SlowDuration, SlowDuration + AccelerationDuration, age)));
            visual.Rotate(Ricochet ? Vector3.up : Vector3.forward, (Ricochet ? 540 : 115) * dt, Space.Self);
            Advance(distance);
            if (age >= lifetime) Finish();
        }

        // Integral of the speed curve: the slow/fast transition has the same travel at any frame rate.
        static float SoftDropDistance(float time)
        {
            float t = Mathf.Clamp01((time - SlowDuration) / AccelerationDuration);
            float ramp = (FastSpeed - SlowSpeed) * AccelerationDuration * (t * t * t - .5f * t * t * t * t);
            return SlowSpeed * Mathf.Min(time, SlowDuration + AccelerationDuration) + ramp
                + FastSpeed * Mathf.Max(0, time - SlowDuration - AccelerationDuration);
        }

        void Advance(float distance)
        {
            // At most four wall contacts can occur before destruction, even with a very long frame.
            for (int step = 0; step < 5 && distance > .0001f && !Finished; step++)
            {
                if (!TetrominoSweep.Cast(gameObject.scene.GetPhysicsScene(), room, transform.position, radius,
                    direction, distance, target, hits, overlaps, out var impact))
                { transform.position += direction * distance; return; }
                float travelled = Mathf.Max(0, impact.distance);
                transform.position += direction * travelled;
                distance = Mathf.Max(0, distance - travelled);
                if (impact.player)
                {
                    DamageAttempts++;
                    HitPlayer = impact.player.TakeDamage(Damage);
                    Finish();
                    return;
                }
                if (!Ricochet) { Finish(); return; }
                BounceCount++;
                if (BounceCount >= 4) { Finish(); return; }
                direction = Vector3.Reflect(direction, impact.normal).normalized;
                // Separation consumes the remaining distance too. This prevents zero-distance corner loops.
                float separation = Mathf.Min(.015f, distance);
                transform.position += impact.normal * separation;
                distance -= separation;
            }
        }

        void OwnerDefeated(TrainingEnemy _) => Finish();
        void Finish()
        {
            if (Finished) return;
            Finished = true;
            gameObject.SetActive(false);
            Destroy(gameObject);
        }
        void OnDestroy()
        {
            if (owner) owner.Defeated -= OwnerDefeated;
            if (target) target.Died -= Finish;
        }
    }

    /// <summary>Non-allocating horizontal sweeps shared by shots and charging summons.</summary>
    internal static class TetrominoSweep
    {
        internal struct Impact
        {
            public float distance;
            public Vector3 normal;
            public LiminalPlayerHealth player;
        }

        internal static bool Cast(PhysicsScene physics, LiminalRoom room, Vector3 origin, float radius,
            Vector3 direction, float distance, LiminalPlayerHealth target, RaycastHit[] hits, Collider[] overlaps, out Impact impact)
        {
            impact = new Impact { distance = distance, normal = -direction };
            bool found = false;
            // Sweeps do not report shapes containing their starting point. A muzzle already
            // touching the player or facing into scenery must still resolve that first contact.
            int overlapCount = physics.OverlapSphere(origin, radius, overlaps, ~0, QueryTriggerInteraction.Ignore);
            if (overlapCount >= overlaps.Length) { impact.distance = 0; return true; }
            for (int i = 0; i < overlapCount; i++)
            {
                var other = overlaps[i];
                if (!other || other.GetComponentInParent<TrainingEnemy>()) continue;
                var player = other.GetComponentInParent<LiminalPlayerHealth>();
                if (player && player != target) continue;
                Vector3 normal = origin - other.ClosestPoint(origin);
                if (!player)
                {
                    if (normal.sqrMagnitude < .0001f) normal = -direction;
                    else
                    {
                        normal.Normalize();
                        if (Mathf.Abs(normal.y) > .6f || Vector3.Dot(normal, direction) >= -.0001f) continue;
                        normal = Vector3.ProjectOnPlane(normal, Vector3.up).normalized;
                    }
                }
                // A wall at the same initial distance wins over a player on its far side.
                if (found && !impact.player && player) continue;
                impact.distance = 0;
                impact.normal = normal.sqrMagnitude > .01f ? normal.normalized : -direction;
                impact.player = player;
                found = true;
            }
            int count = physics.SphereCast(origin, radius, direction, hits, distance, ~0, QueryTriggerInteraction.Ignore);
            // Fail closed rather than tunnel if pathological overlapping geometry fills the fixed buffer.
            if (count >= hits.Length) { impact.distance = 0; return true; }
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (!hit.collider || hit.distance > impact.distance) continue;
                if (hit.collider.GetComponentInParent<TrainingEnemy>()) continue;
                var player = hit.collider.GetComponentInParent<LiminalPlayerHealth>();
                if (player && player != target) continue;
                if (found && hit.distance == impact.distance && !impact.player && player) continue;
                if (!player && Mathf.Abs(hit.normal.y) > .6f) continue;
                Vector3 normal = Vector3.ProjectOnPlane(hit.normal, Vector3.up);
                if (!player && normal.sqrMagnitude < .01f) continue;
                impact.distance = hit.distance;
                impact.normal = normal.sqrMagnitude > .01f ? normal.normalized : -direction;
                impact.player = player;
                found = true;
            }
            if (room)
            {
                // The authored room limit is also a wall, including scenes with an open entrance gate.
                Vector3 local = room.transform.InverseTransformPoint(origin);
                Vector3 delta = room.transform.InverseTransformVector(direction);
                Vector3 scale = room.transform.lossyScale;
                for (int axis = 0; axis <= 2; axis += 2)
                {
                    float inset = radius / Mathf.Max(.001f, Mathf.Abs(scale[axis]));
                    float lower = room.localBounds.min[axis] + inset, upper = room.localBounds.max[axis] - inset;
                    if (Mathf.Abs(delta[axis]) < .0001f) continue;
                    float boundary = delta[axis] > 0 ? upper : lower;
                    float travel = (boundary - local[axis]) / delta[axis];
                    // A muzzle very near a wall may already overlap its inset. Only its outward
                    // movement is blocked; an inward shot can leave that overlap normally.
                    if (travel < -.01f)
                    {
                        bool outward = (local[axis] > upper && delta[axis] > 0) || (local[axis] < lower && delta[axis] < 0);
                        if (!outward) continue;
                        travel = 0;
                    }
                    if (travel > impact.distance) continue;
                    var normal = Vector3.zero; normal[axis] = delta[axis] > 0 ? -1 : 1;
                    impact.distance = Mathf.Max(0, travel);
                    impact.normal = room.transform.TransformDirection(normal).normalized;
                    impact.player = null;
                    found = true;
                }
            }
            return found;
        }
    }
}
