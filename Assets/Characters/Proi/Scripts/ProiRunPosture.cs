using UnityEngine;

/// <summary>
/// 이동(Locomotion) 자세 보정. Animator 다음(LateUpdate)에 동작합니다.
/// Run 클립을 재 보면 (Blender, Proi_Character.fbx)
///  - 엉덩이부터 머리까지 한 덩어리로 약 18° 앞으로 기울어 있을 뿐 허리 / 가슴은 펴져 있고 머리도 같이 숙여져 있으며,
///  - 골반 / 어깨가 걸음과 거꾸로 돕니다: 왼발이 앞으로 나갈 때 골반은 오른쪽이 약 10° 앞으로, 어깨는 왼쪽이 약 14° 앞으로
///    (사람은 왼발이 나가면 왼쪽 골반이 앞으로, 반대인 오른쪽 어깨가 앞으로 옴),
///  - 좌우 흔들림 / 숙임 같은 걸음 리듬이 없습니다.
/// 그래서
///  1) 굽힘: 허리(Spine) / 가슴(Chest)을 앞으로 더 굽히고, 목 / 머리는 들어 시선은 앞으로
///  2) 비틀림 바로잡기: 클립의 거꾸로 된 골반 / 어깨 흔들림을 지우고, 앞으로 나간 발 쪽 골반이 앞으로 / 반대쪽 어깨가 앞으로
///     오게 돌림 (다리는 원래 방향을 유지해 발이 미끄러지지 않고, 머리는 정면을 봄)
///  3) 걸음 리듬: 딛고 있는 발 쪽으로 살짝 기울고, 두 발이 엇갈리는(몸이 가장 낮은) 순간 허리가 조금 더 숙여짐
/// 비틀림 바로잡기는 움직이는 동안 항상, 굽힘 / 흔들림 크기는 걷기 70% → 달리기 100% 로 걸리고,
/// 공격 / 대쉬 / 궁극기처럼 Locomotion 이 아닌 동작에서는 부드럽게 꺼집니다.
/// 위상은 클립 시간이 아니라 실제 발 위치에서 읽어 블렌드 / 재생 속도가 바뀌어도 맞습니다.
/// PlayerMotor 의 조준 방향 상체 회전(LateUpdate)은 이 보정 위에 그대로 더해집니다.
/// </summary>
[DefaultExecutionOrder(-50)]
public class ProiRunPosture : MonoBehaviour
{
    [Header("굽힘 (도, 최고 속도 기준)")]
    [SerializeField] float spineBend = 7f;
    [SerializeField] float chestBend = 5f;
    [Tooltip("목 / 머리를 드는 양 — 클립의 숙임 + 위 굽힘을 상쇄해 시선을 앞으로")]
    [SerializeField] float headLift = 18f;

    [Header("비틀림 (도, 최고 속도 기준)")]
    [Tooltip("골반 회전: 앞으로 나간 발 쪽 골반이 앞으로")]
    [SerializeField] float pelvisTurn = 6f;
    [Tooltip("어깨 회전: 앞으로 나간 발 반대쪽 어깨가 앞으로")]
    [SerializeField] float shoulderTurn = 8f;
    [Tooltip("클립의 거꾸로 된 골반 / 어깨 흔들림을 지우는 정도")]
    [Range(0f, 1f)] [SerializeField] float replaceClipTwist = 1f;

    [Header("걸음 리듬 (도, 최고 속도 기준)")]
    [Tooltip("딛고 있는 발 쪽으로 기울기")]
    [SerializeField] float sideLean = 3f;
    [Tooltip("두 발이 엇갈릴 때 허리가 더 숙여지는 양")]
    [SerializeField] float stepDip = 2.5f;

    [Header("적용")]
    [Tooltip("이 속도(m/s)에서 100% (PlayerMotor.moveSpeed)")]
    [SerializeField] float fullSpeed = 4.2f;
    [SerializeField] string locomotionState = "Locomotion";
    [Tooltip("다른 동작으로 바뀔 때 켜고 끄는 시간 (초)")]
    [SerializeField] float fade = 0.15f;

    Animator animator;
    Transform hips, spine, chest, neck, head, leftFoot, rightFoot, leftHip, rightHip, leftArm, rightArm;
    int locomotionHash;
    Vector3 lastPos, facingLocal;
    float speed, weight, stride, lean, dip, meanPelvis, meanShoulder;
    bool ready;

    void Start()
    {
        animator = GetComponentInChildren<Animator>();
        Transform root = animator != null ? animator.transform : transform;
        hips = FindDeep(root, "Hips");
        spine = FindDeep(root, "Spine");
        chest = FindDeep(root, "Chest");
        neck = FindDeep(root, "Neck");
        head = FindDeep(root, "Head");
        leftFoot = FindDeep(root, "LeftFoot");
        rightFoot = FindDeep(root, "RightFoot");
        leftHip = FindDeep(root, "LeftUpperLeg");
        rightHip = FindDeep(root, "RightUpperLeg");
        leftArm = FindDeep(root, "LeftUpperArm");
        rightArm = FindDeep(root, "RightUpperArm");
        ready = animator != null && hips != null && spine != null && chest != null && neck != null && head != null && leftFoot != null &&
                rightFoot != null && leftHip != null && rightHip != null && leftArm != null && rightArm != null;
        if (!ready) { Debug.LogWarning("[ProiRunPosture] bones not found - disabled"); enabled = false; return; }
        locomotionHash = Animator.StringToHash(locomotionState);
        stride = Vector3.Distance(leftHip.position, leftFoot.position);   // leg length: the feet this far apart = a full step
        // where the model faces in this object's space (Unity: forward = right x up), from the rest pose
        facingLocal = transform.InverseTransformDirection(Vector3.Cross(Flat(rightHip.position - leftHip.position), Vector3.up));
        lastPos = transform.position;
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (!ready || dt <= 0f) return;

        // planar speed of the character, smoothed (works the same for the game motor and the playground controller)
        Vector3 move = Flat(transform.position - lastPos);
        lastPos = transform.position;
        speed = Mathf.Lerp(speed, move.magnitude / dt, 1f - Mathf.Exp(-dt * 8f));

        bool locomotion = animator.GetCurrentAnimatorStateInfo(0).shortNameHash == locomotionHash &&
                          (!animator.IsInTransition(0) || animator.GetNextAnimatorStateInfo(0).shortNameHash == locomotionHash);
        weight = Mathf.MoveTowards(weight, locomotion ? 1f : 0f, dt / Mathf.Max(fade, 0.01f));
        // moving at all (the clip's reversed twist is wrong at any speed) / how fast (walk -> run: bigger bend and swing)
        float moving = weight * Mathf.Clamp01(speed / 1f);
        float pace = Mathf.Clamp01(speed / Mathf.Max(fullSpeed, 0.1f));
        float amount = moving * Mathf.Lerp(0.7f, 1f, pace);

        // a steady body frame from where she faces (the hip line itself swings with the clip)
        Vector3 up = Vector3.up;
        Vector3 forward = Flat(transform.TransformDirection(facingLocal)).normalized;
        Vector3 right = Vector3.Cross(up, forward);

        // the clip's pelvis / shoulder yaw, as swings around their average (Unity SignedAngle about up: + = right side back)
        float pelvisYaw = Vector3.SignedAngle(right, Flat(rightHip.position - leftHip.position), up);
        float shoulderYaw = Vector3.SignedAngle(right, Flat(rightArm.position - leftArm.position), up);
        float m = 1f - Mathf.Exp(-dt / 0.6f);
        meanPelvis = Mathf.LerpAngle(meanPelvis, pelvisYaw, m);
        meanShoulder = Mathf.LerpAngle(meanShoulder, shoulderYaw, m);
        if (moving <= 0.001f) { lean = dip = 0f; return; }
        // (clamped: turning from the guard stance into the run is a real turn, not a swing to undo)
        float pelvisSwing = Mathf.Clamp(Mathf.DeltaAngle(meanPelvis, pelvisYaw), -20f, 20f);
        float shoulderSwing = Mathf.Clamp(Mathf.DeltaAngle(meanShoulder, shoulderYaw), -25f, 25f);

        // step phase from the feet: +1 left foot ahead, -1 right foot ahead; stance = the lower foot
        float ahead = Mathf.Clamp(Vector3.Dot(leftFoot.position - rightFoot.position, forward) / stride, -1f, 1f);
        float stance = Mathf.Clamp((rightFoot.position.y - leftFoot.position.y) / (stride * 0.2f), -1f, 1f);   // +1 standing on the left
        float k = 1f - Mathf.Exp(-dt * 14f);
        lean = Mathf.Lerp(lean, stance, k);
        dip = Mathf.Lerp(dip, 1f - Mathf.Abs(ahead), k);

        // 2) twist: the pelvis turns with the leading leg (left foot ahead -> right side back = +), the shoulders against it
        float r = replaceClipTwist * moving;
        float pelvisFix = amount * pelvisTurn * ahead - r * pelvisSwing;
        float shoulderGoal = (1f - r) * shoulderSwing - amount * shoulderTurn * ahead;
        float chestFix = shoulderGoal - shoulderSwing - pelvisFix;          // on top of the pelvis turn, which carries the spine
        float headFix = -shoulderGoal;                                       // the head keeps looking ahead
        hips.rotation = Quaternion.AngleAxis(pelvisFix, up) * hips.rotation;
        leftHip.rotation = Quaternion.AngleAxis(-pelvisFix, up) * leftHip.rotation;     // the legs keep the clip's direction
        rightHip.rotation = Quaternion.AngleAxis(-pelvisFix, up) * rightHip.rotation;

        // 1) bend + 3) rhythm. Unity: a positive angle about `right` tips the top forward, about `forward` leans it left
        float spinePitch = spineBend * amount + stepDip * dip * moving * pace;
        spine.rotation = Quaternion.AngleAxis(spinePitch, right) * Quaternion.AngleAxis(sideLean * lean * moving * pace, forward) *
                         Quaternion.AngleAxis(chestFix * 0.5f, up) * spine.rotation;
        chest.rotation = Quaternion.AngleAxis(chestBend * amount, right) * Quaternion.AngleAxis(chestFix * 0.5f, up) * chest.rotation;
        // head back up: most in the neck, the rest in the head
        float lift = headLift * amount;
        neck.rotation = Quaternion.AngleAxis(-lift * 0.6f, right) * Quaternion.AngleAxis(headFix * 0.5f, up) * neck.rotation;
        head.rotation = Quaternion.AngleAxis(-lift * 0.4f, right) * Quaternion.AngleAxis(headFix * 0.5f, up) * head.rotation;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t)
        {
            Transform r = FindDeep(c, name);
            if (r != null) return r;
        }
        return null;
    }
}
