using UnityEngine;

namespace AcRoguelike
{
    public sealed class TalismanProjectile : MonoBehaviour
    {
        public Transform card;
        public float speed = 12f;
        public float hitDistance = .35f;
        public float maximumLifetime = 3f;

        public TrainingEnemy Target { get; private set; }

        GameObject spiritFlamePrefab;
        GameObject impactPrefab;
        int flameCount;
        Vector3 lastTargetPoint;
        float lifetime;

        public void Initialize(TrainingEnemy target, GameObject flame, GameObject impact, int count)
        {
            Target = target;
            spiritFlamePrefab = flame;
            impactPrefab = impact;
            flameCount = Mathf.Max(1, count);
            lastTargetPoint = target.AimPoint;
        }

        void Update()
        {
            lifetime += Time.deltaTime;
            if (Target && Target.IsAlive) lastTargetPoint = Target.AimPoint;

            transform.position = Vector3.MoveTowards(
                transform.position, lastTargetPoint, speed * Time.deltaTime);
            if (card && Camera.main)
            {
                var towardCamera = Camera.main.transform.position - card.position;
                card.rotation = Quaternion.LookRotation(towardCamera)
                    * Quaternion.Euler(0f, 0f, lifetime * 540f);
            }

            if ((transform.position - lastTargetPoint).sqrMagnitude <= hitDistance * hitDistance)
                Detonate();
            else if (lifetime >= maximumLifetime)
                Destroy(gameObject);
        }

        void Detonate()
        {
            Vector3 center = lastTargetPoint;
            if (Target && Target.IsAlive) Target.TakeDamage(30);
            if (impactPrefab)
            {
                var burst = Instantiate(impactPrefab, center, Quaternion.identity);
                Destroy(burst, 2f);
            }

            for (int i = 0; i < flameCount; i++)
            {
                float angle = i * Mathf.PI * 2f / flameCount;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * .85f;
                var flame = Instantiate(spiritFlamePrefab,
                    new Vector3(center.x + offset.x, .35f, center.z + offset.z),
                    Quaternion.identity);
                var spirit = flame.GetComponent<SpiritFlame>();
                if (spirit) spirit.Initialize(center, i, flameCount);
            }
            Destroy(gameObject);
        }
    }
}
