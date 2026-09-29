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

        public int Health { get; private set; }
        public int HitCount { get; private set; }
        public bool IsAlive => Health > 0;
        public Vector3 AimPoint => aimAnchor ? aimAnchor.position : transform.position + Vector3.up;

        Collider[] colliders;

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
            HitCount++;
            Health = Mathf.Max(0, Health - damage);
            if (Health == 0)
            {
                Defeated?.Invoke(this);
                if (respawnOnDeath) StartCoroutine(Respawn());
                else
                {
                    foreach (var renderer in visibleRenderers) if (renderer) renderer.enabled = false;
                    foreach (var collider in colliders) if (collider) collider.enabled = false;
                }
            }
        }

        public void Configure(int health, bool shouldRespawn)
        {
            StopAllCoroutines();
            maxHealth = Mathf.Max(1, health);
            Health = maxHealth;
            HitCount = 0;
            respawnOnDeath = shouldRespawn;
            colliders = GetComponentsInChildren<Collider>();
            visibleRenderers = GetComponentsInChildren<Renderer>();
            foreach (var renderer in visibleRenderers) if (renderer) renderer.enabled = true;
            foreach (var collider in colliders) if (collider) collider.enabled = true;
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
