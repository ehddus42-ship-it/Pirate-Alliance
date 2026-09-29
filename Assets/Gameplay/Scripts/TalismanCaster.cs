using UnityEngine;
using UnityEngine.InputSystem;

namespace AcRoguelike
{
    public sealed class TalismanCaster : MonoBehaviour
    {
        public GameObject talismanPrefab;
        public GameObject spiritFlamePrefab;
        public GameObject impactPrefab;
        public Transform castOrigin;
        public float castRange = 14f;
        public float cooldown = .7f;
        public int flameCount = 4;
        public bool holdToCast;
        public bool requireLineOfSight;

        public int CastCount { get; private set; }
        public TrainingEnemy LastTarget { get; private set; }
        public float CooldownRemaining => Mathf.Max(0, nextCastTime - Time.time);

        float nextCastTime;
        bool leftButtonWasHeld;

        void Update()
        {
            bool leftButtonHeld = Mouse.current != null && Mouse.current.leftButton.isPressed;
            if (leftButtonHeld && (!leftButtonWasHeld || holdToCast)) TryCast();
            leftButtonWasHeld = leftButtonHeld;
        }

        public bool TryCast()
        {
            if (Time.time < nextCastTime || !talismanPrefab || !spiritFlamePrefab)
                return false;

            TrainingEnemy nearest = null;
            float closestSquared = castRange * castRange;
            foreach (var enemy in FindObjectsByType<TrainingEnemy>(FindObjectsSortMode.None))
            {
                if (!enemy.IsAlive) continue;
                if (requireLineOfSight && !CanSee(enemy)) continue;
                float distanceSquared = (enemy.transform.position - transform.position).sqrMagnitude;
                if (distanceSquared >= closestSquared) continue;
                closestSquared = distanceSquared;
                nearest = enemy;
            }
            if (!nearest) return false;

            Vector3 origin = castOrigin
                ? castOrigin.position
                : transform.position + Vector3.up * 1.2f + transform.forward * .35f;
            var projectile = Instantiate(talismanPrefab, origin, Quaternion.identity)
                .GetComponent<TalismanProjectile>();
            if (!projectile) return false;
            projectile.Initialize(nearest, spiritFlamePrefab, impactPrefab, flameCount);
            LastTarget = nearest;
            CastCount++;
            nextCastTime = Time.time + cooldown;
            return true;
        }

        bool CanSee(TrainingEnemy enemy)
        {
            Vector3 from = castOrigin ? castOrigin.position : transform.position + Vector3.up * 1.2f;
            Vector3 direction = enemy.AimPoint - from;
            foreach (var hit in Physics.RaycastAll(from, direction.normalized, direction.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(transform) || hit.transform == transform) continue;
                if (hit.collider.GetComponentInParent<TrainingEnemy>() == enemy) continue;
                return false;
            }
            return true;
        }
    }
}
