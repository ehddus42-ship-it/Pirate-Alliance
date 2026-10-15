using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// F 키로 전투 프로이 ↔ 일상 프로이를 전환합니다.
/// 일상 프로이는 이동(Idle ↔ Run)만 하고, 공격 · 기술 · 궁극기 · 대쉬는 전투 프로이일 때만 동작합니다.
/// 기술 사용 중(Busy)에는 전환하지 않습니다.
/// </summary>
[RequireComponent(typeof(ProiPlayerController))]
public class ProiCharacterSwitcher : MonoBehaviour
{
    static readonly int SpeedHash = Animator.StringToHash("Speed");

    [SerializeField] Animator battleModel;
    [SerializeField] Animator dailyModel;
    [SerializeField] bool startAsDaily = false;

    ProiPlayerController player;
    ProiCombat combat;
    ProiUltimate ultimate;
    ProiThrow skill;
    ProiDash dash;
    Behaviour[] battleOnly;
    bool daily;

    public bool IsDaily => daily;

    void Start()
    {
        // ProiPlayerController.Awake 가 전투 기술 컴포넌트를 붙이고 모델 높이를 맞춘 뒤에 실행됩니다.
        player = GetComponent<ProiPlayerController>();
        if (battleModel == null) battleModel = player.Animator;
        combat = GetComponent<ProiCombat>();
        ultimate = GetComponent<ProiUltimate>();
        skill = GetComponent<ProiThrow>();
        dash = GetComponent<ProiDash>();
        battleOnly = new Behaviour[]
        {
            combat, ultimate, skill, dash,
            GetComponent<ProiBlazeCombo>(), GetComponent<ProiBlazeUltimate>(), GetComponent<ProiBlazeThrow>()
        };

        if (dailyModel != null)
        {
            // 손가락 구부림 / 달릴 때 주먹 (Fist 블렌드셰이프)
            if (dailyModel.GetComponent<ProiGeneralHands>() == null) dailyModel.gameObject.AddComponent<ProiGeneralHands>();
            foreach (var smr in dailyModel.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;
        }
        if (dailyModel != null && battleModel != null)
        {
            // 같은 리그 · 같은 발 높이: 전투 프로이와 같은 자리에 둡니다.
            dailyModel.transform.localPosition = battleModel.transform.localPosition;
            dailyModel.transform.localRotation = battleModel.transform.localRotation;
        }
        Apply(startAsDaily);
    }

    bool Busy => (combat != null && combat.Busy) || (ultimate != null && ultimate.Busy) ||
                 (skill != null && skill.Busy) || (dash != null && dash.Busy);

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null && kb.fKey.wasPressedThisFrame && dailyModel != null && !Busy)
            Apply(!daily);
    }

    void Apply(bool toDaily)
    {
        if (dailyModel == null || battleModel == null) return;
        Animator from = daily ? dailyModel : battleModel;
        float speed = from.isActiveAndEnabled ? from.GetFloat(SpeedHash) : 0f;

        daily = toDaily;
        battleModel.gameObject.SetActive(!daily);
        dailyModel.gameObject.SetActive(daily);
        foreach (Behaviour b in battleOnly)
            if (b != null) b.enabled = !daily;

        Animator to = daily ? dailyModel : battleModel;
        player.Animator = to;
        to.SetFloat(SpeedHash, speed);
    }
}
