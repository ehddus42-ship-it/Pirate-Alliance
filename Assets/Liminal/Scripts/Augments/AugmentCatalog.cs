using System;
using System.Collections.Generic;

namespace AcRoguelike.Liminal
{
    /// <summary>
    /// Every augment the game knows. Common augments are listed here; character augments are added with Register
    /// (for example from that hunter's own script) and are drawn from the same pool when that hunter plays.
    /// See Documentation/Augments/README.md.
    /// </summary>
    public static class AugmentCatalog
    {
        public const string EfficientSwordplay = "efficient_swordplay";
        public const string AutoSupport = "auto_support";
        public const string CountingStar = "counting_star";
        public const string Steadfast = "steadfast";
        public const string Yarara = "yarara";
        public const string Deposit = "deposit";
        public const string FxWarrior = "fx_warrior";
        public const string FuturesWarrior = "futures_warrior";

        /// <summary>Offer weight per rarity (all equal until rarities are tuned).</summary>
        public static readonly Dictionary<AugmentRarity, float> RarityWeights = new Dictionary<AugmentRarity, float>
        {
            { AugmentRarity.Common, 1f }, { AugmentRarity.Rare, 1f }, { AugmentRarity.Epic, 1f }, { AugmentRarity.Legendary, 1f },
        };

        static readonly List<AugmentDefinition> all = new List<AugmentDefinition>
        {
            new AugmentDefinition(EfficientSwordplay, "효율적인 검술",
                "기본공격 콤보의 첫 번째·두 번째 타격이 사라진다.\n남은 타격이 있을 때만 다시 등장한다.",
                maxStacks: 99, canOffer: a => a.ComboSwingsAfterNextTrim > 0, onAcquire: a => a.ApplyCombo()),
            new AugmentDefinition(AutoSupport, "알아하쇼",
                $"지원스킬을 직접 쓸 수 없다.\n대신 공격 {HunterAugments.AutoSupportHits}타마다 지원스킬이 자동 발동한다.",
                onAcquire: a => a.ApplySupportLock()),
            new AugmentDefinition(CountingStar, "Counting Star",
                $"치명타 적중 시 별똥별이 떨어진다.\n(재사용 대기 {HunterAugments.StarCooldown:0}초)"),
            new AugmentDefinition(Steadfast, "우직하게",
                $"치명타 확률이 0%로 고정된다.\n모든 공격 피해 ×{HunterAugments.SteadfastMultiplier:0.0}"),
            new AugmentDefinition(Yarara, "야라라라",
                $"콤보 마지막 공격 때 가장 가까운 적 하나를 날려 보낸다. 날아간 적이 다른 적과 부딪히면 그 적이 피해를 입는다.\n보스는 넉백 대신 마지막 공격 피해 ×{HunterAugments.YararaBossMultiplier:0.0}"),
            new AugmentDefinition(Deposit, "예금",
                $"이후 증강을 고를 때마다 마석 +{HunterAugments.DepositPerPick}.\n2번째 증강으로 고르면 즉시 +{HunterAugments.DepositSecond}, 3번째 증강으로 고르면 즉시 +{HunterAugments.DepositThird}.",
                onAcquire: a => a.ApplyDeposit()),
            new AugmentDefinition(FxWarrior, "FX전사 헌터짱",
                $"치명타 확률 +{HunterAugments.FxCritBonus * 100:0}%.\n치명타 피해가 {HunterAugments.FxCritMin}배~{HunterAugments.FxCritMax}배 사이에서 무작위로 들어간다."),
            new AugmentDefinition(FuturesWarrior, "선물거래 전사 헌터짱",
                $"반격 피해가 {HunterAugments.FuturesMin}배~{HunterAugments.FuturesMax}배 사이에서 무작위로 들어간다."),
        };

        public static IReadOnlyList<AugmentDefinition> All => all;

        public static AugmentDefinition Find(string id)
        {
            foreach (var augment in all) if (augment.id == id) return augment;
            return null;
        }

        /// <summary>Adds an augment (character augments, later content). Ids must be unique.</summary>
        public static void Register(AugmentDefinition augment)
        {
            if (augment == null || Find(augment.id) != null) throw new ArgumentException("Augment id is missing or already registered.");
            all.Add(augment);
        }

        /// <summary>Removes a registered augment (tests and hot reload). Returns false when the id is unknown.</summary>
        public static bool Unregister(string id)
        {
            var augment = Find(id);
            return augment != null && all.Remove(augment);
        }

        /// <summary>True when this hunter may be offered the augment right now.</summary>
        public static bool IsOfferable(AugmentDefinition augment, HunterAugments owner, string characterId)
        {
            if (augment == null || owner == null) return false;
            if (!augment.IsCommon && augment.characterId != characterId) return false;
            if (owner.Stacks(augment.id) >= augment.maxStacks) return false;
            // Conflicting effects deliberately stay in the pool: a pick can be useless for this build.
            return augment.canOffer == null || augment.canOffer(owner);
        }

        /// <summary>Draws up to `count` different augments from the common pool plus this hunter's own augments.</summary>
        public static List<AugmentDefinition> Roll(HunterAugments owner, string characterId, int count, Random random)
        {
            var pool = new List<AugmentDefinition>();
            foreach (var augment in all) if (IsOfferable(augment, owner, characterId)) pool.Add(augment);
            var offers = new List<AugmentDefinition>();
            while (offers.Count < count && pool.Count > 0)
            {
                float total = 0;
                foreach (var augment in pool) total += Weight(augment);
                double pick = random.NextDouble() * total;
                int chosen = pool.Count - 1;
                for (int i = 0; i < pool.Count; i++)
                {
                    pick -= Weight(pool[i]);
                    if (pick <= 0) { chosen = i; break; }
                }
                offers.Add(pool[chosen]);
                pool.RemoveAt(chosen);
            }
            return offers;
        }

        static float Weight(AugmentDefinition augment)
            => RarityWeights.TryGetValue(augment.rarity, out float weight) ? Math.Max(0f, weight) : 1f;
    }
}
