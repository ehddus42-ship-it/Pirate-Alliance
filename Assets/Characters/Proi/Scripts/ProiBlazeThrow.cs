using System.Collections.Generic;
using UnityEngine;
using static ProiBlazeKit;

/// <summary>
/// 우클릭 기술(ProiThrow)의 Blender 화염 VFX (D:/Blender_Test/VFX_Scripts/build_proi_skill_fire.py, 렌더
/// Proi_Skill_Fireball_FireVFX.mp4 / Proi_Skill_GrabThrow_FireVFX.mp4 를 게임으로 옮긴 것). 콤보(ProiBlazeCombo)와 같은 계열.
///  - 던지는 오른손: 주먹 화염 3겹 + 팔뚝 화염, 놓는 순간 섬광 + 충격파 고리.
///  - 화염구: 손 위에서 화염 껍질이 커지며(하얗게 달아오른 핵 + 둘레를 도는 고리 2개), 던지면 꼬리가 길게 늘어진 혜성이 되어
///    나선 화염을 두르고 날아가며, 지나간 길에 십자 화염 띠가 남았다가 타 들어갑니다.
///  - 잡아 던지기: 잡는 순간 섬광 + 고리, 잡힌 적이 화염에 휩싸여(날아갈 땐 꼬리가 뒤로 휘날림) 혜성처럼 날아가고, 떨어진 뒤에도 탐.
///  - 폭발 (화염구가 땅에 꽂힐 때 / 던진 적이 떨어질 때): 섬광 + 땅에 눕는 충격파 고리 + 솟구치는 화염 기둥 + 불꽃 파편 + 연기 +
///    그을음 자국 + 주변에 남는 작은 불. 공중에서 터지면 기둥 / 그을음 없이 섬광과 고리만.
/// 게임 동작 / 판정 / 히트스톱 / 카메라 흔들림은 ProiThrow 가 그대로 하고, 이 컴포넌트는 그 이벤트와 상태만 따라 그립니다.
/// </summary>
[DefaultExecutionOrder(100)]          // after ProiThrow.LateUpdate has placed the fireball / the held enemy
public class ProiBlazeThrow : MonoBehaviour
{
    [Header("크기 / 밝기")]
    [SerializeField] float handScale = 1.35f;
    [Tooltip("손 위 화염구 크기 (다 커졌을 때) / 날아갈 때")]
    [SerializeField] Vector2 fireballScale = new Vector2(2.3f, 2.07f);
    [Tooltip("잡힌 / 던져진 적을 감싸는 화염 크기")]
    [SerializeField] float bodyFireScale = 4f;
    [Tooltip("폭발 크기: 화염구 / 던진 적이 떨어질 때")]
    [SerializeField] Vector2 blastScale = new Vector2(1.9f, 2.2f);
    [Tooltip("땅에서 이 높이(m)보다 위에서 터지면 화염 기둥 / 그을음 없이 공중 폭발")]
    [SerializeField] float airBurstHeight = 1.2f;
    [Tooltip("폭발 불기둥 크기 (폭발 크기 대비)")]
    [SerializeField] float pillarSize = 2.4f;
    [Tooltip("그을음 자국이 남는 시간 (초)")]
    [SerializeField] float scorchTime = 4f;
    [SerializeField] float brightness = 1f;
    [Header("빛")]
    [SerializeField] Color lightColor = new Color(1f, 0.42f, 0.1f);
    [SerializeField] float handLight = 1f;
    [SerializeField] float ballLight = 2.5f;
    [SerializeField] float blastLight = 8f;

    class Burner
    {   // flame wrap that follows something (the fireball, the enemy's chest), tails trailing its motion, rising when still
        public Transform anchor;
        public Flame[] wrap;
        public Transform spiral;
        public Material spiralMat;
        public ParticleSystem embers, trail;
        public Light light;
        public Vector3 lastPos, dir;
        public float burn, stretch;
        public bool hasPos;
    }

    class Streak
    {   // cross of four half-ribbons along a flight path, revealed at the head, burnt away from the oldest end
        public Ribbon[] parts;
        public readonly List<float> times = new List<float>();
        public readonly List<Vector3> pts = new List<Vector3>();
        public bool recording, active;
        public float t0, span, life, width;
    }

    ProiThrow skill;
    Animator animator;
    ProiBlazeKit kit;
    Transform rHand, rForearm;

    // the throwing hand
    Transform handAnchor, sleeveAnchor;
    Flame[] handWrap;
    Flame sleeve;
    ParticleSystem handEmbers;
    Light handGlow;
    Impact release;
    float handBurn, handStretch;
    Vector3 handLast, handDir;

    // fireball
    Burner ball;
    Transform ballCore, orbit;
    Transform[] orbitRings;
    Material ballCoreMat;
    Material[] orbitMats;
    Streak ballStreak;
    float ballFormT = -99f, launchT = -99f, burstT = -99f;

    // grab + throw
    Burner body;
    Streak bodyStreak;
    Impact grabHit;
    ProiScarecrow victim;
    float grabT = -99f, throwT = -99f, landT = -99f;

    Blast[] blasts;
    BlastStyle blastStyle;
    int nextBlast;

    /// <summary>에셋을 모두 찾았으면 true.</summary>
    public bool Ready { get; private set; }

    void Awake()
    {
        skill = GetComponent<ProiThrow>();
        kit = skill != null ? ProiBlazeKit.Create("ProiBlazeThrowFX", brightness, nameof(ProiBlazeThrow)) : null;
        Ready = kit != null;
        if (!Ready) { enabled = false; return; }
        skill.OnGrab += HandleGrab;
        skill.OnThrowEnemy += HandleThrowEnemy;
        skill.OnEnemyLand += HandleLand;
        skill.OnFireballLaunch += HandleLaunch;
        skill.OnFireballBurst += HandleBurst;
    }

    void Start()
    {
        animator = GetComponentInChildren<Animator>();
        if (animator == null) { enabled = false; return; }
        rHand = FindDeep(animator.transform, "RightHand");
        rForearm = FindDeep(animator.transform, "RightLowerArm");

        handAnchor = kit.Anchor("BlazeThrowHand", handScale);
        handWrap = kit.MakeWrap(handAnchor, 71f);
        sleeveAnchor = kit.Anchor("BlazeThrowSleeve", 1.35f);
        sleeve = kit.MakeSleeve(sleeveAnchor, false, 71f);
        handEmbers = kit.MakeEmbers("BlazeThrowHandEmbers", new Color(1f, 0.3f, 0.04f));
        handGlow = kit.MakeLight("BlazeThrowHandLight", lightColor, 1.6f);
        release = kit.MakeImpact("Release", 0.9f, 2, 2.2f);
        handLast = Fist();

        ball = MakeBurner("Ball", 1f, 83f);
        ballCoreMat = kit.FxMaterial("BlazeBallCore", "_MODE_FLASH", Row.Flash, 6f);
        ballCoreMat.SetFloat(IdFacingPow, 1f);
        ballCoreMat.SetFloat(IdFade, 1f);
        ballCore = kit.Part("BallCore", "BZ_Flash", ballCoreMat, ball.anchor);
        ballCore.localScale = Vector3.one * 0.07f;
        orbit = kit.Anchor("BlazeBallOrbit");
        orbitMats = new[] { kit.FxMaterial("BlazeOrbit0", "_MODE_RING", Row.Body, 5f), kit.FxMaterial("BlazeOrbit1", "_MODE_RING", Row.Core, 5f) };
        orbitRings = new[] { kit.Part("Orbit0", "BZ_Ring", orbitMats[0], orbit), kit.Part("Orbit1", "BZ_Ring", orbitMats[1], orbit) };
        ballStreak = MakeStreak("Ball", 10f, 0.3f);

        body = MakeBurner("Body", bodyFireScale, 97f);
        bodyStreak = MakeStreak("Body", 10f, 0.4f);
        grabHit = kit.MakeImpact("Grab", 0.9f, 3, 2.2f);

        blasts = new Blast[3];
        for (int i = 0; i < blasts.Length; i++) blasts[i] = kit.MakeBlast(i.ToString(), 4, 6);
        // a tall pillar with fire rings climbing it and spreading out
        blastStyle = new BlastStyle
        {
            airBurstHeight = airBurstHeight, scorchTime = scorchTime, light = blastLight,
            pillar = pillarSize, hold = 9f, die = 30f, rings = 3, ringGap = 3f, ringLife = 12f, ringSpread = 1.3f,
        };
    }

    void OnDestroy()
    {
        if (skill != null)
        {
            skill.OnGrab -= HandleGrab;
            skill.OnThrowEnemy -= HandleThrowEnemy;
            skill.OnEnemyLand -= HandleLand;
            skill.OnFireballLaunch -= HandleLaunch;
            skill.OnFireballBurst -= HandleBurst;
        }
        kit?.Dispose();
    }

    Vector3 Fist() => rHand.position + (rHand.position - rForearm.position).normalized * 0.07f;

    // ================================================================ events from ProiThrow

    void HandleGrab(ProiScarecrow s)
    {
        float t = Time.time;
        victim = s;
        grabT = t; throwT = -99f; landT = -99f;
        body.hasPos = false;
        Vector3 chest = s.Chest, dir = (chest - Fist()).normalized;
        CameraTilted(chest, dir, out Vector3 na, out Vector3 nb);
        kit.TriggerImpact(grabHit, t, chest - dir * 0.15f, dir, na, nb, 50, blastLight * 0.4f);
    }

    void HandleThrowEnemy(ProiScarecrow s)
    {
        throwT = Time.time;
        Release(skill.Forward + Vector3.up * 0.45f);
        StartStreak(bodyStreak, 8f);
    }

    void HandleLand(ProiScarecrow s, Vector3 at)
    {
        landT = Time.time;
        bodyStreak.recording = false;
        Explode(at, at.y, blastScale.y);
    }

    void HandleLaunch(Vector3 pos, Vector3 vel)
    {
        launchT = Time.time;
        Release(vel);
        StartStreak(ballStreak, 18f);
    }

    void HandleBurst(Vector3 pos, Vector3 vel, float scale)
    {
        burstT = Time.time;
        ballStreak.recording = false;
        Explode(pos, transform.position.y, blastScale.x * scale);
    }

    void Release(Vector3 dir)
    {
        dir = dir.normalized;
        Vector3 at = Fist() + dir * 0.12f;
        CameraTilted(at, dir, out Vector3 na, out Vector3 nb);
        kit.TriggerImpact(release, Time.time, at, dir, na, nb, 40, blastLight * 0.3f);
    }

    // ================================================================ per frame

    void LateUpdate()
    {
        float t = Time.time;
        float dt = Mathf.Max(Time.deltaTime, 1e-5f);
        float f30 = dt * 30f;
        Vector3 up = Vector3.up;
        Vector3 fwd = skill.Forward.sqrMagnitude > 0.5f ? skill.Forward : transform.forward;
        float skillT = skill.SkillTime, rel = skill.ReleaseTime;

        // ---- the throwing hand: lit through the skill, roars as it lets go
        float flick = Flick(t, 5f);
        handBurn = Mathf.MoveTowards(handBurn, skill.Active ? 1f : 0f, dt / (skill.Active ? 0.25f : 0.5f));
        float roar = !skill.Active ? 0f : skillT < rel ? Smooth(Seg(skillT, rel - 3f * F, rel))
                                                       : 1f - Smooth(Seg(skillT - rel, 4f * F, 14f * F));
        float hitBoost = skill.Active && skillT >= rel ? 1f - Seg(skillT - rel, 0f, 8f * F) : 0f;
        Vector3 fist = Fist();
        float spd = (fist - handLast).magnitude / dt / 30f;
        handLast = fist;
        handStretch = Mathf.Max(Smooth(Seg(spd, 0.04f, 0.35f)), handStretch * Mathf.Pow(0.78f, f30));
        Vector3 armDir = ((fist - rForearm.position).normalized - up * 0.2f).normalized;
        handDir = handDir == Vector3.zero ? armDir : Vector3.Lerp(handDir, armDir, 1f - Mathf.Pow(0.25f, f30)).normalized;
        bool handOn = handBurn > 0.001f;
        handAnchor.gameObject.SetActive(handOn);
        sleeveAnchor.gameObject.SetActive(handOn);
        if (handOn)
        {
            handAnchor.SetPositionAndRotation(fist, Look(handDir, up, fwd));
            SetWrap(handWrap, handBurn, roar, hitBoost, handStretch, flick);
            Vector3 along = (rHand.position - rForearm.position).normalized;
            sleeveAnchor.SetPositionAndRotation(rHand.position - along * 0.01f, Look(along, up, fwd));
            sleeve.mat.SetFloat(IdIntensity, Mathf.Lerp(-0.8f, sleeve.baseI, EaseOut(handBurn)) + (sleeve.roarI - sleeve.baseI) * roar + 0.04f * flick);
            sleeve.mat.SetFloat(IdStrength, sleeve.strength * (1f + 0.5f * hitBoost));
            sleeve.smr.SetBlendShapeWeight(0, Mathf.Clamp01(0.25f + 0.75f * handStretch + 0.15f * flick) * 100f);
        }
        handEmbers.transform.position = fist;
        var hem = handEmbers.emission;
        hem.rateOverTime = 150f * handBurn;
        handGlow.transform.position = fist;
        handGlow.intensity = (handLight * EaseOut(handBurn) * (1f + 0.25f * flick) + handLight * 0.66f * roar) * brightness;
        handGlow.enabled = handGlow.intensity > 0.01f;
        kit.UpdateImpact(release, t, release.hitPos);

        UpdateFireball(t, dt, f30, up, fwd);
        UpdateBody(t, dt, f30, up, fwd);
        UpdateStreak(ballStreak, t, skill.BallFlying ? skill.BallPosition : (Vector3?)null);
        UpdateStreak(bodyStreak, t, victim != null && victim.Current == ProiScarecrow.State.Flying ? victim.Chest : (Vector3?)null);
        foreach (Blast b in blasts) kit.UpdateBlast(b, t);
    }

    void UpdateFireball(float t, float dt, float f30, Vector3 up, Vector3 fwd)
    {
        bool held = skill.BallHeld, flying = skill.BallFlying;
        if (held && ballFormT < 0f) { ballFormT = t; ball.hasPos = false; launchT = -99f; burstT = -99f; }
        bool on = held || flying;
        if (!on) ballFormT = -99f;
        ball.burn = on ? Mathf.MoveTowards(ball.burn, 1f, dt / 0.23f) : 0f;
        float roar = launchT < 0f ? 0f : 1f - Smooth(Seg(t - launchT, 4f * F, 14f * F));
        float scale = held ? fireballScale.x * Mathf.Lerp(0.25f, 1f, skill.BallGrow) : fireballScale.y;
        UpdateBurner(ball, on, skill.BallPosition, scale, roar, launchT > 0f ? 1f - Seg(t - launchT, 0f, 8f * F) : 0f, dt, f30, up, fwd);
        ballCore.gameObject.SetActive(on);

        // charge rings orbiting the ball while it is held
        bool ringsOn = held;
        orbit.position = skill.BallPosition;
        for (int k = 0; k < orbitRings.Length; k++)
        {
            orbitRings[k].gameObject.SetActive(ringsOn);
            if (!ringsOn) continue;
            float f = t * 30f;
            orbitRings[k].localRotation = Quaternion.Euler((0.9f * f * (k == 1 ? 1f : -1f) * 0.12f + k) * Mathf.Rad2Deg,
                                                           (0.7f + 0.08f * f) * Mathf.Rad2Deg, 0.3f * k * Mathf.Rad2Deg);
            orbitRings[k].localScale = Vector3.one * (k == 0 ? 0.22f : 0.17f) * Mathf.Lerp(0.25f, 1f, skill.BallGrow);
            orbitMats[k].SetFloat(IdFade, EaseOut(Seg(t - ballFormT, 0f, 6f * F)) * 0.8f);
        }

        // spiral round the flight line: wound on at the throw, burnt off as it bursts
        bool spiralOn = flying;
        ball.spiral.gameObject.SetActive(spiralOn);
        if (spiralOn)
        {
            ball.spiralMat.SetFloat(IdHead, Mathf.Lerp(0f, 1.15f, EaseOut(Seg(t - launchT, 0f, 3f * F), 2f)));
            ball.spiralMat.SetFloat(IdTail, -0.3f);
            ball.spiral.localRotation = Quaternion.AngleAxis(-0.32f * Mathf.Rad2Deg * t * 30f, AxisOS);
        }
        if (flying && !ball.trail.isPlaying) ball.trail.Play();
        if (!flying && ball.trail.isPlaying) ball.trail.Stop();
        ball.light.intensity = on ? ballLight * (held ? skill.BallGrow : 1f) * (1f + 0.2f * Flick(t, 4f)) * brightness : 0f;
        ball.light.enabled = ball.light.intensity > 0.01f;
    }

    void UpdateBody(float t, float dt, float f30, Vector3 up, Vector3 fwd)
    {
        ProiScarecrow s = victim;
        bool burning = s != null && grabT > 0f &&
                       (s.Current == ProiScarecrow.State.Held || s.Current == ProiScarecrow.State.Flying ||
                        (s.Current == ProiScarecrow.State.Down && landT > 0f && t - landT < 46f * F));
        if (s != null && s.Current == ProiScarecrow.State.Idle && t - grabT > 0.5f) { victim = null; burning = false; }
        // ignite on the grab, roar on the throw, burn on the ground and die down 30..46 frames after landing
        float target = burning ? (landT > 0f ? 1f - Seg(t - landT, 30f * F, 46f * F) : 1f) : 0f;
        body.burn = Mathf.MoveTowards(body.burn, target, dt / (target > body.burn ? 0.23f : 0.2f));
        // the enemy is big and opaque: keep its fire roaring while held / flying so it reads round the body
        bool airborne = s != null && (s.Current == ProiScarecrow.State.Held || s.Current == ProiScarecrow.State.Flying);
        float roar = Mathf.Max(throwT < 0f ? 0f : 1f - Smooth(Seg(t - throwT, 4f * F, 14f * F)), airborne ? 0.6f : 0f);
        float boost = throwT > 0f ? 1f - Seg(t - throwT, 0f, 8f * F) : 0f;
        Vector3 chest = s != null ? s.Chest + Vector3.up * 0.1f : body.lastPos;
        UpdateBurner(body, body.burn > 0.001f, chest, bodyFireScale, roar, boost, dt, f30, up, fwd);
        kit.UpdateImpact(grabHit, t, grabHit.hitPos);

        bool spiralOn = throwT > 0f && (landT < 0f || t - landT < 7f * F) && body.burn > 0.001f;
        body.spiral.gameObject.SetActive(spiralOn);
        if (spiralOn)
        {
            body.spiralMat.SetFloat(IdHead, Mathf.Lerp(0f, 1.15f, EaseOut(Seg(t - throwT, 0f, 3f * F), 2f)));
            body.spiralMat.SetFloat(IdTail, landT < 0f ? -0.3f : Mathf.Lerp(-0.3f, 1.05f, Mathf.Pow(Seg(t - landT, 0f, 6f * F), 1.2f)));
            body.spiral.localRotation = Quaternion.AngleAxis(-0.32f * Mathf.Rad2Deg * t * 30f, AxisOS);
        }
        bool flying = s != null && s.Current == ProiScarecrow.State.Flying;
        if (flying && !body.trail.isPlaying) body.trail.Play();
        if (!flying && body.trail.isPlaying) body.trail.Stop();
        body.light.intensity = ballLight * 1.6f * EaseOut(body.burn) * (1f + 0.2f * Flick(t, 6f)) * brightness;
        body.light.enabled = body.light.intensity > 0.01f;
    }

    void UpdateBurner(Burner b, bool on, Vector3 pos, float scale, float roar, float boost, float dt, float f30, Vector3 up, Vector3 fwd)
    {
        if (!b.hasPos) { b.lastPos = pos; b.dir = Vector3.zero; b.stretch = 0f; b.hasPos = true; }
        Vector3 v = (pos - b.lastPos) / dt;
        b.lastPos = pos;
        float spd = v.magnitude / 30f;
        // tails trail the motion and rise when it is still (the Blender bake filter, per 30 fps frame)
        Vector3 rest = (-up - fwd * 0.25f).normalized;
        float w = Smooth(Seg(spd, 0.012f, 0.07f));
        Vector3 tgt = spd > 1e-5f ? Vector3.Lerp(rest, v.normalized, w).normalized : rest;
        b.dir = b.dir == Vector3.zero ? tgt : Vector3.Lerp(b.dir, tgt, 1f - Mathf.Pow(0.5f, f30)).normalized;
        b.stretch = Mathf.Max(Smooth(Seg(spd, 0.04f, 0.35f)), b.stretch * Mathf.Pow(0.78f, f30));
        b.anchor.gameObject.SetActive(on);
        if (on)
        {
            b.anchor.SetPositionAndRotation(pos, Look(b.dir, up, fwd));
            b.anchor.localScale = Vector3.one * scale;
            SetWrap(b.wrap, b.burn, roar, boost, b.stretch, Flick(Time.time, scale));
        }
        b.embers.transform.position = pos;
        var em = b.embers.emission;
        em.rateOverTime = on ? 200f * b.burn : 0f;
        b.trail.transform.position = pos;
        b.light.transform.position = pos;
        if (!on) b.hasPos = false;
    }

    // ================================================================ flight streaks

    void StartStreak(Streak s, float spanFrames)
    {
        s.times.Clear(); s.pts.Clear();
        s.recording = true; s.active = true;
        s.t0 = Time.time;
        s.span = spanFrames * F;
    }

    void UpdateStreak(Streak s, float t, Vector3? pos)
    {
        if (s.recording && pos.HasValue && (s.times.Count == 0 || t - s.times[s.times.Count - 1] > 1f / 120f))
        {
            s.times.Add(t);
            s.pts.Add(pos.Value);
            BuildStreak(s);
        }
        float head = (t - s.t0) / s.span;
        float lastU = s.times.Count > 0 ? (s.times[s.times.Count - 1] - s.t0) / s.span : 0f;
        if (s.active && !s.recording && head > lastU + s.life * F / s.span + 0.1f) s.active = false;
        foreach (Ribbon r in s.parts)
        {
            r.go.SetActive(s.active && s.times.Count >= 2);
            r.mat.SetFloat(IdHead, head);
            r.mat.SetFloat(IdLife, s.life * F / s.span);
        }
    }

    void BuildStreak(Streak s)
    {
        int n = s.pts.Count;
        if (n < 2) return;
        var us = new float[n];
        for (int i = 0; i < n; i++) us[i] = (s.times[i] - s.t0) / s.span;
        for (int k = 0; k < 4; k++)
        {
            var inner = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 tangent = (s.pts[Mathf.Min(i + 1, n - 1)] - s.pts[Mathf.Max(i - 1, 0)]).normalized;
                Vector3 side = Vector3.Cross(tangent, Vector3.up).normalized;
                Vector3 upv = Vector3.Cross(side, tangent).normalized;
                float w = s.width * (0.35f + 0.65f * Mathf.Sqrt(i / (float)(n - 1)));   // thin at the start, full at the head
                inner[i] = s.pts[i] + (k < 2 ? upv : side) * w * (k % 2 == 0 ? 1f : -1f);
            }
            BuildRibbon(s.parts[k].mesh, s.pts, inner, us);
        }
    }

    // ================================================================ blasts

    void Explode(Vector3 at, float groundY, float scale)
    {
        Blast b = blasts[nextBlast];
        nextBlast = (nextBlast + 1) % blasts.Length;
        kit.TriggerBlast(b, Time.time, at, groundY, scale, skill.Forward, blastStyle);
    }

    // ================================================================ building

    Burner MakeBurner(string name, float scale, float seed)
    {
        var b = new Burner { anchor = kit.Anchor("BlazeThrow" + name, scale) };
        b.wrap = kit.MakeWrap(b.anchor, seed);
        b.spiral = kit.MakeSpiral("Throw" + name, b.anchor, seed, out b.spiralMat);
        b.embers = kit.MakeEmbers("BlazeThrowEmbers" + name, new Color(1f, 0.3f, 0.04f), 0.1f);
        b.trail = kit.MakeTrailSparks("BlazeThrowTrail" + name, 220f);
        b.light = kit.MakeLight("BlazeThrowLight" + name, lightColor, 2.5f);
        b.anchor.gameObject.SetActive(false);
        return b;
    }

    Streak MakeStreak(string name, float lifeFrames, float width)
    {
        var s = new Streak { life = lifeFrames, width = width, parts = new Ribbon[4] };
        for (int k = 0; k < 4; k++)
            s.parts[k] = kit.MakeRibbon("Comet" + name + k, Row.Body, 3f, 131f + k * 9f, new Vector2(0f, 0.85f));
        return s;
    }
}
