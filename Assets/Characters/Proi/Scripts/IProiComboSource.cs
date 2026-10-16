using UnityEngine;

/// <summary>
/// ProiBlazeCombo 가 따라가는 3타 콤보: 플레이그라운드에서는 ProiCombat, 게임(헌터 교체) 안에서는 ProiHunterKit.
/// </summary>
public interface IProiComboSource
{
    /// <summary>공격 동작 중이면 true.</summary>
    bool Busy { get; }
    /// <summary>재생 중인 타: 0 = 1타 잽, 1 = 2타, 2 = 3타 옆차기 (그 밖은 화염 없음).</summary>
    int CurrentStep { get; }
    /// <summary>재생 중인 클립의 진행도 (0~1).</summary>
    float Progress { get; }
    /// <summary>클립 재생 속도 배율.</summary>
    float AttackSpeed { get; }
    /// <summary>콤보를 재생하는 Proi 의 Animator.</summary>
    Animator ComboAnimator { get; }
    /// <summary>타격 순간 히트스톱 / 카메라 흔들림을 화염 쪽에서 처리할지 (게임에서는 MeleeSlash 가 처리).</summary>
    bool PlayHitFeel { get; }
}
