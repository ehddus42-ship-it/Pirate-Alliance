using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// 궁극기 (R):
///  1) 카메라가 빠르게 정면샷으로 오고, 팔을 벌리는 순간 두 주먹으로 크래시 줌인 -> 가슴 앞에서 두 주먹을 마주 침
///     (히트스톱 + 슬로모션 + 흔들림 + 줌 펀치) -> 전신샷으로 줌아웃
///  2) 깊은 런지(왼발 앞, 오른다리 뒤로 뻗음)로 왼팔 가드, 오른주먹을 허리에 장전하며 화염 차징: 카메라가 주먹 쪽으로 천천히
///     다가가고, 맥박마다 화면이 미세하게 떨림
///  3) 땅이 터지며(줌아웃 펀치) 빠르게 솟아 정점에서 잠깐 떠 있다가(슬로), 착지점으로 당겨 드는 카메라와 함께 2배속으로
///     대각선으로 내리꽂혀 오른주먹으로 땅을 찍음 (긴 히트스톱 + 슬로모션 + 큰 흔들림 / 줌인)
/// 연출 배속(timeWarp): 클립 시간마다 게임 배속을 정해 도입 / 도약 / 내려꽂기는 빠르게, 정점은 느리게 (ProiHitFeedback.BaseScale).
///  4) 찍은 곳에서 용암 균열이 왼쪽 / 가운데 / 오른쪽 세 갈래로 동시에 달려가고, 균열이 닿는 곳마다 세 갈래가 한꺼번에
///     한 줄씩 불기둥이 솟음 (가까운 줄부터 3줄, 바깥으로 갈수록 크고 넓게 퍼짐).
/// 동작은 Assets/Characters/Proi/Animations/Ultimate.anim (Tools > Proi > Bake Skill Clips 로 구움), 시간 값은 그 클립과 맞춰져 있습니다.
/// 이 컴포넌트는 동작 / 카메라 연출 / 히트스톱 / 흔들림과 균열 경로 / 불기둥 배치만 하고, 그림은 ProiBlazeUltimate(Blender 화염 VFX)가
/// 장면마다 내는 이벤트(OnClash / OnChargePulse / OnCharged / OnLeap / OnSlam / OnJet / OnCrack)를 받아 그립니다.
/// [타임라인 컷씬] cutscene 에 PlayableDirector(Assets/Characters/Proi/Timeline/ProiUltimate.playable)가 있으면 그걸 재생합니다:
///  몸 동작 / 카메라 샷 / 연출 배속은 Timeline 트랙이, 장면 시간은 Proi 궁극기 트랙의 비트 마커가 정하고, 이 컴포넌트는
///  그 시간에 맞춰 이동 / 타격감 / 이펙트 이벤트를 냅니다. 비워 두면 아래 코드 값(정면 카메라, 연출 배속 ...)으로 재생합니다.
/// [게임(헌터 교체) 안] ProiHunterUltimate 가 externalInput / cinematicTime 을 끄고 TryBegin 으로 시작합니다:
///  게임 배속(Time.timeScale)은 건드리지 않고(일시정지 / 적 / 히트스톱은 게임 것), 연출 배속은 Proi 의 동작에만 걸리며,
///  카메라는 ProiQuarterViewCamera 가 없으면 게임 카메라 그대로입니다. 캐릭터 기준은 이 컴포넌트가 붙은 Transform 입니다.
/// </summary>
public class ProiUltimate : MonoBehaviour
{
    [Header("입력")]
    [SerializeField] Key key = Key.R;
    [Tooltip("켜면 키를 직접 읽지 않고 TryBegin 으로만 시작합니다 (게임: ProiHunterUltimate)")]
    public bool externalInput;
    [Tooltip("연출 배속 / 히트스톱을 게임 배속(Time.timeScale)으로 겁니다 (플레이그라운드). 끄면 게임 배속은 그대로 두고, 연출 배속은 Proi 의 동작에만 걸립니다")]
    public bool cinematicTime = true;

    [Header("타임라인 컷씬")]
    [Tooltip("궁극기 Timeline 을 재생할 PlayableDirector (Tools > Proi > Build Ultimate Timeline). 비우면 코드 값으로 재생")]
    [SerializeField] PlayableDirector cutscene;

    [Header("동작")]
    [SerializeField] Animator animator;
    [SerializeField] string stateName = "Ultimate";
    [SerializeField] string locomotionState = "Locomotion";
    [SerializeField] float blendIn = 0.12f;
    [SerializeField] float blendOut = 0.25f;
    [Tooltip("다시 쓸 수 있을 때까지 대기 시간 (초, 끝난 뒤부터)")]
    [SerializeField] float cooldown = 0.5f;

    [Header("타이밍 (초, Ultimate.anim 과 맞춤 — 타임라인이 있으면 비트 마커 시간으로 덮어씀)")]
    [SerializeField] float clashTime = 0.48f;
    [SerializeField] float chargeStart = 0.95f;
    [SerializeField] float chargeFull = 2.3f;
    [Tooltip("차징이 끝나는 시점 (맥박 / 화면 떨림이 멈춤, 클로즈업은 그대로 유지)")]
    [SerializeField] float cameraBack = 2.7f;
    [Tooltip("도약 컷: 클로즈업에서 캐릭터가 화면 위로 튀어 나간 직후 와이드샷으로 빠르게 넘어가는 시점 (클립 초) / 전환 시간 (실제 초)")]
    [SerializeField] Vector2 jumpCut = new Vector2(3.03f, 0f);
    [Tooltip("땅을 박차고 솟는 시점 (여기서부터 땅 찍기까지 앞으로 이동)")]
    [SerializeField] float dashStart = 2.96f;
    [SerializeField] float slamTime = 3.98f;
    [Tooltip("대기 동작으로 돌아가기 시작하는 시점 (이후 이동 가능)")]
    [SerializeField] float endTime = 5.0f;

    [Header("연출 배속 — 타임라인이 없을 때 (클립 시간 x 초 -> 게임 배속 y)")]
    [Tooltip("도입은 빠르게, 정점은 체공하듯 느리게, 내려꽂기는 2배속. 히트스톱 / 슬로모션은 그 위에 따로 걸림")]
    [SerializeField] bool useTimeWarp = true;
    [SerializeField] Vector2[] timeWarp =
    {
        new Vector2(0f, 1.35f), new Vector2(0.44f, 1.35f), new Vector2(0.5f, 1f),       // 주먹 마주치기까지 빠르게
        new Vector2(2.75f, 1f), new Vector2(2.85f, 1.4f), new Vector2(3.2f, 1.25f),     // 웅크렸다 폭발적으로 솟음
        new Vector2(3.36f, 0.55f), new Vector2(3.58f, 0.55f),                           // 정점: 체공
        new Vector2(3.68f, 2.1f), new Vector2(3.98f, 2.1f), new Vector2(4.02f, 1f),     // 내려꽂기 2배속
    };

    [Header("아치 점프")]
    [Tooltip("점프하며 앞으로 날아가는 거리 (m)")]
    [SerializeField] float dashDistance = 4.5f;
    [Tooltip("정점 / 내려꽂기 시작 시점 (클립 초, Ultimate.anim 의 apex / turn 자세)")]
    [SerializeField] float apexTime = 3.45f;
    [SerializeField] float diveTime = 3.65f;
    [Tooltip("전진 거리 중 정점까지 / 내려꽂기 시작까지 간 비율: 나머지를 내려꽂으며 가속해 한 번에 감")]
    [SerializeField] Vector2 arcSplit = new Vector2(0.2f, 0.3f);
    [Tooltip("내려꽂기 가속 (클수록 끝에서 빠름)")]
    [SerializeField] float diveEaseIn = 2.2f;
    [Tooltip("점프 중 카메라: 출발점과 착지점 사이를 같은 쿼터뷰 각도로 비추는 거리 (m, 0 = 기본 카메라)")]
    [SerializeField] float jumpCameraDistance = 12f;
    [Tooltip("정점에서 착지점으로 당겨 드는 카메라: 거리 (m, 0 = 끔) / 줄이는 시야각 (도) / 이동 시간 (실제 초)")]
    [SerializeField] Vector3 diveCamera = new Vector3(8f, 4f, 0.32f);

    [Header("카메라 — 타임라인이 없을 때 (있으면 카메라 트랙의 샷 클립)")]
    [Tooltip("정면 전신샷: 캐릭터와의 거리 / 카메라 높이 / 바라보는 높이 (m)")]
    [SerializeField] float frontDistance = 4.2f;
    [SerializeField] float frontHeight = 1.05f;
    [SerializeField] float frontLookHeight = 0.95f;
    [Tooltip("정면에서 캐릭터 오른쪽으로 돌린 각도 (0 = 정면)")]
    [SerializeField] float frontYaw = 0f;
    [SerializeField] float frontFov = 34f;
    [Tooltip("시작 정면샷으로 가는 시간 (실제 초, 0 = 하드 컷: 쿼터뷰와 정면샷 사이를 보간하면 중간에 바닥만 보임)")]
    [SerializeField] float cameraBlendIn = 0f;
    [SerializeField] float cameraBlendOut = 0.55f;
    [Tooltip("시작 정면샷: 거리 / 카메라 높이 / 바라보는 높이 (m) — 상반신")]
    [SerializeField] Vector3 openShot = new Vector3(3.2f, 1.3f, 1.15f);

    [Header("주먹 마주침 연출")]
    [Tooltip("주먹 클로즈업으로 크래시 줌인하는 시점 (클립 초) / 이동 시간 (실제 초)")]
    [SerializeField] Vector2 clashPush = new Vector2(0.3f, 0.15f);
    [Tooltip("주먹 클로즈업: 주먹까지 거리 / 카메라 높이 (m) / 시야각")]
    [SerializeField] Vector3 clashCloseUp = new Vector3(1.75f, 1.4f, 30f);
    [Tooltip("마주친 뒤 슬로모션: 실제 초 / 배속")]
    [SerializeField] Vector2 clashSlow = new Vector2(0.32f, 0.22f);
    [Tooltip("마주친 순간 줌 펀치 (시야각 도, + = 줌인)")]
    [SerializeField] float clashZoom = 4f;
    [Tooltip("전신샷으로 줌아웃하는 시점 (클립 초) / 이동 시간 (실제 초)")]
    [SerializeField] Vector2 clashPullOut = new Vector2(0.62f, 0.4f);
    [Tooltip("차징 중 주먹 쪽으로 천천히 다가가는 카메라: 거리 / 높이 (m)")]
    [SerializeField] Vector2 chargeCloseUp = new Vector2(2.2f, 1.3f);
    [Tooltip("차징 클로즈업이 정면에서 캐릭터 오른쪽으로 돌린 각도")]
    [SerializeField] float chargeCloseUpYaw = 48f;

    [Header("차징")]
    [Tooltip("주먹 화염이 맥박처럼 번쩍이는 간격 (초)")]
    [SerializeField] float chargePulseInterval = 0.3f;
    [Tooltip("차징 중 계속되는 화면 떨림 (m): 차징 시작 -> 풀차지. 웅크렸다 도약하기 직전까지 이어지며 더 세짐")]
    [SerializeField] Vector2 chargeRumble = new Vector2(0.008f, 0.03f);
    [Tooltip("웅크리는 동안 떨림 배율 (풀차지 떨림 대비)")]
    [SerializeField] float crouchRumble = 1.4f;
    [Tooltip("맥박마다 쿵 하는 흔들림 (m) / 줌 펀치 (도): 차징 시작 -> 풀차지")]
    [SerializeField] Vector2 pulseShake = new Vector2(0.02f, 0.05f);
    [SerializeField] Vector2 pulseZoom = new Vector2(0.6f, 1.8f);
    [Tooltip("풀차지 순간 흔들림 (m) / 줌 펀치 (도)")]
    [SerializeField] Vector2 chargedShake = new Vector2(0.07f, 2.5f);
    [Tooltip("풀차지 순간 히트스톱 (초, 실제 시간, 0 = 끔)")]
    [SerializeField] float chargedHitStop = 0f;
    [Tooltip("차징 맥박마다 발밑에서 뻗는 용암 균열: 개수 (0 = 끔) / 처음 길이 / 풀차지 길이 (m). 풀차지 때는 사방으로 한 바퀴")]
    [SerializeField] Vector3 chargeCracks = new Vector3(0f, 0.45f, 1.4f);

    [Header("불기둥: 세 갈래(왼쪽 / 가운데 / 오른쪽) x 3줄 (가까운 줄 / 가운데 줄 / 바깥 줄)")]
    [Tooltip("땅을 찍은 뒤 각 줄이 솟는 시간 (초): 세 갈래가 한꺼번에")]
    [SerializeField] Vector3 rowDelays = new Vector3(0.15f, 0.45f, 0.75f);
    [Tooltip("줄마다 높이 (m): 바깥 줄일수록 크게")]
    [SerializeField] Vector3 rowHeights = new Vector3(1.6f, 2.6f, 3.8f);
    [Tooltip("줄마다 밑동 반지름 (m)")]
    [SerializeField] Vector3 rowRadii = new Vector3(0.26f, 0.34f, 0.46f);
    [Tooltip("가운데 갈래: 찍은 곳에서 앞으로 (m), 줄마다")]
    [SerializeField] Vector3 centerForward = new Vector3(0.8f, 2.1f, 3.4f);
    [Tooltip("양옆 갈래: 찍은 곳에서 앞으로 (m), 줄마다")]
    [SerializeField] Vector3 sideForward = new Vector3(0.75f, 1.95f, 3.2f);
    [Tooltip("양옆 갈래: 찍은 곳에서 옆으로 (m), 줄마다 (바깥으로 퍼짐)")]
    [SerializeField] Vector3 sideOut = new Vector3(1.9f, 2.6f, 3.25f);
    [Tooltip("불기둥 하나가 솟아서 사라질 때까지 (초)")]
    [SerializeField] float pillarLife = 1.25f;
    [Tooltip("불기둥이 솟는 동안 카메라가 같은 쿼터뷰 각도로 캐릭터와 불기둥 사이를 비추는 거리 (m, 0 = 끔)")]
    [SerializeField] float pillarCameraDistance = 11f;
    [Tooltip("땅을 찍은 뒤 와이드샷으로 빠지기까지 (클립 초): 그동안 꽂힌 순간을 가까이 보여 줌")]
    [SerializeField] float wideShotDelay = 0.1f;

    [Header("땅 균열")]
    [Tooltip("용암 균열 폭 (m)")]
    [SerializeField] float crackWidth = 0.26f;
    [Tooltip("균열이 좌우로 꺾이는 정도 (m)")]
    [SerializeField] float crackJag = 0.14f;
    [Tooltip("땅을 찍은 곳에서 솟는 작은 불기둥: 높이 / 반지름 (m, 높이 0 = 끔)")]
    [SerializeField] Vector2 centerJet = new Vector2(1.5f, 0.28f);

    [Header("타격감")]
    [Tooltip("히트스톱 (초, 실제 시간): 주먹 마주침 / 땅 찍기")]
    [SerializeField] Vector2 hitStop = new Vector2(0.08f, 0.11f);
    [Tooltip("카메라 흔들림 (m): 주먹 마주침 / 땅 찍기 / 불기둥")]
    [SerializeField] Vector3 shake = new Vector3(0.045f, 0.17f, 0.06f);
    [Tooltip("땅 찍기 순간 줌인 (시야각 도)")]
    [SerializeField] float slamZoom = 5f;
    [Tooltip("땅 찍은 뒤 슬로모션: 실제 초 / 배속")]
    [SerializeField] Vector2 slamSlow = new Vector2(0.4f, 0.3f);
    [Tooltip("도약 순간 줌아웃 펀치 (시야각 도, - = 넓어짐)")]
    [SerializeField] float leapZoom = -3f;

    // ------------------------------------------------------------------ state

    ProiCombat combat;
    ProiThrow skill;
    ProiDash dash;
    ProiPlayerController mover;
    ProiQuarterViewCamera qcam;
    CharacterController controller;
    Camera viewCam;
    Transform lHand, rHand, rForearm, chestBone, headBone;
    float crackAngle;
    bool active, ending;
    float time, readyAt;
    bool clashed, charged, cameraReturned, slammed, wideShot, closeUp, pushedIn, pulledOut, diveShot, wideShotDone, jumpCutDone;
    float dashDone, nextPulse, nextRumble;
    bool leapt;
    Vector3 leapPoint;
    bool previewing;
    Vector3 previewOrigin, previewForward;
    int rowsSpawned;
    Vector3 slamPoint, forward;
    float fxFadeAt;                     // Time.time when the cracks / scorch start to go
    float localWarp = 1f;               // the time warp on her own clock when cinematicTime is off

    /// <summary>궁극기 동작 중이면 true — 이동 / 평타 입력을 막습니다.</summary>
    public bool Busy => active && !ending;

    /// <summary>Timeline 컷씬으로 재생하는지.</summary>
    public bool UsesTimeline => cutscene != null && cutscene.playableAsset is TimelineAsset;

    // ---- read-only state + events for the effects (ProiBlazeUltimate)
    public bool Active => active;
    /// <summary>궁극기 시작부터 흐른 시간 (초).</summary>
    public float SkillTime => time;
    public float ChargeStart => chargeStart;
    public float ChargeFull => chargeFull;
    public float DashStart => dashStart;
    public float SlamTime => slamTime;
    /// <summary>바라보는 방향 (수평).</summary>
    public Vector3 Forward => forward;
    /// <summary>균열 / 그을음이 사라지기 시작하는 시각 (Time.time).</summary>
    public float FxFadeAt => fxFadeAt;
    public event System.Action<Vector3> OnClash;                         // where the fists met
    public event System.Action<Vector3, float> OnChargePulse;            // fist, charge level 0..1 (heartbeat flare)
    public event System.Action<Vector3> OnCharged;                       // fist at full charge
    public event System.Action<Vector3> OnLeap;                          // take-off point on the ground
    public event System.Action<Vector3> OnSlam;                          // slam point on the ground
    public event System.Action<Vector3, float, float> OnJet;             // a fire pillar: foot, height, radius
    public event System.Action<List<Vector3>, float, float, float> OnCrack;   // points, width, start (Time.time), reach (s)

    // ------------------------------------------------------------------ setup

    void Awake()
    {
        combat = GetComponent<ProiCombat>();
        mover = GetComponent<ProiPlayerController>();
        controller = GetComponentInParent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
    }

    void Start()
    {
        if (animator == null)
        {
            Debug.LogWarning("[ProiUltimate] missing animator - ultimate disabled");
            enabled = false;
            return;
        }

        FindBones();
        qcam = Camera.main != null ? Camera.main.GetComponent<ProiQuarterViewCamera>() : null;
        viewCam = Camera.main;
    }

    void FindBones()
    {   // also from the editor timeline preview, where Start has not run
        if (rHand != null) return;
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator == null) return;
        Transform root = animator.transform;
        lHand = FindDeep(root, "LeftHand");
        rHand = FindDeep(root, "RightHand");
        rForearm = FindDeep(root, "RightLowerArm");
        chestBone = FindDeep(root, "UpperChest") ?? FindDeep(root, "Chest");
        headBone = FindDeep(root, "Head");
    }

    /// <summary>오른주먹 (차징 불꽃이 모이는 곳, 실제 자세).</summary>
    public Vector3 ChargeFistPoint
    {
        get { FindBones(); return FistPoint(rHand); }
    }

    /// <summary>상반신 구도 / 배경 블러 초점: 가슴과 얼굴 사이 (실제 자세).</summary>
    public Vector3 FocusPoint
    {
        get
        {
            FindBones();
            if (chestBone == null || headBone == null) return transform.position + Vector3.up * 1.3f;
            return Vector3.Lerp(chestBone.position, headBone.position, 0.55f);
        }
    }

    // ------------------------------------------------------------------ input + timeline

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (skill == null) skill = GetComponent<ProiThrow>();
        if (dash == null) dash = GetComponent<ProiDash>();
        if (!externalInput && kb != null && kb[key].wasPressedThisFrame && (combat == null || !combat.Busy) &&
            (skill == null || !skill.Busy) && (dash == null || !dash.Busy))
            TryBegin();

        if (!active) return;
        if (cinematicTime) Tick(Time.deltaTime);
        else
        {   // her own clock (and pose) runs at the warp; the world keeps the game speed
            float warp = localWarp;
            Tick(Time.deltaTime * warp);
            if (animator != null && !UsesTimeline) animator.speed = active ? localWarp : 1f;
        }
    }

    void LateUpdate()
    {   // after the animator: the charge heartbeat reads the posed fist
        if (viewCam == null) viewCam = Camera.main;
        UpdateCharge();
    }

    /// <summary>쿨다운이 끝났고 재생 중이 아니면 궁극기를 시작합니다.</summary>
    public bool TryBegin()
    {
        if (active || Time.time < readyAt || animator == null || !isActiveAndEnabled) return false;
        Begin();
        return true;
    }

    void Begin()
    {
        if (mover != null && mover.MoveInputWorld.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(mover.MoveInputWorld, Vector3.up);   // 방향키를 누르고 있으면 그쪽으로
        forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (UsesTimeline) SyncFromTimeline(cutscene.playableAsset as TimelineAsset);
        active = true; ending = false; time = 0f;
        clashed = charged = cameraReturned = slammed = wideShot = closeUp = pushedIn = pulledOut = diveShot = wideShotDone = jumpCutDone = false;
        dashDone = 0f; nextPulse = chargeStart + 0.35f; nextRumble = chargeStart; leapt = false;
        crackAngle = Random.Range(0f, 360f);
        rowsSpawned = 0;
        animator.speed = 1f;
        if (UsesTimeline)
        {   // the timeline poses her, moves the camera and sets the game speed; this component follows its clock
            cutscene.timeUpdateMode = DirectorUpdateMode.GameTime;
            cutscene.extrapolationMode = DirectorWrapMode.None;
            cutscene.time = 0.0;
            cutscene.Play();
            return;
        }
        animator.CrossFadeInFixedTime(stateName, blendIn, 0, 0f);
        ApplyTimeWarp();

        // a quick move to the upper-body front shot (the fists come together at chest height)
        FrontShot(openShot.x, openShot.y, openShot.z, frontFov, cameraBlendIn);
    }

    void FrontShot(float distance, float height, float lookHeight, float fov, float blend)
    {   // front shot, turned frontYaw to her right
        if (qcam == null) return;
        Vector3 dir = Quaternion.AngleAxis(frontYaw, Vector3.up) * forward;
        Vector3 camPos = transform.position + dir * distance + Vector3.up * height;
        Vector3 look = transform.position + Vector3.up * lookHeight;
        qcam.SetCinematic(camPos, Quaternion.LookRotation(look - camPos, Vector3.up), fov, blend);
    }

    void ApplyTimeWarp()
    {
        float warp = useTimeWarp && active ? WarpAt(time) : 1f;
        if (cinematicTime) ProiHitFeedback.BaseScale = warp;
        else localWarp = warp;
    }

    float WarpAt(float t)
    {
        if (timeWarp == null || timeWarp.Length == 0) return 1f;
        if (t <= timeWarp[0].x) return timeWarp[0].y;
        for (int i = 1; i < timeWarp.Length; i++)
            if (t <= timeWarp[i].x)
                return Mathf.Lerp(timeWarp[i - 1].y, timeWarp[i].y, Mathf.InverseLerp(timeWarp[i - 1].x, timeWarp[i].x, t));
        return timeWarp[timeWarp.Length - 1].y;
    }

    void Tick(float dt)
    {
        time += dt;
        bool code = !UsesTimeline;          // camera shots / speed / pose by code (else by the timeline tracks)
        if (code && !pushedIn && time >= clashPush.x) { pushedIn = true; ClashCloseUp(); }
        if (!clashed && time >= clashTime) { clashed = true; Clash(); }
        if (code && !pulledOut && time >= clashPullOut.x)
        {   // zoom back out to the full body for the lunge
            pulledOut = true;
            FrontShot(frontDistance, frontHeight, frontLookHeight, frontFov, clashPullOut.y);
        }
        if (code && !closeUp && time >= chargeStart + 0.3f) { closeUp = true; ChargeCloseUp(); }
        if (!charged && time >= chargeFull) { charged = true; ChargedPulse(); }
        Dash();
        if (!cameraReturned && time >= cameraBack) cameraReturned = true;
        if (code && !jumpCutDone && time >= jumpCut.x) { jumpCutDone = true; JumpShot(); }
        if (!leapt && time >= dashStart) { leapt = true; Leap(); }
        if (code && !diveShot && time >= apexTime) { diveShot = true; DiveShot(); }
        if (!slammed && time >= slamTime) { slammed = true; Slam(); }
        if (code && slammed && !wideShotDone && time >= slamTime + wideShotDelay) { wideShotDone = true; WideShot(); }
        while (slammed && rowsSpawned < 3 && time >= slamTime + rowDelays[rowsSpawned])
        {
            SpawnRow(rowsSpawned);
            rowsSpawned++;
        }
        if (wideShot && rowsSpawned >= 3 && time >= slamTime + rowDelays.z + pillarLife * 0.6f)
        {
            wideShot = false;
            if (qcam != null) qcam.ClearCinematic(0.8f);
        }
        if (!ending && time >= endTime)
        {
            ending = true;
            if (code) animator.CrossFadeInFixedTime(locomotionState, blendOut, 0);   // the timeline eases its clip out itself
        }
        bool shotsDone = code ? wideShotDone && !wideShot : cutscene.state != PlayState.Playing;
        if (ending && rowsSpawned >= 3 && shotsDone)
        {
            active = false;
            readyAt = Time.time + cooldown;
            if (!code && cinematicTime) ProiHitFeedback.BaseScale = 1f;
            localWarp = 1f;
            if (!cinematicTime && code) animator.speed = 1f;
        }
        if (code) ApplyTimeWarp();
    }

    void OnDisable()
    {
        if (cutscene != null && cutscene.state == PlayState.Playing) cutscene.Stop();
        if (qcam != null) qcam.ClearCinematic(0.2f);
        bool was = active;
        active = false;
        localWarp = 1f;
        if (was && cinematicTime) ProiHitFeedback.BaseScale = 1f;
        if (was && animator != null) animator.speed = 1f;
    }

    // ------------------------------------------------------------------ timeline

    /// <summary>Timeline 의 비트 마커 시간을 장면 시간으로 가져옵니다.</summary>
    public void SyncFromTimeline(TimelineAsset timeline)
    {
        if (timeline == null) return;
        float? slam = null, row1 = null, row2 = null, row3 = null;
        var tracks = new List<TrackAsset>(timeline.GetOutputTracks());
        if (timeline.markerTrack != null) tracks.Add(timeline.markerTrack);
        foreach (TrackAsset track in tracks)
            foreach (IMarker m in track.GetMarkers())
            {
                var b = m as ProiUltimateBeat;
                if (b == null) continue;
                float t = (float)b.time;
                switch (b.beat)
                {
                    case ProiBeat.Clash: clashTime = t; break;
                    case ProiBeat.ChargeStart: chargeStart = t; break;
                    case ProiBeat.ChargeFull: chargeFull = t; break;
                    case ProiBeat.ChargeEnd: cameraBack = t; break;
                    case ProiBeat.Leap: dashStart = t; break;
                    case ProiBeat.Apex: apexTime = t; break;
                    case ProiBeat.Dive: diveTime = t; break;
                    case ProiBeat.Slam: slamTime = t; slam = t; break;
                    case ProiBeat.Row1: row1 = t; break;
                    case ProiBeat.Row2: row2 = t; break;
                    case ProiBeat.Row3: row3 = t; break;
                    case ProiBeat.End: endTime = t; break;
                }
            }
        if (slam.HasValue)
        {
            if (row1.HasValue) rowDelays.x = Mathf.Max(row1.Value - slam.Value, 0f);
            if (row2.HasValue) rowDelays.y = Mathf.Max(row2.Value - slam.Value, rowDelays.x);
            if (row3.HasValue) rowDelays.z = Mathf.Max(row3.Value - slam.Value, rowDelays.y);
        }
    }

    /// <summary>카메라 샷 클립이 시작될 때 (Timeline 카메라 트랙이 부름): 그 구도로 컷 / 이동. 구도는 매 프레임 다시 계산해
    /// 캐릭터 뼈를 따라가고, 움직이는 샷은 클립 진행도(start ~ start + duration)에 맞춰 끝 값으로 이어서 움직입니다.</summary>
    public void PlayShot(ProiShot shot, double start, double duration)
    {
        if (qcam == null) return;
        if (shot.type == ProiShotType.Gameplay) { qcam.ClearCinematic(shot.blend); return; }
        qcam.SetCinematic((out Vector3 pos, out Quaternion rot, out float fov) =>
        {
            float k = cutscene != null && duration > 1e-4 ? Mathf.Clamp01((float)((cutscene.time - start) / duration)) : 1f;
            ShotPose(shot.At(k), out pos, out rot, out fov);
        }, shot.blend, shot.ease);
    }

    /// <summary>샷 구도 계산 (플레이 중에는 실제 도약 / 찍은 지점, 에디터 미리보기에서는 시작 위치에서 추정).</summary>
    public void ShotPose(ProiShot s, out Vector3 pos, out Quaternion rot, out float fov)
    {
        bool live = Application.isPlaying && active;
        Vector3 up = Vector3.up;
        Vector3 fwd = live ? forward : Flat(previewing ? previewForward : transform.forward);
        Vector3 here = live || !previewing ? transform.position : previewOrigin;
        Vector3 leap = live ? (leapt ? leapPoint : transform.position) : here;
        Vector3 land = leap + fwd * dashDistance;
        Vector3 slam = live && slammed ? slamPoint : land + fwd * 0.7f;
        Vector3 right = Vector3.Cross(up, fwd);
        Quaternion baseRot = qcam != null ? qcam.BaseRotation : ProiQuarterViewCamera.DefaultRotation;
        float baseFov = qcam != null ? qcam.BaseFieldOfView : ProiQuarterViewCamera.DefaultFieldOfView;
        Vector3 dir = Quaternion.AngleAxis(s.yaw, up) * fwd;
        Quaternion roll = Quaternion.Euler(0f, 0f, s.roll);
        Vector3 look;
        FindBones();
        switch (s.type)
        {
            case ProiShotType.Front:
                pos = here + dir * s.distance + up * s.height;
                look = here + up * s.lookHeight;
                break;
            case ProiShotType.Fists:        // the spot in front of the chest where the fists meet
                look = here + up * s.lookHeight + fwd * 0.22f;
                pos = look + dir * s.distance + up * (s.height - s.lookHeight);
                break;
            case ProiShotType.ChargeAngle:  // between the chest and the fist at the waist
                pos = here + dir * s.distance + up * s.height;
                look = here + up * s.lookHeight + right * 0.2f - fwd * 0.08f;
                break;
            case ProiShotType.FistCloseUp:  // telephoto on the burning right fist, wherever the pose puts it
                look = FistPoint(rHand);
                pos = look + dir * s.distance + up * s.height;
                break;
            case ProiShotType.UpperBody:    // chest to face, wherever the pose puts them (the lunge drops her a lot)
                look = FocusPoint + up * s.lookHeight;
                pos = look + dir * s.distance + up * s.height;
                break;
            case ProiShotType.ChargeFist:   // the gathering fire in the fist as the subject, her face over it
                look = Vector3.Lerp(ChargeFistPoint, FocusPoint, 0.35f) + up * s.lookHeight;
                pos = look + dir * s.distance + up * s.height;
                break;
            case ProiShotType.DiveFollow:   // riding behind her chest, looking down at where she will hit
                Vector3 chest = chestBone != null ? chestBone.position : here + up * 1.2f;
                pos = chest + Quaternion.AngleAxis(s.yaw, up) * -fwd * s.distance + up * s.height;
                look = slam + up * s.lookHeight;
                break;
            case ProiShotType.ArcWide:
                look = leap + fwd * (dashDistance * 0.5f + 0.4f) + up * 1.7f;
                rot = baseRot * roll; fov = baseFov + s.fovOffset;
                pos = look - rot * Vector3.forward * s.distance;
                return;
            case ProiShotType.Landing:
                look = land + fwd * 0.3f + up * 0.9f;
                rot = baseRot * roll; fov = baseFov + s.fovOffset;
                pos = look - rot * Vector3.forward * s.distance;
                return;
            case ProiShotType.PillarWide:
                look = live ? transform.position : land;
                for (int b = -1; b <= 1; b++)
                    for (int r = 0; r < 3; r++) look += PillarPosFrom(slam, fwd, b, r);
                look = look / 10f + up * rowHeights.z * 0.42f;
                rot = baseRot * roll; fov = baseFov + s.fovOffset;
                pos = look - rot * Vector3.forward * s.distance;
                return;
            default:
                ProiQuarterViewCamera.BasePose(qcam, transform.position, out pos, out rot, out fov);
                return;
        }
        rot = Quaternion.LookRotation(look - pos, up) * roll;
        fov = s.fov;
    }

    /// <summary>에디터에서 Timeline 재생바를 움직일 때: 시작 위치를 기억하고 마커 시간을 가져옴.</summary>
    public void BeginPreview(TimelineAsset timeline)
    {
        SyncFromTimeline(timeline);
        previewOrigin = transform.position;
        previewForward = transform.forward;
        previewing = true;
    }

    /// <summary>에디터 미리보기: t 초의 아치 점프 전진 위치로 옮김 (Timeline 이 끝나면 되돌림).</summary>
    public void PreviewAt(float t)
    {
        if (!previewing) return;
        transform.position = previewOrigin + Flat(previewForward) * dashDistance * ArcProgress(t);
    }

    static Vector3 Flat(Vector3 v)
    {
        v = Vector3.ProjectOnPlane(v, Vector3.up);
        return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
    }

    // ------------------------------------------------------------------ beats

    void ClashCloseUp()
    {   // crash zoom onto the spot in front of the chest where the fists are about to meet
        Vector3 dir = Quaternion.AngleAxis(frontYaw, Vector3.up) * forward;
        Vector3 meet = transform.position + Vector3.up * 1.33f + forward * 0.22f;
        Vector3 camPos = meet + dir * clashCloseUp.x + Vector3.up * (clashCloseUp.y - 1.33f);
        if (qcam != null) qcam.SetCinematic(camPos, Quaternion.LookRotation(meet - camPos, Vector3.up), clashCloseUp.z, clashPush.y);
    }

    void Clash()
    {   // the two fists meet in front of the chest: freeze, then slow-mo, a hard shake and a zoom punch
        Vector3 at = (FistPoint(lHand) + FistPoint(rHand)) * 0.5f + forward * 0.04f;
        OnClash?.Invoke(at);
        if (cinematicTime) ProiHitFeedback.HitStop(hitStop.x, 0.03f, clashSlow.x, clashSlow.y);
        if (qcam != null) qcam.Shake(shake.x, 0.35f, forward, clashZoom);
    }

    void ChargeCloseUp()
    {   // push in slowly toward the burning fist for the rest of the charge
        if (qcam == null) return;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 dir = Quaternion.AngleAxis(chargeCloseUpYaw, Vector3.up) * forward;
        Vector3 camPos = transform.position + dir * chargeCloseUp.x + Vector3.up * chargeCloseUp.y;
        Vector3 look = transform.position + Vector3.up * 1.1f + right * 0.2f - forward * 0.08f;  // between the chest and the fist at the waist
        qcam.SetCinematic(camPos, Quaternion.LookRotation(look - camPos, Vector3.up), frontFov - 2f, cameraBack - time - 0.05f);
    }

    void Dash()
    {   // the arch: a little forward while she shoots up, drifting at the apex, then most of the way in the dive,
        // accelerating all the way into the ground
        float d = dashDistance * ArcProgress(time);
        if (d > dashDone && controller != null && controller.enabled) controller.Move(forward * (d - dashDone));
        dashDone = d;
    }

    float ArcProgress(float t)
    {
        if (t <= dashStart) return 0f;
        if (t < apexTime)
        {
            float k = Mathf.InverseLerp(dashStart, apexTime, t);
            return arcSplit.x * (1f - (1f - k) * (1f - k));
        }
        if (t < diveTime) return Mathf.Lerp(arcSplit.x, arcSplit.y, Mathf.InverseLerp(apexTime, diveTime, t));
        return Mathf.Lerp(arcSplit.y, 1f, Mathf.Pow(Mathf.InverseLerp(diveTime, slamTime, t), diveEaseIn));
    }

    void DiveShot()
    {   // at the apex: the camera pulls in on the landing spot, same quarter-view angle, so the dive comes straight at it
        if (qcam == null || diveCamera.x <= 0f) return;
        Vector3 land = leapPoint + forward * dashDistance;
        Vector3 look = land + forward * 0.3f + Vector3.up * 0.9f;
        Quaternion rot = qcam.BaseRotation;
        qcam.SetCinematic(look - rot * Vector3.forward * diveCamera.x, rot, qcam.BaseFieldOfView - diveCamera.y, diveCamera.z);
    }

    void JumpShot()
    {   // back to the quarter-view angle, framing the take-off and the landing so the whole arch shows
        if (qcam == null) return;
        if (jumpCameraDistance <= 0f) { qcam.ClearCinematic(cameraBlendOut); return; }
        Vector3 mid = leapPoint + forward * (dashDistance * 0.5f + 0.4f) + Vector3.up * 1.7f;
        Quaternion rot = qcam.BaseRotation;
        qcam.SetCinematic(mid - rot * Vector3.forward * jumpCameraDistance, rot, qcam.BaseFieldOfView, jumpCut.y);
    }

    void Leap()
    {   // the ground bursts as she shoots up
        Vector3 at = transform.position;
        leapPoint = at - forward * dashDone;
        fxFadeAt = Time.time + (slamTime - dashStart) + rowDelays.z + pillarLife * 0.85f;   // reset again at the slam
        if (qcam != null) qcam.Shake(shake.z, 0.25f, Vector3.up, leapZoom);
        OnLeap?.Invoke(at);
    }

    void ChargedPulse()
    {   // full charge: a flare at the fist
        Vector3 at = FistPoint(rHand);
        if (qcam != null) qcam.Shake(chargedShake.x, 0.3f, forward, chargedShake.y);
        if (chargedHitStop > 0f && cinematicTime) ProiHitFeedback.HitStop(chargedHitStop);
        OnCharged?.Invoke(at);
        if (chargeCracks.x > 0f) ChargeCracks(6, chargeCracks.z * 1.25f, 1f);
    }

    void ChargeCracks(int count, float length, float level)
    {   // lava cracks splitting the ground round her feet, glowing until just after take-off
        if (count <= 0 || length <= 0f) return;
        fxFadeAt = Time.time + Mathf.Max(dashStart - time, 0f) + 0.3f;
        Vector3 feet = transform.position;
        for (int i = 0; i < count; i++)
        {
            crackAngle += count >= 6 ? 360f / count : 137.5f;           // spread round her (a full ring at full charge)
            Vector3 dir = Quaternion.AngleAxis(crackAngle + Random.Range(-12f, 12f), Vector3.up) * forward;
            float len = length * Random.Range(0.8f, 1.15f);
            List<Vector3> path = JaggedPath(feet + dir * 0.25f, feet + dir * (0.25f + len), Random.Range(0f, 100f), feet.y);
            OnCrack?.Invoke(path, crackWidth * Mathf.Lerp(0.45f, 0.8f, level), Time.time, 0.08f + 0.05f * len);
        }
    }

    void Slam()
    {   // the fist hits the ground
        Vector3 fist = FistPoint(rHand);
        slamPoint = new Vector3(fist.x, transform.position.y, fist.z);
        if (cinematicTime) ProiHitFeedback.HitStop(hitStop.y, 0.02f, slamSlow.x, slamSlow.y);
        if (qcam != null) qcam.Shake(shake.y, 0.45f, forward + Vector3.down, slamZoom);
        fxFadeAt = Time.time + rowDelays.z + pillarLife * 0.85f;
        OnSlam?.Invoke(slamPoint);
        if (centerJet.x > 0f) SpawnJet(slamPoint, centerJet.x, centerJet.y, 0f);
        BuildCracks();
    }

    void WideShot()
    {   // keep the quarter-view angle but frame her and all the pillars: look at the middle of them from closer in
        if (qcam == null || pillarCameraDistance <= 0f) return;
        Quaternion rot = qcam.BaseRotation;               // not the camera's own: it is mid-shake / zoom punch right now
        Vector3 mid = transform.position;
        for (int b = -1; b <= 1; b++)
            for (int r = 0; r < 3; r++) mid += PillarPos(b, r);
        mid = mid / 10f + Vector3.up * rowHeights.z * 0.42f;
        qcam.SetCinematic(mid - rot * Vector3.forward * pillarCameraDistance, rot, qcam.BaseFieldOfView, 0.6f);
        wideShot = true;
    }

    void SpawnRow(int row)
    {   // all three branches erupt together, the row further out bigger
        for (int b = -1; b <= 1; b++)
            SpawnJet(PillarPos(b, row), rowHeights[row] * Random.Range(0.92f, 1.08f), rowRadii[row], b == 0 ? 0.6f + 0.3f * row : 0f);
        if (row == 2 && cinematicTime) ProiHitFeedback.HitStop(0.05f);
    }

    void SpawnJet(Vector3 pos, float height, float radius, float shakeK)
    {   // a fire jet bursting out of the ground (drawn by the effects)
        Vector3 dir = pos - slamPoint;
        dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : forward;
        OnJet?.Invoke(pos, height, radius);
        if (qcam != null && shakeK > 0f) qcam.Shake(shake.z * shakeK, 0.22f, dir);
    }

    // ------------------------------------------------------------------ lava cracks

    void BuildCracks()
    {   // three cracks run out from the slam at once (left / middle / right), each reaching its next pillar as it erupts
        float y = transform.position.y;
        for (int b = -1; b <= 1; b++)
        {
            Vector3 from = slamPoint;
            float t0 = 0f;
            for (int r = 0; r < 3; r++)
            {
                Vector3 to = PillarPos(b, r);
                List<Vector3> seg = JaggedPath(from, to, Random.Range(0f, 100f), y);
                float reach = Mathf.Max(rowDelays[r] - t0 - 0.02f, 0.05f);
                OnCrack?.Invoke(seg, crackWidth * (1f - 0.12f * r), Time.time + t0, reach);

                // a little side crack cracking open as the main one passes
                int at = Random.Range(1, Mathf.Max(seg.Count - 2, 2));
                Vector3 along = (seg[Mathf.Min(at + 1, seg.Count - 1)] - seg[at - 1]).normalized;
                Vector3 dir = Quaternion.AngleAxis(Random.Range(35f, 65f) * (Random.value < 0.5f ? -1f : 1f), Vector3.up) * along;
                List<Vector3> twig = JaggedPath(seg[at], seg[at] + dir * Random.Range(0.3f, 0.6f), Random.Range(0f, 100f), y);
                OnCrack?.Invoke(twig, crackWidth * 0.5f, Time.time + t0 + reach * at / (float)(seg.Count - 1), 0.12f);

                from = to;
                t0 = rowDelays[r];
            }
        }
    }

    List<Vector3> JaggedPath(Vector3 a, Vector3 b, float seed, float y)
    {   // zig-zag across the line a -> b, pinned at both ends
        float len = Vector3.Distance(a, b);
        int n = Mathf.Max(2, Mathf.CeilToInt(len / 0.13f));
        Vector3 dir = (b - a) / Mathf.Max(len, 1e-4f);
        Vector3 side = Vector3.Cross(Vector3.up, dir);
        var pts = new List<Vector3>(n + 1);
        for (int j = 0; j <= n; j++)
        {
            float t = j / (float)n, pin = Mathf.Sin(t * Mathf.PI);
            float off = ((Mathf.PerlinNoise(seed, t * len * 2.2f) - 0.5f) * 2f + (Mathf.PerlinNoise(seed + 7.3f, t * len * 8f) - 0.5f)) * crackJag * pin;
            Vector3 q = Vector3.Lerp(a, b, t) + side * off;
            q.y = y + 0.03f;
            pts.Add(q);
        }
        return pts;
    }

    Vector3 PillarPos(int branch, int row) => PillarPosFrom(slamPoint, forward, branch, row);

    Vector3 PillarPosFrom(Vector3 slam, Vector3 fwd, int branch, int row)
    {   // branch -1 left / 0 middle / 1 right; row 0 nearest .. 2 furthest (the side branches spread out as they go)
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        Vector3 pos = branch == 0 ? slam + fwd * centerForward[row]
                                  : slam + fwd * sideForward[row] + right * (branch * sideOut[row]);
        pos.y = slam.y;
        return pos;
    }

    // ------------------------------------------------------------------ charge heartbeat on the right fist

    void UpdateCharge()
    {   // while charging (camera still close): the fist fire's heartbeat, a thump with every beat and the screen
        // trembling harder as it fills; through the crouch the ground keeps rumbling, strongest just before take-off
        if (!active || slammed || leapt || time < chargeStart) return;
        float level = Mathf.Clamp01((time - chargeStart) / Mathf.Max(chargeFull - chargeStart, 0.01f));
        level = 1f - (1f - level) * (1f - level);
        if (level <= 0.01f) return;
        if (!cameraReturned && time >= nextPulse)
        {
            nextPulse = time + chargePulseInterval * Mathf.Lerp(1.3f, 0.75f, level);
            OnChargePulse?.Invoke(FistPoint(rHand), level);
            ChargeCracks((int)chargeCracks.x, Mathf.Lerp(chargeCracks.y, chargeCracks.z, level), level);
            if (qcam != null) qcam.Shake(Mathf.Lerp(pulseShake.x, pulseShake.y, level), 0.18f, Random.onUnitSphere,
                                         Mathf.Lerp(pulseZoom.x, pulseZoom.y, level));
        }
        if (qcam != null && time >= nextRumble)
        {
            nextRumble = time + 0.07f;
            float amp = Mathf.Lerp(chargeRumble.x, chargeRumble.y, level);
            if (cameraReturned) amp = chargeRumble.y * Mathf.Lerp(1f, crouchRumble, Mathf.InverseLerp(cameraBack, dashStart, time));
            qcam.Shake(amp, 0.12f, Random.onUnitSphere);
        }
    }

    Vector3 FistPoint(Transform hand)
    {
        if (hand == null) return transform.position + Vector3.up;
        Transform f = hand == rHand ? rForearm : hand.parent;
        return f != null ? hand.position + (hand.position - f.position).normalized * 0.07f : hand.position;
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
}
