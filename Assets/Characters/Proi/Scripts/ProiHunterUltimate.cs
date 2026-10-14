using AcRoguelike;
using AcRoguelike.Liminal;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 게임(헌터 교체) 안 Proi 의 궁극기 (R, 게임패드 X). 동작 / 아치 점프 / 용암 균열 / 불기둥은 ProiUltimate, 화염은 ProiBlazeUltimate 가
/// 그대로 하고, 이 컴포넌트는 그것을 게임 규칙에 맞춥니다:
///  - 게이트 탐색 중, 대쉬 / 잡힘 / 던져짐이 아닐 때만 시작. 공격 중이면 공격을 끊고 조준 방향으로 돌아서 씀.
///  - 쓰는 동안 이동 / 대쉬 / 평타 입력을 막고 무적 (끝 자세부터 다시 움직임), 헌터 교체도 막힘 (IHunterSkill.Busy).
///  - 피해: 땅 찍기 둘레 + 솟는 불기둥마다 (로비 영구 강화의 공격 배율 적용).
///  - 타격감은 게임 HitFeedback (히트스톱 / 슬로모션 / 흔들림 / 섬광). 게임 배속은 일시정지와 같이 게임이 관리합니다.
///  - 전용 증강 업화해방: 판정 범위 ×1.5, 피해 ×1.4 (ProiAugments).
/// Proi 모델에 붙어 있어 Proi 를 입고 있을 때만 동작합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ProiHunterUltimate : MonoBehaviour, IHunterSkill
{
    [Tooltip("다시 쓸 수 있을 때까지 (초, 시작부터)")]
    public float cooldown = 14f;
    [Tooltip("시작부터 무적 시간 (초): 땅 찍기와 불기둥이 끝날 때까지")]
    public float invulnerability = 5.2f;

    [Header("피해")]
    public int slamDamage = 70;
    [Tooltip("땅 찍기 판정 반지름 (m)")]
    public float slamRadius = 3f;
    [Tooltip("불기둥 하나의 피해")]
    public int pillarDamage = 30;
    [Tooltip("불기둥 반지름에 더하는 판정 여유 (m)")]
    public float pillarReach = .6f;

    static readonly Color Ember = new Color(1f, .45f, .12f);

    ProiUltimate ult;
    PlayerMotor motor;
    PlayerCombat combat;
    MeleeSlash melee;
    LiminalPlayerHealth health;
    LiminalRunDirector director;
    HunterAugments augments;
    InputAction action;
    float readyAt;
    bool holding;

    public string Label => "ULT";
    /// <summary>궁극기 연출이 끝날 때까지 (불기둥까지) true.</summary>
    public bool Busy => ult != null && ult.Active;
    public float CooldownRemaining => Mathf.Max(0f, readyAt - Time.time);
    public int UseCount { get; private set; }
    /// <summary>궁극기로 적에게 들어간 타격 수 (땅 찍기 + 불기둥).</summary>
    public int HitCount { get; private set; }
    public InputAction Action => action;

    public bool CanActivate =>
        ult != null && isActiveAndEnabled && !ult.Active && Time.time >= readyAt && Time.timeScale > 0f &&
        (!director || director.Phase == LiminalRunPhase.Exploring) &&
        motor && motor.isActiveAndEnabled && !motor.IsDashing && !motor.IsHeld && !motor.IsLaunched &&
        (!health || health.IsAlive);

    void Awake()
    {
        ult = GetComponent<ProiUltimate>();
        motor = GetComponentInParent<PlayerMotor>();
        combat = GetComponentInParent<PlayerCombat>();
        melee = GetComponentInParent<MeleeSlash>();
        health = GetComponentInParent<LiminalPlayerHealth>();
        augments = GetComponentInParent<HunterAugments>();
        director = FindFirstObjectByType<LiminalRunDirector>();
        action = new InputAction("ProiUltimate", InputActionType.Button);
        action.AddBinding("<Keyboard>/r");
        action.AddBinding("<Gamepad>/buttonWest");
        if (ult == null) return;
        ult.OnClash += Clash;
        ult.OnChargePulse += ChargePulse;
        ult.OnCharged += Charged;
        ult.OnLeap += Leap;
        ult.OnSlam += Slam;
        ult.OnJet += Jet;
    }

    void OnEnable() => action?.Enable();

    void OnDisable()
    {
        action?.Disable();
        Release();
    }

    void OnDestroy()
    {
        action?.Dispose();
        if (ult == null) return;
        ult.OnClash -= Clash;
        ult.OnChargePulse -= ChargePulse;
        ult.OnCharged -= Charged;
        ult.OnLeap -= Leap;
        ult.OnSlam -= Slam;
        ult.OnJet -= Jet;
    }

    void Update()
    {
        // From her closing pose on she moves again (ProiUltimate.Busy ends before the last pillars fade).
        if (holding && (ult == null || !ult.Busy)) Release();
        if (Application.isFocused && action.WasPressedThisFrame()) TryActivate();
    }

    /// <summary>R: 조건이 맞으면 조준 방향으로 돌아서 궁극기를 시작합니다.</summary>
    public bool TryActivate()
    {
        if (!CanActivate) return false;
        Vector3 aim = Vector3.ProjectOnPlane(motor.AimPoint - motor.transform.position, Vector3.up);
        if (aim.sqrMagnitude > .25f) transform.rotation = Quaternion.LookRotation(aim.normalized, Vector3.up);
        if (combat) combat.CancelAttack(false);
        if (!ult.TryBegin()) return false;
        // The motor and combat keep running (gravity, the HUD, augments) but take no player input while she performs.
        motor.SetAutomationInput(Vector2.zero, motor.AimPoint);
        if (combat) combat.SetAutomationInput(false, false);
        holding = true;
        if (health) health.GrantInvulnerability(invulnerability);
        readyAt = Time.time + cooldown;
        UseCount++;
        return true;
    }

    void Release()
    {
        if (!holding) return;
        holding = false;
        if (motor) motor.ReleaseAutomation();
        if (combat) combat.ReleaseAutomation();
    }

    // ---- beats from ProiUltimate: the game's hit feel and the damage

    void Clash(Vector3 at)
    {
        HitFeedback.HitStop(.06f, .05f);
        HitFeedback.Shake(.045f, .25f);
        HitFeedback.Play(HitFeedback.Sfx.HeavyHit, .6f, 1.2f);
    }

    void ChargePulse(Vector3 fist, float level) => HitFeedback.Shake(Mathf.Lerp(.008f, .03f, level), .12f);

    void Charged(Vector3 fist) => HitFeedback.Shake(.06f, .3f);

    void Leap(Vector3 at)
    {
        HitFeedback.Shake(.06f, .25f);
        HitFeedback.Dust(at, 1.3f);
    }

    void Slam(Vector3 at)
    {
        HitFeedback.HitStop(.1f, .04f);
        HitFeedback.Shake(.17f, .45f);
        HitFeedback.ScreenFlash(new Color(Ember.r, Ember.g, Ember.b, .3f), .3f);
        HitFeedback.Play(HitFeedback.Sfx.HeavyHit, 1f, .8f);
        float reach = slamRadius * RadiusScale;
        if (RadiusScale > 1f) HitFeedback.Ring(at + Vector3.up * .1f, Vector3.up, reach);
        Damage(at, reach, slamDamage, 1.6f);
    }

    /// <summary>업화해방: 궁극기 범위 / 피해 배율.</summary>
    bool Empowered
    {
        get
        {
            if (!augments) augments = GetComponentInParent<HunterAugments>();
            return ProiAugments.Has(augments, ProiAugments.Hellfire);
        }
    }

    float RadiusScale => Empowered ? ProiAugments.HellfireRadius : 1f;

    void Jet(Vector3 foot, float height, float radius)
    {
        HitFeedback.Shake(.05f, .18f);
        Damage(foot, (radius + pillarReach) * RadiusScale, pillarDamage, 1.25f);
    }

    void Damage(Vector3 center, float radius, int amount, float impact)
    {
        float multiplier = (melee ? Mathf.Max(.1f, melee.damageMultiplier) : 1f) * (Empowered ? ProiAugments.HellfireDamage : 1f);
        foreach (var enemy in FindObjectsByType<TrainingEnemy>(FindObjectsSortMode.None))
        {
            if (!enemy.IsAlive || !enemy.CanBeTargeted) continue;
            Vector3 delta = enemy.transform.position - center;
            if (Mathf.Abs(delta.y) > 3f) continue;
            Vector3 offset = Vector3.ProjectOnPlane(delta, Vector3.up);
            var controller = enemy.GetComponent<CharacterController>();
            if (offset.magnitude > radius + (controller ? controller.radius : .45f)) continue;
            Vector3 push = offset.sqrMagnitude > .01f ? offset.normalized : Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            enemy.LastHitDirection = push;
            enemy.LastHitImpact = impact;
            int damage = Mathf.Max(1, Mathf.RoundToInt(amount * multiplier * Random.Range(.95f, 1.05f)));
            enemy.TakeDamage(damage);
            HitFeedback.Hit(enemy, enemy.AimPoint - push * .25f, push, damage, true);
            HitCount++;
        }
    }
}
