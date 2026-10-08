using System.Collections.Generic;
using UnityEngine;

namespace AcRoguelike.Liminal
{
    /// <summary>Counting Star: a small star falls on a crit target and hurts every enemy around the impact.</summary>
    sealed class AugmentStarfall : MonoBehaviour
    {
        static readonly Color Star = new Color(1f, .86f, .35f);
        static readonly Color Tail = new Color(.62f, .7f, 1f);
        Vector3 target, start;
        float fallTime, radius, age, trailClock;
        int damage;
        Transform body;
        LineRenderer warning;

        public static void Drop(Vector3 point, int blastDamage, float blastRadius)
        {
            var star = new GameObject("Augment / Counting Star").AddComponent<AugmentStarfall>();
            star.Launch(point, blastDamage, blastRadius);
        }

        void Launch(Vector3 point, int blastDamage, float blastRadius)
        {
            target = point;
            damage = blastDamage;
            radius = blastRadius;
            fallTime = ProjectileTuning.ScaleFlightDuration(.42f);
            start = target + new Vector3(-2.6f, 13f, -3.8f);
            transform.position = start;
            body = new GameObject("Star").transform;
            body.SetParent(transform, false);
            var glow = SupportVoxels.Mat(Star, 6);
            SupportVoxels.Cube(body, Vector3.zero, .42f, glow);
            foreach (var arm in new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
                SupportVoxels.Cube(body, arm * .3f, .2f, glow);
            SupportVoxels.Glow(transform, Star, 4, 5);
            var marks = new GameObject("Star Warning").transform;
            marks.SetParent(transform, false);
            warning = SupportVoxels.Ring(marks, Star, .08f);
            SupportVoxels.SetRing(warning, target, radius);
        }

        void Update()
        {
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / fallTime);
            transform.position = Vector3.Lerp(start, target + Vector3.up * .5f, t * t);
            body.Rotate(new Vector3(0, 0, 720) * Time.deltaTime, Space.Self);
            trailClock += Time.deltaTime;
            if (trailClock > .03f)
            {
                trailClock = 0;
                SupportVoxels.Burst(transform.position, 2, .6f, .18f, new[] { Star, Tail }, 4f);
            }
            if (t < 1) return;
            foreach (var enemy in FindObjectsByType<TrainingEnemy>(FindObjectsSortMode.None))
            {
                if (!enemy.IsAlive) continue;
                Vector3 offset = enemy.transform.position - target;
                offset.y = 0;
                if (offset.magnitude > radius) continue;
                enemy.LastHitDirection = offset.sqrMagnitude > .01f ? offset.normalized : Vector3.forward;
                enemy.LastHitImpact = .8f;
                enemy.TakeDamage(damage);
                HitFeedback.DamageNumber(enemy.AimPoint + Vector3.up * .5f, damage, true);
            }
            SupportVoxels.Burst(target + Vector3.up * .3f, 26, 7f, .22f, new[] { Star, Tail, Color.white }, 4f);
            HitFeedback.Ring(target + Vector3.up * .1f, Vector3.up, radius * .9f);
            HitFeedback.Shake(.06f, .14f);
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 야라라라: the finisher sends one enemy sliding away; whatever it slams into on the way takes damage (once each).
    /// </summary>
    sealed class AugmentKnockback : MonoBehaviour
    {
        const float Speed = 18f;
        const float Duration = .5f;
        readonly HashSet<TrainingEnemy> struck = new HashSet<TrainingEnemy>();
        TrainingEnemy self;
        CharacterController body;
        Vector3 direction;
        float age;
        int damage;

        public static void Launch(TrainingEnemy enemy, Vector3 direction, int collisionDamage)
        {
            if (!enemy) return;
            if (!enemy.TryGetComponent<AugmentKnockback>(out var push)) push = enemy.gameObject.AddComponent<AugmentKnockback>();
            push.self = enemy;
            push.body = enemy.GetComponent<CharacterController>();
            push.direction = direction;
            push.damage = collisionDamage;
            push.age = 0;
            push.struck.Clear();
            HitFeedback.Dust(enemy.transform.position, 1.2f);
        }

        void Update()
        {
            if (!self || !self.IsAlive || age >= Duration) { Destroy(this); return; }
            float dt = Time.deltaTime;
            age += dt;
            // Fast launch that slides to a stop: about 4.5 m in half a second.
            Vector3 step = direction * (Speed * Mathf.Max(0, 1 - age / Duration) * dt);
            if (body && body.enabled) body.Move(step);
            else transform.position += step;
            float reach = Radius(self) + .15f;
            foreach (var other in FindObjectsByType<TrainingEnemy>(FindObjectsSortMode.None))
            {
                if (other == self || !other.IsAlive || struck.Contains(other)) continue;
                Vector3 offset = other.transform.position - transform.position;
                offset.y = 0;
                if (offset.magnitude > reach + Radius(other)) continue;
                struck.Add(other);
                other.LastHitDirection = offset.sqrMagnitude > .01f ? offset.normalized : direction;
                other.LastHitImpact = 1.2f;
                other.TakeDamage(damage);
                HitFeedback.Hit(other, other.AimPoint, direction, damage, true);
                HitFeedback.Shake(.07f, .16f);
            }
        }

        static float Radius(TrainingEnemy enemy)
            => enemy.TryGetComponent<CharacterController>(out var controller) ? controller.radius : .55f;
    }
}
