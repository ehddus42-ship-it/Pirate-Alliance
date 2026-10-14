using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 임시 적: 허수아비 (Tools > Proi > Create Scarecrow 로 씬에 배치). 발밑이 원점, 가슴 높이를 잡힘 / 타격 기준점으로 씁니다.
///  - Idle: 제자리에 서 있음 (맞으면 흔들림)
///  - Held: 잡힌 동안 손을 따라감 (Hold 를 매 프레임 호출)
///  - Flying: 던져져 포물선으로 날아가며 회전, 땅에 닿으면 Landed 이벤트 후 누워서 Down
///  - Down: respawnDelay 초 뒤 원래 자리에서 다시 일어남
/// </summary>
public class ProiScarecrow : MonoBehaviour
{
    public enum State { Idle, Held, Flying, Down }

    public static readonly List<ProiScarecrow> All = new List<ProiScarecrow>();

    [Tooltip("발밑에서 가슴(잡는 곳)까지 높이 (m)")]
    [SerializeField] float chestHeight = 1.2f;
    [Tooltip("쓰러진 뒤 다시 일어날 때까지 (초)")]
    [SerializeField] float respawnDelay = 1.8f;
    [SerializeField] float gravity = 16f;

    public State Current { get; private set; }
    public float ChestHeight => chestHeight;
    /// <summary>가슴(잡는 곳) 위치.</summary>
    public Vector3 Chest => transform.position + transform.up * chestHeight;
    /// <summary>몸 중심 (회전과 상관없이 착지 판정에 씀).</summary>
    public Vector3 Center => transform.position + transform.up * 0.95f;
    /// <summary>땅에 떨어진 순간 (떨어진 곳).</summary>
    public event System.Action<ProiScarecrow, Vector3> Landed;

    Vector3 home, velocity, spinAxis, wobbleDir;
    Quaternion homeRot;
    Collider body;
    float stateTime, spinSpeed, wobble, groundY;

    void Awake()
    {
        home = transform.position;
        homeRot = transform.rotation;
        groundY = home.y;
        body = GetComponent<Collider>();
    }

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    /// <summary>잡힐 예정: 플레이어가 다가올 수 있게 충돌을 끔.</summary>
    public void Reserve()
    {
        if (body != null) body.enabled = false;
    }

    public void Grab()
    {
        Current = State.Held;
        stateTime = 0f;
        if (body != null) body.enabled = false;
    }

    /// <summary>잡힌 동안: 가슴이 손에 오도록, 몸은 up 방향으로 세우고 face 쪽을 바라봄. blend 0..1 로 부드럽게 옮겨 감.</summary>
    public void Hold(Vector3 hand, Vector3 up, Vector3 face, float blend)
    {
        Quaternion rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(face, up).normalized, up);
        Vector3 pos = hand - up * chestHeight;
        pos.y = Mathf.Max(pos.y, groundY + 0.05f);      // never dragged through the ground when held low
        transform.SetPositionAndRotation(Vector3.Lerp(transform.position, pos, blend), Quaternion.Slerp(transform.rotation, rot, blend));
    }

    public void Throw(Vector3 vel, Vector3 axis, float spinDegPerSec)
    {
        Current = State.Flying;
        stateTime = 0f;
        velocity = vel;
        spinAxis = axis.normalized;
        spinSpeed = spinDegPerSec;
    }

    /// <summary>맞음: dir 쪽으로 크게 흔들림.</summary>
    public void Hit(Vector3 dir)
    {
        if (Current != State.Idle) return;
        wobble = 1f;
        wobbleDir = Vector3.ProjectOnPlane(dir, Vector3.up).normalized;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        stateTime += dt;
        switch (Current)
        {
            case State.Idle:
                if (wobble > 0f)
                {   // a damped rocking after a hit
                    wobble = Mathf.MoveTowards(wobble, 0f, dt * 1.2f);
                    float a = Mathf.Sin(stateTime * 14f) * 22f * wobble;
                    Vector3 axis = Vector3.Cross(Vector3.up, wobbleDir);
                    transform.rotation = Quaternion.AngleAxis(a, axis.sqrMagnitude > 1e-4f ? axis : Vector3.right) * homeRot;
                }
                break;

            case State.Flying:
                velocity += Vector3.down * gravity * dt;
                transform.position += velocity * dt;
                transform.rotation = Quaternion.AngleAxis(spinSpeed * dt, spinAxis) * transform.rotation;
                if (velocity.y < 0f && Center.y <= groundY + 0.3f) Land();
                break;

            case State.Down:
                if (stateTime >= respawnDelay) Respawn();
                break;
        }
    }

    void Land()
    {   // lies on its back, head pointing the way it flew
        Vector3 flat = Vector3.ProjectOnPlane(velocity, Vector3.up);
        flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : transform.forward;
        Vector3 at = new Vector3(Center.x, groundY, Center.z);
        transform.rotation = Quaternion.LookRotation(Vector3.up, flat);
        transform.position = at - flat * 0.95f + Vector3.up * 0.12f;
        Current = State.Down;
        stateTime = 0f;
        Landed?.Invoke(this, at);
    }

    void Respawn()
    {
        transform.SetPositionAndRotation(home, homeRot);
        Current = State.Idle;
        stateTime = 0f;
        wobble = 0.6f;
        wobbleDir = transform.forward;
        if (body != null) body.enabled = true;
    }
}
