using UnityEngine;

/// <summary>
/// 일상 프로이 손: 서 있을 때는 손가락을 자연스럽게 살짝 구부리고, 달리면 주먹을 쥡니다.
/// "Fist_L" / "Fist_R" 블렌드셰이프(전투 프로이의 주먹 모양)를 Animator 의 Speed 로 블렌드합니다.
/// </summary>
public class ProiGeneralHands : MonoBehaviour
{
    static readonly int SpeedHash = Animator.StringToHash("Speed");

    [SerializeField] SkinnedMeshRenderer body;
    [Tooltip("서 있을 때 손가락 구부림 (0 = 쫙 편 손, 100 = 주먹)")]
    [Range(0f, 100f)] [SerializeField] float relaxed = 85f;
    [Range(0f, 100f)] [SerializeField] float running = 100f;
    [Tooltip("Speed 가 이 값 이상이면 완전히 주먹")]
    [SerializeField] float fullFistSpeed = 0.35f;

    Animator animator;
    int left = -1, right = -1;

    void Awake() => Init();

    void Init()
    {
        animator = GetComponent<Animator>();
        if (body == null)
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.sharedMesh != null && smr.sharedMesh.GetBlendShapeIndex("Fist_L") >= 0) { body = smr; break; }
        if (body != null && body.sharedMesh != null)
        {
            left = body.sharedMesh.GetBlendShapeIndex("Fist_L");
            right = body.sharedMesh.GetBlendShapeIndex("Fist_R");
        }
    }

    void LateUpdate() => Apply();

    /// <summary>현재 Speed 로 손 모양을 적용합니다. (에디터 미리보기에서도 호출)</summary>
    public void Apply()
    {
        if (body == null) Init();
        float speed = animator != null ? animator.GetFloat(SpeedHash) : 0f;
        float w = Mathf.Lerp(relaxed, running, Mathf.SmoothStep(0f, 1f, speed / Mathf.Max(0.01f, fullFistSpeed)));
        if (left >= 0) body.SetBlendShapeWeight(left, w);
        if (right >= 0) body.SetBlendShapeWeight(right, w);
    }
}
