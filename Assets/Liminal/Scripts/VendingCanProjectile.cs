using UnityEngine;

namespace AcRoguelike.Liminal
{
    /// <summary>A swept, ballistic can. Ownership survives animation release and prevents self hits.</summary>
    public sealed class VendingCanProjectile : MonoBehaviour
    {
        public float radius = .085f;
        public float lifetime = 5f;
        Vector3 velocity;
        Transform owner;
        LiminalPlayerHealth target;
        int damage;
        float age;
        bool launched;
        public static int ImpactCount { get; private set; }

        public void Launch(Vector3 destination, float flightTime, int amount, Transform source, LiminalPlayerHealth player)
        {
            owner = source; target = player; damage = amount;
            flightTime = Mathf.Max(.25f, flightTime);
            velocity = (destination - transform.position) / flightTime - Physics.gravity * (.5f * flightTime);
            launched = true;
        }

        void Update()
        {
            if (!launched || Time.deltaTime <= 0) return;
            age += Time.deltaTime;
            if (age > lifetime || !owner || !target || !target.IsAlive || !owner.GetComponent<TrainingEnemy>().IsAlive) { Destroy(gameObject); return; }
            float dt = Time.deltaTime;
            Vector3 step = velocity * dt + Physics.gravity * (.5f * dt * dt);
            var hits = Physics.SphereCastAll(transform.position, radius, step.normalized, step.magnitude,
                ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (!hit.collider || hit.transform == transform || hit.transform.IsChildOf(transform) ||
                    hit.transform == owner || hit.transform.IsChildOf(owner)) continue;
                var player = hit.collider.GetComponentInParent<LiminalPlayerHealth>();
                if (player) player.TakeDamage(damage);
                ImpactCount++;
                Destroy(gameObject);
                return;
            }
            transform.position += step;
            velocity += Physics.gravity * dt;
            transform.Rotate(new Vector3(570, 170, 95) * dt, Space.Self);
        }
    }
}
