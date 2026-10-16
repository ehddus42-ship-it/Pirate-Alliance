using UnityEngine;

namespace AcRoguelike
{
    public sealed class SpiritFlame : MonoBehaviour
    {
        public Transform flameBody;
        public Light glow;
        public float duration = 3.5f;
        public float damageRadius = .55f;
        public int contactDamage = 8;

        public int HitCount { get; private set; }

        Vector3 center;
        float angle;
        float age;
        float damageTimer;

        public void Initialize(Vector3 impactPoint, int index, int count)
        {
            center = new Vector3(impactPoint.x, .35f, impactPoint.z);
            angle = index * Mathf.PI * 2f / Mathf.Max(1, count);
        }

        void Update()
        {
            age += Time.deltaTime;
            angle += Time.deltaTime * 1.3f;
            float radius = .85f + Mathf.Sin(age * 3.7f) * .10f;
            transform.position = center
                + new Vector3(Mathf.Cos(angle) * radius,
                    .30f + Mathf.Sin(age * 5f + angle) * .12f,
                    Mathf.Sin(angle) * radius);
            if (flameBody)
            {
                flameBody.Rotate(Vector3.up, 100f * Time.deltaTime, Space.Self);
                flameBody.localScale = Vector3.one * (1f + Mathf.Sin(age * 11f) * .08f);
            }
            if (glow) glow.intensity = 2.2f + Mathf.Sin(age * 12f) * .55f;

            damageTimer -= Time.deltaTime;
            if (damageTimer <= 0f)
            {
                foreach (var enemy in FindObjectsByType<TrainingEnemy>(FindObjectsSortMode.None))
                {
                    if (!enemy.IsAlive || (enemy.AimPoint - transform.position).sqrMagnitude
                        > damageRadius * damageRadius) continue;
                    enemy.TakeDamage(contactDamage);
                    HitCount++;
                }
                damageTimer = .45f;
            }
            if (age >= duration) Destroy(gameObject);
        }
    }
}
