using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 대쉬 (Shift): 누르는 순간 이동 키 방향(없으면 바라보는 방향)으로 몸을 돌려 "슉" 하고 dashDistance 만큼 순식간에 지나갑니다.
/// 동작은 Assets/Characters/Proi/Animations/Dash.anim (Tools > Proi > Bake Skill Clips): 몸을 앞으로 숙이고 팔을 뒤로 뻗은 채 미끄러지다가
/// 앞발로 브레이크. 지나간 자리에는 불꽃 잔상(몸을 구운 실루엣이 화염처럼 타 사라짐) + 불꽃 / 불티.
/// 공격 / 궁극기 / 우클릭 기술 중에는 쓸 수 없고, 대쉬 중에는 그것들도 시작되지 않습니다.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class ProiDash : MonoBehaviour
{
    [Header("동작")]
    [SerializeField] Animator animator;
    [SerializeField] string stateName = "Dash";
    [SerializeField] string locomotionState = "Locomotion";
    [Tooltip("대쉬 자세로 바뀌는 시간 (초): 0 이면 즉시 - 첫 잔상부터 숙인 자세로 남음")]
    [SerializeField] float blendIn = 0f;
    [SerializeField] float blendOut = 0.15f;

    [Header("대쉬")]
    [Tooltip("지나가는 거리 (m)")]
    [SerializeField] float dashDistance = 4.5f;
    [Tooltip("이동하는 시간 (초): 처음에 가장 빠르고 끝에서 감속")]
    [SerializeField] float dashTime = 0.2f;
    [Tooltip("이 시점(초)부터 대기 / 이동 동작으로 돌아감 (Dash.anim 의 브레이크 자세)")]
    [SerializeField] float endTime = 0.3f;
    [Tooltip("다시 쓸 수 있을 때까지 (초, 끝난 뒤부터)")]
    [SerializeField] float cooldown = 0.35f;

    [Header("이펙트")]
    [Tooltip("잔상을 남기는 간격 (m): 지나간 길을 따라 고르게, 도착 지점 근처(ghostClear)에는 남기지 않음")]
    [SerializeField] float ghostSpacing = 0.75f;
    [SerializeField] float ghostClear = 0.9f;
    [Tooltip("잔상이 타서 사라지는 시간 (초)")]
    [SerializeField] float ghostLife = 0.32f;
    [SerializeField] float shake = 0.015f;

    // ------------------------------------------------------------------ state

    ProiCombat combat;
    ProiUltimate ultimate;
    ProiThrow skill;
    ProiPlayerController mover;
    CharacterController controller;
    ProiFireFx fx;
    ProiQuarterViewCamera qcam;
    SkinnedMeshRenderer[] bodies;
    Material ghostMat;
    MaterialPropertyBlock block;

    bool active, ending, braked;
    float time, done, nextGhost, readyAt;
    Vector3 dir, startPos;

    class Ghost
    {
        public GameObject go;
        public MeshRenderer[] renderers;
        public Mesh[] meshes;
        public float born;
        public bool alive;
    }
    readonly List<Ghost> ghosts = new List<Ghost>();

    /// <summary>대쉬 중이면 true — 이동 / 다른 공격 입력을 막습니다.</summary>
    public bool Busy => active && !ending;

    void Awake()
    {
        combat = GetComponent<ProiCombat>();
        ultimate = GetComponent<ProiUltimate>();
        skill = GetComponent<ProiThrow>();
        mover = GetComponent<ProiPlayerController>();
        controller = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        fx = GetComponent<ProiFireFx>();
        if (fx == null) fx = gameObject.AddComponent<ProiFireFx>();
    }

    void Start()
    {
        Shader ghostShader = Resources.Load<Shader>("ProiGhost");
        if (animator == null || ghostShader == null || !fx.Ready)
        {
            Debug.LogWarning("[ProiDash] missing animator / ghost shader / fire effects - dash disabled");
            enabled = false;
            return;
        }
        bodies = animator.GetComponentsInChildren<SkinnedMeshRenderer>();
        ghostMat = new Material(ghostShader) { name = "ProiDashGhost" };
        block = new MaterialPropertyBlock();
        qcam = Camera.main != null ? Camera.main.GetComponent<ProiQuarterViewCamera>() : null;
    }

    // ------------------------------------------------------------------ input + timeline

    void Update()
    {
        Keyboard kb = Keyboard.current;
        bool pressed = kb != null && (kb.leftShiftKey.wasPressedThisFrame || kb.rightShiftKey.wasPressedThisFrame);
        bool otherBusy = (combat != null && combat.Busy) || (ultimate != null && ultimate.Busy) || (skill != null && skill.Busy);
        if (pressed && !active && !otherBusy && Time.time >= readyAt) Begin();

        if (active) Tick();
        UpdateGhosts();
    }

    void Begin()
    {
        Vector3 input = mover != null ? mover.MoveInputWorld : Vector3.zero;
        dir = input.sqrMagnitude > 0.01f ? input.normalized : Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        active = true; ending = false; braked = false;
        time = 0f; done = 0f; nextGhost = 0f;
        startPos = transform.position;
        animator.speed = 1f;
        animator.CrossFadeInFixedTime(stateName, blendIn, 0, 0f);
        animator.Update(0f);                                // pose now, so the first afterimage is already leaning

        // the kick-off: a flare at her back, embers and flames sprayed behind
        Vector3 back = transform.position + Vector3.up * 0.9f - dir * 0.25f;
        fx.Flash(back, 0.9f);
        fx.Embers(back, 18, 1.5f, 4f, -dir * 3f + Vector3.up * 0.5f);
        for (int i = 0; i < 6; i++)
            fx.Flame(transform.position + Random.insideUnitSphere * 0.25f + Vector3.up * 0.15f,
                     -dir * Random.Range(1.5f, 3f) + Vector3.up * Random.Range(0.8f, 1.6f), fx.FlameSize * Random.Range(0.8f, 1.2f), Random.Range(0.25f, 0.35f));
        if (qcam != null) qcam.Shake(shake, 0.12f, dir);
    }

    void Tick()
    {
        time += Time.deltaTime;

        // "슉": nearly all the distance in the first instant, easing into the brake (cubic ease-out)
        float k = Mathf.Clamp01(time / Mathf.Max(dashTime, 0.01f));
        float d = dashDistance * (1f - (1f - k) * (1f - k) * (1f - k));
        if (d > done && controller.enabled) controller.Move(dir * (d - done));
        done = d;

        // afterimages every ghostSpacing m along the way (a fast frame drops several, each at its own spot on the path)
        float travelled = Vector3.ProjectOnPlane(transform.position - startPos, Vector3.up).magnitude;
        while (nextGhost <= travelled && nextGhost <= dashDistance - ghostClear)
        {
            SpawnGhost(transform.position - dir * (travelled - nextGhost));
            nextGhost += ghostSpacing;
        }
        if (time <= dashTime && Random.value < 60f * Time.deltaTime)
            fx.Flame(transform.position - dir * Random.Range(0.6f, 2f) + Random.insideUnitSphere * 0.2f + Vector3.up * Random.Range(0.1f, 1.2f),
                     -dir * 1.2f + Vector3.up * 1.2f, fx.FlameSize * Random.Range(0.7f, 1.1f), Random.Range(0.22f, 0.32f));

        if (!braked && time >= dashTime + 0.04f)
        {   // braking: a puff of embers at the feet
            braked = true;
            fx.Embers(transform.position + Vector3.up * 0.15f + dir * 0.2f, 10, 0.8f, 2.2f, dir * 1.5f + Vector3.up * 0.8f);
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

    void OnDisable()
    {
        active = false;
    }

    // ------------------------------------------------------------------ afterimages

    void SpawnGhost(Vector3 at)
    {
        Ghost g = ghosts.Find(x => !x.alive);
        if (g == null)
        {
            g = new Ghost { go = new GameObject("ProiDashGhost") };
            g.renderers = new MeshRenderer[bodies.Length];
            g.meshes = new Mesh[bodies.Length];
            for (int i = 0; i < bodies.Length; i++)
            {
                var part = new GameObject(bodies[i].name);
                part.transform.SetParent(g.go.transform, false);
                g.meshes[i] = new Mesh { name = "ProiDashGhost" };
                part.AddComponent<MeshFilter>().sharedMesh = g.meshes[i];
                var mr = part.AddComponent<MeshRenderer>();
                var mats = new Material[bodies[i].sharedMesh.subMeshCount];
                for (int m = 0; m < mats.Length; m++) mats[m] = ghostMat;
                mr.sharedMaterials = mats;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                g.renderers[i] = mr;
            }
            ghosts.Add(g);
        }
        Vector3 shift = at - transform.position;
        for (int i = 0; i < bodies.Length; i++)
        {
            bodies[i].BakeMesh(g.meshes[i], true);
            g.renderers[i].transform.SetPositionAndRotation(bodies[i].transform.position + shift, bodies[i].transform.rotation);
        }
        g.go.SetActive(true);
        g.born = Time.time;
        g.alive = true;
        SetGhost(g, 1f, 0f);
    }

    void UpdateGhosts()
    {
        foreach (Ghost g in ghosts)
        {
            if (!g.alive) continue;
            float u = (Time.time - g.born) / Mathf.Max(ghostLife, 0.01f);
            if (u >= 1f)
            {
                g.alive = false;
                g.go.SetActive(false);
                continue;
            }
            SetGhost(g, 1f - u * u, u);
        }
    }

    void SetGhost(Ghost g, float alpha, float burn)
    {
        block.SetFloat("_Alpha", alpha);
        block.SetFloat("_Burn", burn);
        foreach (MeshRenderer r in g.renderers) r.SetPropertyBlock(block);
    }

    void OnDestroy()
    {
        foreach (Ghost g in ghosts)
        {
            foreach (Mesh m in g.meshes) Destroy(m);
            Destroy(g.go);
        }
        if (ghostMat != null) Destroy(ghostMat);
    }
}
