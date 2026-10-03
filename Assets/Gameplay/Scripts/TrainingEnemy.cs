using System.Collections;
using UnityEngine;

namespace AcRoguelike
{
    public sealed class TrainingEnemy : MonoBehaviour
    {
        public int maxHealth = 60;
        public Transform aimAnchor;
        public Renderer[] visibleRenderers;
        public float respawnDelay = 4f;
        public bool respawnOnDeath = true;
        public event System.Action<TrainingEnemy> Defeated;
        /// <summary>Raised for every hit that deals damage (amount, still alive afterwards).</summary>
        public event System.Action<TrainingEnemy, int> Damaged;
        [Tooltip("When set, the owner plays its own death (knock-up, landing) and calls HideNow when it is done.")]
        public bool deferDeathVisuals;
        /// <summary>Horizontal direction of the latest hit, set by attackers before TakeDamage (zero when unknown).</summary>
        public Vector3 LastHitDirection { get; set; }
        /// <summary>Strength of the latest hit, 1 for a normal slash; finishers are larger.</summary>
        public float LastHitImpact { get; set; } = 1;

        public int Health { get; private set; }
        public int HitCount { get; private set; }
        public bool IsAlive => Health > 0;
        public bool CanBeTargeted { get; set; } = true;
        public Vector3 AimPoint => aimAnchor ? aimAnchor.position : transform.position + Vector3.up;

        Collider[] colliders;
        float incomingDamageMultiplier = 1;

        void Awake()
        {
            Health = maxHealth;
            colliders = GetComponentsInChildren<Collider>();
            if (visibleRenderers == null || visibleRenderers.Length == 0)
                visibleRenderers = GetComponentsInChildren<Renderer>();
        }

        public void TakeDamage(int damage)
        {
            if (!IsAlive || damage <= 0) return;
            damage = Mathf.Max(1, Mathf.RoundToInt(damage * incomingDamageMultiplier));
            HitCount++;
            Health = Mathf.Max(0, Health - damage);
            Damaged?.Invoke(this, damage);
            if (Health == 0)
            {
                Defeated?.Invoke(this);
                if (respawnOnDeath) StartCoroutine(Respawn());
                else if (deferDeathVisuals)
                {
                    foreach (var collider in colliders) if (collider) collider.enabled = false;
                }
                else HideNow();
            }
        }

        /// <summary>Hides the defeated body (renderers and colliders).</summary>
        public void HideNow()
        {
            foreach (var renderer in visibleRenderers) if (renderer) renderer.enabled = false;
            foreach (var collider in colliders) if (collider) collider.enabled = false;
        }

        public void Configure(int health, bool shouldRespawn)
        {
            StopAllCoroutines();
            maxHealth = Mathf.Max(1, health);
            Health = maxHealth;
            HitCount = 0;
            respawnOnDeath = shouldRespawn;
            incomingDamageMultiplier = 1;
            colliders = GetComponentsInChildren<Collider>();
            visibleRenderers = GetComponentsInChildren<Renderer>();
            foreach (var renderer in visibleRenderers) if (renderer) renderer.enabled = true;
            foreach (var collider in colliders) if (collider) collider.enabled = true;
        }

        /// <summary>Scales an initialized enemy without resetting its targeting, colliders, or owner death hooks.</summary>
        public void ApplyMissionStats(float healthMultiplier, float damageReceivedMultiplier)
        {
            float fraction = maxHealth > 0 ? Health / (float)maxHealth : 1;
            maxHealth = Mathf.Max(1, Mathf.RoundToInt(maxHealth * Mathf.Max(1, healthMultiplier)));
            Health = IsAlive ? Mathf.Max(1, Mathf.RoundToInt(maxHealth * fraction)) : 0;
            incomingDamageMultiplier = Mathf.Clamp(damageReceivedMultiplier, .01f, 1);
        }

        IEnumerator Respawn()
        {
            foreach (var renderer in visibleRenderers) if (renderer) renderer.enabled = false;
            foreach (var collider in colliders) if (collider) collider.enabled = false;
            yield return new WaitForSeconds(respawnDelay);
            Health = maxHealth;
            foreach (var renderer in visibleRenderers) if (renderer) renderer.enabled = true;
            foreach (var collider in colliders) if (collider) collider.enabled = true;
        }
    }
}
