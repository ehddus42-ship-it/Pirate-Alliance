using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 대쉬(ProiDash)용 셀 화염 이펙트: 별 섬광(ProiImpactStar 플립북), 불티(ProiEmber), 셀 불꽃 파티클(ProiFlameCel 플립북).
/// ProiDash 가 같은 오브젝트의 이 컴포넌트를 불러 씁니다.
/// 셰이더 / 텍스처는 Resources 에서 읽고, 하나라도 없으면 Ready = false 로 아무것도 그리지 않습니다.
/// </summary>
public class ProiFireFx : MonoBehaviour
{
    [SerializeField] float flameSize = 0.32f;

    public bool Ready { get; private set; }
    public float FlameSize => flameSize;

    Shader spriteShader;
    Texture2D starTex;
    Material emberMat, flameMat;
    ParticleSystem embers, flames;
    Camera cam;

    class FlashFx { public GameObject go; public Material mat; public Mesh mesh; public Vector3 pos; public float start, size, rot; public bool active; }

    readonly List<FlashFx> flashes = new List<FlashFx>();

    void Awake()
    {
        spriteShader = Resources.Load<Shader>("ProiFireSprite");
        Shader emberShader = Resources.Load<Shader>("ProiEmber");
        starTex = Resources.Load<Texture2D>("ProiImpactStar");
        Texture2D emberTex = Resources.Load<Texture2D>("ProiEmber");
        Texture2D flameTex = Resources.Load<Texture2D>("ProiFlameCel");
        if (spriteShader == null || emberShader == null || starTex == null || emberTex == null || flameTex == null)
        {
            Debug.LogWarning("[ProiFireFx] missing fire assets in Resources - dash fire effects disabled");
            return;
        }
        emberMat = new Material(emberShader) { name = "ProiFxEmber" };
        emberMat.SetTexture("_MainTex", emberTex);
        flameMat = new Material(emberShader) { name = "ProiFxFlame" };
        flameMat.SetTexture("_MainTex", flameTex);
        flameMat.SetFloat("_Intensity", 1.4f);
        embers = MakeEmbers();
        flames = MakeFlames();
        Ready = true;
    }

    void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        UpdateFlashes();
    }

    // ------------------------------------------------------------------ particles

    public void Embers(Vector3 at, int count, float minSpeed, float maxSpeed, Vector3 lift)
    {
        if (!Ready) return;
        for (int k = 0; k < count; k++)
        {
            var ep = new ParticleSystem.EmitParams
            {
                position = at + Random.insideUnitSphere * 0.08f,
                velocity = Random.onUnitSphere * Random.Range(minSpeed, maxSpeed) + lift,
                applyShapeToPosition = false,
            };
            embers.Emit(ep, 1);
        }
    }

    public void Flame(Vector3 pos, Vector3 vel, float size, float life)
    {
        if (!Ready) return;
        var ep = new ParticleSystem.EmitParams
        {
            position = pos, velocity = vel, startSize = size, startLifetime = life,
            rotation = Random.Range(-15f, 15f), applyShapeToPosition = false,
        };
        flames.Emit(ep, 1);
    }

    // ------------------------------------------------------------------ flashes (cel star flipbook)

    public void Flash(Vector3 pos, float size)
    {
        if (!Ready) return;
        FlashFx f = flashes.Find(x => !x.active);
        if (f == null)
        {
            f = new FlashFx();
            f.mat = new Material(spriteShader) { name = "ProiFxFlash" };
            f.mat.SetTexture("_MainTex", starTex);
            f.mat.SetFloat("_Intensity", 1.9f);
            f.mat.SetFloat("_PivotU", 0f); f.mat.SetFloat("_Reveal", 1f); f.mat.SetFloat("_TailAlpha", 1f);
            f.mat.SetFloat("_Distort", 0f); f.mat.SetFloat("_OccludedAlpha", 0.6f);
            f.mesh = new Mesh { name = "ProiFxFlashQuad" };
            f.mesh.vertices = new Vector3[4]; f.mesh.uv = new Vector2[4]; f.mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            f.go = new GameObject("ProiFxFlash");
            f.go.AddComponent<MeshFilter>().sharedMesh = f.mesh;
            var mr = f.go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = f.mat; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            flashes.Add(f);
        }
        f.pos = pos; f.size = size * Random.Range(0.92f, 1.08f); f.rot = Random.Range(0f, 360f);
        f.start = Time.time; f.active = true;
        f.go.SetActive(true);
    }

    void UpdateFlashes()
    {
        foreach (FlashFx f in flashes)
        {
            if (!f.active) continue;
            int frame = Mathf.FloorToInt((Time.time - f.start) / 0.04f);
            if (frame > 3) { f.active = false; f.go.SetActive(false); continue; }
            float u0 = (frame % 2) * 0.5f, v0 = 0.5f - (frame / 2) * 0.5f;
            f.mesh.uv = new[] { new Vector2(u0, v0), new Vector2(u0 + 0.5f, v0), new Vector2(u0 + 0.5f, v0 + 0.5f), new Vector2(u0, v0 + 0.5f) };
            Transform c = cam != null ? cam.transform : transform;
            float a = f.rot * Mathf.Deg2Rad;
            Vector3 ix = Mathf.Cos(a) * c.right + Mathf.Sin(a) * c.up, iy = -Mathf.Sin(a) * c.right + Mathf.Cos(a) * c.up;
            float s = f.size * (frame == 0 ? 0.8f : 1f) * 0.5f;
            f.go.transform.SetPositionAndRotation(f.pos, Quaternion.identity);
            f.mesh.vertices = new[] { (-ix - iy) * s, (ix - iy) * s, (ix + iy) * s, (-ix + iy) * s };
            f.mesh.RecalculateBounds();
        }
    }

    // ------------------------------------------------------------------ building

    ParticleSystem MakeEmbers()
    {
        var go = new GameObject("ProiFxEmbers");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true; main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 1500;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 1.1f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
        main.gravityModifier = -0.12f;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.8f), new Color(1f, 0.6f, 0.45f));
        var em = ps.emission; em.enabled = false;
        var sh = ps.shape; sh.enabled = false;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.55f, 0.35f), 0.5f),
                          new GradientColorKey(new Color(0.9f, 0.15f, 0.12f), 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var noise = ps.noise; noise.enabled = true; noise.strength = 0.6f; noise.frequency = 1.8f;
        var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.drag = 2.5f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.material = emberMat;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        ps.Play();
        return ps;
    }

    ParticleSystem MakeFlames()
    {   // Blender cel flame flipbook (ProiFlameCel.png 4x4): one flame rising, splitting and dying over a particle's life
        var go = new GameObject("ProiFxFlames");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true; main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 1200;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.45f);
        main.startSpeed = 0f;
        main.startSize = flameSize;
        main.gravityModifier = -0.45f;
        main.startColor = new ParticleSystem.MinMaxGradient(Color.white, new Color(1f, 0.86f, 0.82f));
        var em = ps.emission; em.enabled = false;
        var sh = ps.shape; sh.enabled = false;
        var sheet = ps.textureSheetAnimation;
        sheet.enabled = true; sheet.mode = ParticleSystemAnimationMode.Grid;
        sheet.numTilesX = 4; sheet.numTilesY = 4;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 0.999f));
        sheet.cycleCount = 1;
        var size = ps.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.75f), new Keyframe(1f, 1.15f)));
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var noise = ps.noise; noise.enabled = true; noise.strength = 0.35f; noise.frequency = 2.2f;
        var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.drag = 2.5f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.material = flameMat;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        ps.Play();
        return ps;
    }

    void OnDestroy()
    {
        if (emberMat != null) Destroy(emberMat);
        if (flameMat != null) Destroy(flameMat);
        foreach (FlashFx f in flashes) { Destroy(f.mat); Destroy(f.mesh); Destroy(f.go); }
    }
}
