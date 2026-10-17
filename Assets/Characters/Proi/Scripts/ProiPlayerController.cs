using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Proi 이동: WASD (카메라 기준). 입력 방향으로 몸을 돌리고 Idle ↔ Run 을 속도로 블렌드합니다.
/// 공격 중(ProiCombat.Busy) / 궁극기 중(ProiUltimate.Busy) / 우클릭 기술 중(ProiThrow.Busy) / 대쉬 중(ProiDash.Busy)에는 이동 입력을 받지 않습니다.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class ProiPlayerController : MonoBehaviour
{
    static readonly int SpeedHash = Animator.StringToHash("Speed");

    [Header("References")]
    [SerializeField] Animator animator;
    [SerializeField] Transform cameraTransform;

    [Header("Movement")]
    [SerializeField] float moveSpeed = 3.2f;
    [Tooltip("초당 가속 (moveSpeed 의 배수)")]
    [SerializeField] float acceleration = 10f;
    [SerializeField] float turnSpeed = 720f;
    [SerializeField] float gravity = -20f;

    CharacterController controller;
    ProiCombat combat;
    ProiUltimate ultimate;
    ProiThrow skill;
    ProiDash dash;
    Vector3 planarVelocity;
    float verticalVelocity;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        combat = GetComponent<ProiCombat>();
        if (combat == null) combat = gameObject.AddComponent<ProiCombat>();   // 마우스 좌클릭 3타 콤보
        if (GetComponent<ProiBlazeCombo>() == null) gameObject.AddComponent<ProiBlazeCombo>();   // 콤보 Blender 화염 VFX
        ultimate = GetComponent<ProiUltimate>();
        if (ultimate == null) ultimate = gameObject.AddComponent<ProiUltimate>();              // R 궁극기
        if (GetComponent<ProiBlazeUltimate>() == null) gameObject.AddComponent<ProiBlazeUltimate>(); // 궁극기 Blender 화염 VFX
        skill = GetComponent<ProiThrow>();
        if (skill == null) skill = gameObject.AddComponent<ProiThrow>();                       // 우클릭 잡아 던지기 / 화염구
        if (GetComponent<ProiBlazeThrow>() == null) gameObject.AddComponent<ProiBlazeThrow>(); // 우클릭 기술 Blender 화염 VFX
        dash = GetComponent<ProiDash>();
        if (dash == null) dash = gameObject.AddComponent<ProiDash>();                          // Shift 대쉬
        if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;

        // quarter-view camera
        if (cameraTransform != null && cameraTransform.GetComponent<ProiQuarterViewCamera>() == null)
            cameraTransform.gameObject.AddComponent<ProiQuarterViewCamera>().Configure(transform);

        // fists (blendshapes) if the scene was set up before ProiFist existed
        if (animator != null && animator.GetComponent<ProiFist>() == null) animator.gameObject.AddComponent<ProiFist>();

        // post-processing (bloom / colour) on the game camera
        if (cameraTransform != null && cameraTransform.GetComponent<Camera>() != null && cameraTransform.GetComponent<ProiPostFX>() == null)
            cameraTransform.gameObject.AddComponent<ProiPostFX>();

        // CharacterController rests skinWidth above the ground: lower the model by that gap so the soles touch the floor
        if (animator != null && animator.transform != transform)
        {
            float capsuleBottom = controller.center.y - controller.height * 0.5f;
            Vector3 p = animator.transform.localPosition;
            animator.transform.localPosition = new Vector3(p.x, capsuleBottom - controller.skinWidth, p.z);
        }
    }

    /// <summary>카메라 기준 이동 입력 방향 (월드, 크기 0~1). 공격을 시작할 때 바라볼 방향으로 씁니다.</summary>
    public Vector3 MoveInputWorld { get; private set; }

    void Update()
    {
        MoveInputWorld = ToWorldDirection(ReadMoveInput());
        bool attacking = (combat != null && combat.Busy) || (ultimate != null && ultimate.Busy) || (skill != null && skill.Busy) ||
                         (dash != null && dash.Busy);
        Vector3 move = attacking ? Vector3.zero : MoveInputWorld;
        if (attacking) planarVelocity = Vector3.zero;      // 공격은 제자리에서 시작 (이동량은 ProiCombat 이 줌)
        planarVelocity = Vector3.MoveTowards(planarVelocity, move * moveSpeed, acceleration * moveSpeed * Time.deltaTime);

        if (move.sqrMagnitude > 0.0001f)
        {
            Quaternion target = Quaternion.LookRotation(move, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
        }

        if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
        verticalVelocity += gravity * Time.deltaTime;
        controller.Move((planarVelocity + Vector3.up * verticalVelocity) * Time.deltaTime);

        if (animator != null)
            animator.SetFloat(SpeedHash, planarVelocity.magnitude / moveSpeed, 0.08f, Time.deltaTime);
    }

    static Vector2 ReadMoveInput()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return Vector2.zero;

        Vector2 v = Vector2.zero;
        if (kb.wKey.isPressed) v.y += 1f;
        if (kb.sKey.isPressed) v.y -= 1f;
        if (kb.dKey.isPressed) v.x += 1f;
        if (kb.aKey.isPressed) v.x -= 1f;
        return Vector2.ClampMagnitude(v, 1f);
    }

    Vector3 ToWorldDirection(Vector2 input)
    {
        if (input == Vector2.zero) return Vector3.zero;

        Vector3 forward = Vector3.forward;
        Vector3 right = Vector3.right;
        if (cameraTransform != null)
        {
            forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
        }
        return Vector3.ClampMagnitude(forward * input.y + right * input.x, 1f);
    }
}
