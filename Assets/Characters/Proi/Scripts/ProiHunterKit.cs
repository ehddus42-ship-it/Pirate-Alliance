using AcRoguelike;
using UnityEngine;

/// <summary>
/// 게임(헌터 교체, F) 안의 Proi. 플레이그라운드의 ProiPlayerController / ProiCombat / ProiDash 대신 게임 시스템이 몸을 움직입니다:
/// 이동 / 대쉬는 PlayerMotor, 3타 콤보 입력과 타이밍은 PlayerCombat, 적 판정 / 데미지 / 히트스톱은 MeleeSlash.
/// 이 컴포넌트는 그 둘레에서 플레이그라운드가 하던 일을 이어서 합니다:
///  - 제자리 콤보 클립의 이동량(ProiAttackData)을 MeleeSlash 로 넘겨 디딘 발이 미끄러지지 않게 (적 앞에서 멈춤은 MeleeSlash 가 처리)
///  - 클립 재생 속도: 콤보 속도(공격 속도 강화 포함), 대쉬, 달리기 속도에 맞춘 Run
///  - 화염 콤보(ProiBlazeCombo)의 콤보 소스 (히트스톱 / 카메라 흔들림은 MeleeSlash 쪽)
///  - 대쉬마다 등 뒤 화염 분출 + 잔상, 대쉬가 끝나면 Dash 자세에서 이동 동작으로 복귀
/// 궁극기(R, ProiHunterUltimate) 중에는 ProiUltimate 가 몸과 재생 속도를 맡으므로 손을 뗍니다.
/// Proi 모델에 붙어 있어 헌터를 바꾸면 같이 켜지고 꺼집니다.
/// </summary>
[DefaultExecutionOrder(10)]   // PlayerMotor(-20) / PlayerCombat(0) 다음: 이번 프레임에 시작한 공격 / 대쉬를 바로 봅니다.
[DisallowMultipleComponent]
public sealed class ProiHunterKit : MonoBehaviour, IProiComboSource
{
    /// <summary>플레이그라운드 ProiCombat.attackSpeed: 콤보가 맞춰진 재생 속도.</summary>
    public const float ClipSpeed = 1.1f;
    /// <summary>Run 클립 재생 배율이 맞춰진 이동 속도 (ProiSetup.MoveSpeed, m/s).</summary>
    public const float RunTunedSpeed = 3.2f;
    const float DashClipSpeed = 1.15f;
    const float DashBlendOut = 0.15f;

    // 콤보 클립 (30 fps): 프레임 수, 타격 순간 / 다음 타로 이어지는 순간 (클립 진행도). ProiBlazeCombo.impactAt / ProiCombat 과 같은 값.
    static readonly int[] ClipFrames = { 18, 24, 28 };
    static readonly float[] ImpactAt = { 0.47f, 0.478f, 0.42f };
    static readonly float[] ChainAt = { 0.62f, 0.72f, 1f };
    static readonly int Locomotion = Animator.StringToHash("Base Layer.Locomotion");
    static readonly int DashState = Animator.StringToHash("Base Layer.Dash");

    static float ClipSeconds(int step) => (ClipFrames[step] - 1) / 30f;

    /// <summary>PlayerCombat 의 타 길이 (공격 속도 1 기준 초): 플레이그라운드가 다음 타로 이어가던 순간에서 끝납니다.</summary>
    public static float[] AttackDurations()
    {
        var d = new float[ClipFrames.Length];
        for (int i = 0; i < d.Length; i++) d[i] = ChainAt[i] * ClipSeconds(i) / ClipSpeed;
        return d;
    }

    /// <summary>PlayerCombat 의 타격 시각: 화염 이펙트의 타격 순간과 같습니다.</summary>
    public static float[] HitTimes()
    {
        var h = new float[ClipFrames.Length];
        for (int i = 0; i < h.Length; i++) h[i] = ImpactAt[i] * ClipSeconds(i) / ClipSpeed;
        return h;
    }

    PlayerMotor motor;
    PlayerCombat combat;
    MeleeSlash melee;
    Animator animator;
    ProiFireFx fx;
    ProiUltimate ultimate;
    SkinnedMeshRenderer[] skins;
    int step = -1, lastAttackCount, lastDashCount;
    float clipTime;
    Vector2 lastTravel;
    Vector3 forward, right;
    bool wasDashing;

    public bool Busy => step >= 0;
    public int CurrentStep => step;
    public float Progress { get; private set; }
    public float AttackSpeed => ClipSpeed * (combat ? combat.AnimationSpeed : 1f);
    public Animator ComboAnimator => animator;
    public bool PlayHitFeel => false;

    void Awake()
    {
        motor = GetComponentInParent<PlayerMotor>();
        combat = GetComponentInParent<PlayerCombat>();
        melee = GetComponentInParent<MeleeSlash>();
        animator = GetComponent<Animator>();
        fx = GetComponent<ProiFireFx>();
        if (fx == null) fx = gameObject.AddComponent<ProiFireFx>();
        skins = GetComponentsInChildren<SkinnedMeshRenderer>(true);
    }

    void OnEnable()
    {
        step = -1;
        Progress = 0f;
        lastAttackCount = combat ? combat.AttackCount : 0;
        lastDashCount = motor ? motor.DashCount : 0;
        wasDashing = false;
    }

    void OnDisable()
    {
        step = -1;
        if (animator) animator.speed = 1f;
    }

    void Update()
    {
        if (!motor || !combat || !animator) return;
        if (ultimate == null) ultimate = GetComponent<ProiUltimate>();
        // The ultimate poses her and runs the clip at its own warp; nothing to do until it hands back.
        if (ultimate != null && ultimate.Active)
        {
            step = -1;
            wasDashing = false;
            lastDashCount = motor.DashCount;
            return;
        }
        UpdateCombo(Time.deltaTime);

        bool dashing = motor.IsDashing;
        if (motor.DashCount != lastDashCount)
        {
            lastDashCount = motor.DashCount;
            DashBurst();
        }
        // The Dash state has no exit transition: hand back to locomotion once the motor's dash is over.
        if (wasDashing && !dashing && !combat.IsAttacking && InDashPose()) animator.CrossFadeInFixedTime(Locomotion, DashBlendOut, 0);
        wasDashing = dashing;

        if (step >= 0) animator.speed = AttackSpeed;
        else if (dashing) animator.speed = DashClipSpeed;
        else
        {
            // Run is tuned for 3.2 m/s; the hunter runs at PlayerMotor's speed (augments included), so the feet keep up.
            float run = Mathf.Clamp01(motor.PlanarVelocity.magnitude / Mathf.Max(.01f, motor.moveSpeed));
            animator.speed = Mathf.Lerp(1f, motor.moveSpeed / RunTunedSpeed, run);
        }
    }

    void UpdateCombo(float dt)
    {
        if (!combat.IsAttacking || combat.AttackIndex < 0 || combat.AttackIndex >= ClipFrames.Length)
        {
            step = -1;
            Progress = 0f;
            lastAttackCount = combat.AttackCount;
            return;
        }
        if (combat.AttackCount != lastAttackCount || combat.AttackIndex != step)
        {   // a new swing: the clip starts this frame
            lastAttackCount = combat.AttackCount;
            step = combat.AttackIndex;
            clipTime = 0f;
            lastTravel = Vector2.zero;
            Vector3 aim = Vector3.ProjectOnPlane(combat.AttackDirection, Vector3.up);
            forward = aim.sqrMagnitude > .01f ? aim.normalized : Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            right = Vector3.Cross(Vector3.up, forward);
        }
        else clipTime += dt * AttackSpeed;

        float u = Mathf.Clamp01(clipTime / ClipSeconds(step));
        Progress = u;
        // root travel of the in-place clip (planted foot stays put); MeleeSlash steers it and stops short of the target
        Vector2 travel = SampleTravel(step, u);
        Vector2 d = travel - lastTravel;
        lastTravel = travel;
        if (melee && d.sqrMagnitude > 1e-10f) melee.ApplyRootMotion(forward * d.x + right * d.y);
    }

    bool InDashPose()
    {
        if (!animator.isActiveAndEnabled) return false;
        if (animator.GetCurrentAnimatorStateInfo(0).fullPathHash == DashState) return true;
        return animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).fullPathHash == DashState;
    }

    /// <summary>The kick-off (ProiDash): a flare at her back, embers and flames sprayed behind, a burning afterimage.</summary>
    void DashBurst()
    {
        Vector3 dir = Vector3.ProjectOnPlane(motor.PlanarVelocity, Vector3.up);
        dir = dir.sqrMagnitude > .01f ? dir.normalized : Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        HitFeedback.Afterimage(skins, new Color(1f, .42f, .12f) * .6f, .3f);
        if (fx == null || !fx.Ready) return;
        Vector3 back = transform.position + Vector3.up * 0.9f - dir * 0.25f;
        fx.Flash(back, 0.9f);
        fx.Embers(back, 18, 1.5f, 4f, -dir * 3f + Vector3.up * 0.5f);
        for (int i = 0; i < 6; i++)
            fx.Flame(transform.position + Random.insideUnitSphere * 0.25f + Vector3.up * 0.15f,
                     -dir * Random.Range(1.5f, 3f) + Vector3.up * Random.Range(0.8f, 1.6f), fx.FlameSize * Random.Range(0.8f, 1.2f), Random.Range(0.25f, 0.35f));
    }

    static Vector2 SampleTravel(int step, float u)
    {
        float[] f, r;
        switch (step)
        {
            case 0: f = ProiAttackData.Attack1Forward; r = ProiAttackData.Attack1Right; break;
            case 1: f = ProiAttackData.Attack2Forward; r = ProiAttackData.Attack2Right; break;
            default: f = ProiAttackData.Attack3Forward; r = ProiAttackData.Attack3Right; break;
        }
        float x = u * (f.Length - 1);
        int i = Mathf.Min((int)x, f.Length - 2);
        float t = x - i;
        return new Vector2(Mathf.Lerp(f[i], f[i + 1], t), Mathf.Lerp(r[i], r[i + 1], t));
    }
}
