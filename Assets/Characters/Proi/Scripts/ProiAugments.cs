using AcRoguelike.Liminal;
using UnityEngine;

/// <summary>
/// 프로이 전용 증강 6종 (기획서 "증강 13"). 공통 증강과 같은 선택 풀에 섞이고, 프로이를 입고 있을 때만 나옵니다
/// (AugmentDefinition.characterId = "proi"). 효과는 Proi 모델의 컴포넌트가 HunterAugments.Has 로 확인합니다:
///  - 화염질주 / 불씨수호 / 반사의화염: ProiAugmentKit
///  - 업화해방: ProiHunterUltimate
///  - 맹화돌진 / 폭발확장: ProiHunterThrow (기본 스킬)
/// 맹화돌진 + 폭발확장 동시 보유 규칙은 기획 미정이라, 폭발확장이 돌진 끝 폭발에도 적용되도록 두었습니다.
/// </summary>
public static class ProiAugments
{
    public const string CharacterId = "proi";

    public const string BlazeDash = "proi_blaze_dash";        // 화염질주
    public const string EmberGuard = "proi_ember_guard";      // 불씨수호
    public const string Hellfire = "proi_hellfire";           // 업화해방
    public const string ReflectFlame = "proi_reflect_flame";  // 반사의화염
    public const string BlazeRush = "proi_blaze_rush";        // 맹화돌진
    public const string BlastExpand = "proi_blast_expand";    // 폭발확장

    // Tuning (the card texts read these).
    public const int BlazeDashDamage = 26;
    public const float BlazeDashWidth = 1.2f;
    public const float EmberGuardRecharge = 12f;
    public const float HellfireRadius = 1.5f;
    public const float HellfireDamage = 1.4f;
    public const float ReflectRecharge = 16f;
    public const float ReflectMultiplier = 2f;
    public const float ReflectRadius = 3.5f;
    public const float BlastRadius = 1.6f;

    static readonly AugmentDefinition[] Definitions =
    {
        new AugmentDefinition(BlazeDash, "화염질주",
            $"대시에 화염이 붙는다.\n대시 경로의 적에게 피해 {BlazeDashDamage} (대시마다 적 하나에 1번).", characterId: CharacterId),
        new AugmentDefinition(EmberGuard, "불씨수호",
            $"몸을 도는 불씨가 적의 공격 1회를 막는다.\n막은 뒤 {EmberGuardRecharge:0}초 후 다시 생긴다.", characterId: CharacterId),
        new AugmentDefinition(Hellfire, "업화해방",
            $"궁극기 범위 +{(HellfireRadius - 1) * 100:0}%.\n궁극기 피해 +{(HellfireDamage - 1) * 100:0}%.", characterId: CharacterId),
        new AugmentDefinition(ReflectFlame, "반사의화염",
            $"화염 보호막이 다음 피격을 막고, 그 피해의 {ReflectMultiplier:0}배를 주변 적에게 되돌린다.\n보호막은 {ReflectRecharge:0}초마다 다시 생긴다.", characterId: CharacterId),
        new AugmentDefinition(BlazeRush, "맹화돌진",
            "기본 스킬이 바뀐다: 잡아 던지기 대신 전방으로 화염 돌진.\n경로의 적에게 피해를 주고 끝에서 폭발한다.", characterId: CharacterId),
        new AugmentDefinition(BlastExpand, "폭발확장",
            $"기본 스킬의 폭발 범위 +{(BlastRadius - 1) * 100:0}%.\n(투척 착지, 화염구, 맹화돌진의 끝 폭발)", characterId: CharacterId),
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Register()
    {
        foreach (var augment in Definitions)
            if (AugmentCatalog.Find(augment.id) == null) AugmentCatalog.Register(augment);
    }

    /// <summary>True when the hunter wearing this model holds the augment in the current run.</summary>
    public static bool Has(HunterAugments augments, string id) => augments && augments.Has(id);
}
