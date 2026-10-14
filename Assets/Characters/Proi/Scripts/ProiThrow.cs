using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 일반 기술 (마우스 우클릭): 잡아 던지기 / 화염구. 동작은 Assets/Characters/Proi/Animations/Throw.anim (Tools > Proi > Bake Skill Clips).
///  - 근처(grabRange)에 적(ProiScarecrow)이 있으면: 그쪽을 보고 다가가 오른손을 뻗어 잡고, 오른쪽 뒤로
///    들어 올렸다가 앞으로 던짐. 적은 날아가 땅에 떨어지는 순간 폭발.
///  - 적이 없으면: 같은 동작으로 뻗은 오른손에 화염구가 생겨 커지고, 앞으로 던져 날아감. 적 / 땅에 닿거나 사거리 끝에서 폭발.
/// 이 컴포넌트는 동작 / 판정 / 히트스톱 / 카메라 흔들림만 하고, 그림은 ProiBlazeThrow(Blender 화염 VFX)가 그립니다.
/// 이벤트(OnGrab / OnThrowEnemy / OnEnemyLand / OnFireballLaunch / OnFireballBurst)와
/// 상태(Active, BallHeld, BallPosition ...)를 공개해 이펙트가 기술 흐름을 따라갈 수 있게 합니다.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class ProiThrow : MonoBehaviour
{
    [Header("동작")]
    [SerializeField] Animator animator;
    [SerializeField] string stateName = "Throw";
    [SerializeField] string locomotionState = "Locomotion";
    [SerializeField] float blendIn = 0.08f;
    [SerializeField] float blendOut = 0.2f;
    [Tooltip("다시 쓸 수 있을 때까지 대기 시간 (초, 끝난 뒤부터)")]
    [SerializeField] float cooldown = 0.2f;

    [Header("타이밍 (초, Throw.anim 과 맞춤)")]
    [Tooltip("손을 뻗어 적에게 다가가는 구간 끝 / 잡는 순간")]
    [SerializeField] float lungeEnd = 0.26f;
    [SerializeField] float grabTime = 0.3f;
    [Tooltip("손을 떠나는 순간")]
    [SerializeField] float releaseTime = 0.78f;
    [Tooltip("대기 동작으로 돌아가기 시작하는 시점 (이후 이동 가능)")]
    [SerializeField] float endTime = 1.12f;
    [Tooltip("던지며 앞으로 내딛는 거리 (m) / 구간 (초). Throw.anim 이 이만큼 뒤로 당겨져 구워져 있음 (ProiUltimateBaker.ThrowStride)")]
    [SerializeField] float strideDistance = 0.3f;
    [SerializeField] Vector2 strideTime = new Vector2(0.7f, 0.95f);

    [Header("잡아 던지기")]
    [Tooltip("이 거리 안의 적을 잡음 (m)")]
    [SerializeField] float grabRange = 2.6f;
    [Tooltip("잡는 순간 적까지 거리 (m): 이만큼 떨어질 때까지 다가감")]
    [SerializeField] float grabReach = 0.62f;
    [Tooltip("던지는 속도 (m/s): 앞 / 위")]
    [SerializeField] Vector2 throwSpeed = new Vector2(8f, 4f);
    [Tooltip("날아가며 앞으로 구르는 속도 (도/초)")]
    [SerializeField] float throwSpin = 540f;

    [Header("화염구")]
    [Tooltip("손에서 생기기 시작하는 시점 (초)")]
    [SerializeField] float fireballForm = 0.2f;
    [Tooltip("날아가는 속도 (m/s): 살짝 떨어지며 약 7m 앞 땅에 꽂혀 폭발")]
    [SerializeField] float fireballSpeed = 11f;
    [SerializeField] float fireballGravity = 9f;
    [Tooltip("최대 거리 (m): 땅에 닿기 전에 여기까지 가면 공중 폭발")]
    [SerializeField] float fireballRange = 9f;

    [Header("타격감")]
    [Tooltip("히트스톱 (초, 실제 시간): 잡기 / 던진 적이 떨어질 때 / 화염구 폭발")]
    [SerializeField] Vector3 hitStop = new Vector3(0.04f, 0.09f, 0.06f);
    [Tooltip("카메라 흔들림 (m): 잡기 / 던진 적이 떨어질 때 / 화염구 폭발")]
    [SerializeField] Vector3 shake = new Vector3(0.02f, 0.08f, 0.05f);

    // ------------------------------------------------------------------ state

    ProiCombat combat;
    ProiUltimate ultimate;
    ProiDash dash;
    ProiPlayerController mover;
    CharacterController controller;
    ProiQuarterViewCamera qcam;
    Transform rHand, rForearm;

    bool active, ending, grabbed, released;
    float time, readyAt, lungeDone, lungeDistance, strideDone;
    Vector3 forward, handLast, handVel;
    ProiScarecrow target;

    // the thrown enemy (until it lands)
    ProiScarecrow thrown;

    // the fireball
    class Fireball
    {
        public bool held, flying;
        public Vector3 pos, vel, start;
        public float born;
    }
    readonly Fireball ball = new Fireball();

    /// <summary>기술 동작 중이면 true — 이동 / 다른 공격 입력을 막습니다.</summary>
    public bool Busy => active && !ending;

    // ---- read-only state + events for the effects (ProiBlazeThrow)
    /// <summary>기술 동작 중 (끝난 뒤 대기 자세로 돌아가는 구간 포함).</summary>
    public bool Active => active;
    /// <summary>기술 시작부터 흐른 시간 (초).</summary>
    public float SkillTime => time;
    public float ReleaseTime => releaseTime;
    /// <summary>던지는 방향 (수평).</summary>
    public Vector3 Forward => forward;
    /// <summary>오른주먹 끝 (불덩이 / 잡은 적이 붙는 곳).</summary>
    public Vector3 Hand => HandPoint();
    public bool BallHeld => ball.held;
    public bool BallFlying => ball.flying;
    public Vector3 BallPosition => ball.pos;
    public Vector3 BallVelocity => ball.vel;
    /// <summary>손에서 커지는 불덩이 크기 0..1 (던지면 1).</summary>
    public float BallGrow => ball.flying ? 1f : !ball.held ? 0f
        : 1f - Mathf.Pow(1f - Mathf.Clamp01((Time.time - ball.born) / Mathf.Max(releaseTime - fireballForm, 0.05f)), 2f);
    /// <summary>잡고 있는 적 (던지기 전까지).</summary>
    public ProiScarecrow HeldEnemy => active && grabbed && !released ? target : null;
    /// <summary>던져서 날아가는 적 (떨어지면 null).</summary>
    public ProiScarecrow ThrownEnemy => thrown;
    public event System.Action<ProiScarecrow> OnGrab;
    public event System.Action<ProiScarecrow> OnThrowEnemy;
    public event System.Action<ProiScarecrow, Vector3> OnEnemyLand;
    public event System.Action<Vector3, Vector3> OnFireballLaunch;          // position, velocity
    public event System.Action<Vector3, Vector3, float> OnFireballBurst;    // position, velocity, scale

    void Awake()
    {
        combat = GetComponent<ProiCombat>();
        ultimate = GetComponent<ProiUltimate>();
        mover = GetComponent<ProiPlayerController>();
        controller = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
    }

    void Start()
    {
        if (animator == null)
        {
            Debug.LogWarning("[ProiThrow] missing animator - skill disabled");
            enabled = false;
            return;
        }
        rHand = FindDeep(animator.transform, "RightHand");
        rForearm = FindDeep(animator.transform, "RightLowerArm");
        qcam = Camera.main != null ? Camera.main.GetComponent<ProiQuarterViewCamera>() : null;
    }

    // ------------------------------------------------------------------ input + timeline

    void Update()
    {
        Mouse mouse = Mouse.current;
        if (dash == null) dash = GetComponent<ProiDash>();
        bool otherBusy = (combat != null && combat.Busy) || (ultimate != null && ultimate.Busy) || (dash != null && dash.Busy);
        if (!active && !otherBusy && mouse != null && mouse.rightButton.wasPressedThisFrame && Time.time >= readyAt)
            Begin();

        if (active) Tick();
        UpdateFireball();
    }

    void LateUpdate()
    {   // after the animator: things held in the hand follow the posed hand
        if (!enabled || rHand == null) return;
        Vector3 hand = HandPoint();
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        handVel = Vector3.Lerp(handVel, (hand - handLast) / dt, 0.35f);
        handLast = hand;

        if (grabbed && !released && target != null)
        {   // the enemy hangs from the hand, held out away from her body (in front, then round the side and behind as she
            // winds up): chest at the fist, its foot leaning away from her and swinging behind the hand's motion
            Vector3 away = Vector3.ProjectOnPlane(hand - transform.position, Vector3.up);
            away = away.sqrMagnitude > 0.01f ? away.normalized : forward;
            Vector3 up = (Vector3.up * 6f - away * 1.6f - handVel * 0.3f).normalized;
            target.Hold(hand + away * 0.26f, up, -away, Mathf.Clamp01((time - grabTime) / 0.08f));
        }
        if (ball.held) ball.pos = hand + Vector3.up * 0.06f;
    }

    void Begin()
    {
        target = FindTarget();
        if (target != null)
        {
            Vector3 to = Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up);
            if (to.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
            lungeDistance = Mathf.Max(to.magnitude - grabReach, 0f);
            target.Reserve();
        }
        else
        {
            if (mover != null && mover.MoveInputWorld.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(mover.MoveInputWorld, Vector3.up);   // 방향키를 누르고 있으면 그쪽으로
            lungeDistance = 0f;
        }
        forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        active = true; ending = false; grabbed = released = false;
        time = 0f; lungeDone = 0f; strideDone = 0f;
        handLast = HandPoint(); handVel = Vector3.zero;
        animator.speed = 1f;
        animator.CrossFadeInFixedTime(stateName, blendIn, 0, 0f);
    }

    void Tick()
    {
        time += Time.deltaTime;

        // close in on the enemy while the arm shoots out
        float k = Mathf.Clamp01(time / Mathf.Max(lungeEnd, 0.01f));
        float d = lungeDistance * (1f - (1f - k) * (1f - k));
        if (d > lungeDone && controller.enabled) controller.Move(forward * (d - lungeDone));
        lungeDone = d;

        // the pitcher's stride: carried forward onto the planted front foot as she throws
        float s = Mathf.Clamp01((time - strideTime.x) / Mathf.Max(strideTime.y - strideTime.x, 0.01f));
        s = strideDistance * s * s * (3f - 2f * s);
        if (s > strideDone && controller.enabled) controller.Move(forward * (s - strideDone));
        strideDone = s;

        if (target == null && !ball.held && !ball.flying && time >= fireballForm && !released) FormFireball();
        if (!grabbed && time >= grabTime)
        {
            grabbed = true;
            if (target != null) Grab();
        }
        if (!released && time >= releaseTime)
        {
            released = true;
            if (target != null) ThrowEnemy();
            else if (ball.held) LaunchFireball();
        }
        if (!ending && time >= endTime)
        {
            ending = true;
            animator.CrossFadeInFixedTime(locomotionState, blendOut, 0);
        }
        if (ending && time >= endTime + blendOut)
        {
            active = false;
            readyAt = Time.time + cooldown;
        }
    }

    ProiScarecrow FindTarget()
    {   // the nearest standing enemy within reach, any direction (she turns to it)
        ProiScarecrow best = null;
        float bestD = grabRange;
        foreach (ProiScarecrow s in ProiScarecrow.All)
        {
            if (s == null || s.Current != ProiScarecrow.State.Idle) continue;
            float dist = Vector3.ProjectOnPlane(s.transform.position - transform.position, Vector3.up).magnitude;
            if (dist < bestD) { bestD = dist; best = s; }
        }
        return best;
    }

    void OnDisable()
    {
        if (active && target != null && grabbed && !released) target.Throw(Vector3.zero, Vector3.right, 0f);
        active = false;
    }

    // ------------------------------------------------------------------ grab + throw the enemy

    void Grab()
    {
        target.Grab();
        OnGrab?.Invoke(target);
        ProiHitFeedback.HitStop(hitStop.x);
        if (qcam != null) qcam.Shake(shake.x, 0.1f, forward);
    }

    void ThrowEnemy()
    {
        Vector3 vel = forward * throwSpeed.x + Vector3.up * throwSpeed.y;
        target.Throw(vel, Vector3.Cross(Vector3.up, forward), throwSpin);
        target.Landed += OnEnemyLanded;
        thrown = target;
        OnThrowEnemy?.Invoke(target);
    }

    void OnEnemyLanded(ProiScarecrow s, Vector3 at)
    {
        s.Landed -= OnEnemyLanded;
        if (thrown == s) thrown = null;
        OnEnemyLand?.Invoke(s, at);
        ProiHitFeedback.HitStop(hitStop.y);
        if (qcam != null) qcam.Shake(shake.y, 0.28f, forward, 1f);
    }

    // ------------------------------------------------------------------ fireball

    void FormFireball()
    {
        ball.held = true; ball.flying = false; ball.born = Time.time;
        ball.pos = HandPoint() + Vector3.up * 0.06f;
    }

    void LaunchFireball()
    {
        ball.held = false; ball.flying = true;
        ball.start = ball.pos;
        ball.vel = forward * fireballSpeed + Vector3.up * 0.6f;
        OnFireballLaunch?.Invoke(ball.pos, ball.vel);
    }

    void UpdateFireball()
    {
        if (!ball.flying) return;
        float dt = Time.deltaTime;
        ball.vel += Vector3.down * fireballGravity * dt;
        ball.pos += ball.vel * dt;

        // hits an enemy, the ground, or burns out at the end of its range
        foreach (ProiScarecrow s in ProiScarecrow.All)
        {
            if (s == null || s.Current != ProiScarecrow.State.Idle) continue;
            Vector3 a = s.transform.position, b = s.Chest + s.transform.up * 0.4f;
            Vector3 closest = a + Vector3.Project(ball.pos - a, b - a);
            if (Vector3.Distance(ball.pos, closest) < 0.5f && ball.pos.y <= b.y + 0.2f)
            {
                s.Hit(ball.vel);
                ExplodeFireball(1.1f);
                return;
            }
        }
        if (ball.pos.y <= transform.position.y + 0.1f) { ExplodeFireball(1f); return; }
        if (Vector3.Distance(ball.start, ball.pos) >= fireballRange) ExplodeFireball(0.8f);
    }

    void ExplodeFireball(float scale)
    {
        ball.flying = false;
        OnFireballBurst?.Invoke(ball.pos, ball.vel, scale);
        ProiHitFeedback.HitStop(hitStop.z);
        if (qcam != null) qcam.Shake(shake.z, 0.22f, ball.vel);
    }

    // ------------------------------------------------------------------ helpers

    Vector3 HandPoint()
    {
        if (rHand == null) return transform.position + Vector3.up;
        return rForearm != null ? rHand.position + (rHand.position - rForearm.position).normalized * 0.07f : rHand.position;
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t)
        {
            Transform f = FindDeep(c, name);
            if (f != null) return f;
        }
        return null;
    }

    void OnDestroy()
    {
        if (thrown != null) thrown.Landed -= OnEnemyLanded;
    }
}
