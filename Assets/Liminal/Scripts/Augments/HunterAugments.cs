using System.Collections.Generic;
using UnityEngine;

namespace AcRoguelike.Liminal
{
    /// <summary>
    /// The augments a hunter holds in the current run and what they do. Added to the player by the run director and
    /// cleared at every new run. Melee effects come through IMeleeHitHooks (MeleeSlash asks at every cut).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HunterAugments : MonoBehaviour, IMeleeHitHooks
    {
        // Tuning (the card texts read these, so cards and code stay in step).
        public const int AutoSupportHits = 12;
        public const float StarCooldown = 5f;
        public const int StarDamage = 32;
        public const float StarRadius = 2.4f;
        public const float SteadfastMultiplier = 1.3f;
        public const float YararaBossMultiplier = 1.5f;
        public const float YararaRange = 7f;
        public const int YararaCollisionDamage = 26;
        public const int DepositPerPick = 20;
        public const int DepositSecond = 20;
        public const int DepositThird = 35;
        public const float FxCritBonus = .3f;
        public const float FxCritMin = .1f;
        public const float FxCritMax = 3f;
        public const float FuturesMin = .1f;
        public const float FuturesMax = 3.5f;

        readonly Dictionary<string, int> stacks = new Dictionary<string, int>();
        readonly List<AugmentDefinition> picks = new List<AugmentDefinition>();
        LiminalRunDirector run;
        MeleeSlash melee;
        PlayerCombat combat;
        SupportCharacterSkill support;
        bool depositPickedFirst;
        float nextStarAt;

        /// <summary>Augments taken this run, in pick order.</summary>
        public IReadOnlyList<AugmentDefinition> Picks => picks;
        public int PickCount => picks.Count;
        /// <summary>Melee cuts counted toward the next automatic support skill (알아하쇼).</summary>
        public int SupportHitProgress { get; private set; }
        public int AutoSupportCasts { get; private set; }
        public int StarsDropped { get; private set; }
        public int KnockbackLaunches { get; private set; }
        public int DepositPaid { get; private set; }
        public string CharacterId => TryGetComponent<HunterCharacter>(out var hunter) ? hunter.characterId : HunterCharacter.TestHunterId;

        public void Initialize(LiminalRunDirector director)
        {
            run = director;
            melee = GetComponent<MeleeSlash>();
            combat = GetComponent<PlayerCombat>();
            if (melee) melee.RefreshHooks();
        }

        public int Stacks(string id) => id != null && stacks.TryGetValue(id, out int count) ? count : 0;
        public bool Has(string id) => Stacks(id) > 0;

        /// <summary>Forgets every augment and undoes their effects (new run).</summary>
        public void ResetRun()
        {
            stacks.Clear();
            picks.Clear();
            depositPickedFirst = false;
            nextStarAt = 0;
            SupportHitProgress = AutoSupportCasts = StarsDropped = KnockbackLaunches = DepositPaid = 0;
            if (combat) combat.comboStartIndex = 0;
            var skill = Support;
            if (skill) skill.SetManualLock(false);
        }

        /// <summary>Takes one augment: stacks it, pays a deposit taken earlier, then applies its own effect.</summary>
        public void Acquire(AugmentDefinition augment)
        {
            if (augment == null) return;
            picks.Add(augment);
            stacks[augment.id] = Stacks(augment.id) + 1;
            // 예금 taken as the first augment pays at every later pick.
            if (depositPickedFirst && augment.id != AugmentCatalog.Deposit) Pay(DepositPerPick);
            augment.onAcquire?.Invoke(this);
        }

        // ---- per-augment effects -------------------------------------------------------------------------
        /// <summary>Swings left in the combo if 효율적인 검술 were taken once more.</summary>
        public int ComboSwingsAfterNextTrim => (combat ? combat.ComboLength : 6) - 2 * (Stacks(AugmentCatalog.EfficientSwordplay) + 1);

        internal void ApplyCombo()
        {
            if (combat) combat.comboStartIndex = 2 * Stacks(AugmentCatalog.EfficientSwordplay);
        }

        internal void ApplySupportLock()
        {
            SupportHitProgress = 0;
            var skill = Support;
            if (skill) skill.SetManualLock(true, $"알아하쇼: 지원스킬은 공격 {AutoSupportHits}타마다 자동으로 발동해.");
        }

        internal void ApplyDeposit()
        {
            // First pick: pays at every later pick. Second: +20 now. Third (or later): +35 now, once.
            if (PickCount <= 1) depositPickedFirst = true;
            else Pay(PickCount == 2 ? DepositSecond : DepositThird);
        }

        void Pay(int stones)
        {
            if (stones <= 0) return;
            DepositPaid += stones;
            if (run) run.AddReward(stones);
        }

        SupportCharacterSkill Support
        {
            get
            {
                if (!support) support = FindFirstObjectByType<SupportCharacterSkill>();
                return support;
            }
        }

        // ---- IMeleeHitHooks -------------------------------------------------------------------------------
        public float CritChance(float baseChance)
        {
            if (Has(AugmentCatalog.Steadfast)) return 0;
            return Mathf.Clamp01(baseChance + FxCritBonus * Stacks(AugmentCatalog.FxWarrior));
        }

        public float CritMultiplier(float baseMultiplier)
            => Has(AugmentCatalog.FxWarrior) ? Random.Range(FxCritMin, FxCritMax) : baseMultiplier;

        public float CounterMultiplier(float baseMultiplier)
            => Has(AugmentCatalog.FuturesWarrior) ? Random.Range(FuturesMin, FuturesMax) : baseMultiplier;

        public float DamageMultiplier(TrainingEnemy target, bool comboFinal)
        {
            float multiplier = 1;
            if (Has(AugmentCatalog.Steadfast)) multiplier *= SteadfastMultiplier;
            if (comboFinal && Has(AugmentCatalog.Yarara) && IsBoss(target)) multiplier *= YararaBossMultiplier;
            return multiplier;
        }

        public void OnCrit(TrainingEnemy target)
        {
            if (!Has(AugmentCatalog.CountingStar) || !target || Time.time < nextStarAt) return;
            nextStarAt = Time.time + StarCooldown;
            StarsDropped++;
            int damage = Mathf.RoundToInt(StarDamage * (melee ? Mathf.Max(.1f, melee.damageMultiplier) : 1));
            AugmentStarfall.Drop(target.transform.position, damage, StarRadius);
        }

        public void OnCutLanded(TrainingEnemy first, bool comboFinal)
        {
            if (Has(AugmentCatalog.AutoSupport))
            {
                SupportHitProgress = Mathf.Min(SupportHitProgress + 1, AutoSupportHits);
                // A kill that opens the augment pick pauses the run; the charge waits for play to resume.
                if (SupportHitProgress >= AutoSupportHits && run && run.Phase == LiminalRunPhase.Exploring)
                {
                    SupportHitProgress -= AutoSupportHits;
                    var skill = Support;
                    if (skill && skill.ForceActivate()) AutoSupportCasts++;
                }
            }
            if (comboFinal && Has(AugmentCatalog.Yarara)) LaunchNearest();
        }

        void LaunchNearest()
        {
            TrainingEnemy nearest = null;
            float best = YararaRange * YararaRange;
            foreach (var enemy in FindObjectsByType<TrainingEnemy>(FindObjectsSortMode.None))
            {
                if (!enemy.IsAlive || !enemy.CanBeTargeted) continue;
                Vector3 offset = enemy.transform.position - transform.position;
                offset.y = 0;
                if (offset.sqrMagnitude < best) { best = offset.sqrMagnitude; nearest = enemy; }
            }
            // Bosses stand their ground; they take the finisher bonus instead (DamageMultiplier).
            if (!nearest || IsBoss(nearest)) return;
            Vector3 direction = nearest.transform.position - transform.position;
            direction.y = 0;
            if (direction.sqrMagnitude < .01f) direction = transform.forward;
            int damage = Mathf.RoundToInt(YararaCollisionDamage * (melee ? Mathf.Max(.1f, melee.damageMultiplier) : 1));
            AugmentKnockback.Launch(nearest, direction.normalized, damage);
            KnockbackLaunches++;
        }

        /// <summary>Boss bodies (and their parts) are any enemy under a component whose type name ends in "Boss".</summary>
        public static bool IsBoss(TrainingEnemy enemy)
        {
            if (!enemy) return false;
            foreach (var behaviour in enemy.GetComponentsInParent<MonoBehaviour>(true))
                if (behaviour && behaviour.GetType().Name.EndsWith("Boss")) return true;
            return false;
        }
    }
}
