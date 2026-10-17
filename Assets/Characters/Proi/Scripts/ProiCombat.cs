using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 마우스 좌클릭 3타 콤보: 1) 왼주먹 잽  2) 오른주먹 + 오른발 앞으로  3) 뒤돌아 왼발 옆차기.
/// 공격 중에 다시 클릭하면 다음 타로 이어지고, 2타에서 멈추면 오른발을 뒤로 빼며 준비 자세로 돌아옵니다.
/// 클립은 제자리 동작이라, 디딘 발이 미끄러지지 않도록 ProiAttackData 의 이동량만큼 캐릭터를 옮깁니다.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class ProiCombat : MonoBehaviour, IProiComboSource
{
    class Step
    {
        public string state;
        public float[] forward, right;
        public float inputFrom;   // 이 시점(0~1)부터 다음 타 입력을 받음
        public float chainAt;     // 다음 타 입력이 있으면 이 시점에 이어감
        public int next = -1;     // 이어질 타 (-1 = 없음)
        public int fallback = -1; // 입력 없이 끝나면 재생할 동작 (-1 = 대기 자세로)
        public float frames => forward.Length;
    }

    const int Jab = 0, Cross = 1, SpinKick = 2, Recover = 3;

    [SerializeField] Animator animator;
    [Tooltip("공격 재생 속도 배율")]
    [SerializeField] float attackSpeed = 1.1f;
    [SerializeField] float blendIn = 0.08f;
    [SerializeField] float blendOut = 0.2f;
    [SerializeField] string locomotionState = "Locomotion";

    Step[] steps;
    CharacterController controller;
    ProiPlayerController mover;
    ProiUltimate ultimate;
    ProiThrow skill;
    ProiDash dash;
    int current = -1;
    bool queued;
    float time;
    Vector2 lastTravel;
    Quaternion moveFrame;

    /// <summary>공격(또는 복귀) 동작 중이면 true — 이동 입력을 막습니다.</summary>
    public bool Busy => current >= 0;

    /// <summary>재생 중인 동작: 0 = 1타 잽, 1 = 2타, 2 = 3타 옆차기, 3 = 복귀, -1 = 없음.</summary>
    public int CurrentStep => current;

    /// <summary>재생 중인 동작의 진행도 (0~1).</summary>
    public float Progress { get; private set; }

    /// <summary>공격 재생 속도 배율.</summary>
    public float AttackSpeed => attackSpeed;

    public Animator ComboAnimator => animator;

    /// <summary>플레이그라운드에서는 화염 콤보가 히트스톱 / 카메라 흔들림까지 처리합니다.</summary>
    public bool PlayHitFeel => true;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        mover = GetComponent<ProiPlayerController>();
        ultimate = GetComponent<ProiUltimate>();
        skill = GetComponent<ProiThrow>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        steps = new[]
        {
            new Step { state = "Attack1", forward = ProiAttackData.Attack1Forward, right = ProiAttackData.Attack1Right,
                       inputFrom = 0.15f, chainAt = 0.62f, next = Cross },
            new Step { state = "Attack2", forward = ProiAttackData.Attack2Forward, right = ProiAttackData.Attack2Right,
                       inputFrom = 0.2f, chainAt = 0.72f, next = SpinKick, fallback = Recover },
            new Step { state = "Attack3", forward = ProiAttackData.Attack3Forward, right = ProiAttackData.Attack3Right,
                       inputFrom = 1f, chainAt = 1f },
            new Step { state = "Recover2", forward = ProiAttackData.Recover2Forward, right = ProiAttackData.Recover2Right,
                       inputFrom = 0.3f, chainAt = 0.8f, next = Jab },
        };
    }

    void Update()
    {
        if (ultimate == null) ultimate = GetComponent<ProiUltimate>();
        if (skill == null) skill = GetComponent<ProiThrow>();
        if (dash == null) dash = GetComponent<ProiDash>();
        if ((ultimate != null && ultimate.Busy) || (skill != null && skill.Busy) || (dash != null && dash.Busy)) return;   // 궁극기 / 우클릭 기술 / 대쉬 중에는 평타 없음
        Mouse mouse = Mouse.current;
        bool click = mouse != null && mouse.leftButton.wasPressedThisFrame;

        if (current < 0)
        {
            if (click) Begin(Jab);
            return;
        }

        Step s = steps[current];
        float duration = Mathf.Max(s.frames - 1f, 1f) / 30f / attackSpeed;
        time += Time.deltaTime;
        float u = Mathf.Clamp01(time / duration);
        Progress = u;
        if (click && u >= s.inputFrom && s.next >= 0) queued = true;

        // root travel of the in-place clip (planted foot stays put)
        Vector2 travel = Sample(s, u);
        Vector2 d = travel - lastTravel;
        lastTravel = travel;
        controller.Move(moveFrame * new Vector3(d.y, 0f, d.x));

        if (queued && u >= s.chainAt) { Begin(s.next); return; }
        if (u >= 1f)
        {
            if (s.fallback >= 0) Begin(s.fallback);
            else End();
        }
    }

    void Begin(int index)
    {
        if (index == Jab && mover != null)
        {
            Vector3 dir = mover.MoveInputWorld;          // 방향키를 누르고 있으면 그쪽을 보고 공격
            if (dir.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }
        current = index;
        queued = false;
        time = 0f;
        Progress = 0f;
        lastTravel = Vector2.zero;
        moveFrame = transform.rotation;
        animator.speed = attackSpeed;
        animator.CrossFadeInFixedTime(steps[index].state, blendIn, 0, 0f);
    }

    void End()
    {
        current = -1;
        animator.speed = 1f;
        animator.CrossFadeInFixedTime(locomotionState, blendOut, 0);
    }

    static Vector2 Sample(Step s, float u)
    {
        float x = u * (s.forward.Length - 1);
        int i = Mathf.Min((int)x, s.forward.Length - 2);
        float t = x - i;
        return new Vector2(Mathf.Lerp(s.forward[i], s.forward[i + 1], t), Mathf.Lerp(s.right[i], s.right[i + 1], t));
    }
}
