using AcRoguelike;
using AcRoguelike.Liminal;
using UnityEngine;

/// <summary>
/// 플레이 가능한 헌터 Proi (게이트 안에서 F 로 교체). 에셋은 Resources/Hunters 에 있어야 HunterRoster 가 찾습니다.
/// 맨손 3타 콤보: 1) 왼주먹 잽  2) 오른주먹  3) 뒤돌아 왼발 옆차기 (넓게 휘두르는 마무리).
/// 궁극기 (R): 화염 차징 → 아치 점프 → 땅 찍기 → 세 갈래 불기둥 (ProiHunterUltimate).
/// 일상복 (G): everydayModel = Proi_General (같은 리그) — 이동만 하고 서 있을 땐 손을 모은 자세, 달릴 땐 주먹 쥐고 소심하게.
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
        visual.AddComponent<ProiTailSpring>();    // 트윈테일 흔들림
        visual.AddComponent<ProiFireFx>();        // 대쉬 화염
        visual.AddComponent<ProiHunterKit>();     // 콤보 이동량 / 재생 속도 / 대쉬
        visual.AddComponent<ProiBlazeCombo>();    // 주먹 / 발 화염 콤보
        // 궁극기 (R): 플레이그라운드 동작 / 균열 / 불기둥을 게임 배속을 건드리지 않는 방식으로, 시작 / 피해는 ProiHunterUltimate
        var ultimate = visual.AddComponent<ProiUltimate>();
        ultimate.externalInput = true;
        ultimate.cinematicTime = false;
        visual.AddComponent<ProiBlazeUltimate>();
        visual.AddComponent<ProiHunterUltimate>();
    }

    public override void ConfigureEveryday(GameObject visual, Animator animator)
    {
        visual.AddComponent<ProiGeneralHands>();        // 손가락 구부림 / 달릴 때 주먹 (Fist 블렌드셰이프)
        visual.AddComponent<ProiGeneralHunterKit>();    // Run 재생 속도를 이동 속도에 맞춤
    }

    /// <summary>각 타의 판정 기준점: 1타 왼손, 2타 오른손, 3타 왼발.</summary>
    public override Transform[] AttackOrigins(GameObject visual, Animator animator) => new[]
    {
        ProiBlazeKit.FindDeep(visual.transform, "LeftHand"),
        ProiBlazeKit.FindDeep(visual.transform, "RightHand"),
        ProiBlazeKit.FindDeep(visual.transform, "LeftFoot"),
    };
}
