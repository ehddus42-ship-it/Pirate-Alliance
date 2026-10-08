using System.Collections;
using UnityEngine;

namespace AcRoguelike.Liminal
{
    /// <summary>Applied once, after the enemy owner's initialization. Does not replace its combat or death events.</summary>
    [DisallowMultipleComponent]
    public sealed class GateMissionEnemyModifier : MonoBehaviour
    {
        public const float ArmorDamageMultiplier = .75f;
        public const float BurstDelay = 1.25f;
        public const float BurstRadius = 3f;
        public const int BurstDamage = 14;

        TrainingEnemy enemy;
        LiminalPlayerHealth target;
        LiminalRunDirector director;
        LiminalRoom room;
        GateEnemyGimmick gimmicks;
        Telegraph warning;
        bool applied;

        public void Apply(TrainingEnemy health, GateMissionDefinition mission, LiminalPlayerHealth player, LiminalRunDirector run)
        {
            if (applied || !health || mission == null) return;
            applied = true;
            enemy = health;
            target = player;
            director = run;
            room = GetComponentInParent<LiminalRoom>();
            gimmicks = mission.enemyGimmicks;
            enemy.ApplyMissionStats(mission.healthMultiplier,
                (gimmicks & GateEnemyGimmick.Armor) != 0 ? ArmorDamageMultiplier : 1);
            if ((gimmicks & GateEnemyGimmick.DeathBurst) != 0) enemy.Defeated += OnDefeated;
        }

        void OnDefeated(TrainingEnemy defeated)
        {
            enemy.Defeated -= OnDefeated;
            if (isActiveAndEnabled) StartCoroutine(DeathBurst());
        }

        IEnumerator DeathBurst()
        {
            // Anchor the warning to the floor where the enemy died, not to its flying death animation.
            Vector3 center = transform.position;
            if (room) center.y = room.transform.position.y;
            warning = Telegraph.Create(room ? room.transform : transform.parent, "MissionDeathBurstWarning");
            for (float elapsed = 0; elapsed < BurstDelay; elapsed += Time.deltaTime)
            {
                if (!director || director.Phase == LiminalRunPhase.Lobby || director.Phase == LiminalRunPhase.InvalidConfiguration)
                { ClearWarning(); yield break; }
                warning.Circle(center, BurstRadius, elapsed / BurstDelay);
                AttackAnticipation.Show(transform, elapsed / BurstDelay, allowDefeated: true);
                yield return null;
            }
            AttackAnticipation.Hide(transform);
            warning.Circle(center, BurstRadius, 1);
            warning.Release();
            if (director && director.Phase == LiminalRunPhase.Exploring && target && target.IsAlive)
            {
                Vector3 delta = target.transform.position - center;
                if (Mathf.Abs(delta.y) <= 2.5f && new Vector2(delta.x, delta.z).sqrMagnitude <= BurstRadius * BurstRadius)
                    target.TakeDamage(BurstDamage);
            }
            yield return new WaitForSeconds(.2f);
            ClearWarning();
        }

        void ClearWarning()
        {
            AttackAnticipation.Hide(transform);
            if (warning) { warning.Hide(); Destroy(warning.gameObject); }
            warning = null;
        }

        void OnDisable() { StopAllCoroutines(); ClearWarning(); }
        void OnDestroy()
        {
            if (enemy) enemy.Defeated -= OnDefeated;
            ClearWarning();
        }
    }
}
