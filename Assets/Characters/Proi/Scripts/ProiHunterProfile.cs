using AcRoguelike;
using AcRoguelike.Liminal;
using UnityEngine;

/// <summary>
/// 플레이 가능한 헌터 Proi (게이트 안에서 F 로 교체). 에셋은 Resources/Hunters 에 있어야 HunterRoster 가 찾습니다.
/// 맨손 3타 콤보: 1) 왼주먹 잽  2) 오른주먹  3) 뒤돌아 왼발 옆차기 (넓게 휘두르는 마무리).
/// 기본 스킬 (우클릭 / K): 가까운 적을 잡아 던지기, 없으면 화염공 (ProiHunterThrow).
/// 궁극기 (R): 화염 차징 → 아치 점프 → 땅 찍기 → 세 갈래 불기둥 (ProiHunterUltimate).
/// 전용 증강 6종은 ProiAugments.
/// 콤보 타이밍은 클립에서 계산 (ProiHunterKit), 데미지 / 사거리 / 히트스톱은 아래 swings 로 조절합니다.
/// </summary>
[CreateAssetMenu(menuName = "Pirate Alliance/Hunters/Proi", fileName = "Proi")]
public sealed class ProiHunterProfile : HunterProfile
{
    public ProiHunterProfile()
    {
        characterId = "proi";
        displayName = "프로이";
        order = 10;
        summary = "화염 배틀메이지 · 건틀릿 인파이터\n3타 콤보 · 우클릭 잡아 던지기 · R 땅 폭발";
        portraitResource = "Loadout/hunter_proi";
        headBone = "Head";
        attackDurations = ProiHunterKit.AttackDurations();
        hitTimes = ProiHunterKit.HitTimes();
        attackMovementMultiplier = .05f;
        comboQueueWindow = .3f;
        animationBlend = .08f;
        returnBlend = .2f;
        blade = false;
        swings = new[]
        {
            // 1: left jab
            new MeleeSlash.Swing { damage = 22, range = 2.3f, halfAngle = 65, impact = .85f, hitStop = .045f, shake = .03f, arcRadius = 1.5f },
            // 2: right straight
            new MeleeSlash.Swing { damage = 28, range = 2.4f, halfAngle = 65, impact = 1f, hitStop = .055f, shake = .045f, arcRadius = 1.6f },
            // 3: spinning side kick: wide, heavy, ends the combo
            new MeleeSlash.Swing { damage = 48, range = 2.7f, halfAngle = 140, impact = 1.35f, hitStop = .085f, shake = .08f, arcRadius = 2f, finisher = true },
        };
    }

    public override void Configure(GameObject visual, Animator animator)
    {
        visual.AddComponent<ProiFist>();          // 주먹 쥐기 (블렌드셰이프)
        visual.AddComponent<ProiBlink>();         // 눈 깜빡임
#if MAGICACLOTH2
        // Proi_Magica 프리팹은 머리카락 / 옷을 Magica Cloth 로 움직이므로 스프링이 겹치지 않게 합니다.
        if (visual.GetComponentInChildren<MagicaCloth2.MagicaCloth>(true) == null)
#endif
            visual.AddComponent<ProiTailSpring>();    // 트윈테일 흔들림
#if MAGICACLOTH2
        else
            visual.AddComponent<ProiHairFlutter>();   // 궁극기 동안 머리카락 / 옷 휘날림 + Proi 시계에 맞춘 물리 속도
#endif
        visual.AddComponent<ProiRunPosture>();    // 달리기 / 걷기 상체 굽힘 + 걸음 리듬
        visual.AddComponent<ProiFireFx>();        // 대쉬 화염
        visual.AddComponent<ProiHunterKit>();     // 콤보 이동량 / 재생 속도 / 대쉬
        visual.AddComponent<ProiBlazeCombo>();    // 주먹 / 발 화염 콤보
        // 궁극기 (R): 플레이그라운드 동작 / 균열 / 불기둥을 게임 배속을 건드리지 않는 방식으로, 시작 / 피해는 ProiHunterUltimate
        var ultimate = visual.AddComponent<ProiUltimate>();
        ultimate.externalInput = true;
        ultimate.cinematicTime = false;
        visual.AddComponent<ProiBlazeUltimate>();
        visual.AddComponent<ProiHunterUltimate>();
        // 기본 스킬 (우클릭): 잡아 던지기 / 화염공, 맹화돌진 증강 시 화염 돌진. 궁극기 뒤에 붙여 HUD 는 궁극기를 보여 줌.
        visual.AddComponent<ProiHunterThrow>();
        // 전용 증강 중 몸에 붙는 효과 (화염질주 / 불씨수호 / 반사의화염)
        visual.AddComponent<ProiAugmentKit>();
    }

    /// <summary>각 타의 판정 기준점: 1타 왼손, 2타 오른손, 3타 왼발.</summary>
    public override Transform[] AttackOrigins(GameObject visual, Animator animator) => new[]
    {
        ProiBlazeKit.FindDeep(visual.transform, "LeftHand"),
        ProiBlazeKit.FindDeep(visual.transform, "RightHand"),
        ProiBlazeKit.FindDeep(visual.transform, "LeftFoot"),
    };
}
