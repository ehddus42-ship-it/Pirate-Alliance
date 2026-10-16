using System.Collections.Generic;
using UnityEngine;
using static ProiBlazeKit;

/// <summary>
/// Blender 화염 콤보 VFX (D:/Blender_Test/VFX_Scripts, 렌더 Proi_Combo_FireVFX.mp4 를 게임으로 옮긴 것).
///  - 주먹 / 발에 감기는 화염: Blender 화염 껍질 3겹(Resources/Proi_BlazeFX.fbx)이 주먹을 감싸고, 꼬리가 팔뚝(정강이)을
///    타고 팔꿈치(무릎) 쪽으로 감겨 흐르며, 빠르게 지를수록 길게 늘어집니다(블렌드셰이프 Stretch).
///  - 팔꿈치 / 무릎까지: 팔뚝(정강이)을 따라 화염 껍질이 하나 더 감기고 관절 쪽으로 옅어집니다.
///  - 지르기: 매 타마다 팔뚝(정강이)을 감는 나선 화염, 1·2타에 스피드라인, 지르는 동안 불꽃 궤적.
///  - 타격: 섬광 + 충격파 고리 2개 + 주먹 둘레로 피는 화염 + 불꽃 파편 + 연기 + 빛. 고리 / 섬광은 맞은 자리에 머물고
///    높이만 주먹(발)을 따라 올라가며(아래로 떨어지거나 몸 쪽으로 되돌아오지 않음), 파편 / 연기는 맞은 자리에 남습니다.
///  - 3타 발차기: 발이 실제로 지나간 길을 따라 초승달 화염 띠(+ 뜨거운 가장자리 + 양옆 날개)가 그려지고 꼬리부터 타 들어갑니다.
/// 부품은 ProiBlazeKit (셰이더 Proi/BlazeFX, 색 램프 ProiBlazeRamps.png). 1타 왼주먹, 2타 오른주먹, 3타 왼발.
/// 히트스톱 / 카메라 흔들림도 여기서 처리합니다 (콤보 쪽 PlayHitFeel 이 true 일 때 - 게임 안에서는 MeleeSlash 가 처리).
/// 콤보는 IProiComboSource (플레이그라운드: ProiCombat, 게임: ProiHunterKit) 에서 읽고, 꺼지면 화염도 바로 숨깁니다.
/// </summary>
public class ProiBlazeCombo : MonoBehaviour
{
    [Header("크기 / 밝기")]
    [SerializeField] float fistScale = 1.4f;
    [SerializeField] float footScale = 1.5f;
    [Tooltip("타격 이펙트 크기: 1타 / 2타 / 3타")]
    [SerializeField] Vector3 impactScale = new Vector3(1f, 1.25f, 1.45f);
    [Tooltip("전체 밝기 (HDR, 블룸)")]
    [SerializeField] float brightness = 1f;
    [Header("타이밍 (콤보 진행도 0~1)")]
    [Tooltip("타격 순간: 1타 / 2타 / 3타")]
    [SerializeField] Vector3 impactAt = new Vector3(0.47f, 0.478f, 0.42f);
    [Tooltip("3타 초승달 궤적이 그려지기 시작하는 진행도")]
    [SerializeField] float crescentFrom = 0.1f;
    [Tooltip("공격이 끝난 뒤 주먹 화염이 사그라드는 시간 (초)")]
    [SerializeField] float burnOut = 0.7f;
    [Tooltip("공격하지 않을 때도 주먹에 남는 화염 (0 = 끔)")]
    [Range(0f, 1f)] [SerializeField] float idleBurn = 0f;
    [Header("타격감")]
    [Tooltip("히트스톱 시간 (초, 실제 시간): 1타 / 2타 / 3타")]
    [SerializeField] Vector3 hitStop = new Vector3(0.05f, 0.07f, 0.10f);
    [Tooltip("카메라 흔들림 세기 (m): 1타 / 2타 / 3타")]
    [SerializeField] Vector3 shakeAmplitude = new Vector3(0.02f, 0.03f, 0.055f);
    [SerializeField] Vector3 shakeDuration = new Vector3(0.08f, 0.10f, 0.14f);
    [SerializeField] float kickZoom = 0.8f;
    [Header("빛")]
    [SerializeField] Color lightColor = new Color(1f, 0.42f, 0.1f);
    [SerializeField] float fistLight = 0.8f;
    [SerializeField] float hitLight = 3f;
    [SerializeField] float lightRange = 1.4f;

    static readonly int[] ClipFrames = { 18, 24, 28 };              // Attack1 / Attack2 / Attack3

    class Limb
    {
        public string name;
        public int step;                      // combo step this limb strikes in
        public bool foot;
        public Transform bone, joint, tip;    // hand / foot, lower arm / lower leg, toes (foot only)
        public Transform anchor, sleeveAnchor, spiral, lines;
        public Flame[] wrap;
        public Flame sleeve;
        public Material spiralMat, linesMat;
        public ParticleSystem embers, trail;
        public Light glow;
        public Impact impact;
        public Vector3 lastPos, dir;
        public float stretch, burn, thrustT = -99f, hitT = -99f;
        public bool primed;
        public Vector3 Point => foot
            ? Vector3.Lerp(bone.position, tip != null ? tip.position : bone.position, 0.45f)
            : bone.position + (bone.position - joint.position).normalized * 0.07f;
    }

    class Crescent
    {
        public Ribbon[] parts;
        public float[] life;                  // frames
        public readonly List<float> times = new List<float>();
        public readonly List<Vector3> outer = new List<Vector3>(), inner = new List<Vector3>();
        public bool recording, active;
        public float t0, hitT = -99f;
    }

    IProiComboSource combat;
    Animator animator;
    ProiBlazeKit kit;
    Limb[] limbs;
    Crescent crescent;
    int lastStep = -1;
    float lastProgress;

    /// <summary>에셋을 모두 찾았으면 true.</summary>
    public bool Ready { get; private set; }

    void Awake()
    {
        kit = ProiBlazeKit.Create("ProiBlazeFX", brightness, nameof(ProiBlazeCombo));
        Ready = kit != null;
        if (!Ready) { enabled = false; return; }
    }

    void Start()
    {
        combat = GetComponentInParent<IProiComboSource>();
        animator = combat != null ? combat.ComboAnimator : null;
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (combat == null || animator == null)
        {
            Debug.LogWarning("[ProiBlazeCombo] no combo source / Animator - effect disabled");
            enabled = false;
            return;
        }
        Transform root = animator.transform;
        limbs = new[]
        {
            MakeLimb("L", 0, false, FindDeep(root, "LeftHand"), FindDeep(root, "LeftLowerArm"), null, fistScale),
            MakeLimb("R", 1, false, FindDeep(root, "RightHand"), FindDeep(root, "RightLowerArm"), null, fistScale),
            MakeLimb("K", 2, true, FindDeep(root, "LeftFoot"), FindDeep(root, "LeftLowerLeg"), FindDeep(root, "LeftToes"), footScale),
        };
        crescent = new Crescent
        {
            parts = new[]
            {
                kit.MakeRibbon("Crescent", Row.Body, 1.7f, 91f, new Vector2(0f, 0.7f)),
                kit.MakeRibbon("CrescentCore", Row.Core, 1.8f, 97f, new Vector2(0.1f, 0.8f)),
                kit.MakeRibbon("CrescentFinA", Row.Body, 1.3f, 140f, new Vector2(0f, 0.7f)),
                kit.MakeRibbon("CrescentFinB", Row.Body, 1.3f, 147f, new Vector2(0f, 0.7f)),
            },
            life = new[] { 9f, 5.4f, 7f, 7f },
        };
    }

    void OnDestroy()
    {
        kit?.Dispose();
    }

    // 헌터 교체로 Proi 가 빠지면 화염을 남기지 않고 바로 숨기고, 다시 들어오면 꺼진 상태에서 시작합니다.
    void OnEnable()
    {
        if (kit != null && kit.Root != null) kit.Root.gameObject.SetActive(true);
    }

    void OnDisable()
    {
        if (kit != null && kit.Root != null) kit.Root.gameObject.SetActive(false);
        if (limbs != null)
            foreach (Limb l in limbs)
            {
                l.burn = 0f; l.stretch = 0f; l.primed = false;
                l.thrustT = -99f; l.hitT = -99f;
                l.lastPos = l.Point;
            }
        if (crescent != null) { crescent.recording = false; crescent.active = false; crescent.hitT = -99f; }
        lastStep = -1;
        lastProgress = 0f;
    }

    // ================================================================ per frame

    void LateUpdate()
    {
        float t = Time.time;
        float dt = Mathf.Max(Time.deltaTime, 1e-5f);
        float f30 = dt * 30f;
        int step = combat.Busy ? combat.CurrentStep : -1;
        float p = combat.Progress;
        bool newStep = step != lastStep;
        bool comboStart = newStep && step == 0;
        Vector3 up = Vector3.up;
        Vector3 fwd = Vector3.ProjectOnPlane(animator.transform.forward, up).normalized;

        foreach (Limb l in limbs)
        {
            if (comboStart || (newStep && step == l.step)) { l.thrustT = -99f; l.hitT = -99f; l.primed = true; }

            // ---- strike timing from the combo progress (lead = 3 Blender frames before contact)
            if (step == l.step && l.primed)
            {
                float hitP = impactAt[l.step];
                float clipSec = (ClipFrames[l.step] - 1) / 30f / Mathf.Max(combat.AttackSpeed, 0.01f);
                float leadP = 3f * F / clipSec;
                float prevP = newStep ? 0f : lastProgress;
                if (l.thrustT < 0f && p >= hitP - leadP) { l.thrustT = t; l.trail.Play(); }
                if (prevP < hitP && p >= hitP) { Hit(l, t); l.primed = false; }
            }

            // ---- ignition: fists burn through the combo, the foot only for the kick
            bool active = l.foot ? step == 2 : (step >= 0 && step <= 2);
            float target = active ? 1f : (l.foot ? 0f : idleBurn);
            l.burn = Mathf.MoveTowards(l.burn, target, dt / (target > l.burn ? 0.25f : Mathf.Max(burnOut, 0.05f)));

            // ---- the flame runs along the limb (elbow -> fist, knee -> foot) so its tails wrap the forearm / shin back
            // to the elbow / knee; a slight downward tilt lets the tails lick upward. Speed only stretches the tails.
            Vector3 pos = l.Point;
            Vector3 v = (pos - l.lastPos) / dt;
            l.lastPos = pos;
            float spd = v.magnitude / 30f;                    // metres per Blender frame
            Vector3 tgt = ((pos - l.joint.position).normalized - up * 0.2f).normalized;
            l.dir = l.dir == Vector3.zero ? tgt : Vector3.Lerp(l.dir, tgt, 1f - Mathf.Pow(0.25f, f30)).normalized;
            l.stretch = Mathf.Max(Smooth(Seg(spd, 0.04f, 0.35f)), l.stretch * Mathf.Pow(0.78f, f30));

            UpdateLimb(l, t, pos, up, fwd);
        }

        UpdateCrescent(t, step, p, newStep);
        foreach (Limb l in limbs)
        {   // flash / rings stay where the hit landed but rise with the fist / foot (never drop, never swing back)
            Impact im = l.impact;
            Vector3 fp = im.hitPos;
            fp.y = Mathf.Max(fp.y, l.Point.y + im.dir.y * (l.foot ? 0.12f : 0.1f));
            // the shockwaves draw back into the fist (where it is now) / into the foot where it struck (the leg drops away)
            Vector3 sink = l.foot ? im.hitPos - im.stackNormal * 0.12f : l.Point + im.dir * 0.03f;
            kit.UpdateImpact(im, t, fp, sink);
        }
        lastStep = step;
        lastProgress = p;
    }

    void Hit(Limb l, float t)
    {
        l.hitT = t;
        Vector3 dir = l.foot ? (l.dir.sqrMagnitude > 0.5f ? l.dir : animator.transform.forward)
                             : (l.bone.position - l.joint.position).normalized;
        if (l.foot && Vector3.Dot(dir, Vector3.down) > 0.7f) dir = (l.Point - l.joint.position).normalized;
        Vector3 at = l.Point + dir * (l.foot ? 0.12f : l.step == 0 ? 0.09f : 0.1f);
        CameraTilted(at, dir, out Vector3 na, out Vector3 nb);
        // the kick's rings stand upright, facing straight ahead of the character
        kit.TriggerImpact(l.impact, t, at, dir, na, nb, l.step == 0 ? 60 : l.step == 1 ? 100 : 140, hitLight,
                          l.foot ? animator.transform.forward : (Vector3?)null);
        if (combat.PlayHitFeel) HitFeel(l.step, dir);
        if (l.foot && crescent.recording) crescent.hitT = t;
    }

    void HitFeel(int step, Vector3 dir)
    {
        int k = Mathf.Clamp(step, 0, 2);
        ProiHitFeedback.HitStop(hitStop[k]);
        Camera cam = Camera.main;
        var qcam = cam != null ? cam.GetComponent<ProiQuarterViewCamera>() : null;
        if (qcam != null) qcam.Shake(shakeAmplitude[k], shakeDuration[k], dir, k == 2 ? kickZoom : 0f);
    }

    void UpdateLimb(Limb l, float t, Vector3 pos, Vector3 up, Vector3 fwd)
    {
        float burn = l.burn;
        bool visible = burn > 0.001f || (l.hitT > 0f && t - l.hitT < 0.5f);
        float flick = Flick(t, l.step);

        // roar: rises from the thrust to the hit, holds 4 frames, settles over 10
        float roar = 0f;
        if (l.thrustT > 0f)
            roar = l.hitT < 0f ? Smooth(Seg(t - l.thrustT, 0f, 3f * F)) : 1f - Smooth(Seg(t - l.hitT, 4f * F, 14f * F));
        float hitBoost = l.hitT > 0f ? 1f - Seg(t - l.hitT, 0f, 8f * F) : 0f;

        // ---- flame wrap round the fist / foot (world anchor, the Blender meshes' flame axis along the limb)
        l.anchor.gameObject.SetActive(visible);
        if (visible)
        {
            l.anchor.SetPositionAndRotation(pos, Look(l.dir, up, fwd));
            SetWrap(l.wrap, burn, roar, hitBoost, l.stretch, flick);
        }

        // ---- sleeve along the forearm / shin up to the elbow / knee
        l.sleeveAnchor.gameObject.SetActive(visible);
        if (visible)
        {
            Vector3 along = (l.bone.position - l.joint.position).normalized;
            l.sleeveAnchor.SetPositionAndRotation(l.bone.position - along * 0.01f, Look(along, up, fwd));
            Flame s = l.sleeve;
            s.mat.SetFloat(IdIntensity, Mathf.Lerp(-0.8f, s.baseI, EaseOut(burn)) + (s.roarI - s.baseI) * roar + 0.04f * flick);
            s.mat.SetFloat(IdStrength, s.strength * (1f + 0.5f * hitBoost));
            s.smr.SetBlendShapeWeight(0, Mathf.Clamp01(0.25f + 0.75f * l.stretch + 0.15f * flick) * 100f);
        }

        // ---- spiral: revealed over the thrust, burnt from the far end after the hit
        bool on = l.thrustT > 0f && t - l.thrustT > -F && (l.hitT < 0f || t - l.hitT < 9f * F);
        l.spiral.gameObject.SetActive(on);
        if (on)
        {
            float head = Mathf.Lerp(0f, 1.15f, EaseOut(Seg(t - l.thrustT, 0f, 4f * F), 2f));
            float tail = l.hitT < 0f ? -0.3f : Mathf.Lerp(-0.3f, 1.05f, Mathf.Pow(Seg(t - l.hitT, 0f, 8f * F), 1.2f));
            l.spiralMat.SetFloat(IdHead, head);
            l.spiralMat.SetFloat(IdTail, tail);
            l.spiral.localRotation = Quaternion.AngleAxis(-0.32f * Mathf.Rad2Deg * t * 30f, AxisOS);
        }
        // ---- speed lines (punches)
        if (l.lines != null)
        {
            float fade = l.thrustT < 0f ? 0f : l.hitT < 0f ? Smooth(Seg(t - l.thrustT, F, 2f * F))
                                                           : 1f - Seg(t - l.hitT, 0f, 2f * F);
            l.lines.gameObject.SetActive(fade > 0.001f);
            l.linesMat.SetFloat(IdFade, fade);
        }

        // ---- embers / thrust sparks / light
        l.embers.transform.position = pos;
        l.trail.transform.position = pos;
        var em = l.embers.emission;
        em.rateOverTime = 170f * burn;
        if (l.trail.isPlaying && (l.hitT > 0f || t - l.thrustT > 6f * F)) l.trail.Stop();
        l.glow.transform.position = pos;
        l.glow.intensity = (fistLight * EaseOut(burn) * (1f + 0.25f * flick) + fistLight * 0.66f * roar) * brightness;
        l.glow.enabled = l.glow.intensity > 0.01f;
    }

    // ================================================================ kick crescent

    void UpdateCrescent(float t, int step, float p, bool newStep)
    {
        Crescent c = crescent;
        Limb k = limbs[2];
        if (newStep && step == 2)
        {
            c.recording = false; c.active = false; c.hitT = -99f;
            c.times.Clear(); c.outer.Clear(); c.inner.Clear();
        }
        if (step == 2 && !c.recording && !c.active && p >= crescentFrom && p < impactAt.z)
        {
            c.recording = true; c.active = true; c.t0 = t;
        }
        if (c.recording)
        {
            if (c.times.Count == 0 || t - c.times[c.times.Count - 1] > 1f / 240f)
            {
                Vector3 foot = k.Point;
                Vector3 inner = Vector3.Lerp(k.bone.position, k.joint.position, 0.75f);
                c.times.Add(t);
                c.outer.Add(foot + (foot - inner).normalized * 0.07f);
                c.inner.Add(inner);
            }
            if (c.hitT > 0f && t - c.hitT > 1.5f * F) c.recording = false;
            if (step != 2) c.recording = false;
        }
        if (!c.active) { foreach (Ribbon r in c.parts) r.go.SetActive(false); return; }

        float clipSec = (ClipFrames[2] - 1) / 30f / Mathf.Max(combat.AttackSpeed, 0.01f);
        float tHit = c.hitT > 0f ? c.hitT : c.t0 + Mathf.Max(impactAt.z - crescentFrom, 0.05f) * clipSec;
        float span = Mathf.Max(tHit - c.t0, F);
        float head = (t - c.t0) / span;
        bool alive = c.times.Count >= 2 && head < 1.2f + 9f * F / span;
        if (!alive && !c.recording) { c.active = false; foreach (Ribbon r in c.parts) r.go.SetActive(false); return; }
        if (c.recording) BuildCrescent(c, span);
        for (int i = 0; i < c.parts.Length; i++)
        {
            c.parts[i].go.SetActive(c.times.Count >= 2);
            c.parts[i].mat.SetFloat(IdHead, head);
            c.parts[i].mat.SetFloat(IdLife, c.life[i] * F / span);
        }
    }

    void BuildCrescent(Crescent c, float span)
    {
        int n = c.times.Count;
        if (n < 2) return;
        var us = new float[n];
        for (int i = 0; i < n; i++) us[i] = (c.times[i] - c.t0) / span;
        // main crescent (foot -> shin), hot core along the outer edge, two fins across the arc
        var inA = new Vector3[n]; var inB = new Vector3[n]; var core = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            Vector3 tangent = (c.outer[Mathf.Min(i + 1, n - 1)] - c.outer[Mathf.Max(i - 1, 0)]).normalized;
            Vector3 legDir = (c.inner[i] - c.outer[i]).normalized;
            Vector3 side = Vector3.Cross(legDir, tangent).normalized;
            inA[i] = c.outer[i] + side * 0.16f;
            inB[i] = c.outer[i] - side * 0.16f;
            core[i] = Vector3.Lerp(c.inner[i], c.outer[i], 0.72f);
        }
        BuildRibbon(c.parts[0].mesh, c.outer, c.inner, us);
        BuildRibbon(c.parts[1].mesh, c.outer, core, us);
        BuildRibbon(c.parts[2].mesh, c.outer, inA, us);
        BuildRibbon(c.parts[3].mesh, c.outer, inB, us);
    }

    // ================================================================ building

    Limb MakeLimb(string name, int step, bool foot, Transform bone, Transform joint, Transform tip, float scale)
    {
        var l = new Limb { name = name, step = step, foot = foot, bone = bone, joint = joint, tip = tip };
        l.lastPos = l.Point;
        float seed = step * 13.1f;
        l.anchor = kit.Anchor("Blaze_" + name, scale);
        l.wrap = kit.MakeWrap(l.anchor, seed);
        l.sleeveAnchor = kit.Anchor("BlazeSleeve_" + name, foot ? 1.25f : 1.35f);   // outside the chunky gauntlet / boot
        l.sleeve = kit.MakeSleeve(l.sleeveAnchor, foot, seed);
        l.spiral = kit.MakeSpiral(name, l.anchor, seed, out l.spiralMat);          // wound round the forearm / shin
        if (!foot) l.lines = kit.MakeLines(name, l.anchor, out l.linesMat);        // speed lines (punches)
        // particle emitters sit on the (always active) FX root and follow the limb: embers in flight survive the flames
        l.embers = kit.MakeEmbers("BlazeEmbers_" + name, new Color(1f, 0.3f, 0.04f));
        l.trail = kit.MakeTrailSparks("BlazeTrail_" + name);
        l.glow = kit.MakeLight("BlazeLight_" + name, lightColor, lightRange);
        l.impact = kit.MakeImpact(name, impactScale[step], name == "L" ? 3 : 5, lightRange * 1.6f);
        return l;
    }
}
