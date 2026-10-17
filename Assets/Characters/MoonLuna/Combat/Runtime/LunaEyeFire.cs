using System.Collections.Generic;
using MoonLuna.Motion;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoonLuna
{
    /// <summary>Soft pale-gold eye radiance, with translucent pearl/lavender afterimages in world space.</summary>
    [DefaultExecutionOrder(260)]
    [DisallowMultipleComponent]
    public sealed class LunaEyeFire : MonoBehaviour
    {
        public Material flameMaterial;
        public Material wispMaterial;
        public Camera CaptureCamera;

        public bool Active => requestedActive && UltimateActive && isActiveAndEnabled && visual != null
            && visual.gameObject.activeInHierarchy && flameMaterial != null && effectRoot != null
            && effectRoot.gameObject.activeInHierarchy;
        public int VisibleRendererCount
        {
            get
            {
                int count = 0;
                foreach (var flame in flames) if (Visible(flame)) count++;
                foreach (var particle in particles)
                    if (particle != null && Visible(particle.sprite)) count++;
                return count;
            }
        }
        public int LiveParticleCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < particles.Length; i++)
                    if (particles[i] != null && particles[i].live) count++;
                return count;
            }
        }
        public IReadOnlyList<Transform> EyeAnchors => eyeAnchors;
        public Transform RightEyeAnchor => eyeAnchors[0];
        public Transform LeftEyeAnchor => eyeAnchors[1];
        public Transform Visual => visual;

        // Measured from the original battle FBX's textured pupils. Visual uses metres;
        // the deeper MoonLuna_Toon node has a 100x scale and a -90 degree X rotation.
        static readonly Vector3[] EyeOffsets =
        {
            new Vector3(0.0366f, 1.5633f, 0.07941f),
            new Vector3(-0.0366f, 1.5633f, 0.07946f)
        };
        static readonly int TintId = Shader.PropertyToID("_Tint");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        const int FlameCapacity = 48;
        const int WispCapacity = 10;

        sealed class Sprite
        {
            public Transform transform;
            public MeshRenderer renderer;
        }

        sealed class Remnant
        {
            public Sprite sprite;
            public Vector3 position, velocity, rise;
            public float age, lifetime, width, height;
            public int eye;
            public bool live, wisp;
        }

        readonly Transform[] eyeAnchors = new Transform[2];
        readonly Vector3[] lastEyePositions = new Vector3[2];
        readonly Sprite[] flames = new Sprite[4];
        readonly Remnant[] particles = new Remnant[FlameCapacity + WispCapacity];
        MaterialPropertyBlock properties;
        Transform visual, effectRoot;
        LunaMoonGait gait;
        LunaCombatController combat;
        Mesh bottomPivotQuad, flameTongue, irisCore;
        Vector3 lastRootPosition, planarVelocity;
        float flameEmission, wispEmission, clock;
        int flameCursor, wispCursor;
        bool requestedActive, hasHistory;

        bool UltimateActive
        {
            get
            {
                if (combat == null) combat = GetComponent<LunaCombatController>();
                return combat != null && combat.IsAwakened;
            }
        }

        static bool Visible(Sprite sprite) => sprite != null && sprite.renderer != null
            && sprite.renderer.enabled && sprite.renderer.gameObject.activeInHierarchy;

        // Preview framing can locate the eyes without briefly enabling their effect.
        public bool PrepareAnchors() => EnsureVisual();

        public void SetActiveFire(bool active)
        {
            // The actual ultimate timer owns permission to emit, including direct VFX calls.
            active = active && isActiveAndEnabled && UltimateActive;
            bool starting = active && !requestedActive;
            requestedActive = active;
            if (!active)
            {
                ClearEffect();
                return;
            }
            if (!isActiveAndEnabled || !EnsureVisual()) return;
            EnsureSprites();
            if (starting) ResetHistory();
            effectRoot.gameObject.SetActive(true);
            // Materials are normally assigned after AddComponent/Awake, so never cache
            // their absence and never instantiate replacement materials here.
            UpdateFlames(EffectCamera(), ModelScale());
        }

        void OnEnable() => SetActiveFire(false);

        void OnDisable()
        {
            requestedActive = false;
            ClearEffect();
        }

        void OnDestroy()
        {
            ClearEffect();
            DestroyAnchors();
            if (effectRoot != null) Destroy(effectRoot.gameObject);
            if (bottomPivotQuad != null) Destroy(bottomPivotQuad);
            if (flameTongue != null) Destroy(flameTongue);
            if (irisCore != null) Destroy(irisCore);
        }

        bool EnsureVisual()
        {
            if (gait == null)
            {
                gait = GetComponent<LunaMoonGait>();
                if (gait == null) gait = GetComponentInParent<LunaMoonGait>();
                if (gait == null) gait = GetComponentInChildren<LunaMoonGait>(true);
            }
            Transform next = gait != null ? gait.visual : transform.Find("Visual");
            // Never attach combat eyes to the peaceful humanoid when a form is replaced.
            if (next != null && next.Find("MoonLuna_Toon/Head/Face_Head") == null)
                next = null;
            if (next != visual)
            {
                ClearEffect();
                DestroyAnchors();
                visual = next;
            }
            if (visual == null || !visual.gameObject.activeInHierarchy) return false;
            for (int i = 0; i < eyeAnchors.Length; i++)
            {
                if (eyeAnchors[i] != null) continue;
                var anchor = new GameObject(i == 0 ? "LunaRightEyeFireAnchor" : "LunaLeftEyeFireAnchor");
                anchor.layer = gameObject.layer;
                eyeAnchors[i] = anchor.transform;
                eyeAnchors[i].SetParent(visual, false);
                eyeAnchors[i].localPosition = EyeOffsets[i];
            }
            return true;
        }

        void DestroyAnchors()
        {
            for (int i = 0; i < eyeAnchors.Length; i++)
            {
                if (eyeAnchors[i] != null) Destroy(eyeAnchors[i].gameObject);
                eyeAnchors[i] = null;
            }
        }

        void EnsureSprites()
        {
            if (effectRoot != null) return;
            properties = new MaterialPropertyBlock();
            if (bottomPivotQuad != null) Destroy(bottomPivotQuad);
            if (flameTongue != null) Destroy(flameTongue);
            if (irisCore != null) Destroy(irisCore);
            bottomPivotQuad = BuildSpriteMesh("LunaEyeWispQuad", new Vector4(0, 0, 1, 1), false);
            // The source's alpha bounds are U .297-.722, V .057-.965, with the
            // bright point at (.50,.065). Normalise that point to the emitter.
            flameTongue = BuildSpriteMesh("LunaEyeFlameTongue", new Vector4(.29f, .065f, .73f, .965f), true);
            irisCore = BuildIrisCore(new Vector4(.46f, .07f, .535f, .17f));
            // Owned by this component but deliberately outside the moving hierarchy.
            // Every remnant retains its world position when the character turns/moves.
            effectRoot = new GameObject(name + "_EyeFireWorld").transform;
            effectRoot.gameObject.layer = gameObject.layer;
            for (int i = 0; i < flames.Length; i++)
                flames[i] = MakeSprite("EyeFlame_" + i, i % 2 == 0 ? flameTongue : irisCore);
            for (int i = 0; i < particles.Length; i++)
                particles[i] = new Remnant
                {
                    sprite = MakeSprite("EyeRemnant_" + i, i >= FlameCapacity ? bottomPivotQuad : flameTongue),
                    wisp = i >= FlameCapacity
                };
        }

        static Mesh BuildSpriteMesh(string meshName, Vector4 crop, bool curved)
        {
            int rows = curved ? 8 : 1;
            int columns = curved ? 6 : 1;
            int stride = columns + 1;
            var vertices = new Vector3[(rows + 1) * stride];
            var uv = new Vector2[vertices.Length];
            var colors = new Color[vertices.Length];
            var indices = new int[rows * columns * 6];
            for (int row = 0; row <= rows; row++)
            {
                float t = row / (float)rows;
                // Keep the foot at the iris, but let the tongue clear the eyelashes
                // and fringe. Depth follows height: a 10 cm flame bends forward 6 cm.
                float depth = curved ? .6f * t * (2f - t) : 0f;
                for (int column = 0; column <= columns; column++)
                {
                    float u = column / (float)columns;
                    int i = row * stride + column;
                    vertices[i] = new Vector3(u - .5f, t, depth);
                    uv[i] = new Vector2(Mathf.Lerp(crop.x, crop.z, u), Mathf.Lerp(crop.y, crop.w, t));
                    float feather = curved
                        ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Min(u, 1f - u) * 5f))
                            * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.70f, 1f, t)))
                        : 1f;
                    colors[i] = new Color(1f, 1f, 1f, feather);
                }
                if (row == rows) continue;
                for (int column = 0; column < columns; column++)
                {
                    int k = (row * columns + column) * 6, a = row * stride + column;
                    indices[k] = a; indices[k + 1] = a + 1; indices[k + 2] = a + stride;
                    indices[k + 3] = a + 1; indices[k + 4] = a + stride + 1; indices[k + 5] = a + stride;
                }
            }
            var mesh = new Mesh { name = meshName, vertices = vertices, uv = uv, colors = colors, triangles = indices };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh BuildIrisCore(Vector4 crop)
        {
            const int segments = 24, rings = 3;
            float[] radii = { .40f, .72f, 1f };
            float[] alpha = { 1f, .45f, 0f };
            var vertices = new Vector3[1 + segments * rings];
            var uv = new Vector2[vertices.Length];
            var colors = new Color[vertices.Length];
            var indices = new int[segments * (1 + 2 * (rings - 1)) * 3];
            vertices[0] = new Vector3(0f, .5f, 0f);
            uv[0] = new Vector2((crop.x + crop.z) * .5f, (crop.y + crop.w) * .5f);
            colors[0] = Color.white;
            int k = 0;
            for (int ring = 0; ring < rings; ring++)
                for (int segment = 0; segment < segments; segment++)
                {
                    float angle = segment * (Mathf.PI * 2f / segments);
                    float x = Mathf.Cos(angle) * radii[ring] * .5f;
                    float y = Mathf.Sin(angle) * radii[ring] * .5f + .5f;
                    int i = 1 + ring * segments + segment;
                    int next = 1 + ring * segments + (segment + 1) % segments;
                    vertices[i] = new Vector3(x, y, 0f);
                    uv[i] = new Vector2(Mathf.Lerp(crop.x, crop.z, x + .5f), Mathf.Lerp(crop.y, crop.w, y));
                    colors[i] = new Color(1f, 1f, 1f, alpha[ring]);
                    if (ring == 0)
                    {
                        indices[k++] = 0; indices[k++] = i; indices[k++] = next;
                    }
                    else
                    {
                        int inner = i - segments, innerNext = next - segments;
                        indices[k++] = inner; indices[k++] = i; indices[k++] = next;
                        indices[k++] = inner; indices[k++] = next; indices[k++] = innerNext;
                    }
                }
            var mesh = new Mesh { name = "LunaEyeFeatheredIris", vertices = vertices, uv = uv, colors = colors, triangles = indices };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        Sprite MakeSprite(string spriteName, Mesh geometry)
        {
            var go = new GameObject(spriteName);
            go.layer = gameObject.layer;
            go.transform.SetParent(effectRoot, false);
            go.AddComponent<MeshFilter>().sharedMesh = geometry;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.enabled = false;
            return new Sprite { transform = go.transform, renderer = renderer };
        }

        void LateUpdate()
        {
            if (!requestedActive) return;
            if (!UltimateActive)
            {
                SetActiveFire(false);
                return;
            }
            if (!EnsureVisual() || flameMaterial == null)
            {
                ClearEffect();
                return;
            }
            EnsureSprites();
            effectRoot.gameObject.SetActive(true);
            float dt = Time.deltaTime;
            float size = ModelScale();
            Vector3 rootPosition = gait != null ? gait.transform.position : transform.position;
            bool discontinuity = !hasHistory || !Finite(rootPosition) || dt > 0.25f
                || (rootPosition - lastRootPosition).sqrMagnitude > 9f * size * size;
            for (int i = 0; i < eyeAnchors.Length && !discontinuity; i++)
                discontinuity = (eyeAnchors[i].position - lastEyePositions[i]).sqrMagnitude > size * size;
            if (discontinuity)
            {
                ClearParticles();
                ResetHistory();
            }
            else if (dt > 0f)
            {
                Vector3 velocity = Vector3.ProjectOnPlane((rootPosition - lastRootPosition) / dt, Vector3.up);
                planarVelocity = Vector3.Lerp(planarVelocity, Vector3.ClampMagnitude(velocity, 20f * size),
                    1f - Mathf.Exp(-18f * dt));
            }
            Camera camera = EffectCamera();
            if (dt > 0f && !discontinuity)
            {
                clock += dt;
                UpdateParticles(dt, size, camera);
                EmitParticles(dt, size);
            }
            UpdateFlames(camera, size);
            lastRootPosition = rootPosition;
            for (int i = 0; i < eyeAnchors.Length; i++) lastEyePositions[i] = eyeAnchors[i].position;
            hasHistory = true;
        }

        Vector3 RiseDirection()
        {
            Vector3 up = Vector3.Lerp(Vector3.up, visual.up, 0.25f).normalized;
            float bend = Mathf.Clamp01(planarVelocity.magnitude / 6f) * 0.3f;
            return (up - planarVelocity.normalized * bend).normalized;
        }

        void UpdateFlames(Camera camera, float size)
        {
            if (flameMaterial == null || properties == null) return;
            Vector3 rise = RiseDirection();
            for (int eye = 0; eye < 2; eye++)
            {
                Vector3 facing = CameraFacing(eyeAnchors[eye].position, camera);
                float visibility = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-.08f, .20f, Vector3.Dot(visual.forward, facing)));
                Vector3 origin = eyeAnchors[eye].position + facing * (.012f * size);
                Draw(flames[eye * 2], flameMaterial, origin, rise, 0.020f * size,
                    FlameHeight(eye) * size, new Color(1f, .975f, .84f, .63f * visibility), 1.1f, camera);
                // A separate small lower-texture patch lights the iris itself. It is
                // centred on the pupil rather than beginning above its transparent pad.
                Vector3 coreUp = Vector3.ProjectOnPlane(rise, facing).normalized;
                Draw(flames[eye * 2 + 1], flameMaterial, origin - coreUp * (.007f * size),
                    rise, 0.013f * size, 0.014f * size,
                    new Color(1f, .98f, .87f, .76f * visibility), 1.5f, camera);
            }
        }

        float FlameHeight(int eye)
        {
            float phase = clock * 1.8f + eye * .25f;
            return .098f * (1f + .025f * Mathf.Sin(phase));
        }

        void EmitParticles(float dt, float size)
        {
            float speed = planarVelocity.magnitude / size;
            // Quiet breathing while idle; movement leaves a continuous translucent afterimage.
            flameEmission += dt * Mathf.Lerp(0f, 70f, Mathf.Clamp01((speed - .2f) / 4f));
            int flameCount = Mathf.Min(5, Mathf.FloorToInt(flameEmission));
            flameEmission -= Mathf.Floor(flameEmission);
            wispEmission += dt * 8f;
            int wispCount = Mathf.Min(2, Mathf.FloorToInt(wispEmission));
            wispEmission -= Mathf.Floor(wispEmission);
            for (int eye = 0; eye < 2; eye++)
            {
                for (int j = 0; j < flameCount; j++)
                    Spawn(eye, false, (j + 1f) / flameCount, size);
                if (wispMaterial != null)
                    for (int j = 0; j < wispCount; j++) Spawn(eye, true, (j + 1f) / wispCount, size);
            }
        }

        void Spawn(int eye, bool wisp, float interpolation, float size)
        {
            int index = wisp ? FlameCapacity + wispCursor++ % WispCapacity : flameCursor++ % FlameCapacity;
            Remnant p = particles[index];
            p.sprite.renderer.enabled = false;
            p.live = true;
            p.eye = eye;
            p.age = 0f;
            p.lifetime = wisp ? 0.29f : 0.20f;
            p.rise = RiseDirection();
            p.position = Vector3.Lerp(lastEyePositions[eye], eyeAnchors[eye].position, interpolation);
            Camera camera = EffectCamera();
            Vector3 facing = CameraFacing(p.position, camera);
            Vector3 projectedUp = Vector3.ProjectOnPlane(p.rise, facing);
            if (projectedUp.sqrMagnitude < .0001f) projectedUp = camera != null ? camera.transform.up : visual.up;
            projectedUp.Normalize();
            // Sample the visible upper tongue itself, including its forward curve.
            // A raw iris emitter leaves almost every trailing particle inside the head.
            float fraction = wisp ? .86f : .75f;
            float height = FlameHeight(eye) * size;
            p.position += projectedUp * (height * fraction)
                + facing * (.012f * size + .6f * fraction * (2f - fraction) * height);
            float moving = Mathf.Clamp01(planarVelocity.magnitude / (4.2f * size));
            Vector3 lift = (Vector3.up * .75f + p.rise * .25f).normalized;
            p.velocity = lift * (Mathf.Lerp(wisp ? .32f : .4f, wisp ? .65f : .85f, moving) * size)
                - planarVelocity * .035f;
            p.width = (wisp ? .020f : .014f) * size;
            // Shorter detached tongues keep the wake delicate instead of building a tall fire column.
            p.height = (wisp ? .036f : .038f) * size;
        }

        void UpdateParticles(float dt, float size, Camera camera)
        {
            for (int i = 0; i < particles.Length; i++)
            {
                Remnant p = particles[i];
                if (!p.live) continue;
                p.age += dt;
                p.position += p.velocity * dt;
                float distance = Vector3.ProjectOnPlane(p.position - eyeAnchors[p.eye].position, Vector3.up).magnitude / size;
                if (p.age >= p.lifetime || distance >= 0.4f || !Finite(p.position))
                {
                    p.live = false;
                    p.sprite.renderer.enabled = false;
                    continue;
                }
                float t = p.age / p.lifetime;
                float fade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(p.age / .035f)) * Mathf.Pow(1f - t, 1.3f)
                    * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.4f, distance)));
                Color tint = p.wisp
                    ? new Color(.84f, .82f, .98f, fade * .10f)
                    : Color.Lerp(new Color(1f, .98f, .87f, fade * .42f),
                        new Color(.83f, .82f, .98f, fade * .42f), Mathf.SmoothStep(0f, 1f, t));
                Material material = p.wisp ? wispMaterial : flameMaterial;
                Draw(p.sprite, material, p.position, p.rise,
                    p.width * (p.wisp ? 1f + t * .7f : 1f + t * .25f),
                    p.height * (1f - t * .15f), tint, p.wisp ? .5f : .95f, camera);
            }
        }

        void Draw(Sprite sprite, Material material, Vector3 position, Vector3 rise,
            float width, float height, Color tint, float intensity, Camera camera)
        {
            if (sprite == null || sprite.renderer == null || sprite.transform == null) return;
            if (material == null || !Finite(position))
            {
                sprite.renderer.enabled = false;
                return;
            }
            Vector3 facing = CameraFacing(position, camera);
            Vector3 up = Vector3.ProjectOnPlane(rise, facing);
            if (up.sqrMagnitude < 0.0001f)
                up = camera != null ? camera.transform.up : visual.up;
            sprite.transform.SetPositionAndRotation(position, Quaternion.LookRotation(facing, up));
            sprite.transform.localScale = new Vector3(width, height, height);
            if (sprite.renderer.sharedMaterial != material) sprite.renderer.sharedMaterial = material;
            properties.Clear();
            properties.SetColor(TintId, tint);
            properties.SetFloat(IntensityId, intensity);
            sprite.renderer.SetPropertyBlock(properties);
            sprite.renderer.enabled = true;
        }

        float ModelScale()
        {
            if (visual == null) return 1f;
            Vector3 s = visual.lossyScale;
            if (!Finite(s)) return 1f;
            return Mathf.Clamp((Mathf.Abs(s.x) + Mathf.Abs(s.y) + Mathf.Abs(s.z)) / 3f, 0.01f, 10f);
        }

        Camera EffectCamera()
        {
            return CaptureCamera != null ? CaptureCamera : Camera.main;
        }

        Vector3 CameraFacing(Vector3 position, Camera camera)
        {
            Vector3 facing = camera != null
                ? (camera.orthographic ? -camera.transform.forward : camera.transform.position - position)
                : visual.forward;
            if (facing.sqrMagnitude < .00001f) facing = visual.forward;
            return facing.normalized;
        }

        void ResetHistory()
        {
            hasHistory = false;
            planarVelocity = Vector3.zero;
            flameEmission = wispEmission = 0f;
            lastRootPosition = gait != null ? gait.transform.position : transform.position;
            for (int i = 0; i < eyeAnchors.Length; i++)
                if (eyeAnchors[i] != null) lastEyePositions[i] = eyeAnchors[i].position;
        }

        void ClearParticles()
        {
            for (int i = 0; i < particles.Length; i++)
            {
                Remnant p = particles[i];
                if (p == null) continue;
                p.live = false;
                if (p.sprite.renderer != null) p.sprite.renderer.enabled = false;
            }
            ResetHistory();
        }

        void ClearEffect()
        {
            ClearParticles();
            for (int i = 0; i < flames.Length; i++)
                if (flames[i] != null && flames[i].renderer != null) flames[i].renderer.enabled = false;
            if (effectRoot != null) effectRoot.gameObject.SetActive(false);
        }

        static bool Finite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
                && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
                && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }
}
