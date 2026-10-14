using System.Collections.Generic;
using AcRoguelike;
using AcRoguelike.Liminal;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 게임(헌터 교체) 안 Proi 의 기본 스킬 (마우스 우클릭 / K / 게임패드 오른쪽 스틱 누르기). 기획서 "프로이 기획서 11":
///  - 가까이(grabRange)에 일반 적이 있으면: 그쪽으로 돌아 다가가 오른손으로 잡고, 뒤로 들어 올렸다가 앞으로 던짐.
///    던진 적은 포물선으로 날아가 땅 / 벽 / 다른 적에 닿는 순간 폭발 (던진 적 + 주변 적 피해). 보스는 잡을 수 없음.
///  - 잡을 적이 없으면: 같은 동작으로 뻗은 손에 화염공이 자라나 던져짐. 적 / 땅 / 벽에 닿거나 사거리 끝에서 폭발.
///  - 연출: 시전 시 카메라 5% 줌인, 던질 때 줌아웃. 모든 던지기에 불꽃 (ProiFireFx).
/// 전용 증강 (ProiAugments):
///  - 맹화돌진: 던지기 대신 전방으로 화염 돌진. 경로의 적에게 피해, 끝에서 폭발.
///  - 폭발확장: 착지 폭발 / 화염구 폭발 / 돌진 끝 폭발의 범위 ×1.6.
/// 동작은 Proi.controller 의 Throw 상태 (Throw.anim), 돌진은 Dash 상태. 쓰는 동안 이동 / 평타 입력을 막습니다.
/// Proi 모델에 붙어 있어 Proi 를 입고 있을 때만 동작하고, 헌터를 바꾸면 날아가던 것도 그 자리에서 마무리됩니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ProiHunterThrow : MonoBehaviour, IHunterSkill
{
    [Tooltip("다시 쓸 수 있을 때까지 (초, 시작부터)")]
    public float cooldown = 3.5f;

    [Header("타이밍 (초, Throw.anim 과 맞춤)")]
    public float lungeEnd = .26f;
    public float grabTime = .3f;
    public float releaseTime = .78f;
    public float endTime = 1.12f;
    public float fireballForm = .2f;
    [Tooltip("던지며 앞으로 내딛는 거리 (m) / 구간 (초). Throw.anim 이 이만큼 뒤로 당겨져 구워져 있음")]
    public float strideDistance = .3f;
    public Vector2 strideTime = new Vector2(.7f, .95f);

    [Header("잡아 던지기")]
    public float grabRange = 2.8f;
    [Tooltip("잡는 순간 적까지 거리 (m)")]
    public float grabReach = .75f;
    [Tooltip("던지는 속도 (m/s): 앞 / 위")]
    public Vector2 throwSpeed = new Vector2(9f, 4.5f);
    public float throwSpin = 540f;
    [Tooltip("던져진 적이 받는 피해")]
    public int thrownDamage = 55;
    [Tooltip("착지 폭발이 주변 적에게 주는 피해 / 반지름 (m)")]
    public int blastDamage = 40;
    public float blastRadius = 2.4f;

    [Header("화염공")]
    public float fireballSpeed = 12f;
    public float fireballGravity = 6f;
    public float fireballRange = 10f;
    public int fireballDamage = 45;
    public float fireballRadius = 2f;

    [Header("맹화돌진 (전용 증강)")]
    public float rushDistance = 6.5f;
    public float rushTime = .34f;
    public float rushRecover = .22f;
    public int rushDamage = 40;
    public float rushWidth = 1.3f;
    public int rushBurstDamage = 35;
    public float rushBurstRadius = 2.2f;

    static readonly int ThrowState = Animator.StringToHash("Base Layer.Throw");
    static readonly int DashState = Animator.StringToHash("Base Layer.Dash");
    static readonly int Locomotion = Animator.StringToHash("Base Layer.Locomotion");
    static readonly Color Ember = new Color(1f, .45f, .12f);

    enum Mode { None, Grab, Fireball, Rush }

    sealed class Flight
    {
        public TrainingEnemy enemy;
        public CharacterController body;
        public bool wasTargetable;
        public float yaw;
        public Transform ball;
        public Light light;
        public Vector3 pos, vel, start, spinAxis;
        public float age, spin;
    }

    Animator animator;
    PlayerMotor motor;
    PlayerCombat combat;
    MeleeSlash melee;
    LiminalPlayerHealth health;
    HunterAugments augments;
    ProiFireFx fx;
    ProiUltimate ultimate;
    CharacterController controller;
    LiminalRunDirector director;
    InputAction action;
    Transform rHand, rForearm;

    Mode mode;
    float time, readyAt, lungeDistance, lungeDone, strideDone, flameClock;
    bool grabbed, released, holding;
    Vector3 forward;
    TrainingEnemy target;
    CharacterController targetBody;
    bool targetWasTargetable;
    float targetYaw;
    Transform ball;
    Light ballLight;
    readonly List<Flight> flights = new List<Flight>();
    readonly HashSet<TrainingEnemy> rushHits = new HashSet<TrainingEnemy>();

    public string Label => "SKILL";
    public bool Busy => mode != Mode.None;
    public float CooldownRemaining => Mathf.Max(0f, readyAt - Time.time);
    public int UseCount { get; private set; }
    public int ThrowCount { get; private set; }
    public int FireballCount { get; private set; }
    public int RushCount { get; private set; }
    public int HitCount { get; private set; }
    public InputAction Action => action;

    public bool CanActivate =>
        animator && mode == Mode.None && isActiveAndEnabled && Time.time >= readyAt && Time.timeScale > 0f &&
        (!director || director.Phase == LiminalRunPhase.Exploring) && (ultimate == null || !ultimate.Active) &&
        motor && motor.isActiveAndEnabled && !motor.IsDashing && !motor.IsHeld && !motor.IsLaunched &&
        (!health || health.IsAlive);

    void Awake()
    {
        animator = GetComponent<Animator>();
        motor = GetComponentInParent<PlayerMotor>();
        combat = GetComponentInParent<PlayerCombat>();
        melee = GetComponentInParent<MeleeSlash>();
        health = GetComponentInParent<LiminalPlayerHealth>();
        augments = GetComponentInParent<HunterAugments>();
        controller = GetComponentInParent<CharacterController>();
        fx = GetComponent<ProiFireFx>();
        ultimate = GetComponent<ProiUltimate>();
        director = FindFirstObjectByType<LiminalRunDirector>();
        rHand = ProiBlazeKit.FindDeep(transform, "RightHand");
        rForearm = ProiBlazeKit.FindDeep(transform, "RightLowerArm");
        action = new InputAction("ProiSkill", InputActionType.Button);
        action.AddBinding("<Mouse>/rightButton");
        action.AddBinding("<Keyboard>/k");
        action.AddBinding("<Gamepad>/rightStickPress");
    }

    void OnEnable() => action?.Enable();

    static bool quitting;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ListenForQuit()
    {
        quitting = false;
        Application.quitting -= OnQuit;
        Application.quitting += OnQuit;
    }

    static void OnQuit() => quitting = true;

    void OnDisable()
    {
        action?.Disable();
        if (quitting || !gameObject.scene.isLoaded) return;
        // Swapped out (or the lobby): nothing may stay in her hand or in the air.
        DropHeld();
        for (int i = flights.Count - 1; i >= 0; i--) Resolve(flights[i]);
        flights.Clear();
        if (ball) Destroy(ball.gameObject);
        ball = null;
        mode = Mode.None;
        Release();
    }

    void OnDestroy() => action?.Dispose();

    bool Has(string id)
    {
        if (!augments) augments = GetComponentInParent<HunterAugments>();
        return ProiAugments.Has(augments, id);
    }

    float BlastScale => Has(ProiAugments.BlastExpand) ? ProiAugments.BlastRadius : 1f;
    float DamageScale => melee ? Mathf.Max(.1f, melee.damageMultiplier) : 1f;

    void Update()
    {
        if (Application.isFocused && action.WasPressedThisFrame()) TryActivate();
        if (mode == Mode.Rush) TickRush(Time.deltaTime);
        else if (mode != Mode.None) Tick(Time.deltaTime);
    }

    void LateUpdate()
    {
        // After the animator and the enemies' own movement: what is held follows the posed hand.
        if (mode == Mode.Grab && grabbed && !released)
        {
            if (!target || !target.IsAlive) DropHeld();
            else Carry();
        }
        if (ball && mode == Mode.Fireball && !released)
        {
            ball.position = HandPoint() + Vector3.up * .08f;
            float grow = Mathf.Clamp01((time - fireballForm) / Mathf.Max(.01f, releaseTime - fireballForm));
            ball.localScale = Vector3.one * Mathf.Lerp(.12f, .5f, grow);
            if (ballLight) ballLight.intensity = Mathf.Lerp(.5f, 3f, grow);
        }
        float dt = Time.deltaTime;
        for (int i = flights.Count - 1; i >= 0; i--)
            if (UpdateFlight(flights[i], dt)) flights.RemoveAt(i);
    }

    /// <summary>우클릭: 조건이 맞으면 기본 스킬을 시작합니다 (맹화돌진을 가졌으면 돌진).</summary>
    public bool TryActivate()
    {
        if (!CanActivate) return false;
        Vector3 aim = Vector3.ProjectOnPlane(motor.AimPoint - motor.transform.position, Vector3.up);
        forward = aim.sqrMagnitude > .25f ? aim.normalized : Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (combat) combat.CancelAttack(false);
        time = lungeDone = strideDone = 0f;
        grabbed = released = false;
        readyAt = Time.time + cooldown;
        UseCount++;

        if (Has(ProiAugments.BlazeRush))
        {
            mode = Mode.Rush;
            RushCount++;
            rushHits.Clear();
            Face(forward);
            animator.CrossFadeInFixedTime(DashState, .05f, 0, 0f);
            if (health) health.GrantInvulnerability(rushTime + .1f);
            HitFeedback.Zoom(.05f, .06f, rushTime, .3f);
            HitFeedback.Play(HitFeedback.Sfx.Swing, .8f, .8f);
            if (fx) { fx.Flash(motor.transform.position + Vector3.up * .9f - forward * .3f, 1f); fx.Embers(motor.transform.position + Vector3.up * .9f, 16, 1.5f, 4f, -forward * 3f); }
        }
        else
        {
            target = FindGrabTarget();
            lungeDistance = 0f;
            if (target)
            {
                Vector3 to = Vector3.ProjectOnPlane(target.transform.position - motor.transform.position, Vector3.up);
                if (to.sqrMagnitude > 1e-4f) forward = to.normalized;
                lungeDistance = Mathf.Max(0f, to.magnitude - grabReach);
                mode = Mode.Grab;
            }
            else mode = Mode.Fireball;
            Face(forward);
            animator.speed = 1f;
            animator.CrossFadeInFixedTime(ThrowState, .08f, 0, 0f);
            // 기술 시전 시 카메라 5% 줌인 (던질 때 줌아웃).
            HitFeedback.Zoom(.05f, .15f, releaseTime, .2f);
        }
        motor.SetAutomationInput(Vector2.zero, motor.transform.position + forward * 3f);
        if (combat) combat.SetAutomationInput(false, false);
        holding = true;
        return true;
    }

    void Face(Vector3 direction)
    {
        if (direction.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
    }

    TrainingEnemy FindGrabTarget()
    {
        TrainingEnemy best = null;
        float bestDistance = grabRange;
        Vector3 origin = motor.transform.position;
        foreach (var enemy in FindObjectsByType<TrainingEnemy>(FindObjectsSortMode.None))
        {
            if (!enemy.IsAlive || !enemy.CanBeTargeted || HunterAugments.IsBoss(enemy)) continue;
            Vector3 offset = enemy.transform.position - origin;
            if (Mathf.Abs(offset.y) > 1.5f) continue;
            float distance = Vector3.ProjectOnPlane(offset, Vector3.up).magnitude;
            if (distance < bestDistance) { bestDistance = distance; best = enemy; }
        }
        return best;
    }

    // ---- grab / fireball timeline -------------------------------------------------------------------------
    void Tick(float dt)
    {
        time += dt;
        // Close in on the enemy while the arm shoots out, then the pitcher's stride onto the front foot.
        float k = Mathf.Clamp01(time / Mathf.Max(lungeEnd, .01f));
        float d = lungeDistance * (1f - (1f - k) * (1f - k));
        if (d > lungeDone) Move(forward * (d - lungeDone));
        lungeDone = d;
        float s = Mathf.Clamp01((time - strideTime.x) / Mathf.Max(strideTime.y - strideTime.x, .01f));
        s = strideDistance * s * s * (3f - 2f * s);
        if (s > strideDone) Move(forward * (s - strideDone));
        strideDone = s;

        if (mode == Mode.Grab && !grabbed && time >= grabTime)
        {
            // The enemy died or slipped away before the hand closed: a fireball instead.
            if (target && target.IsAlive && target.CanBeTargeted) Grab();
            else { target = null; mode = Mode.Fireball; }
        }
        if (mode == Mode.Fireball && !ball && !released && time >= fireballForm) FormFireball();
        if (ball && !released) FeedFlames(ball.position, .6f);
        if (!released && time >= releaseTime) Throw();
        if (time >= endTime) Finish();
    }

    void Grab()
    {
        grabbed = true;
        targetBody = target.GetComponent<CharacterController>();
        targetWasTargetable = target.CanBeTargeted;
        target.CanBeTargeted = false;
        targetYaw = target.transform.eulerAngles.y;
        HitFeedback.HitStop(.04f, .05f);
        HitFeedback.Shake(.02f, .1f);
        HitFeedback.Play(HitFeedback.Sfx.Hit, .6f, .9f);
        if (fx) { fx.Flash(HandPoint(), .7f); fx.Embers(HandPoint(), 10, 1f, 3f, Vector3.up); }
    }

    /// <summary>The enemy hangs from her fist, its chest at the hand, leaning away from her body.</summary>
    void Carry()
    {
        Vector3 hand = HandPoint();
        Vector3 away = Vector3.ProjectOnPlane(hand - motor.transform.position, Vector3.up);
        away = away.sqrMagnitude > .01f ? away.normalized : forward;
        Vector3 chest = target.AimPoint - target.transform.position;
        target.transform.position = hand + away * .25f - chest;
        target.transform.rotation = Quaternion.Euler(0, targetYaw, 0);
        if (fx && Random.value < .3f) fx.Flame(hand, Vector3.up, fx.FlameSize * .7f, .2f);
    }

    void FormFireball()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());
        go.name = "Proi Fireball";
        go.GetComponent<Renderer>().sharedMaterial = SupportVoxels.Mat(new Color(1f, .55f, .18f), 4f);
        ball = go.transform;
        ball.position = HandPoint();
        ball.localScale = Vector3.one * .12f;
        ballLight = SupportVoxels.Glow(ball, Ember, .5f, 4f);
    }

    void Throw()
    {
        released = true;
        // 던질 때 줌아웃.
        HitFeedback.Zoom(-.05f, .15f, .25f, .5f);
        HitFeedback.Play(HitFeedback.Sfx.Swing, .9f, .75f);
        if (mode == Mode.Grab && grabbed && target)
        {
            var flight = new Flight
            {
                enemy = target, body = targetBody, wasTargetable = targetWasTargetable, yaw = targetYaw,
                pos = target.transform.position, start = target.transform.position,
                vel = forward * throwSpeed.x + Vector3.up * throwSpeed.y,
                spinAxis = Vector3.Cross(Vector3.up, forward), spin = 0,
            };
            flights.Add(flight);
            ThrowCount++;
            target = null;
            if (fx) fx.Flash(HandPoint(), 1f);
        }
        else if (ball)
        {
            flights.Add(new Flight { ball = ball, light = ballLight, pos = ball.position, start = ball.position, vel = forward * fireballSpeed + Vector3.up * .8f });
            ball = null;
            ballLight = null;
            FireballCount++;
        }
    }

    void Finish()
    {
        if (mode == Mode.Grab && !released) DropHeld();
        mode = Mode.None;
        if (animator && animator.isActiveAndEnabled)
        {
            var state = animator.GetCurrentAnimatorStateInfo(0).fullPathHash;
            if (state == ThrowState || state == DashState) animator.CrossFadeInFixedTime(Locomotion, .2f, 0);
        }
        Release();
    }

    void Release()
    {
        if (!holding) return;
        holding = false;
        if (motor) motor.ReleaseAutomation();
        if (combat) combat.ReleaseAutomation();
    }

    /// <summary>Lets go of a held enemy where it hangs (swap, lobby or an early end).</summary>
    void DropHeld()
    {
        if (target && grabbed && !released)
        {
            Vector3 at = target.transform.position;
            Place(target, targetBody, new Vector3(at.x, Ground(at, target), at.z), targetYaw);
            target.CanBeTargeted = targetWasTargetable;
        }
        target = null;
        grabbed = false;
    }

    // ---- flights ----------------------------------------------------------------------------------------
    /// <summary>Moves a thrown enemy or fireball; true once it has burst.</summary>
    bool UpdateFlight(Flight f, float dt)
    {
        bool enemyFlight = f.enemy;
        if (!enemyFlight && !f.ball) return true;
        if (enemyFlight && !f.enemy.IsAlive) { Resolve(f); return true; }
        f.age += dt;
        f.vel += Vector3.down * (enemyFlight ? 16f : fireballGravity) * dt;
        Vector3 from = f.pos;
        Vector3 step = f.vel * dt;
        f.pos += step;

        // A wall in the way: burst against it.
        if (step.sqrMagnitude > 1e-6f && Physics.Raycast(from + Vector3.up * .3f, step.normalized, out var wall, step.magnitude + .2f, ~0, QueryTriggerInteraction.Ignore)
            && !Ignored(wall.collider, f.enemy) && Vector3.Angle(wall.normal, Vector3.up) > 50f)
        {
            f.pos = wall.point - step.normalized * .35f - Vector3.up * .3f;
            Burst(f);
            return true;
        }
        if (enemyFlight)
        {
            f.spin += throwSpin * dt;
            f.enemy.transform.position = f.pos;
            f.enemy.transform.rotation = Quaternion.AngleAxis(f.spin, f.spinAxis) * Quaternion.Euler(0, f.yaw, 0);
            if (fx && Random.value < .6f) fx.Flame(f.enemy.AimPoint, -f.vel * .1f + Vector3.up, fx.FlameSize, .3f);
        }
        else
        {
            f.ball.position = f.pos;
            FeedFlames(f.pos, .8f);
        }

        // Another enemy in the way, the floor, or the end of the fireball's range.
        if (HitsEnemy(f)) { Burst(f); return true; }
        float floor = Ground(f.pos, f.enemy);
        if (f.vel.y < 0 && f.pos.y <= floor + (enemyFlight ? .02f : .15f)) { f.pos.y = floor; Burst(f); return true; }
        if (!enemyFlight && Vector3.Distance(f.start, f.pos) >= fireballRange) { Burst(f); return true; }
        if (f.age > 3f) { Burst(f); return true; }
        return false;
    }

    bool HitsEnemy(Flight f)
    {
        Vector3 point = f.enemy ? f.enemy.AimPoint : f.pos;
        float reach = f.enemy ? Radius(f.enemy) : .35f;
        foreach (var enemy in FindObjectsByType<TrainingEnemy>(FindObjectsSortMode.None))
        {
            if (enemy == f.enemy || !enemy.IsAlive || !enemy.CanBeTargeted) continue;
            Vector3 offset = enemy.AimPoint - point;
            if (Mathf.Abs(offset.y) > 1.6f) continue;
            offset.y = 0;
            if (offset.magnitude <= reach + Radius(enemy)) return true;
        }
        return false;
    }

    /// <summary>A flight ends early (swap, the enemy died): it bursts where it is.</summary>
    void Resolve(Flight f)
    {
        if (f.enemy && !f.enemy.IsAlive)
        {
            f.enemy.CanBeTargeted = f.wasTargetable;
            Vector3 at = f.enemy.transform.position;
            Place(f.enemy, f.body, new Vector3(at.x, Ground(at, f.enemy), at.z), f.yaw);
            return;
        }
        Burst(f);
    }

    void Burst(Flight f)
    {
        float scale = BlastScale;
        Vector3 center = f.pos;
        if (f.enemy)
        {
            float floor = Ground(center, f.enemy);
            center.y = floor;
            Place(f.enemy, f.body, center, f.yaw);
            f.enemy.CanBeTargeted = f.wasTargetable;
            if (f.enemy.IsAlive)
            {
                int damage = Mathf.Max(1, Mathf.RoundToInt(thrownDamage * DamageScale));
                f.enemy.LastHitDirection = Vector3.ProjectOnPlane(f.vel, Vector3.up).normalized;
                f.enemy.LastHitImpact = 1.5f;
                f.enemy.TakeDamage(damage);
                HitFeedback.Hit(f.enemy, f.enemy.AimPoint, f.enemy.LastHitDirection, damage, true);
                HitCount++;
            }
            Damage(center, blastRadius * scale, blastDamage, 1.3f, f.enemy);
            HitFeedback.HitStop(.09f, .05f);
            HitFeedback.Shake(.08f, .3f);
        }
        else
        {
            if (f.ball) Destroy(f.ball.gameObject);
            Damage(center, fireballRadius * scale, fireballDamage, 1.15f, null);
            HitFeedback.HitStop(.06f, .05f);
            HitFeedback.Shake(.05f, .22f);
        }
        Explosion(center, (f.enemy ? blastRadius : fireballRadius) * scale);
    }

    // ---- 맹화돌진 ----------------------------------------------------------------------------------------
    void TickRush(float dt)
    {
        float before = time;
        time += dt;
        if (before < rushTime)
        {
            // Fast start that eases out, like the dash curve.
            float a = Mathf.Clamp01(before / rushTime), b = Mathf.Clamp01(time / rushTime);
            float covered = rushDistance * ((1 - (1 - b) * (1 - b)) - (1 - (1 - a) * (1 - a)));
            Move(forward * covered);
            Vector3 feet = motor.transform.position;
            if (fx)
                for (int i = 0; i < 3; i++)
                    fx.Flame(feet + Random.insideUnitSphere * .35f + Vector3.up * .5f, -forward * Random.Range(1.5f, 3f) + Vector3.up, fx.FlameSize * 1.2f, .35f);
            foreach (var enemy in FindObjectsByType<TrainingEnemy>(FindObjectsSortMode.None))
            {
                if (!enemy.IsAlive || !enemy.CanBeTargeted || rushHits.Contains(enemy)) continue;
                Vector3 offset = enemy.transform.position - feet;
                if (Mathf.Abs(offset.y) > 2f) continue;
                offset.y = 0;
                if (offset.magnitude > rushWidth + Radius(enemy)) continue;
                rushHits.Add(enemy);
                Vector3 side = Vector3.Cross(Vector3.up, forward);
                Vector3 push = (forward + side * Mathf.Sign(Vector3.Dot(offset, side) + .001f) * .8f).normalized;
                Strike(enemy, rushDamage, push, 1.4f, true);
            }
            if (time >= rushTime)
            {
                Vector3 front = motor.transform.position + forward * .8f;
                float radius = rushBurstRadius * BlastScale;
                Damage(front, radius, rushBurstDamage, 1.4f, null);
                Explosion(front, radius);
                HitFeedback.HitStop(.07f, .05f);
                HitFeedback.Shake(.07f, .25f);
            }
        }
        if (time >= rushTime + rushRecover) Finish();
    }

    // ---- shared -----------------------------------------------------------------------------------------
    void Move(Vector3 delta)
    {
        if (controller && controller.enabled) controller.Move(delta);
    }

    void Damage(Vector3 center, float radius, int amount, float impact, TrainingEnemy exclude)
    {
        foreach (var enemy in FindObjectsByType<TrainingEnemy>(FindObjectsSortMode.None))
        {
            if (enemy == exclude || !enemy.IsAlive || !enemy.CanBeTargeted) continue;
            Vector3 delta = enemy.transform.position - center;
            if (Mathf.Abs(delta.y) > 3f) continue;
            Vector3 offset = Vector3.ProjectOnPlane(delta, Vector3.up);
            if (offset.magnitude > radius + Radius(enemy)) continue;
            Strike(enemy, amount, offset.sqrMagnitude > .01f ? offset.normalized : forward, impact, true);
        }
    }

    void Strike(TrainingEnemy enemy, int amount, Vector3 push, float impact, bool heavy)
    {
        enemy.LastHitDirection = push;
        enemy.LastHitImpact = impact;
        int damage = Mathf.Max(1, Mathf.RoundToInt(amount * DamageScale * Random.Range(.95f, 1.05f)));
        enemy.TakeDamage(damage);
        HitFeedback.Hit(enemy, enemy.AimPoint - push * .25f, push, damage, heavy);
        HitCount++;
    }

    void Explosion(Vector3 center, float radius)
    {
        HitFeedback.Play(HitFeedback.Sfx.HeavyHit, .9f, .85f);
        HitFeedback.Ring(center + Vector3.up * .1f, Vector3.up, radius);
        HitFeedback.Dust(center, 1.2f);
        HitFeedback.ScreenFlash(new Color(Ember.r, Ember.g, Ember.b, .16f), .2f);
        if (!fx || !fx.Ready) { SupportVoxels.Burst(center + Vector3.up * .4f, 18, 6f, .14f, new[] { Ember, new Color(1f, .8f, .3f) }, 4f); return; }
        fx.Flash(center + Vector3.up * .6f, 1.4f * radius / 2.4f + .4f);
        fx.Embers(center + Vector3.up * .4f, 36, 2.5f, 7f, Vector3.up * 2f);
        int count = 14;
        for (int i = 0; i < count; i++)
        {
            Vector3 dir = Quaternion.Euler(0, i * 360f / count + Random.Range(-8f, 8f), 0) * Vector3.forward;
            fx.Flame(center + dir * .3f + Vector3.up * .2f, dir * radius * 2.2f + Vector3.up * Random.Range(1f, 2.5f), fx.FlameSize * 1.6f, Random.Range(.35f, .5f));
        }
    }

    void FeedFlames(Vector3 at, float size)
    {
        if (!fx) return;
        flameClock += Time.deltaTime;
        if (flameClock < .025f) return;
        flameClock = 0;
        fx.Flame(at + Random.insideUnitSphere * .12f, Random.insideUnitSphere * .6f + Vector3.up * .8f, fx.FlameSize * size * Random.Range(.8f, 1.2f), Random.Range(.15f, .25f));
    }

    Vector3 HandPoint()
    {
        if (!rHand) return motor ? motor.transform.position + Vector3.up * 1.2f + forward * .4f : transform.position + Vector3.up;
        return rForearm ? rHand.position + (rHand.position - rForearm.position).normalized * .07f : rHand.position;
    }

    /// <summary>Floor height under a point, ignoring the flying enemy and the hunter.</summary>
    float Ground(Vector3 at, TrainingEnemy self)
    {
        float best = motor ? motor.transform.position.y : at.y;
        bool found = false;
        foreach (var hit in Physics.RaycastAll(at + Vector3.up * 1.2f, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (Ignored(hit.collider, self) || Vector3.Angle(hit.normal, Vector3.up) > 50f) continue;
            if (!found || hit.point.y > best) { best = hit.point.y; found = true; }
        }
        return best;
    }

    bool Ignored(Collider c, TrainingEnemy self)
    {
        if (!c) return true;
        if (motor && c.transform.IsChildOf(motor.transform)) return true;
        var enemy = c.GetComponentInParent<TrainingEnemy>();
        return enemy && (enemy == self || enemy.IsAlive);
    }

    static void Place(TrainingEnemy enemy, CharacterController body, Vector3 position, float yaw)
    {
        if (!enemy) return;
        // The controller keeps its own copy of the position: switching it off and on takes the new one.
        bool on = body && body.enabled;
        if (on) body.enabled = false;
        enemy.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
        if (on) body.enabled = true;
    }

    static float Radius(TrainingEnemy enemy)
        => enemy.TryGetComponent<CharacterController>(out var body) ? body.radius : .45f;
}
