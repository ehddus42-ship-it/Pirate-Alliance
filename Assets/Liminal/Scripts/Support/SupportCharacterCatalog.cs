using System.Collections.Generic;
using UnityEngine;

namespace AcRoguelike.Liminal
{
    /// <summary>One support character as the loadout window and the HUD card show it.</summary>
    public sealed class SupportCharacterInfo
    {
        public readonly string id, name, skillName, kind, summary, portraitPath;
        /// <summary>Cast with a key (유니). Passive supports (꼬끼오) have nothing for 알아하쇼 to take over.</summary>
        public readonly bool active;

        public SupportCharacterInfo(string id, string name, string skillName, bool active, string kind, string summary, string portraitPath)
        {
            this.id = id; this.name = name; this.skillName = skillName; this.active = active; this.kind = kind; this.summary = summary; this.portraitPath = portraitPath;
        }

        public Texture2D Portrait => string.IsNullOrEmpty(portraitPath) ? null : Resources.Load<Texture2D>(portraitPath);
    }

    /// <summary>
    /// The support characters (지원 캐릭터 일람). One is in the party at a time (HunterLoadout.SupportId); the others
    /// stay idle. Each one's skill lives in its own component on the run director: 유니 = SupportCharacterSkill,
    /// 꼬끼오 = KokkioSupport.
    /// </summary>
    public static class SupportCharacterCatalog
    {
        public const string Yuni = "yuni";
        public const string Kokkio = "kokkio";
        public const string DefaultId = Yuni;

        static readonly List<SupportCharacterInfo> all = new List<SupportCharacterInfo>
        {
            new SupportCharacterInfo(Yuni, "유니", "보너스 스테이지!", true, "액티브 · Q",
                "토끼 후드의 게이머. 운석 낙하 뒤 1UP 5초 동안 공격마다 추가 투사체.",
                "SupportSkill/support_yuni_portrait"),
            new SupportCharacterInfo(Kokkio, "꼬끼오", "헌터 야작 식당", false, "패시브",
                "정체불명의 닭꼬치 상인. 처치 시 33% 확률로 닭꼬치 드랍, 주우면 최대 체력 3% 회복.",
                "SupportSkill/support_kokkio_portrait"),
        };

        public static IReadOnlyList<SupportCharacterInfo> All => all;

        public static SupportCharacterInfo Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var support in all) if (support.id == id) return support;
            return null;
        }

        public static SupportCharacterInfo Selected => Find(HunterLoadout.SupportId);
    }
}
