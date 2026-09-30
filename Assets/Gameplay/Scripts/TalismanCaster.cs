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
        [Tooltip("When enabled, PlayerCombat emits casts at the attack animation's hit time.")]
        public bool externalInput;

        public int CastCount { get; private set; }
        public TrainingEnemy LastTarget { get; private set; }
        public float CooldownRemaining => Mathf.Max(0, nextCastTime - Time.time);

        float nextCastTime;
        bool leftButtonWasHeld;

        void Update()
        {
            if (externalInput || !Application.isFocused || Time.timeScale <= 0) { leftButtonWasHeld = false; return; }
            bool leftButtonHeld = Mouse.current != null && Mouse.current.leftButton.isPressed;
            if (leftButtonHeld && (!leftButtonWasHeld || holdToCast)) TryCast();
            leftButtonWasHeld = leftButtonHeld;
        }

        public bool TryCast()
        {
            if (Time.time < nextCastTime) return false;
            return Cast(FindTarget(Vector3.zero));
        }

        /// <summary>Finds a target in the aimed forward cone without snapping to enemies behind the character.</summary>
        public TrainingEnemy FindAnimationTarget(Vector3 direction) => FindTarget(Vector3.ProjectOnPlane(direction, Vector3.up).normalized);

        // PlayerCombat owns the cadence for animation-driven casts, including cooldown upgrades.
        public bool TryCastFromAnimation(TrainingEnemy target) => isActiveAndEnabled && externalInput && Cast(target);

        TrainingEnemy FindTarget(Vector3 aimDirection)
        {
            TrainingEnemy nearest = null;
            float bestScore = float.PositiveInfinity;
            foreach (var enemy in FindObjectsByType<TrainingEnemy>(FindObjectsSortMode.None))
            {
                if (!enemy.IsAlive || !enemy.CanBeTargeted) continue;
                Vector3 delta = enemy.transform.position - transform.position;
                float distanceSquared = delta.sqrMagnitude;
                if (distanceSquared >= castRange * castRange) continue;
                float alignment = aimDirection.sqrMagnitude > .01f
                    ? Vector3.Dot(aimDirection, Vector3.ProjectOnPlane(delta, Vector3.up).normalized) : 1;
                if (alignment < .5f) continue;
                float score = distanceSquared * (1 + (1 - alignment) * 2);
                if (score >= bestScore || (requireLineOfSight && !CanSee(enemy))) continue;
                bestScore = score;
                nearest = enemy;
            }
            return nearest;
        }

        bool Cast(TrainingEnemy target)
        {
            if (!talismanPrefab || !spiritFlamePrefab || !target || !target.IsAlive) return false;
            if ((target.transform.position - transform.position).sqrMagnitude >= castRange * castRange) return false;
            if (requireLineOfSight && !CanSee(target)) return false;

            Vector3 origin = castOrigin
                ? castOrigin.position
                : transform.position + Vector3.up * 1.2f + transform.forward * .35f;
            var projectileObject = Instantiate(talismanPrefab, origin, Quaternion.identity);
            var projectile = projectileObject.GetComponent<TalismanProjectile>();
            if (!projectile) { Destroy(projectileObject); return false; }
            projectile.Initialize(target, spiritFlamePrefab, impactPrefab, flameCount);
            LastTarget = target;
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
