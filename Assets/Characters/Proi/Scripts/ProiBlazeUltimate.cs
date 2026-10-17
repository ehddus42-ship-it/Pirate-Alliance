using System.Collections.Generic;
using UnityEngine;
using static ProiBlazeKit;

/// <summary>
/// 궁극기(ProiUltimate)의 Blender 화염 VFX (D:/Blender_Test/VFX_Scripts/build_proi_ultimate_fire.py, 렌더
/// Proi_Ultimate_FireVFX.mp4 를 게임으로 옮긴 것). 콤보 / 우클릭 기술과 같은 계열 (부품은 ProiBlazeKit).
///  - 두 주먹 마주침: 두 주먹에 화염이 붙고(팔뚝까지) 가운데서 섬광 + 충격파 고리.
///  - 차징: 오른주먹 화염이 맥박마다 커지며 번쩍이고, 주먹을 도는 고리 2개, 팔뚝을 감는 나선, 빨려 드는 불씨,
///    발밑에서 피어오르는 불꽃, 맥박마다 바닥에 퍼지는 고리.
///  - 도약: 땅이 폭발하며 솟고, 몸 전체가 혜성처럼 불타며 오른주먹이 지나간 길에 화염 띠가 남음.
///    빠르게 내리꽂는 동안에는 몸을 따라 스피드라인이 뻗음.
///  - 땅 찍기: 가장 큰 폭발. 세 갈래 용암 균열(ProiUltimate 가 만든 경로 그대로)이 달려가 한동안 빛나다 타 사라지고,
///    균열이 닿는 곳마다 불기둥이 땅에서 터지듯 솟구침(순식간에 치솟아 넘치고, 화염 고리가 연달아 터지며 기둥을 타고 퍼짐).
/// 동작 / 카메라 연출 / 히트스톱 / 흔들림은 ProiUltimate 가 그대로 하고, 이 컴포넌트는 그 이벤트와 상태만 따라 그립니다.
/// </summary>
[DefaultExecutionOrder(100)]          // after ProiUltimate / the animator have posed her
public class ProiBlazeUltimate : MonoBehaviour
{
    [Header("크기 / 밝기")]
    [SerializeField] float fistScale = 1.45f;
    [Tooltip("도약 중 몸을 감싸는 혜성 화염 크기")]
    [SerializeField] float bodyFireScale = 2.6f;
    [Tooltip("내려꽂는 동안 몸을 따라 뻗는 스피드라인 크기 / 켜지기 시작하는 속도 ~ 다 켜지는 속도 (m/s)")]
    [SerializeField] Vector3 diveLines = new Vector3(3.2f, 7f, 13f);
    [Tooltip("폭발 크기: 도약 / 땅 찍기")]
    [SerializeField] Vector2 blastScale = new Vector2(1.6f, 2.8f);
    [Tooltip("불기둥: 폭발 크기 = 반지름 x 이 값")]
    [SerializeField] float jetBlastPerRadius = 3.8f;
    [Tooltip("불기둥 높이 배율 (ProiUltimate 의 높이 대비)")]
    [SerializeField] float jetHeightScale = 1.1f;
    [SerializeField] float brightness = 1f;
    [Header("빛")]
    [SerializeField] Color lightColor = new Color(1f, 0.42f, 0.1f);
    [SerializeField] float fistLight = 1.2f;
    [SerializeField] float blastLight = 8f;

    class Fist
    {
        public Transform hand, forearm, anchor, sleeveAnchor;
        public Flame[] wrap;
        public Flame sleeve;
        public ParticleSystem embers;
        public Light light;
        public Vector3 last, dir;
        public float burn, stretch;
        public Vector3 Point => hand.position + (hand.position - forearm.position).normalized * 0.07f;
    }

    class Crack
    {
        public Ribbon a, b;
        public float start, reach;
        public bool active;
    }

    ProiUltimate ult;
    Animator animator;
    ProiBlazeKit kit;
    Transform chest;
    Fist right, left;

    // charge
    Transform orbit, spiral, suckAnchor;
    Transform[] orbitRings;
    Material[] orbitMats;
    Material spiralMat;
    ParticleSystem suck;
    Transform[] feetFires;
    Flame[] feetFlames;
    Vector3 feet;
    Impact[] pulseFist, pulseGround;
    int nextPulse;
    readonly List<float> pulseTimes = new List<float>();
    readonly List<float> pulseLevels = new List<float>();
    Impact clash, charged;

    // dive
    Transform bodyAnchor;
    Flame[] bodyWrap;
    ParticleSystem bodyEmbers, bodyTrail;
    Vector3 bodyLast, bodyDir;
    float bodyStretch, bodyBurn;
    Ribbon[] dive;
    Transform linesAnchor, lines;
    Material linesMat;
    float linesFade;
    readonly List<float> diveTimes = new List<float>();
    readonly List<Vector3> divePts = new List<Vector3>();

    // slam
    Blast[] blasts;
    int nextBlast;
    BlastStyle leapStyle, slamStyle, jetStyle;
    readonly List<Crack> cracks = new List<Crack>();

    float clashT = -99f, leapT = -99f, slamT = -99f, fadeAt = -99f;
    bool wasActive;

    /// <summary>에셋을 모두 찾았으면 true.</summary>
    public bool Ready { get; private set; }

    void Awake()
    {
        ult = GetComponent<ProiUltimate>();
        kit = ult != null ? ProiBlazeKit.Create("ProiBlazeUltimateFX", brightness, nameof(ProiBlazeUltimate)) : null;
        Ready = kit != null;
        if (!Ready) { enabled = false; return; }
        ult.OnClash += HandleClash;
        ult.OnChargePulse += HandlePulse;
        ult.OnCharged += HandleCharged;
        ult.OnLeap += HandleLeap;
        ult.OnSlam += HandleSlam;
        ult.OnJet += HandleJet;
        ult.OnCrack += HandleCrack;
    }

    void Start()
    {
        animator = GetComponentInChildren<Animator>();
        if (animator == null) { enabled = false; return; }
        Transform root = animator.transform;
        chest = FindDeep(root, "UpperChest") ?? FindDeep(root, "Chest");
        right = MakeFist("R", FindDeep(root, "RightHand"), FindDeep(root, "RightLowerArm"), 113f);
        left = MakeFist("L", FindDeep(root, "LeftHand"), FindDeep(root, "LeftLowerArm"), 127f);

        orbit = kit.Anchor("BlazeUltOrbit");
        orbitMats = new[] { kit.FxMaterial("BlazeUltOrbit0", "_MODE_RING", Row.Body, 5f), kit.FxMaterial("BlazeUltOrbit1", "_MODE_RING", Row.Core, 5f) };
        orbitRings = new[] { kit.Part("Orbit0", "BZ_Ring", orbitMats[0], orbit), kit.Part("Orbit1", "BZ_Ring", orbitMats[1], orbit) };
        spiral = kit.MakeSpiral("Ult", right.anchor, 113f, out spiralMat);
        suck = kit.MakeEmbers("BlazeUltSuck", new Color(1f, 0.35f, 0.05f), 0.75f);
        var sm = suck.main;                                      // embers sucked in from all round: start on a shell, fly inward
        sm.startSpeed = -1.7f;
        sm.startLifetime = 13f * F;
        sm.gravityModifier = 0f;
        var sh = suck.shape;
        sh.radiusThickness = 0f;
        var sn = suck.noise;
        sn.enabled = false;
        feetFires = new Transform[6];
        feetFlames = new Flame[6];
        for (int k = 0; k < 6; k++)
        {
            feetFires[k] = kit.Anchor("BlazeUltFeet" + k);
            feetFlames[k] = kit.MakeFlame("BZ_FlameBody", feetFires[k], Row.Body, new Vector3(5, 10, 1), new Vector2(1.0f, 0.25f),
                                          0.95f, 0.6f, 2.5f, 141f + k * 5f, 0.13f, 0.3f, 0.3f, 1f);
            feetFires[k].gameObject.SetActive(false);
        }
        pulseFist = new[] { kit.MakeImpact("UltPulseA", 0.5f, 0, 1.6f), kit.MakeImpact("UltPulseB", 0.5f, 0, 1.6f) };
        pulseGround = new[] { kit.MakeImpact("UltPulseGroundA", 1.5f, 0, 2.5f), kit.MakeImpact("UltPulseGroundB", 1.5f, 0, 2.5f) };
        clash = kit.MakeImpact("UltClash", 1.3f, 3, 2.5f);
        charged = kit.MakeImpact("UltCharged", 0.7f, 0, 2f);

        bodyAnchor = kit.Anchor("BlazeUltBody", bodyFireScale);
        bodyWrap = kit.MakeWrap(bodyAnchor, 151f);
        bodyAnchor.gameObject.SetActive(false);
        bodyEmbers = kit.MakeEmbers("BlazeUltBodyEmbers", new Color(1f, 0.3f, 0.04f), 0.15f);
        bodyTrail = kit.MakeTrailSparks("BlazeUltBodyTrail", 260f);
        linesAnchor = kit.Anchor("BlazeUltDiveLines", diveLines.x);
        lines = kit.MakeLines("UltDive", linesAnchor, out linesMat);
        dive = new Ribbon[4];
        for (int k = 0; k < 4; k++) dive[k] = kit.MakeRibbon("UltDive" + k, Row.Body, 3f, 171f + k * 9f, new Vector2(0f, 0.85f));

        blasts = new Blast[13];
        for (int i = 0; i < blasts.Length; i++) blasts[i] = kit.MakeBlast("Ult" + i, i < 2 ? 5 : 2, i < 2 ? 6 : 3);
        leapStyle = new BlastStyle { light = blastLight, fires = 3, pillar = 1.5f, scorchTime = 3.5f };
        slamStyle = new BlastStyle { light = blastLight * 1.2f, fires = 5, pillar = 1.7f, hold = 7f, die = 26f, rings = 2,
                                     ringSpread = 1.4f, erupt = 0.6f, scorchTime = 3.5f };
        // the pillars burst out of the ground: up in a frame, overshooting, fire rings popping one after another up the column
        jetStyle = new BlastStyle { light = blastLight * 0.6f, fires = 2, sparks = 220f, hold = 14f, die = 36f, scorch = 0.9f,
                                    scorchTime = 3f, rings = 4, ringGap = 2f, ringLife = 10f, ringSpread = 1.5f, ringRise = 0.85f,
                                    erupt = 1f };
    }

    // 헌터 교체로 Proi 가 빠지면 (멈춘 채로 남지 않게) 화염도 같이 숨깁니다.
    void OnEnable()
    {
        if (kit != null && kit.Root != null) kit.Root.gameObject.SetActive(true);
    }

    void OnDisable()
    {
        if (kit != null && kit.Root != null) kit.Root.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (ult != null)
        {
            ult.OnClash -= HandleClash;
            ult.OnChargePulse -= HandlePulse;
            ult.OnCharged -= HandleCharged;
            ult.OnLeap -= HandleLeap;
            ult.OnSlam -= HandleSlam;
            ult.OnJet -= HandleJet;
            ult.OnCrack -= HandleCrack;
        }
        kit?.Dispose();
    }

    // ================================================================ events from ProiUltimate

    void HandleClash(Vector3 at)
    {
        float t = Time.time;
        clashT = t; leapT = -99f; slamT = -99f; fadeAt = -99f;
        pulseTimes.Clear(); pulseLevels.Clear();
        feet = transform.position;
        Vector3 dir = Facing(at);
        CameraTilted(at, dir, out Vector3 na, out Vector3 nb);
        kit.TriggerImpact(clash, t, at, dir, na, nb, 140, blastLight * 0.5f);
    }

    void HandlePulse(Vector3 fist, float level)
    {
        float t = Time.time;
        pulseTimes.Add(t); pulseLevels.Add(level);
        Impact a = pulseFist[nextPulse], g = pulseGround[nextPulse];
        nextPulse = (nextPulse + 1) % pulseFist.Length;
        a.follow.localScale = Vector3.one * (0.25f + 0.25f * level); a.scale = 0.25f + 0.25f * level;
        Vector3 dir = Facing(fist);
        CameraTilted(fist, dir, out Vector3 na, out Vector3 nb);
        kit.TriggerImpact(a, t, fist, dir, na, nb, 0, blastLight * 0.2f);
        g.follow.localScale = Vector3.one * (0.9f + 1.2f * level); g.scale = 0.9f + 1.2f * level;
        Vector3 nb2 = Quaternion.AngleAxis(9f, Vector3.Cross(Vector3.up, ult.Forward)) * Vector3.up;
        kit.TriggerImpact(g, t, feet + Vector3.up * 0.05f, Vector3.up, Vector3.up, nb2, 0, 0f);
    }

    void HandleCharged(Vector3 fist)
    {
        Vector3 dir = Facing(fist);
        CameraTilted(fist, dir, out Vector3 na, out Vector3 nb);
        kit.TriggerImpact(charged, Time.time, fist, dir, na, nb, 40, blastLight * 0.4f);
    }

    void HandleLeap(Vector3 at)
    {
        leapT = Time.time;
        diveTimes.Clear(); divePts.Clear();
        Explode(at, at.y, blastScale.x, leapStyle);
    }

    void HandleSlam(Vector3 at)
    {
        slamT = Time.time;
        fadeAt = ult.FxFadeAt;
        Explode(at, at.y, blastScale.y, slamStyle);
    }

    void HandleJet(Vector3 pos, float height, float radius)
    {
        if (slamT > 0f && Time.time - slamT < 0.02f && Vector3.Distance(pos, blasts[(nextBlast + blasts.Length - 1) % blasts.Length].groundPos) < 0.05f)
            return;                               // the centre jet at the slam point is the slam blast itself
        float s = radius * jetBlastPerRadius;
        var style = new BlastStyle
        {
            light = jetStyle.light, fires = jetStyle.fires, sparks = jetStyle.sparks, hold = jetStyle.hold, die = jetStyle.die,
            scorch = jetStyle.scorch, scorchTime = jetStyle.scorchTime, rings = jetStyle.rings, ringGap = jetStyle.ringGap,
            ringLife = jetStyle.ringLife, ringSpread = jetStyle.ringSpread, ringRise = jetStyle.ringRise, erupt = jetStyle.erupt,
            pillar = height * jetHeightScale / 1.15f / s,
        };
        Explode(pos, pos.y, s, style);
    }

    void HandleCrack(List<Vector3> pts, float width, float start, float reach)
    {
        Crack c = cracks.Find(x => !x.active);
        if (c == null)
        {
            int i = cracks.Count;
            c = new Crack
            {
                a = kit.MakeRibbon("UltCrack" + i + "a", Row.Body, 3.2f, 200f + i * 3f, new Vector2(0f, 0.8f)),
                b = kit.MakeRibbon("UltCrack" + i + "b", Row.Body, 3.2f, 201f + i * 3f, new Vector2(0f, 0.8f)),
            };
            cracks.Add(c);
        }
        c.active = true;
        c.start = start;
        c.reach = Mathf.Max(reach, F);
        int n = pts.Count;
        var outer = new Vector3[n];
        var innerA = new Vector3[n];
        var innerB = new Vector3[n];
        var us = new float[n];
        for (int j = 0; j < n; j++)
        {   // a glowing lava seam: two half-ribbons, hottest on the seam, wider near the slam
            Vector3 d = pts[Mathf.Min(j + 1, n - 1)] - pts[Mathf.Max(j - 1, 0)];
            Vector3 side = Vector3.Cross(Vector3.up, d).normalized;
            float w = width * Mathf.Lerp(1.15f, 0.7f, j / (float)(n - 1)) * 0.75f;
            outer[j] = pts[j];
            innerA[j] = pts[j] + side * w;
            innerB[j] = pts[j] - side * w;
            us[j] = j / (float)(n - 1);
        }
        BuildRibbon(c.a.mesh, outer, innerA, us);
        BuildRibbon(c.b.mesh, outer, innerB, us);
        Row row = width < 0.2f ? Row.Deep : Row.Body;
        foreach (Ribbon r in new[] { c.a, c.b })
        {
            r.mat.SetFloat(IdRow, (int)row);
            r.mat.SetFloat(IdHold, Mathf.Max(fadeAt - start, F) / c.reach);   // glow until the ultimate's fade time
            r.mat.SetFloat(IdLife, 10f * F / c.reach);                        // then burn away from the slam outward
            r.mat.SetFloat(IdHead, -1f);
        }
    }

    void Explode(Vector3 at, float groundY, float scale, BlastStyle style)
    {
        Blast b = blasts[nextBlast];
        nextBlast = (nextBlast + 1) % blasts.Length;
        kit.TriggerBlast(b, Time.time, at, groundY, scale, ult.Forward, style);
    }

    Vector3 Facing(Vector3 at)
    {
        Camera cam = Camera.main;
        return cam != null ? (cam.transform.position - at).normalized : -ult.Forward;
    }

    // ================================================================ per frame

    void LateUpdate()
    {
        float t = Time.time;
        float dt = Mathf.Max(Time.deltaTime, 1e-5f);
        float f30 = dt * 30f;
        Vector3 up = Vector3.up;
        Vector3 fwd = ult.Forward.sqrMagnitude > 0.5f ? ult.Forward : transform.forward;
        bool active = ult.Active;
        if (active && !wasActive) { clashT = -99f; leapT = -99f; slamT = -99f; }
        wasActive = active;
        float st = ult.SkillTime;
        bool charging = active && clashT > 0f && st >= ult.ChargeStart && leapT < 0f;
        float level = charging || (active && leapT > 0f && slamT < 0f)
            ? 1f - Mathf.Pow(1f - Seg(st, ult.ChargeStart, ult.ChargeFull), 2f) : 0f;
        float kick = 0f;
        for (int i = 0; i < pulseTimes.Count; i++)
            kick += pulseLevels[i] * (1f - Seg(t - pulseTimes[i], 0f, 5f * F)) * (t >= pulseTimes[i] ? 1f : 0f);

        // ---- fists: both lit at the clash; the left goes out after take-off, the right carries the dive and the slam
        bool rLit = active && clashT > 0f && (slamT < 0f || t - slamT < 12f * F);
        bool lLit = active && clashT > 0f && leapT < 0f;
        float slamBoost = slamT > 0f ? 1f - Seg(t - slamT, 0f, 8f * F) : 0f;
        float roarR = Mathf.Clamp01(level * 1.1f + kick * 0.6f + (slamT > 0f ? 1f - Smooth(Seg(t - slamT, 4f * F, 14f * F)) : 0f));
        UpdateFist(right, rLit, roarR, slamBoost, t, dt, f30, up, fwd);
        UpdateFist(left, lLit, clashT > 0f ? 1f - Smooth(Seg(t - clashT, 4f * F, 14f * F)) : 0f,
                   clashT > 0f ? 1f - Seg(t - clashT, 0f, 8f * F) : 0f, t, dt, f30, up, fwd);
        right.light.intensity = right.burn > 0.001f ? (fistLight * (1f + 3f * level + 2f * kick)) * brightness : 0f;
        right.light.enabled = right.light.intensity > 0.01f;

        // ---- charge: orbiting rings, the spiral wound on the forearm, embers sucked in, fire round the feet
        Vector3 fistR = right.Point;
        orbit.position = fistR;
        for (int k = 0; k < orbitRings.Length; k++)
        {
            orbitRings[k].gameObject.SetActive(charging);
            if (!charging) continue;
            float f = t * 30f;
            orbitRings[k].localRotation = Quaternion.Euler((0.11f * f * (k == 1 ? 1f : -1f) + k) * Mathf.Rad2Deg, (0.7f + 0.09f * f) * Mathf.Rad2Deg, 0.3f * k * Mathf.Rad2Deg);
            orbitRings[k].localScale = Vector3.one * (k == 0 ? 0.15f : 0.115f) * (0.6f + 0.6f * level) * (1f + 0.25f * kick) * fistScale;
            orbitMats[k].SetFloat(IdFade, 0.85f * EaseOut(Seg(st, ult.ChargeStart, ult.ChargeStart + 8f * F)));
        }
        bool spiralOn = active && clashT > 0f && st >= ult.ChargeStart && (slamT < 0f || t - slamT < 9f * F);
        spiral.gameObject.SetActive(spiralOn && right.anchor.gameObject.activeSelf);
        if (spiralOn)
        {
            spiralMat.SetFloat(IdHead, Mathf.Lerp(0f, 1.15f, EaseOut(Seg(st, ult.ChargeStart, ult.ChargeStart + 20f * F), 2f)));
            spiralMat.SetFloat(IdTail, slamT < 0f ? -0.3f : Mathf.Lerp(-0.3f, 1.05f, Mathf.Pow(Seg(t - slamT, 0f, 8f * F), 1.2f)));
            spiral.localRotation = Quaternion.AngleAxis(-0.32f * Mathf.Rad2Deg * t * 30f, AxisOS);
        }
        suck.transform.position = fistR;
        var se = suck.emission;
        se.rateOverTime = charging && st < ult.DashStart - 4f * F ? 220f * (0.4f + 0.6f * level) : 0f;
        for (int k = 0; k < feetFires.Length; k++)
        {   // fire licking up round her feet: rises with the charge, dies down after take-off
            float h0 = ult.ChargeStart + k * 2f * F;
            float upK = active && clashT > 0f ? EaseOut(Seg(st, h0, h0 + 12f * F)) : 0f;
            float down = leapT > 0f ? EaseIn(Seg(t - leapT, 0f, 22f * F), 1.6f) : 0f;
            bool on = upK > 0.001f && down < 0.999f;
            feetFires[k].gameObject.SetActive(on);
            if (!on) continue;
            float a = k * Mathf.PI / 3f + 0.3f;
            feetFires[k].SetPositionAndRotation(feet + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.5f + up * 0.02f,
                                                Look(Vector3.down, Vector3.forward, Vector3.right));
            feetFires[k].localScale = Vector3.one * (0.55f + 0.1f * (k % 2));
            float pk = 0.36f + 0.9f;
            feetFlames[k].mat.SetFloat(IdIntensity, -0.9f + pk * upK - pk * down + 0.04f * Flick(t, 61f + k));
            feetFlames[k].smr.SetBlendShapeWeight(0, upK * 60f);
        }
        for (int i = 0; i < pulseFist.Length; i++)
        {
            kit.UpdateImpact(pulseFist[i], t, pulseFist[i].hitPos);
            kit.UpdateImpact(pulseGround[i], t, pulseGround[i].hitPos);
        }
        kit.UpdateImpact(clash, t, clash.hitPos);
        kit.UpdateImpact(charged, t, charged.hitPos);

        // ---- the dive: she burns like a comet, a streak left behind the burning fist
        bool diving = active && leapT > 0f && (slamT < 0f || t - slamT < 7f * F);
        Vector3 c = chest.position;
        Vector3 v = (c - bodyLast) / dt;
        bodyLast = c;
        float spd = v.magnitude / 30f;
        Vector3 rest = (-up - fwd * 0.25f).normalized;
        Vector3 tgt = spd > 1e-5f ? Vector3.Lerp(rest, v.normalized, Smooth(Seg(spd, 0.012f, 0.07f))).normalized : rest;
        bodyDir = bodyDir == Vector3.zero ? tgt : Vector3.Lerp(bodyDir, tgt, 1f - Mathf.Pow(0.5f, f30)).normalized;
        bodyStretch = Mathf.Max(Smooth(Seg(spd, 0.04f, 0.35f)), bodyStretch * Mathf.Pow(0.78f, f30));
        float bodyTarget = diving && (slamT < 0f || t - slamT < 2f * F) ? 1f : 0f;
        bodyBurn = Mathf.MoveTowards(bodyBurn, bodyTarget, dt / (bodyTarget > bodyBurn ? 0.23f : 0.15f));
        bool bodyOn = bodyBurn > 0.001f;
        bodyAnchor.gameObject.SetActive(bodyOn);
        if (bodyOn)
        {
            bodyAnchor.SetPositionAndRotation(c, Look(bodyDir, up, fwd));
            SetWrap(bodyWrap, bodyBurn, slamT > 0f ? 1f : 0f, 0f, bodyStretch, Flick(t, 9f));
        }
        bodyEmbers.transform.position = c;
        var be = bodyEmbers.emission;
        be.rateOverTime = 220f * bodyBurn;
        bodyTrail.transform.position = fistR;
        bool flying = diving && slamT < 0f;
        if (flying && !bodyTrail.isPlaying) bodyTrail.Play();
        if (!flying && bodyTrail.isPlaying) bodyTrail.Stop();
        UpdateDive(t, flying ? fistR : (Vector3?)null);

        // ---- speed lines streaming off her while she dives fast (fade out fast once she hits)
        float mps = v.magnitude;
        linesFade = Mathf.Max(flying ? Smooth(Seg(mps, diveLines.y, diveLines.z)) : 0f, linesFade - dt / 0.1f);
        bool linesOn = linesFade > 0.001f;
        lines.gameObject.SetActive(linesOn);
        if (linesOn)
        {
            if (flying && mps > 1f) linesAnchor.SetPositionAndRotation(c, Look(v / mps, up, fwd));
            else linesAnchor.position = c;
            linesMat.SetFloat(IdFade, linesFade);
        }

        // ---- slam: blasts / pillars, lava cracks
        foreach (Blast b in blasts) kit.UpdateBlast(b, t);
        foreach (Crack k in cracks)
        {
            if (!k.active) continue;
            float head = (t - k.start) / k.reach;
            bool on = t >= k.start - F && (fadeAt < 0f || t < fadeAt + k.reach + 0.6f);
            if (!on && t > k.start) k.active = false;
            k.a.go.SetActive(on);
            k.b.go.SetActive(on);
            k.a.mat.SetFloat(IdHead, head);
            k.b.mat.SetFloat(IdHead, head);
        }
    }

    void UpdateFist(Fist f, bool lit, float roar, float boost, float t, float dt, float f30, Vector3 up, Vector3 fwd)
    {
        f.burn = Mathf.MoveTowards(f.burn, lit ? 1f : 0f, dt / (lit ? 0.25f : 0.6f));
        Vector3 pos = f.Point;
        float spd = (pos - f.last).magnitude / dt / 30f;
        f.last = pos;
        f.stretch = Mathf.Max(Smooth(Seg(spd, 0.04f, 0.35f)), f.stretch * Mathf.Pow(0.78f, f30));
        Vector3 armDir = ((pos - f.forearm.position).normalized - up * 0.2f).normalized;
        f.dir = f.dir == Vector3.zero ? armDir : Vector3.Lerp(f.dir, armDir, 1f - Mathf.Pow(0.25f, f30)).normalized;
        bool on = f.burn > 0.001f;
        f.anchor.gameObject.SetActive(on);
        f.sleeveAnchor.gameObject.SetActive(on);
        float flick = Flick(t, f.wrap[0].strength);
        if (on)
        {
            f.anchor.SetPositionAndRotation(pos, Look(f.dir, up, fwd));
            SetWrap(f.wrap, f.burn, roar, boost, f.stretch, flick);
            Vector3 along = (f.hand.position - f.forearm.position).normalized;
            f.sleeveAnchor.SetPositionAndRotation(f.hand.position - along * 0.01f, Look(along, up, fwd));
            Flame s = f.sleeve;
            s.mat.SetFloat(IdIntensity, Mathf.Lerp(-0.8f, s.baseI, EaseOut(f.burn)) + (s.roarI - s.baseI) * roar + 0.04f * flick);
            s.mat.SetFloat(IdStrength, s.strength * (1f + 0.5f * boost));
            s.smr.SetBlendShapeWeight(0, Mathf.Clamp01(0.25f + 0.75f * f.stretch + 0.15f * flick) * 100f);
        }
        f.embers.transform.position = pos;
        var em = f.embers.emission;
        em.rateOverTime = 150f * f.burn;
        f.light.transform.position = pos;
        if (f != right)
        {
            f.light.intensity = fistLight * EaseOut(f.burn) * (1f + 0.25f * flick) * brightness;
            f.light.enabled = f.light.intensity > 0.01f;
        }
    }

    void UpdateDive(float t, Vector3? pos)
    {   // cross-shaped streak along the burning fist's flight, revealed at its head, burnt away from the take-off end
        float span = Mathf.Max(ult.SlamTime - ult.DashStart, 0.2f);
        if (pos.HasValue && (diveTimes.Count == 0 || t - diveTimes[diveTimes.Count - 1] > 1f / 120f))
        {
            diveTimes.Add(t);
            divePts.Add(pos.Value);
            int n = divePts.Count;
            if (n >= 2)
            {
                var us = new float[n];
                for (int i = 0; i < n; i++) us[i] = (diveTimes[i] - leapT) / span;
                for (int k = 0; k < 4; k++)
                {
                    var inner = new Vector3[n];
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 tangent = (divePts[Mathf.Min(i + 1, n - 1)] - divePts[Mathf.Max(i - 1, 0)]).normalized;
                        Vector3 side = Vector3.Cross(tangent, Vector3.up).normalized;
                        Vector3 upv = Vector3.Cross(side, tangent).normalized;
                        float w = 0.35f * (0.35f + 0.65f * Mathf.Sqrt(i / (float)(n - 1)));
                        inner[i] = divePts[i] + (k < 2 ? upv : side) * w * (k % 2 == 0 ? 1f : -1f);
                    }
                    BuildRibbon(dive[k].mesh, divePts, inner, us);
                }
            }
        }
        float head = leapT > 0f ? (t - leapT) / span : -1f;
        float lastU = diveTimes.Count > 0 ? (diveTimes[diveTimes.Count - 1] - leapT) / span : 0f;
        bool show = leapT > 0f && diveTimes.Count >= 2 && head < lastU + 10f * F / span + 0.1f;
        foreach (Ribbon r in dive)
        {
            r.go.SetActive(show);
            r.mat.SetFloat(IdHead, head);
            r.mat.SetFloat(IdLife, 10f * F / span);
        }
    }

    // ================================================================ building

    Fist MakeFist(string name, Transform hand, Transform forearm, float seed)
    {
        var f = new Fist { hand = hand, forearm = forearm };
        f.last = f.Point;
        f.anchor = kit.Anchor("BlazeUltFist" + name, name == "R" ? fistScale : fistScale * 0.9f);
        f.wrap = kit.MakeWrap(f.anchor, seed);
        f.sleeveAnchor = kit.Anchor("BlazeUltSleeve" + name, 1.35f);
        f.sleeve = kit.MakeSleeve(f.sleeveAnchor, false, seed);
        f.embers = kit.MakeEmbers("BlazeUltEmbers" + name, new Color(1f, 0.3f, 0.04f));
        f.light = kit.MakeLight("BlazeUltLight" + name, lightColor, 1.8f);
        f.anchor.gameObject.SetActive(false);
        f.sleeveAnchor.gameObject.SetActive(false);
        return f;
    }
}
