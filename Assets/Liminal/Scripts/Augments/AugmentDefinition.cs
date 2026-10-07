using System;
using UnityEngine;

namespace AcRoguelike.Liminal
{
    /// <summary>Augment rarity. Every augment is Common for now; weights per rarity come with the rarity update.</summary>
    public enum AugmentRarity { Common, Rare, Epic, Legendary }

    /// <summary>
    /// One augment (증강). Common augments have no characterId and can appear for every hunter; character augments set
    /// characterId to a HunterCharacter id and join the same random pool only for that hunter.
    /// </summary>
    public sealed class AugmentDefinition
    {
        public readonly string id;
        public readonly string title;
        public readonly string description;
        public readonly AugmentRarity rarity;
        /// <summary>Null or empty: a common augment. Otherwise only offered to the hunter with this id.</summary>
        public readonly string characterId;
        /// <summary>How many times one run can take it (1 = never offered again once taken).</summary>
        public readonly int maxStacks;
        /// <summary>Extra offer rule (for example "only while the combo keeps at least one swing").</summary>
        public readonly Func<HunterAugments, bool> canOffer;
        /// <summary>Applied once per pick, after the stack count went up.</summary>
        public readonly Action<HunterAugments> onAcquire;

        public bool IsCommon => string.IsNullOrEmpty(characterId);

        public AugmentDefinition(string id, string title, string description, AugmentRarity rarity = AugmentRarity.Common,
            string characterId = null, int maxStacks = 1,
            Func<HunterAugments, bool> canOffer = null, Action<HunterAugments> onAcquire = null)
        {
            this.id = id;
            this.title = title;
            this.description = description;
            this.rarity = rarity;
            this.characterId = characterId;
            this.maxStacks = Mathf.Max(1, maxStacks);
            this.canOffer = canOffer;
            this.onAcquire = onAcquire;
        }

        public static string RarityLabel(AugmentRarity rarity) => rarity switch
        {
            AugmentRarity.Rare => "희귀",
            AugmentRarity.Epic => "영웅",
            AugmentRarity.Legendary => "전설",
            _ => "일반",
        };

        public static Color RarityColor(AugmentRarity rarity) => rarity switch
        {
            AugmentRarity.Rare => new Color(.44f, .74f, 1f),
            AugmentRarity.Epic => new Color(.71f, .5f, 1f),
            AugmentRarity.Legendary => new Color(1f, .74f, .3f),
            _ => new Color(.86f, .76f, .49f),
        };
    }
}
