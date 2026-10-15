using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Blender 화염 VFX 공용 부품 (ProiBlazeCombo / ProiBlazeThrow 가 같이 씀). D:/Blender_Test/VFX_Scripts/fire_vfx_lib.py 의 Unity 판.
///  - 화염 껍질 3겹 (Resources/Proi_BlazeFX.fbx 의 BZ_FlameCore / Body / Wisps, 블렌드셰이프 Stretch), 팔뚝 / 정강이 화염
///  - 나선 화염, 스피드라인, 타격 이펙트(섬광 + 충격파 고리 2개 + 화염 꽃 + 연기 + 불꽃 파편 + 빛), 리본 궤적(초승달 / 혜성 꼬리)
///  - 불씨 / 불꽃 파티클 (Proi/Ember 셰이더)
/// 셰이더 Proi/BlazeFX (모드: 화염 / 나선 / 스피드라인 / 고리 / 섬광 / 리본), 색 램프 ProiBlazeRamps.png.
/// 모든 이펙트는 Root(월드 공간) 아래에 만들어집니다. Blender 커브는 30 fps 프레임 단위라 시간은 F(1/30초) 로 셉니다.
/// </summary>
public class ProiBlazeKit
{
    public const float F = 1f / 30f;
    // Blender mesh space -> Unity mesh space: flame front (Blender +X) = -X, Blender up (+Z) = +Z
    public static readonly Vector3 AxisOS = Vector3.left, UpOS = Vector3.forward;
    static readonly Quaternion MeshBasisInv = Quaternion.Inverse(Quaternion.LookRotation(AxisOS, UpOS));

    public static readonly int IdRamp = Shader.PropertyToID("_Ramp"), IdRow = Shader.PropertyToID("_RampRow"),
        IdStrength = Shader.PropertyToID("_Strength"), IdIntensity = Shader.PropertyToID("_Intensity"),
        IdTailFall = Shader.PropertyToID("_TailFall"), IdErode = Shader.PropertyToID("_Erode"),
        IdFacingPow = Shader.PropertyToID("_FacingPow"), IdNoiseScale = Shader.PropertyToID("_NoiseScale"),
        IdScroll = Shader.PropertyToID("_Scroll"), IdWSpeed = Shader.PropertyToID("_WSpeed"),
        IdDetail = Shader.PropertyToID("_Detail"), IdDistort = Shader.PropertyToID("_Distort"),
        IdSeed = Shader.PropertyToID("_Seed"), IdWobble = Shader.PropertyToID("_Wobble"),
        IdHead = Shader.PropertyToID("_Head"), IdTail = Shader.PropertyToID("_Tail"), IdFade = Shader.PropertyToID("_Fade"),
        IdLife = Shader.PropertyToID("_Life"), IdEdge = Shader.PropertyToID("_Edge"),
        IdBrightness = Shader.PropertyToID("_Brightness"), IdDensity = Shader.PropertyToID("_Density"),
        IdGlow = Shader.PropertyToID("_Glow"), IdHold = Shader.PropertyToID("_Hold");

    public enum Row { Body = 0, Core = 1, Deep = 2, Flash = 3 }

    public class Flame
    {
        public Transform t;
        public SkinnedMeshRenderer smr;
        public Material mat;
        public float baseI, roarI, strength, ignitePow;
    }

    public class Impact
    {
        public Transform follow, still;
        public Transform flash, ringA, ringB, flare;
        public Material flashMat, ringAMat, ringBMat, flareMat;
        public Transform[] smoke;
        public Material smokeMat;
        public Vector3[] smokeBase, smokeDrift;
        public Vector2[] smokeRadius;
        public ParticleSystem sparks;
        public Light light;
        public Vector3 dir, normalA, normalB, hitPos;
        public Vector3 stackNormal;   // sinking rings: shared disc normal (signed along the strike)
        public float stackGap;        // ... and how far ahead the small ring sits (x scale)
        public float scale, lightPower, hitT = -99f;
    }

    /// <summary>A ribbon rebuilt from sampled points (crescent / comet streak): u along the path, v 0 inner .. 1 outer.</summary>
    public class Ribbon
    {
        public Mesh mesh;
        public GameObject go;
        public Material mat;
    }

    /// <summary>How a ground blast looks (Blender proi_capture.ground_blast).</summary>
    public class BlastStyle
    {
        public float pillar = 1.5f;          // pillar size / blast scale
        public float hold = 5f, die = 22f;   // pillar: frames at full roar / frame it has burnt out by
        public int fires = 4;                // small fires left round the crater
        public float sparks = 150f;          // per unit of scale
        public float scorch = 0.75f;         // scorch radius / blast scale
        public float scorchTime = 4f;        // seconds the scorch stays
        public float light = 8f;
        public float airBurstHeight = 1.2f;  // higher than this above the ground: no pillar / scorch / fires
        public int rings = 0;                // fire rings born round the pillar, climbing it and spreading out
        public float ringGap = 3f;           // frames between them
        public float ringLife = 11f;         // frames each lives
        public float ringSpread = 1f;        // how wide they spread
        public float ringRise = 0.7f;        // how far up the pillar the top ring climbs (fraction of its height)
        public float erupt = 0f;             // 0..1: the pillar bursts out of the ground (faster, overshooting)
    }

    /// <summary>Ground blast: flash + flat shockwaves + flare, a pillar of fire, sparks, smoke, scorch, small fires.</summary>
    public class Blast
    {
        public Impact impact;
        public Transform pillar;
        public Flame[] pillarWrap;
        public Transform[] fires;
        public Flame[] fireFlames;
        public Transform scorch;
        public Material scorchMat;
        public Light light;
        public Transform[] rings;
        public Material[] ringMats;
        public Vector3 groundPos;
        public BlastStyle style;
        public float t = -99f, scale;
        public bool ground;
    }

    public Transform Root { get; private set; }
    public float Brightness = 1f;
    readonly Shader fxShader, smokeShader, emberShader, scorchShader;
    readonly Texture2D ramps, emberTex;
    readonly Dictionary<string, Mesh> meshByName = new Dictionary<string, Mesh>();
    readonly List<Object> owned = new List<Object>();

    ProiBlazeKit()
    {
        fxShader = Resources.Load<Shader>("ProiBlazeFX");
        smokeShader = Resources.Load<Shader>("ProiBlazeSmoke");
        scorchShader = Resources.Load<Shader>("ProiBlazeScorch");
        emberShader = Resources.Load<Shader>("ProiEmber");
        ramps = Resources.Load<Texture2D>("ProiBlazeRamps");
        emberTex = Resources.Load<Texture2D>("ProiEmber");
        GameObject fbx = Resources.Load<GameObject>("Proi_BlazeFX");
        if (fbx == null) return;
        foreach (var mf in fbx.GetComponentsInChildren<MeshFilter>(true)) meshByName[mf.name] = mf.sharedMesh;
        foreach (var sm in fbx.GetComponentsInChildren<SkinnedMeshRenderer>(true)) meshByName[sm.name] = sm.sharedMesh;
    }

    bool Loaded => fxShader != null && smokeShader != null && scorchShader != null && emberShader != null &&
                   ramps != null && emberTex != null && meshByName.Count >= 11;

    /// <summary>Loads the Blaze assets from Resources; null (with a warning) when any is missing.</summary>
    public static ProiBlazeKit Create(string rootName, float brightness, string owner)
    {
        var kit = new ProiBlazeKit { Brightness = brightness };
        if (!kit.Loaded)
        {
            Debug.LogWarning("[" + owner + "] missing Blaze FX assets in Resources - effect disabled");
            return null;
        }
        kit.Root = new GameObject(rootName).transform;      // world space: effects must not ride the root motion
        return kit;
    }

    public void Dispose()
    {
        if (Root != null) Object.Destroy(Root.gameObject);
        foreach (Object o in owned) if (o != null) Object.Destroy(o);
    }

    // ================================================================ materials / parts

    public Material FxMaterial(string name, string mode, Row row, float strength)
    {
        var m = new Material(fxShader) { name = name };
        foreach (string k in new[] { "_MODE_FLAME", "_MODE_SPIRAL", "_MODE_LINES", "_MODE_RING", "_MODE_FLASH", "_MODE_CRESCENT" })
            m.DisableKeyword(k);
        m.EnableKeyword(mode);
        m.SetTexture(IdRamp, ramps);
        m.SetFloat(IdRow, (int)row);
        m.SetFloat(IdStrength, strength);
        m.SetFloat(IdBrightness, Brightness);
        m.SetFloat(IdFacingPow, 0.9f);
        m.SetVector("_AxisOS", AxisOS);
        m.SetVector("_UpOS", UpOS);
        owned.Add(m);
        return m;
    }

    public Material Owned(Material m)
    {
        owned.Add(m);
        return m;
    }

    public static void SetNoise(Material m, Vector3 scale, Vector2 scroll, float seed, float detail = 4f, float distort = 0.3f)
    {
        m.SetVector(IdNoiseScale, scale);
        m.SetVector(IdScroll, scroll);
        m.SetFloat(IdSeed, seed);
        m.SetFloat(IdDetail, detail);
        m.SetFloat(IdDistort, distort);
        m.SetFloat(IdWSpeed, 0.06f);
    }

    public Transform Anchor(string name, float scale = 1f)
    {
        var t = new GameObject(name).transform;
        t.SetParent(Root, false);
        t.localScale = Vector3.one * scale;
        return t;
    }

    public Transform Part(string name, string mesh, Material m, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = meshByName[mesh];
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        go.SetActive(false);
        return go.transform;
    }

    public Flame MakeFlame(string mesh, Transform parent, Row row, Vector3 noise, Vector2 scroll, float tailFall, float erode,
                           float strength, float seed, float radius, float baseI, float roarI, float ignitePow)
    {
        var m = FxMaterial("Blaze_" + mesh + "_" + parent.name, "_MODE_FLAME", row, strength);
        m.SetFloat(IdTailFall, tailFall);
        m.SetFloat(IdErode, erode);
        m.SetFloat(IdWobble, radius * 0.35f);
        m.SetFloat(IdIntensity, -1f);
        SetNoise(m, noise, scroll, seed);
        var go = new GameObject(mesh);
        go.transform.SetParent(parent, false);
        var smr = go.AddComponent<SkinnedMeshRenderer>();
        smr.sharedMesh = meshByName[mesh];
        smr.sharedMaterial = m;
        smr.updateWhenOffscreen = true;
        smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        smr.receiveShadows = false;
        return new Flame { t = go.transform, smr = smr, mat = m, baseI = baseI, roarI = roarI, strength = strength, ignitePow = ignitePow };
    }

    /// <summary>The three nested flame teardrops (Blender build_flame_wrap) on an anchor whose mesh axis points along the flame.</summary>
    public Flame[] MakeWrap(Transform anchor, float seed)
    {
        return new[]
        {
            MakeFlame("BZ_FlameCore", anchor, Row.Core, new Vector3(7, 13, 1), new Vector2(1.2f, 0.2f), 1.1f, 0.75f, 4.5f, 3.1f + seed, 0.085f, 0.22f, 0.38f, 1.3f),
            MakeFlame("BZ_FlameBody", anchor, Row.Body, new Vector3(5, 10, 1), new Vector2(1.0f, 0.25f), 0.95f, 0.6f, 2.8f, 11.4f + seed, 0.13f, 0.22f, 0.42f, 1f),
            MakeFlame("BZ_FlameWisps", anchor, Row.Deep, new Vector3(3.5f, 14, 1), new Vector2(1.4f, 0.35f), 0.75f, 0.5f, 2.0f, 27.9f + seed, 0.15f, 0.12f, 0.4f, 1.15f),
        };
    }

    /// <summary>Flame sheath along a forearm / shin (Blender build_sleeve).</summary>
    public Flame MakeSleeve(Transform anchor, bool leg, float seed)
    {
        return MakeFlame(leg ? "BZ_SleeveLeg" : "BZ_SleeveArm", anchor, Row.Body, new Vector3(4, 12, 1),
                         new Vector2(0.9f, 0.12f), 1.05f, 0.5f, 3.0f, 61f + seed, leg ? 0.08f : 0.072f, 0.3f, 0.5f, 1f);
    }

    public Transform MakeSpiral(string name, Transform anchor, float seed, out Material mat)
    {
        mat = FxMaterial("BlazeSpiral_" + name, "_MODE_SPIRAL", Row.Body, 3f);
        SetNoise(mat, new Vector3(4, 16, 1), new Vector2(1.6f, 0f), 41f + seed);
        return Part("Spiral_" + name, "BZ_Spiral", mat, anchor);
    }

    public Transform MakeLines(string name, Transform anchor, out Material mat)
    {
        mat = FxMaterial("BlazeLines_" + name, "_MODE_LINES", Row.Body, 1.8f);
        return Part("Lines_" + name, "BZ_SpeedLines", mat, anchor);
    }

    public Light MakeLight(string name, Color color, float range)
    {
        var lg = new GameObject(name);
        lg.transform.SetParent(Root, false);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.range = range;
        l.shadows = LightShadows.None;
        l.enabled = false;
        return l;
    }

    public Impact MakeImpact(string name, float scale, int puffs, float lightRange)
    {
        var im = new Impact { scale = scale };
        im.follow = Anchor("BlazeImpact_" + name, scale);
        im.still = Anchor("BlazeImpactPoint_" + name);
        im.flashMat = FxMaterial("BlazeFlash_" + name, "_MODE_FLASH", Row.Flash, 9f);
        im.flashMat.SetFloat(IdFacingPow, 1f);
        im.flash = Part("Flash", "BZ_Flash", im.flashMat, im.follow);
        im.ringAMat = FxMaterial("BlazeRingA_" + name, "_MODE_RING", Row.Body, 4f);
        im.ringBMat = FxMaterial("BlazeRingB_" + name, "_MODE_RING", Row.Core, 5f);
        im.ringAMat.SetFloat(IdSeed, 15f); im.ringBMat.SetFloat(IdSeed, 15f + 2f);
        im.ringA = Part("RingA", "BZ_Ring", im.ringAMat, Root);
        im.ringB = Part("RingB", "BZ_Ring", im.ringBMat, Root);
        im.flareMat = FxMaterial("BlazeFlare_" + name, "_MODE_FLAME", Row.Body, 3.5f);
        im.flareMat.SetFloat(IdTailFall, 1.2f);
        im.flareMat.SetFloat(IdErode, 0.35f);
        SetNoise(im.flareMat, new Vector3(4, 4, 1.4f), new Vector2(0f, 0.4f), 55f);
        im.flare = Part("Flare", "BZ_Flare", im.flareMat, im.follow);
        im.smokeMat = Owned(new Material(smokeShader) { name = "BlazeSmoke_" + name });
        im.smoke = new Transform[puffs];
        im.smokeBase = new Vector3[puffs]; im.smokeDrift = new Vector3[puffs]; im.smokeRadius = new Vector2[puffs];
        for (int i = 0; i < puffs; i++) im.smoke[i] = Part("Smoke" + i, "BZ_Smoke", im.smokeMat, im.still);
        im.sparks = MakeSparks("BlazeSparks_" + name, im.still, scale);
        im.light = MakeLight("BlazeHitLight_" + name, new Color(1f, 0.6f, 0.25f), lightRange);
        im.follow.gameObject.SetActive(false);
        im.still.gameObject.SetActive(false);
        return im;
    }

    public Ribbon MakeRibbon(string name, Row row, float strength, float seed, Vector2 edge)
    {
        var m = FxMaterial("Blaze" + name, "_MODE_CRESCENT", row, strength);
        m.SetFloat(IdSeed, seed);
        m.SetVector(IdEdge, edge);
        m.SetVector(IdScroll, new Vector4(0.05f, 0f, 0f, 0f));
        m.SetFloat(IdWSpeed, 0.1f);
        var mesh = new Mesh { name = "Blaze" + name };
        mesh.MarkDynamic();
        owned.Add(mesh);
        var go = new GameObject("Blaze" + name);
        go.transform.SetParent(Root, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        go.SetActive(false);
        return new Ribbon { mesh = mesh, go = go, mat = m };
    }

    public Blast MakeBlast(string name, int fires, int puffs)
    {
        var b = new Blast { impact = MakeImpact("Blast" + name, 2f, puffs, 5f) };
        b.pillar = Anchor("BlazeBlastPillar" + name);
        b.pillarWrap = MakeWrap(b.pillar, 101f + name.Length * 7f);
        b.pillar.gameObject.SetActive(false);
        b.fires = new Transform[fires];
        b.fireFlames = new Flame[fires];
        for (int k = 0; k < fires; k++)
        {
            b.fires[k] = Anchor("BlazeBlastFire" + name + "_" + k);
            b.fireFlames[k] = MakeFlame("BZ_FlameBody", b.fires[k], Row.Body, new Vector3(5, 10, 1), new Vector2(1.0f, 0.25f),
                                        0.95f, 0.6f, 2.5f, 121f + k * 5f + name.Length * 17f, 0.13f, 0.3f, 0.3f, 1f);
            b.fires[k].gameObject.SetActive(false);
        }
        b.scorch = MakeScorch("Blast" + name, out b.scorchMat);
        b.light = MakeLight("BlazeBlastLight" + name, new Color(1f, 0.55f, 0.2f), 6f);
        b.rings = new Transform[4];
        b.ringMats = new Material[4];
        for (int k = 0; k < b.rings.Length; k++)
        {
            b.ringMats[k] = FxMaterial("BlazePillarRing" + name + "_" + k, "_MODE_RING", k % 2 == 0 ? Row.Body : Row.Core, k % 2 == 0 ? 4.5f : 5.5f);
            b.ringMats[k].SetFloat(IdSeed, 31f + k * 7f + name.Length);
            b.rings[k] = Part("PillarRing" + k, "BZ_Ring", b.ringMats[k], Root);
        }
        return b;
    }

    /// <summary>Sets a blast off at `at` (groundY: the ground under it; viewForward tilts the second shockwave).</summary>
    public void TriggerBlast(Blast b, float t, Vector3 at, float groundY, float scale, Vector3 viewForward, BlastStyle style)
    {
        b.t = t;
        b.scale = scale;
        b.style = style;
        b.ground = at.y - groundY < style.airBurstHeight;
        Vector3 ground = new Vector3(at.x, groundY, at.z);
        Vector3 centre = b.ground ? new Vector3(at.x, Mathf.Max(at.y, groundY + 0.12f), at.z) : at;
        b.groundPos = ground;
        b.impact.follow.localScale = Vector3.one * scale;
        b.impact.scale = scale;
        Vector3 side = Vector3.Cross(Vector3.up, viewForward);
        Vector3 na = Vector3.up, nb = Quaternion.AngleAxis(14f, side.sqrMagnitude > 1e-4f ? side : Vector3.right) * Vector3.up;
        if (!b.ground) CameraTilted(centre, Vector3.up, out na, out nb);
        TriggerImpact(b.impact, t, centre, Vector3.up, na, nb, Mathf.RoundToInt(style.sparks * scale), style.light * 0.5f);
        b.pillar.SetPositionAndRotation(ground + Vector3.up * 0.05f, Look(Vector3.down, Vector3.forward, Vector3.right));
        b.pillar.localScale = Vector3.one * scale * style.pillar;
        for (int k = 0; k < b.fires.Length; k++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            Vector3 p = ground + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Random.Range(0.3f, 0.6f) * scale + Vector3.up * 0.02f;
            b.fires[k].SetPositionAndRotation(p, Look(Vector3.down, Vector3.forward, Vector3.right));
            b.fires[k].localScale = Vector3.one * scale * Random.Range(0.45f, 0.65f);
        }
        b.scorch.SetPositionAndRotation(ground + Vector3.up * 0.01f, Quaternion.FromToRotation(Vector3.forward, Vector3.up));
        b.scorch.localScale = Vector3.one * style.scorch * scale;
        b.scorchMat.SetFloat(IdSeed, Random.Range(0f, 50f));
        b.light.transform.position = centre + Vector3.up * 0.3f;
    }

    public void UpdateBlast(Blast b, float t)
    {
        if (b.style == null) return;
        BlastStyle st = b.style;
        float h = (t - b.t) * 30f;
        UpdateImpact(b.impact, t, b.impact.hitPos);
        bool live = b.t > 0f && b.ground;
        bool pillarOn = live && h < st.die + 4f;
        b.pillar.gameObject.SetActive(pillarOn);
        if (pillarOn)
        {   // flashes up in 2 frames, roars for `hold`, burns out by `die` (Blender build_flame_burst).
            // erupt: bursts out of the ground in a frame and overshoots before settling into its roar
            float[] peaks = { 0.47f, 0.55f, 0.5f };
            float e = st.erupt;
            for (int i = 0; i < b.pillarWrap.Length; i++)
            {
                float lag = i * (1f - 0.6f * e);
                float rise = Mathf.Lerp(2f, 0.8f, e);
                float upK = EaseOut(Seg(h, lag * 0.5f, rise + lag * 0.5f)), down = EaseIn(Seg(h, 2f + st.hold, st.die + lag), 1.6f);
                float pk = peaks[i] + 0.9f + e * 0.3f * (1f - Smooth(Seg(h, 2f, 9f)));
                b.pillarWrap[i].mat.SetFloat(IdIntensity, -0.9f + pk * upK - pk * down + 0.04f * Flick(t, 51f + lag));
                float stretch = EaseOut(Seg(h, 0f, Mathf.Lerp(4f, 1.5f, e) + lag)) * (1f - 0.5f * Seg(h, 2f + st.hold, st.die));
                b.pillarWrap[i].smr.SetBlendShapeWeight(0, stretch * 100f);
                b.pillarWrap[i].mat.SetFloat(IdStrength, b.pillarWrap[i].strength * (1f + e * 0.8f * (1f - Seg(h, 0f, 8f))));
            }
        }
        for (int k = 0; k < b.rings.Length; k++)
        {   // fire rings born round the pillar one after another: they climb it and spread out as they burn away
            float hk = h - k * st.ringGap;
            bool on = live && k < st.rings && hk >= 0f && hk <= st.ringLife;
            b.rings[k].gameObject.SetActive(on);
            if (!on) continue;
            float q = hk / st.ringLife;
            float height = 1.15f * b.scale * st.pillar;
            float top = height * st.ringRise * (st.rings > 1 ? Mathf.Lerp(0.45f, 1f, k / (float)(st.rings - 1)) : 1f);
            float y = Mathf.Lerp(0.12f, top, EaseOut(q, 2f));
            float r = Mathf.Lerp(0.1f, 0.42f * st.ringSpread, EaseOut(q, 3f)) * b.scale * (1f + 0.2f * k);
            Quaternion tilt = Quaternion.Euler(6f * Mathf.Sin(k * 2.1f), k * 47f, 6f * Mathf.Cos(k * 1.7f));
            b.rings[k].SetPositionAndRotation(b.groundPos + Vector3.up * y, tilt * Quaternion.FromToRotation(Vector3.forward, Vector3.up));
            b.rings[k].localScale = Vector3.one * r;
            b.ringMats[k].SetFloat(IdFade, Smooth(Seg(q, 0f, 0.12f)) * (1f - EaseIn(q, 1.4f)));
        }
        for (int k = 0; k < b.fires.Length; k++)
        {   // small fires left burning round the crater
            float hk = h - (3f + 2f * k), hold = 10f + 4f * k, die = 34f + 5f * k;
            bool on = live && k < st.fires && hk > -1f && hk < die + 1f;
            b.fires[k].gameObject.SetActive(on);
            if (!on) continue;
            float pk = 0.42f + 0.9f;
            float upK = EaseOut(Seg(hk, 0f, 3f)), down = EaseIn(Seg(hk, 3f + hold, die), 1.6f);
            b.fireFlames[k].mat.SetFloat(IdIntensity, -0.9f + pk * upK - pk * down + 0.04f * Flick(t, 61f + k));
            b.fireFlames[k].smr.SetBlendShapeWeight(0, EaseOut(Seg(hk, 0f, 5f)) * (1f - 0.5f * Seg(hk, 3f + hold, die)) * 100f);
        }
        float age = t - b.t;
        bool scorchOn = live && st.scorch > 0f && age < st.scorchTime + 1f;
        b.scorch.gameObject.SetActive(scorchOn);
        if (scorchOn)
        {
            b.scorchMat.SetFloat(IdFade, EaseOut(Seg(h, 0f, 3f)) * (1f - Smooth(Seg(age, st.scorchTime, st.scorchTime + 1f))));
            b.scorchMat.SetFloat(IdGlow, 1f - EaseOut(Seg(h, 0f, 25f), 2f));
        }
        b.light.intensity = b.t > 0f ? st.light * b.scale * (1f - EaseOut(Seg(h, 0f, 12f), 2f)) * Brightness : 0f;
        b.light.enabled = b.light.intensity > 0.01f;
    }

    /// <summary>Alpha-blended burnt patch on the ground (Proi/BlazeScorch on the shockwave disc).</summary>
    public Transform MakeScorch(string name, out Material mat)
    {
        mat = Owned(new Material(scorchShader) { name = "BlazeScorch_" + name });
        return Part("Scorch_" + name, "BZ_Ring", mat, Root);
    }

    // ================================================================ particles

    Material EmberMaterial(string name)
    {
        var m = new Material(emberShader) { name = name };
        m.SetTexture("_MainTex", emberTex);
        m.SetFloat("_Intensity", 2.4f * Brightness);
        return Owned(m);
    }

    ParticleSystem NewParticles(string name, Transform parent, out ParticleSystemRenderer r)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;
        main.maxParticles = 800;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = EmberMaterial(name);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    static Gradient Fade()
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.6f, 0.4f), 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        return g;
    }

    /// <summary>Blender PS_Embers: slow drift up and back, a little wander, 16 frames (+-50%). Emission rate set by the caller.</summary>
    public ParticleSystem MakeEmbers(string name, Color color, float radius = 0.08f)
    {
        var ps = NewParticles(name, Root, out var r);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(8f * F, 24f * F);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.022f);
        main.startColor = color;
        main.gravityModifier = -0.04f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = radius;
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.25f;
        noise.frequency = 2f;
        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(Fade());
        var em = ps.emission;
        em.rateOverTime = 0f;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        ps.Play();
        return ps;
    }

    /// <summary>Blender PS_TrailSparks: shed behind a fist / foot / fireball while it flies (Play / Stop by the caller).</summary>
    public ParticleSystem MakeTrailSparks(string name, float rate = 300f)
    {
        var ps = NewParticles(name, Root, out var r);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(4f * F, 12f * F);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.014f);
        main.startColor = new Color(1f, 0.5f, 0.12f);
        main.gravityModifier = 0.2f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.07f;
        var em = ps.emission;
        em.rateOverTime = rate;
        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(Fade());
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.04f;
        r.lengthScale = 1.5f;
        return ps;
    }

    /// <summary>Blender PS_ImpactSparks: a burst forward and out, a third of gravity, 12 frames (+-60%). Emit() by the caller.</summary>
    public ParticleSystem MakeSparks(string name, Transform parent, float scale)
    {
        var ps = NewParticles(name, parent, out var r);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(4f * F, 14f * F);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f * scale, 2.6f * scale);
        main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.014f);
        main.startColor = new Color(1f, 0.5f, 0.12f);
        main.gravityModifier = 0.35f;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 70f;
        shape.radius = 0.04f;
        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.drag = 2.5f;
        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(Fade());
        var em = ps.emission;
        em.rateOverTime = 0f;
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.02f;
        r.lengthScale = 1.2f;
        ps.Play();
        return ps;
    }

    // ================================================================ per frame

    /// <summary>Flame-wrap shells: burn 0..1 ignition, roar 0..1 (strike), hitBoost 0..1 (just hit), stretch 0..1 (tail length).</summary>
    public static void SetWrap(Flame[] wrap, float burn, float roar, float hitBoost, float stretch, float flick)
    {
        foreach (Flame fl in wrap)
        {
            float ig = EaseOut(Mathf.Pow(Mathf.Clamp01(burn), fl.ignitePow));
            fl.mat.SetFloat(IdIntensity, Mathf.Lerp(-0.8f, fl.baseI, ig) + (fl.roarI - fl.baseI) * roar + 0.04f * flick);
            fl.mat.SetFloat(IdStrength, fl.strength * (1f + 0.6f * hitBoost));
            fl.smr.SetBlendShapeWeight(0, Mathf.Clamp01(stretch) * 100f);
        }
    }

    /// <summary>Starts an impact at `at`. normalA / normalB: the two shockwave discs' normals.</summary>
    /// <summary>ringAxis: when given, the sinking rings stand upright facing straight along it (no tilting toward the camera).</summary>
    public void TriggerImpact(Impact im, float t, Vector3 at, Vector3 dir, Vector3 normalA, Vector3 normalB, int sparks, float lightPower,
                              Vector3? ringAxis = null)
    {
        im.dir = dir;
        im.hitT = t;
        im.hitPos = at;
        im.lightPower = lightPower;
        im.still.position = at;
        im.follow.position = at;
        Quaternion q = Look(dir, Vector3.up, Vector3.forward);
        im.follow.rotation = q;
        im.still.rotation = q;
        im.normalA = normalA;
        im.normalB = normalB;
        if (ringAxis.HasValue) UprightRings(at, ringAxis.Value, out im.stackNormal, out im.stackGap);
        else StackedRings(at, dir, out im.stackNormal, out im.stackGap);
        im.still.gameObject.SetActive(true);
        im.sparks.transform.SetPositionAndRotation(at, Quaternion.LookRotation(dir, Mathf.Abs(dir.y) > 0.95f ? Vector3.forward : Vector3.up));
        if (!im.sparks.isPlaying) im.sparks.Play();
        if (sparks > 0) im.sparks.Emit(sparks);
        for (int i = 0; i < im.smoke.Length; i++)
        {
            im.smokeBase[i] = at - dir * Random.Range(0f, 0.25f) * im.scale +
                              new Vector3(Random.Range(-0.1f, 0.1f), Random.Range(-0.03f, 0.06f), Random.Range(-0.1f, 0.1f)) * im.scale;
            im.smokeDrift[i] = new Vector3(Random.Range(-0.05f, 0.08f), Random.Range(0.12f, 0.25f), Random.Range(-0.05f, 0.05f)) * im.scale;
            im.smokeRadius[i] = new Vector2(Random.Range(0.04f, 0.06f), Random.Range(0.12f, 0.2f)) * im.scale;
        }
    }

    /// <summary>Shockwave normals for a strike seen by the camera: tilted from the strike direction toward the viewer.</summary>
    public static void CameraTilted(Vector3 at, Vector3 dir, out Vector3 a, out Vector3 b)
    {
        Camera cam = Camera.main;
        Vector3 toCam = cam != null ? (cam.transform.position - at).normalized : -dir;
        a = Vector3.Slerp(dir, toCam, 0.35f).normalized;
        b = Vector3.Slerp(dir, toCam, 0.55f).normalized;
    }

    const float StackBig = 0.34f, StackSmall = 0.15f;

    /// <summary>
    /// Sinking rings: a big ring at the fist and a small one ahead, parallel along `normal`.
    /// Tilted toward the viewer only as far as the gap can still keep their outlines apart on screen;
    /// seen end-on (strike toward / away from the camera) they nest instead, small inside big.
    /// </summary>
    public static void StackedRings(Vector3 at, Vector3 dir, out Vector3 normal, out float gap)
    {
        Camera cam = Camera.main;
        Vector3 view = cam != null ? (at - cam.transform.position).normalized : dir;
        const float k = 1.15f * (StackBig + StackSmall);                   // apart when gap*sin >= k*cos
        const float openLo = 0.38f, openHi = 0.49f, nestLo = 0.8f;       // |cos| bands: open + apart / nested
        Vector3 axis = Vector3.Cross(dir, view);
        normal = dir;
        float best = 1e9f, bestC = Mathf.Abs(Vector3.Dot(dir, view));
        bool nested = true;
        if (axis.sqrMagnitude > 1e-6f)
        {   // turn the strike direction (toward / away from the viewer) by the least angle that lands in a band
            axis.Normalize();
            for (float deg = -45f; deg <= 45.01f; deg += 1.5f)
            {
                Vector3 n = Quaternion.AngleAxis(deg, axis) * dir;
                float c = Mathf.Abs(Vector3.Dot(n, view));
                bool open = c >= openLo && c <= openHi, nest = c >= nestLo;
                if (!open && !nest) continue;
                float score = Mathf.Abs(deg) + (open ? 0f : 4f);           // a slight preference for side by side
                if (score >= best) continue;
                best = score; normal = n; bestC = c; nested = nest;
            }
        }
        float sn = Mathf.Sqrt(Mathf.Max(1f - bestC * bestC, 1e-6f));
        gap = nested ? Mathf.Clamp(0.7f * (StackBig - StackSmall) * bestC / sn, 0.08f, 0.3f)
                     : Mathf.Clamp(k * bestC / sn, 0.2f, 0.34f);
    }

    /// <summary>
    /// Sinking rings standing upright, facing straight along `axis` (flattened). Only the gap adapts to the view:
    /// side by side when it can keep their outlines apart, otherwise nested, small inside big.
    /// </summary>
    public static void UprightRings(Vector3 at, Vector3 axis, out Vector3 normal, out float gap)
    {
        axis.y = 0f;
        normal = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.forward;
        Camera cam = Camera.main;
        Vector3 view = cam != null ? (at - cam.transform.position).normalized : normal;
        float c = Mathf.Abs(Vector3.Dot(normal, view)), sn = Mathf.Sqrt(Mathf.Max(1f - c * c, 1e-6f));
        float apart = 1.15f * (StackBig + StackSmall) * c / sn;
        gap = apart <= 0.45f ? Mathf.Clamp(apart, 0.2f, 0.45f)
                             : Mathf.Clamp(0.7f * (StackBig - StackSmall) * c / sn, 0.08f, 0.3f);
    }

    /// <summary>Animates an impact (Blender curves, frames since contact). followPos: where the flash / rings sit now.</summary>
    /// <summary>sink: when given, the shockwaves spread, then draw back into this point (the fist / foot) as they die.</summary>
    public void UpdateImpact(Impact im, float t, Vector3 followPos, Vector3? sink = null)
    {
        float h = (t - im.hitT) * 30f;
        bool any = im.hitT > 0f && h < 40f;
        im.follow.gameObject.SetActive(any && h < 12f);
        im.still.gameObject.SetActive(any);
        if (!any) { im.light.enabled = false; return; }
        if (h <= 12f) im.follow.position = followPos;

        bool flashOn = h <= 6f;
        im.flash.gameObject.SetActive(flashOn);
        if (flashOn)
        {
            im.flash.localScale = Vector3.one * (Mathf.Lerp(0.02f, 0.075f, EaseOut(Seg(h, 0f, 2f))) + 0.03f * Seg(h, 2f, 6f));
            im.flashMat.SetFloat(IdFade, 1f - EaseOut(Seg(h, 0f, 6f), 2f));
        }
        if (sink.HasValue)
        {   // big ring at the fist, small one out in front; parallel and apart so they never cross,
            // both drawing back into the fist / foot (and shrinking) as they die
            Vector3 ahead = im.stackNormal * im.follow.lossyScale.x;
            Ring(im.ringA, im.ringAMat, im.stackNormal, h, 0f, 13f, 0.04f, StackBig, 1.4f, im.follow, sink, ahead * -0.02f);
            Ring(im.ringB, im.ringBMat, im.stackNormal, h, 1f, 13.5f, 0.02f, StackSmall, 1.2f, im.follow, sink, ahead * im.stackGap);
        }
        else
        {
            Ring(im.ringA, im.ringAMat, im.normalA, h, 0f, 9f, 0.04f, 0.34f, 1.4f, im.follow, null, Vector3.zero);
            Ring(im.ringB, im.ringBMat, im.normalB, h, 1f, 11f, 0.03f, 0.22f, 1.2f, im.follow, null, Vector3.zero);
        }

        bool flareOn = h <= 11f;
        im.flare.gameObject.SetActive(flareOn);
        if (flareOn)
        {
            float a = Mathf.Lerp(0.03f, 0.09f, EaseOut(Seg(h, 0f, 5f)));
            float r = Mathf.Lerp(0.05f, 0.17f, EaseOut(Seg(h, 0f, 6f)));
            im.flare.localScale = new Vector3(a, r, r);                  // mesh X = flame axis
            im.flare.localPosition = AxisOS * -0.03f;
            im.flare.localRotation = Quaternion.AngleAxis(0.12f * Mathf.Rad2Deg * t * 30f, AxisOS);
            im.flareMat.SetFloat(IdIntensity, Mathf.Lerp(0.55f, -0.7f, Mathf.Pow(Seg(h, 1f, 10f), 1.5f)));
        }

        for (int i = 0; i < im.smoke.Length; i++)
        {
            bool on = h >= 2f;
            im.smoke[i].gameObject.SetActive(on);
            if (!on) continue;
            float k = Seg(h, 2f, 40f);
            im.smoke[i].position = im.smokeBase[i] + im.smokeDrift[i] * EaseOut(k, 2f);
            im.smoke[i].localScale = Vector3.one * Mathf.Lerp(im.smokeRadius[i].x, im.smokeRadius[i].y, EaseOut(k, 2f));
        }
        im.smokeMat.SetFloat(IdDensity, Mathf.Lerp(-0.6f, 0.15f, EaseOut(Seg(h, 2f, 10f))) - 0.75f * Mathf.Pow(Seg(h, 14f, 40f), 1.2f));

        im.light.transform.position = im.follow.position;
        im.light.intensity = im.lightPower * im.scale * (1f - EaseOut(Seg(h, 0f, 7f), 2f)) * Brightness;
        im.light.enabled = im.light.intensity > 0.01f;
    }

    static void Ring(Transform ring, Material m, Vector3 normal, float h, float a, float b, float s0, float s1, float fadePow,
                     Transform parent, Vector3? sink, Vector3 offset)
    {
        bool on = h >= a && h <= b;
        ring.gameObject.SetActive(on);
        if (!on) return;
        float k = Seg(h, a, b);
        float r = Mathf.Lerp(s0, s1, EaseOut(k, 3f)) * parent.lossyScale.x;
        Vector3 pos = parent.position + offset;
        if (sink.HasValue)
        {   // spread out fast, then draw back toward the fist / foot, shrinking, as the fire is pulled in;
            // the gap between the rings closes with them so the front one comes back too
            float back = Smooth(Seg(k, 0.45f, 1f));
            r = Mathf.Lerp(s0, s1, EaseOut(Seg(k, 0f, 0.5f), 3f)) * Mathf.Lerp(1f, 0.3f, back) * parent.lossyScale.x;
            pos = Vector3.Lerp(parent.position, sink.Value, back * 0.45f) + offset * Mathf.Lerp(1f, 0.3f, back);
        }
        ring.SetPositionAndRotation(pos, Quaternion.FromToRotation(Vector3.forward, normal));   // disc normal = mesh +Z
        ring.localScale = Vector3.one * r;                                 // rings live under the unscaled FX root
        m.SetFloat(IdFade, 1f - Mathf.Pow(k, fadePow));
    }

    /// <summary>Rebuilds a ribbon between two point lists (us = position along it, 0..1+).</summary>
    public static void BuildRibbon(Mesh mesh, IList<Vector3> outer, IList<Vector3> inner, IList<float> us)
    {
        int n = outer.Count;
        var v = new Vector3[n * 2];
        var uv = new Vector2[n * 2];
        var tri = new int[(n - 1) * 6];
        for (int i = 0; i < n; i++)
        {
            v[i * 2] = inner[i]; v[i * 2 + 1] = outer[i];
            uv[i * 2] = new Vector2(us[i], 0f); uv[i * 2 + 1] = new Vector2(us[i], 1f);
            if (i == 0) continue;
            int k = (i - 1) * 2, j = (i - 1) * 6;
            tri[j] = k; tri[j + 1] = k + 2; tri[j + 2] = k + 3;
            tri[j + 3] = k; tri[j + 4] = k + 3; tri[j + 5] = k + 1;
        }
        mesh.Clear();
        mesh.vertices = v;
        mesh.uv = uv;
        mesh.triangles = tri;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    // ================================================================ helpers

    /// <summary>Rotation that points the Blender meshes' flame axis along dir.</summary>
    public static Quaternion Look(Vector3 dir, Vector3 up, Vector3 fallbackUp)
    {
        Vector3 hint = Mathf.Abs(Vector3.Dot(dir.normalized, up)) > 0.97f ? fallbackUp : up;
        return Quaternion.LookRotation(dir, hint) * MeshBasisInv;
    }

    public static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t)
        {
            Transform f = FindDeep(c, name);
            if (f != null) return f;
        }
        return null;
    }

    public static float Flick(float t, float seed) => Mathf.PerlinNoise(t * 39f, seed * 7.3f) * 2f - 1f;
    public static float Seg(float x, float a, float b) => Mathf.Clamp01((x - a) / (b - a));
    public static float Smooth(float t) => t * t * (3f - 2f * t);
    public static float EaseOut(float t, float p = 3f) => 1f - Mathf.Pow(1f - t, p);
    public static float EaseIn(float t, float p = 2f) => Mathf.Pow(t, p);
}
