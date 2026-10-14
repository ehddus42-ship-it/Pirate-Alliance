using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 트윈테일 흔들림.
///  1) 목표 흔들림: 가만히 있으면 천천히 살랑, 달리면 걸음 박자에 맞춰 좌우로 흔들리고 속도만큼 뒤로 날림
///  2) 스프링: 목표를 관성으로 부드럽게 따라감 (갑자기 멈추거나 방향을 바꾸면 출렁임)
/// Animator 다음(LateUpdate)에 동작합니다.
/// </summary>
public class ProiTailSpring : MonoBehaviour
{
    [SerializeField] string[] chainRoots = { "LeftTail1", "RightTail1" };

    [Header("Sway (목표 흔들림)")]
    [Tooltip("대기 중 좌우 살랑임 (도, 꼬리 끝 기준)")]
    [SerializeField] float idleSway = 5f;
    [Tooltip("달릴 때 좌우 흔들림 (도, 꼬리 끝 기준)")]
    [SerializeField] float runSway = 10f;
    [Tooltip("달릴 때 뒤로 날리는 각도 (도, 꼬리 끝 기준)")]
    [SerializeField] float runLift = 10f;
    [SerializeField] float idleFrequency = 0.45f;
    [Tooltip("달릴 때 흔들림 주기 (Hz). Run 클립의 한 주기(두 걸음) ≈ 1.5 Hz")]
    [SerializeField] float runFrequency = 1.5f;
    [Tooltip("달리기 최고 속도 (m/s) — 이 속도에서 달리기 흔들림이 100%")]
    [SerializeField] float fullSpeed = 3.2f;

    [Header("Spring (관성)")]
    [Range(0.01f, 1f)] [SerializeField] float stiffness = 0.2f;
    [Range(0f, 1f)] [SerializeField] float damping = 0.3f;
    [Tooltip("캐릭터 이동을 관성으로 얼마나 느낄지 (0 = 이동 무시, 1 = 완전한 월드 관성)")]
    [Range(0f, 1f)] [SerializeField] float inertia = 0.12f;
    [Tooltip("스프링이 목표에서 벗어날 수 있는 최대 각도 (도, 본마다·누적 안 됨)")]
    [SerializeField] float lagLimit = 12f;

    class Node
    {
        public Transform bone;
        public Quaternion restLocal;
        public Vector3 axisLocal;
        public float length, weight;
        public Vector3 tip, prevTip, animDir;
    }

    readonly List<List<Node>> chains = new List<List<Node>>();
    readonly List<float> chainSide = new List<float>();
    Vector3 lastRootPos;
    float phase, speed01;

    void Start()
    {
        foreach (string rootName in chainRoots)
        {
            Transform t = FindDeep(transform, rootName);
            var chain = new List<Node>();
            Vector3 lastAxis = Vector3.down; float lastLen = 0.2f;
            while (t != null)
            {
                Transform child = t.childCount > 0 ? t.GetChild(0) : null;
                var n = new Node { bone = t, restLocal = t.localRotation };
                if (child != null)
                {
                    n.axisLocal = child.localPosition.normalized;
                    n.length = Vector3.Distance(child.position, t.position);
                }
                else
                {
                    n.axisLocal = lastAxis; n.length = lastLen;
                }
                lastAxis = n.axisLocal; lastLen = n.length;
                n.tip = n.prevTip = t.position + t.TransformDirection(n.axisLocal) * n.length;
                chain.Add(n);
                t = child;
            }
            for (int i = 0; i < chain.Count; i++) chain[i].weight = (i + 1f) / chain.Count;   // root moves least
            if (chain.Count > 0)
            {
                chains.Add(chain);
                chainSide.Add(rootName.StartsWith("Left") ? 1f : -1f);
            }
        }
        lastRootPos = transform.position;
    }

    void LateUpdate()
    {
        float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
        if (dt <= 0f) return;
        float k = 1f - Mathf.Pow(1f - stiffness, dt * 60f);

        Vector3 rootDelta = transform.position - lastRootPos;
        lastRootPos = transform.position;
        Vector3 carry = rootDelta * (1f - inertia);
        float speed = new Vector3(rootDelta.x, 0f, rootDelta.z).magnitude / dt;
        speed01 = Mathf.MoveTowards(speed01, Mathf.Clamp01(speed / fullSpeed), dt * 3f);

        float freq = Mathf.Lerp(idleFrequency, runFrequency, speed01);
        phase = Mathf.Repeat(phase + freq * dt, 1f);
        float wave = Mathf.Sin(phase * 2f * Mathf.PI);
        float sway = Mathf.Lerp(idleSway, runSway, speed01) * wave;
        float lift = runLift * speed01;

        Vector3 fwd = transform.forward, right = transform.right;
        for (int c = 0; c < chains.Count; c++)
        {
            var chain = chains[c];
            float side = chainSide[c];
            foreach (var n in chain) n.bone.localRotation = n.restLocal;
            foreach (var n in chain) n.animDir = n.bone.TransformDirection(n.axisLocal);

            foreach (var n in chain)
            {
                // target: animated direction + sway (tails open/close as a pair) + backward lift while running
                Quaternion target = Quaternion.AngleAxis(sway * side * n.weight, fwd) *
                                    Quaternion.AngleAxis(lift * n.weight, right);    // + about right = tip swings backward (-forward)
                Vector3 targetDir = target * n.animDir;

                n.tip += carry; n.prevTip += carry;
                Vector3 head = n.bone.position;
                Vector3 curDir = n.bone.TransformDirection(n.axisLocal);
                Vector3 targetTip = head + targetDir * n.length;

                Vector3 next = n.tip + (n.tip - n.prevTip) * (1f - damping);
                next += (targetTip - next) * k;
                Vector3 dir = Vector3.RotateTowards(targetDir, next - head, lagLimit * Mathf.Deg2Rad, 0f).normalized;
                next = head + dir * n.length;

                n.prevTip = n.tip; n.tip = next;
                n.bone.rotation = Quaternion.FromToRotation(curDir, dir) * n.bone.rotation;
            }
        }
    }

    static Transform FindDeep(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        foreach (Transform c in parent)
        {
            Transform f = FindDeep(c, name);
            if (f != null) return f;
        }
        return null;
    }
}
