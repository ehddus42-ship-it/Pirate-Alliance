using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AcRoguelike.Liminal.Editor
{
    public static class TrafficLightBossBuilder
    {
        public const string Folder = "Assets/Liminal/Art/TrafficLightBoss";
        public const string PrefabPath = "Assets/Liminal/Prefabs/Enemies/TrafficLightBoss.prefab";
        public const string DemoPath = "Assets/Liminal/Scenes/TrafficLightBossShowcase.unity";
        const string Meshy = Folder + "/Meshy";
        const string ControllerPath = Folder + "/Animations/TrafficLightBoss.controller";
        // Everything is authored in metres. The signal stands 12 m tall, about 3.9x the vending monster's 3.08 m.
        const float ModelHeight = 8f, PoleStub = .8f, SourceModelHeight = 1.9f;
        static readonly (string key, float length, float yaw)[] Cars =
        {
            ("rusted_sedan", 4.6f, -90), ("yellow_taxi", 4.6f, -90), ("delivery_van", 5.6f, -90)
        };
        // Lamp centres in the normalised Meshy model (height 1.9, centred on the origin), top lamp first.
        // The glass plane sits just behind z=0 and tilts slightly downward (measured from the mesh).
        static readonly Vector3[] LampCentres = { new Vector3(-.015f, .594f, -.012f), new Vector3(-.015f, -.024f, -.022f), new Vector3(-.015f, -.65f, -.028f) };
        const float LampRadius = .205f, LampDepth = .03f;

        [MenuItem("AC Roguelike/Liminal/Traffic Light Boss/Build Boss")]
        public static string Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before authoring.");
            foreach (string sub in new[] { "Animations", "Materials", "Audio", "Meshes", "Textures" }) Directory.CreateDirectory(Folder + "/" + sub);
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath)); AssetDatabase.Refresh();
            var bodySource = AssetDatabase.LoadAssetAtPath<GameObject>(Meshy + "/traffic_light_body/traffic_light_body.glb");
            if (!bodySource) throw new InvalidOperationException("Meshy traffic light body must be imported.");
            var carPrefabs = Cars.Select(c => BuildCar(c.key, c.length, c.yaw)).Where(c => c).ToArray();
            var preview = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("TrafficLightBoss"); SceneManager.MoveGameObjectToScene(root, preview);
            try
            {
                var visual = new GameObject("Visual").transform; visual.SetParent(root.transform, false);
                var animator = visual.gameObject.AddComponent<Animator>(); animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                visual.gameObject.AddComponent<TrafficLightBossAnimationEvents>();
                var rig = visual.gameObject.AddComponent<TrafficLightBossRig>();
                var skeleton = new GameObject("Skeleton").transform; skeleton.SetParent(visual, false);
                Transform Bone(string n, Transform parent, Vector3 rootPosition)
                { var t = new GameObject(n).transform; t.SetParent(parent, false); t.position = rootPosition; return t; }
                var bodyBone = Bone("Body", skeleton, new Vector3(0, TrafficLightBossRig.HipHeight, 0)); rig.body = bodyBone;
                Vector3 hip = bodyBone.position;
                var arm = new[] { new LimbSpec("L", hip + new Vector3(-1.65f, 4.6f, 0), 4.4f, 4.6f, 1.8f, true, 11), new LimbSpec("R", hip + new Vector3(1.65f, 4.6f, 0), 4.4f, 4.6f, 1.8f, true, 29) };
                var leg = new[] { new LimbSpec("L", hip + new Vector3(-.85f, 0, 0), 2f, 2f, .9f, false, 47), new LimbSpec("R", hip + new Vector3(.85f, 0, 0), 2f, 2f, .9f, false, 83) };
                var cableMaterials = CableMaterials();
                var skins = new List<SkinnedMeshRenderer>();
                TrafficLightBossRig.Limb MakeLimb(LimbSpec spec, string prefix, string[] names)
                {
                    var upper = Bone(names[0] + "_" + spec.side, bodyBone, spec.start);
                    var lower = Bone(names[1] + "_" + spec.side, upper, spec.start + Vector3.down * spec.l1);
                    var end = Bone(names[2] + "_" + spec.side, lower, spec.start + Vector3.down * (spec.l1 + spec.l2));
                    var bones = new[] { upper, lower, end };
                    var holder = new GameObject(prefix + spec.side); holder.transform.SetParent(visual, false);
                    var skin = holder.AddComponent<SkinnedMeshRenderer>();
                    skin.sharedMesh = LimbMesh(prefix + spec.side, spec, bones, holder.transform);
                    skin.bones = bones; skin.rootBone = bodyBone; skin.quality = SkinQuality.Bone4; skin.updateWhenOffscreen = true;
                    skin.localBounds = new Bounds(new Vector3(0, 6, 0), new Vector3(24, 16, 24)); skin.sharedMaterials = cableMaterials;
                    skins.Add(skin);
                    return new TrafficLightBossRig.Limb { upper = upper, lower = lower, end = end };
                }
                rig.leftArm = MakeLimb(arm[0], "WireArm_", new[] { "UpperArm", "Forearm", "Hand" });
                rig.rightArm = MakeLimb(arm[1], "WireArm_", new[] { "UpperArm", "Forearm", "Hand" });
                rig.leftLeg = MakeLimb(leg[0], "WireLeg_", new[] { "Thigh", "Shin", "Foot" });
                rig.rightLeg = MakeLimb(leg[1], "WireLeg_", new[] { "Thigh", "Shin", "Foot" });

                var housing = Object.Instantiate(bodySource, bodyBone); housing.name = "SignalHousing";
                float scale = ModelHeight / SourceModelHeight;
                housing.transform.localScale = Vector3.one * scale;
                housing.transform.localPosition = new Vector3(0, -PoleStub + SourceModelHeight * .5f * scale, 0);
                foreach (var c in housing.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                var lit = new[] { Emissive("LensRed", new Color(.35f, .02f, .01f), new Color(1, .06f, .03f) * 5.5f, .5f),
                    Emissive("LensYellow", new Color(.4f, .22f, .01f), new Color(1, .62f, .05f) * 5f, .5f), Emissive("LensGreen", new Color(.02f, .3f, .06f), new Color(.1f, 1, .25f) * 5f, .5f) };
                var lenses = new Renderer[3];
                for (int i = 0; i < 3; i++)
                {
                    var lens = GameObject.CreatePrimitive(PrimitiveType.Sphere); lens.name = "Lamp" + new[] { "Red", "Yellow", "Green" }[i];
                    Object.DestroyImmediate(lens.GetComponent<Collider>());
                    lens.transform.SetParent(housing.transform, false);
                    lens.transform.localPosition = LampCentres[i]; lens.transform.localScale = new Vector3(LampRadius * 2, LampRadius * 2, LampDepth * 2);
                    lenses[i] = lens.GetComponent<Renderer>(); lenses[i].enabled = false;
                    lenses[i].shadowCastingMode = ShadowCastingMode.Off;
                }
                var glowObject = new GameObject("LampGlow"); glowObject.transform.SetParent(bodyBone, false);
                glowObject.transform.localPosition = new Vector3(0, 3.6f, 4.5f);
                var glow = glowObject.AddComponent<Light>(); glow.type = LightType.Point; glow.range = 38; glow.intensity = 22; glow.shadows = LightShadows.None; glow.enabled = false;

                var carSocket = new GameObject("CarSocket").transform; carSocket.SetParent(rig.rightArm.end, false);
                carSocket.position = rig.rightArm.end.position + Vector3.down * 1.3f;
                var aim = new GameObject("AimAnchor").transform; aim.SetParent(bodyBone, false); aim.localPosition = new Vector3(0, 3.4f, 0);

                var body = root.AddComponent<CharacterController>(); body.radius = 2f; body.height = TrafficLightBossRig.DormantHeight;
                body.center = Vector3.up * (TrafficLightBossRig.DormantHeight * .5f); body.stepOffset = .4f; body.skinWidth = .08f;
                var enemy = root.AddComponent<TrainingEnemy>(); enemy.maxHealth = 1200; enemy.respawnOnDeath = false; enemy.aimAnchor = aim;
                var boss = root.AddComponent<TrafficLightBoss>(); boss.animator = animator; boss.rig = rig; boss.carSocket = carSocket;
                boss.carPrefabs = carPrefabs; boss.lampLenses = lenses; boss.lensLit = lit; boss.glow = glow;
                boss.limbRenderers = skins.ToArray();

                var warningObject = new GameObject("CarTelegraph"); warningObject.transform.SetParent(root.transform, false);
                var warning = warningObject.AddComponent<LineRenderer>(); warning.useWorldSpace = true; warning.numCornerVertices = 2; warning.numCapVertices = 2;
                warning.shadowCastingMode = ShadowCastingMode.Off; warning.receiveShadows = false; warning.widthMultiplier = .1f;
                warning.sharedMaterial = Unlit("CarWarning", new Color(1, .3f, .06f)); warning.startColor = warning.endColor = new Color(1, .35f, .07f); warning.enabled = false;
                boss.warning = warning;

                var fieldObject = new GameObject("RedField"); fieldObject.transform.SetParent(root.transform, false);
                var field = fieldObject.AddComponent<TrafficLightRedField>(); boss.field = field;
                var ring = RingTexture();
                field.floorMaterial = Transparent("FieldRed", null); field.telegraphMaterial = Transparent("FieldSafeTelegraph", ring); field.safeMaterial = Transparent("FieldSafeActive", ring);

                boss.voice = root.AddComponent<AudioSource>(); boss.voice.spatialBlend = 1; boss.voice.minDistance = 6; boss.voice.maxDistance = 60; boss.voice.playOnAwake = false;
                boss.emergeSound = Sound("SignalUnfold", 3.2f, 0); boss.stepSound = Sound("GiantStep", .55f, 1); boss.lampSound = Sound("LampClick", .09f, 2);
                boss.slamSound = Sound("FieldSlam", .9f, 3); boss.carRiseSound = Sound("CarRise", .6f, 4); boss.throwSound = Sound("CarThrow", .8f, 5); boss.impactSound = Sound("CarImpact", .5f, 6);

                rig.CacheRestPose();
                BakeAnimations(rig, animator);
                rig.Sample(TrafficLightBossState.Dormant, 0);
                foreach (var r in skins) r.enabled = false;
                enemy.visibleRenderers = root.GetComponentsInChildren<Renderer>(true).Where(r => r != warning).ToArray();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                return "Created " + PrefabPath + ": 4 procedural wire limbs, 3 lamps, 3 cars, 7 editable clips, 2 attack patterns.";
            }
            finally { Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(preview); }
        }

        // ---- Cars -------------------------------------------------------------------------------------------
        static GameObject BuildCar(string key, float length, float yaw)
        {
            string path = "Assets/Liminal/Prefabs/Enemies/TrafficLightCar_" + key + ".prefab";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Meshy + "/" + key + "/" + key + ".glb");
            if (!source) { Debug.LogWarning("Meshy car not imported yet, skipped: " + key); return null; }
            var root = new GameObject("ThrownCar_" + key);
            var model = Object.Instantiate(source, root.transform); model.name = "Model";
            float scale = length / 1.9f;
            model.transform.localScale = Vector3.one * scale; model.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            foreach (var c in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            var renderers = model.GetComponentsInChildren<Renderer>(); var b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds);
            model.transform.localPosition = -b.center;
            var car = root.AddComponent<TrafficLightCarProjectile>(); car.halfExtents = b.extents;
            var result = PrefabUtility.SaveAsPrefabAsset(root, path); Object.DestroyImmediate(root); return result;
        }

        // ---- Procedural wire limbs --------------------------------------------------------------------------
        sealed class LimbSpec
        {
            public string side; public Vector3 start; public float l1, l2, lh; public bool arm; public int seed;
            public LimbSpec(string side, Vector3 start, float l1, float l2, float lh, bool arm, int seed)
            { this.side = side; this.start = start; this.l1 = l1; this.l2 = l2; this.lh = lh; this.arm = arm; this.seed = seed; }
        }

        // Material slots: rubber, red, yellow, green, blue, white, exposed copper, connector.
        static Material[] CableMaterials()
        {
            return new[] { Lit("CableRubber", new Color(.07f, .07f, .075f), 0, .2f), Lit("CableRed", new Color(.85f, .03f, .02f), 0, .35f),
                Lit("CableYellow", new Color(.95f, .68f, .03f), 0, .35f), Lit("CableGreen", new Color(.05f, .65f, .12f), 0, .35f),
                Lit("CableBlue", new Color(.05f, .22f, .85f), 0, .35f), Lit("CableWhite", new Color(.75f, .75f, .7f), 0, .4f),
                Lit("CableCopper", new Color(.78f, .42f, .22f), .95f, .55f), Lit("CableConnector", new Color(.12f, .125f, .13f), .7f, .4f) };
        }

        sealed class Geometry
        {
            public List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); public List<Vector2> uv = new List<Vector2>();
            public List<BoneWeight> w = new List<BoneWeight>(); public List<int>[] tris = Enumerable.Range(0, 8).Select(_ => new List<int>()).ToArray();
        }

        static float Smooth(float a, float b, float x) { x = Mathf.Clamp01((x - a) / (b - a)); return x * x * (3 - 2 * x); }

        static BoneWeight Weights(LimbSpec spec, float s)
        {
            float j1 = spec.l1, j2 = spec.l1 + spec.l2;
            float t1 = Smooth(j1 - 1.1f, j1 + 1.1f, s), t2 = Smooth(j2 - .9f, j2 + .9f, s);
            return new BoneWeight { boneIndex0 = 0, weight0 = 1 - t1, boneIndex1 = 1, weight1 = t1 * (1 - t2), boneIndex2 = 2, weight2 = t2 };
        }

        static Mesh LimbMesh(string name, LimbSpec spec, Transform[] bones, Transform meshSpace)
        {
            var rand = new System.Random(spec.seed); float Rand(float a, float b) => a + (float)rand.NextDouble() * (b - a);
            var g = new Geometry();
            const int Segments = 56, Sides = 9;
            float reach = spec.l1 + spec.l2, total = reach + spec.lh;
            int[] palette = { 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 5, 0 };
            int cables = palette.Length;
            for (int i = 0; i < cables; i++)
            {
                float radius = Rand(.12f, .2f) * (spec.arm ? 1 : 1.12f), fraction = i < 3 ? Rand(0, .25f) : Rand(.45f, 1f);
                float phase = Rand(0, Mathf.PI * 2), twist = Rand(.55f, 1.1f) * (i % 2 == 0 ? 1 : -1), wobble = Rand(.1f, .28f), wobblePhase = Rand(0, 6.28f);
                float flare = spec.arm ? Rand(.9f, 1.75f) : Rand(.55f, 1.25f);
                var ringCentres = new List<Vector3>(); var ringRadii = new List<float>(); var ringS = new List<float>(); var stripMaterial = new List<int>();
                for (int k = 0; k <= Segments; k++)
                {
                    float s = total * k / Segments, t = Mathf.Clamp01(s / reach);
                    float bundle = Mathf.Lerp(spec.arm ? .64f : .72f, spec.arm ? .38f : .5f, t)
                        + .3f * Mathf.Exp(-Mathf.Pow((s - spec.l1) / .9f, 2)) + .26f * Mathf.Max(0, 1 - s / .9f);
                    float q = Mathf.Clamp01((s - reach) / spec.lh);
                    float rho = fraction * bundle + flare * Mathf.Pow(q, 1.5f) * (spec.arm ? 1 : .9f);
                    float angle = phase + twist * s * .55f + wobble * Mathf.Sin(s * 1.35f + wobblePhase);
                    Vector3 offset = Vector3.right * Mathf.Cos(angle) * rho + Vector3.forward * Mathf.Sin(angle) * rho * (spec.arm ? 1 : 1.25f);
                    if (!spec.arm) offset += Vector3.forward * .55f * Mathf.Pow(q, 1.4f);
                    ringCentres.Add(spec.start + Vector3.down * s + offset); ringRadii.Add(radius * (1 - .12f * t)); ringS.Add(s); stripMaterial.Add(palette[i]);
                }
                // Tip: bare copper strands or a chunky connector plug.
                Vector3 tail = (ringCentres[Segments] - ringCentres[Segments - 1]).normalized; float tipRadius = ringRadii[Segments];
                bool plug = i % 3 == 0;
                AddTip(ringCentres, ringRadii, ringS, stripMaterial, tail, tipRadius, total, plug);
                AddTube(g, spec, ringCentres, ringRadii, ringS, stripMaterial, Sides, bones, meshSpace);
            }
            var mesh = new Mesh { name = name, indexFormat = g.v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(g.v); mesh.SetNormals(g.n); mesh.SetUVs(0, g.uv); mesh.boneWeights = g.w.ToArray();
            mesh.subMeshCount = g.tris.Length; for (int i = 0; i < g.tris.Length; i++) mesh.SetTriangles(g.tris[i], i);
            mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * meshSpace.localToWorldMatrix).ToArray();
            mesh.RecalculateBounds();
            string path = Folder + "/Meshes/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing) { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); return existing; }
            AssetDatabase.CreateAsset(mesh, path); return mesh;
        }

        static void AddTip(List<Vector3> centres, List<float> radii, List<float> ss, List<int> materials, Vector3 tail, float r, float s, bool plug)
        {
            Vector3 p = centres[centres.Count - 1];
            void Ring(float distance, float radius, int material) { p += tail * distance; centres.Add(p); radii.Add(radius); ss.Add(s); materials.Add(material); }
            if (plug)
            {
                Ring(.04f, r * 1.75f, 7); Ring(.42f, r * 1.75f, 7); Ring(.03f, r * .42f, 7); Ring(.34f, r * .42f, 6); Ring(.06f, r * .1f, 6);
            }
            else { Ring(.03f, r * .8f, 6); Ring(.26f, r * .78f, 6); Ring(.3f, r * .16f, 6); }
        }

        static void AddTube(Geometry g, LimbSpec spec, List<Vector3> centres, List<float> radii, List<float> ss, List<int> materials, int sides, Transform[] bones, Transform meshSpace)
        {
            int rings = centres.Count; int baseIndex = g.v.Count;
            Vector3 tangent = (centres[1] - centres[0]).normalized, normal = Vector3.Cross(tangent, Vector3.right).sqrMagnitude > .01f ? Vector3.Cross(tangent, Vector3.right).normalized : Vector3.Cross(tangent, Vector3.forward).normalized;
            for (int k = 0; k < rings; k++)
            {
                tangent = (centres[Mathf.Min(rings - 1, k + 1)] - centres[Mathf.Max(0, k - 1)]).normalized;
                normal = (normal - Vector3.Dot(normal, tangent) * tangent).normalized; Vector3 binormal = Vector3.Cross(tangent, normal);
                var weight = Weights(spec, ss[k]);
                for (int j = 0; j <= sides; j++)
                {
                    float a = j * Mathf.PI * 2 / sides; Vector3 dir = Mathf.Cos(a) * normal + Mathf.Sin(a) * binormal;
                    g.v.Add(centres[k] + dir * radii[k]); g.n.Add(dir); g.uv.Add(new Vector2((float)j / sides, ss[k] * .5f)); g.w.Add(weight);
                }
            }
            for (int k = 0; k < rings - 1; k++)
            {
                var list = g.tris[materials[k]];
                for (int j = 0; j < sides; j++)
                {
                    int a = baseIndex + k * (sides + 1) + j, b = a + 1, c = a + sides + 1, d = c + 1;
                    list.Add(a); list.Add(c); list.Add(b); list.Add(b); list.Add(c); list.Add(d);
                }
            }
        }

        // ---- Materials and textures ------------------------------------------------------------------------
        static Material LoadMaterial(string name, string shader)
        {
            string path = Folder + "/Materials/" + name + ".mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
            return m;
        }

        static Material Lit(string name, Color color, float metallic, float smoothness)
        {
            var m = LoadMaterial(name, "Universal Render Pipeline/Lit"); m.color = color; m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", smoothness); EditorUtility.SetDirty(m); return m;
        }

        static Material Emissive(string name, Color color, Color emission, float smoothness)
        {
            var m = Lit(name, color, 0, smoothness);
            m.EnableKeyword("_EMISSION"); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; m.SetColor("_EmissionColor", emission); return m;
        }

        static Material Unlit(string name, Color color)
        {
            var m = LoadMaterial(name, "Universal Render Pipeline/Unlit"); m.color = color; m.SetColor("_BaseColor", color); EditorUtility.SetDirty(m); return m;
        }

        static Material Transparent(string name, Texture texture)
        {
            var m = LoadMaterial(name, "Universal Render Pipeline/Unlit");
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetFloat("_ZWrite", 0);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetOverrideTag("RenderType", "Transparent"); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = (int)RenderQueue.Transparent;
            m.SetShaderPassEnabled("ShadowCaster", false);
            if (texture) m.SetTexture("_BaseMap", texture);
            EditorUtility.SetDirty(m); return m;
        }

        static Texture2D RingTexture()
        {
            string path = Folder + "/Textures/SafeZoneRing.png";
            // Always rewritten so tuning the pattern art below takes effect on the next build.
            {
                const int size = 256; var tex = new Texture2D(size, size, TextureFormat.RGBA32, false); var pixels = new Color[size * size];
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    float dx = (x + .5f) / size * 2 - 1, dy = (y + .5f) / size * 2 - 1, r = Mathf.Sqrt(dx * dx + dy * dy);
                    float disc = Mathf.SmoothStep(1, .94f, r), rim = Mathf.Exp(-Mathf.Pow((r - .9f) / .035f, 2));
                    float inner = .72f + .1f * Mathf.Sin(r * 30);
                    pixels[y * size + x] = new Color(1, 1, 1, Mathf.Clamp01(disc * Mathf.Max(inner, rim * 1.6f)));
                }
                tex.SetPixels(pixels); File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.alphaIsTransparency = true; importer.wrapMode = TextureWrapMode.Clamp; importer.mipmapEnabled = false; importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---- Animation clips ---------------------------------------------------------------------------------
        static void BakeAnimations(TrafficLightBossRig rig, Animator animator)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (!controller) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var machine = controller.layers[0].stateMachine;
            foreach (var state in machine.states) machine.RemoveState(state.state);
            var transforms = new List<Transform> { rig.body };
            foreach (var limb in new[] { rig.leftArm, rig.rightArm, rig.leftLeg, rig.rightLeg }) transforms.AddRange(new[] { limb.upper, limb.lower, limb.end });
            string[] props = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z", "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w", "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z" };
            foreach (TrafficLightBossState motion in Enum.GetValues(typeof(TrafficLightBossState)))
            {
                if (motion == TrafficLightBossState.Dead) continue;
                float duration = TrafficLightBossRig.Duration(motion); int frames = Mathf.CeilToInt(duration * 60);
                var curves = new List<Keyframe>[transforms.Count, 10];
                for (int j = 0; j < transforms.Count; j++) for (int k = 0; k < 10; k++) curves[j, k] = new List<Keyframe>();
                var previous = new Quaternion[transforms.Count];
                for (int f = 0; f <= frames; f++)
                {
                    float t = duration * f / frames; rig.Sample(motion, t);
                    for (int j = 0; j < transforms.Count; j++)
                    {
                        var tr = transforms[j]; var q = tr.localRotation;
                        if (f > 0 && Quaternion.Dot(q, previous[j]) < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                        previous[j] = q; var p = tr.localPosition; var s = tr.localScale;
                        float[] values = { p.x, p.y, p.z, q.x, q.y, q.z, q.w, s.x, s.y, s.z };
                        for (int k = 0; k < 10; k++) curves[j, k].Add(new Keyframe(t, values[k]));
                    }
                }
                string path = Folder + "/Animations/" + motion + ".anim";
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (!clip) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
                clip.ClearCurves(); clip.name = motion.ToString(); clip.frameRate = 60;
                for (int j = 0; j < transforms.Count; j++) for (int k = 0; k < 10; k++)
                {
                    var keys = Simplify(curves[j, k], k < 3 ? .0015f : .0008f); var curve = new AnimationCurve(keys.ToArray());
                    for (int n = 0; n < curve.length; n++) { AnimationUtility.SetKeyLeftTangentMode(curve, n, AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(curve, n, AnimationUtility.TangentMode.Linear); }
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(transforms[j], rig.transform), typeof(Transform), props[k]), curve);
                }
                clip.EnsureQuaternionContinuity(); var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = motion == TrafficLightBossState.Idle || motion == TrafficLightBossState.Walk || motion == TrafficLightBossState.Dormant;
                settings.loopBlend = false; AnimationUtility.SetAnimationClipSettings(clip, settings); EditorUtility.SetDirty(clip);
                AnimationUtility.SetAnimationEvents(clip, motion == TrafficLightBossState.CarThrow
                    ? new[] { new AnimationEvent { time = TrafficLightBossRig.CarSpawnTime, functionName = "SpawnCar" }, new AnimationEvent { time = TrafficLightBossRig.CarReleaseTime, functionName = "ReleaseCar" } }
                    : new AnimationEvent[0]);
                var state = machine.AddState(motion.ToString()); state.motion = clip; state.writeDefaultValues = true;
                if (motion == TrafficLightBossState.Dormant) machine.defaultState = state;
            }
            animator.runtimeAnimatorController = controller; EditorUtility.SetDirty(controller); rig.RestoreRestPose();
        }

        static List<Keyframe> Simplify(List<Keyframe> keys, float tolerance)
        {
            var kept = new SortedSet<int> { 0, keys.Count - 1 };
            void Segment(int a, int b)
            {
                float worst = tolerance; int index = -1;
                for (int i = a + 1; i < b; i++) { float t = (keys[i].time - keys[a].time) / (keys[b].time - keys[a].time); float error = Mathf.Abs(keys[i].value - Mathf.Lerp(keys[a].value, keys[b].value, t)); if (error > worst) { worst = error; index = i; } }
                if (index < 0) return; kept.Add(index); Segment(a, index); Segment(index, b);
            }
            Segment(0, keys.Count - 1); return kept.Select(i => keys[i]).ToList();
        }

        // ---- Audio ---------------------------------------------------------------------------------------------
        static AudioClip Sound(string name, float duration, int kind)
        {
            string path = Folder + "/Audio/" + name + ".wav"; int sampleRate = 22050, count = (int)(sampleRate * duration);
            var random = new System.Random(3100 + kind);
            using (var writer = new BinaryWriter(File.Open(path, FileMode.Create)))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 2); writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(sampleRate); writer.Write(sampleRate * 2); writer.Write((short)2); writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(count * 2);
                for (int i = 0; i < count; i++)
                {
                    float t = (float)i / sampleRate, u = t / duration, noise = (float)random.NextDouble() * 2 - 1;
                    float env, frequency, noiseAmount;
                    switch (kind)
                    {
                        case 0: env = Mathf.Sin(Mathf.PI * u) * .9f; frequency = 38 + 26 * u + 6 * Mathf.Sin(t * 9); noiseAmount = .22f; break;
                        case 1: env = Mathf.Exp(-u * 7); frequency = 46; noiseAmount = .3f; break;
                        case 2: env = Mathf.Exp(-u * 6); frequency = 1240; noiseAmount = .05f; break;
                        case 3: env = Mathf.Exp(-u * 4.5f); frequency = 52 - 14 * u; noiseAmount = .5f; break;
                        case 4: env = Mathf.Sin(Mathf.PI * u); frequency = 96 + 40 * u; noiseAmount = .35f; break;
                        case 5: env = Mathf.Sin(Mathf.PI * u) * Mathf.Exp(-u * 1.5f); frequency = 150; noiseAmount = .85f; break;
                        default: env = Mathf.Exp(-u * 6); frequency = 170; noiseAmount = .5f; break;
                    }
                    float v = (Mathf.Sin(t * frequency * 2 * Mathf.PI) * .55f + Mathf.Sin(t * frequency * 2.73f * 2 * Mathf.PI) * .14f + noise * noiseAmount) * env * .6f;
                    writer.Write((short)(Mathf.Clamp(v, -1, 1) * 32760));
                }
            }
            AssetDatabase.ImportAsset(path); return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        // ---- Playable showcase ---------------------------------------------------------------------------------
        [MenuItem("AC Roguelike/Liminal/Traffic Light Boss/Create Playable Showcase")]
        public static string CreateShowcase()
        {
            // Re-creating the showcase while it is open would overwrite an open scene; drop it first.
            var open = SceneManager.GetSceneByPath(DemoPath);
            if (open.isLoaded)
            {
                if (SceneManager.sceneCount == 1) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                else EditorSceneManager.CloseScene(open, true);
            }
            // Additive keeps whatever the author has open; an unsaved untitled scene cannot be kept, so replace it.
            var mode = string.IsNullOrEmpty(SceneManager.GetActiveScene().path) ? NewSceneMode.Single : NewSceneMode.Additive;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, mode); SceneManager.SetActiveScene(scene);
            var tile = Lit("ShowcaseTile", new Color(.5f, .51f, .43f), 0, .2f); var wallMaterial = Lit("ShowcaseWall", new Color(.65f, .62f, .43f), 0, .15f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "ShowcaseFloor"; floor.transform.position = new Vector3(0, -.1f, 0); floor.transform.localScale = new Vector3(28, .2f, 36);
            floor.GetComponent<Renderer>().sharedMaterial = tile;
            void Wall(Vector3 position, Vector3 size) { var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "ArenaWall"; wall.transform.position = position; wall.transform.localScale = size; wall.GetComponent<Renderer>().sharedMaterial = wallMaterial; }
            Wall(new Vector3(-14.1f, 2.3f, 0), new Vector3(.2f, 4.6f, 36.4f)); Wall(new Vector3(14.1f, 2.3f, 0), new Vector3(.2f, 4.6f, 36.4f));
            Wall(new Vector3(0, 2.3f, 18.1f), new Vector3(28.4f, 4.6f, .2f)); Wall(new Vector3(0, 2.3f, -18.1f), new Vector3(28.4f, 4.6f, .2f));
            for (int z = -12; z <= 12; z += 8)
            {
                var lamp = new GameObject("FluorescentPool"); lamp.transform.position = new Vector3(0, 4.2f, z);
                var light = lamp.AddComponent<Light>(); light.type = LightType.Point; light.range = 14; light.intensity = 2.2f; light.color = new Color(.85f, .95f, .84f); light.shadows = LightShadows.None;
            }
            var sun = new GameObject("SoftKey").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.1f; sun.transform.rotation = Quaternion.Euler(50, -35, 0); sun.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.36f, .39f, .38f);
            var monster = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene);
            monster.transform.position = new Vector3(0, 0, 10); monster.transform.rotation = Quaternion.Euler(0, 180, 0);
            var boss = monster.GetComponent<TrafficLightBoss>(); boss.arenaCenter = Vector3.zero; boss.arenaCenterSet = true;
            var playerSource = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/Astraia/AstraiaPlayer.prefab");
            if (!playerSource) playerSource = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Liminal/Prefabs/Explorer/LiminalExplorer.prefab");
            var player = (GameObject)PrefabUtility.InstantiatePrefab(playerSource, scene); player.name = "ShowcasePlayer"; player.transform.position = new Vector3(0, .05f, -12);
            if (!player.GetComponent<LiminalPlayerHealth>()) player.AddComponent<LiminalPlayerHealth>();
            var camera = new GameObject("Main Camera").AddComponent<Camera>(); camera.tag = "MainCamera"; camera.fieldOfView = 36; camera.nearClipPlane = .1f; camera.farClipPlane = 140;
            camera.gameObject.AddComponent<AudioListener>(); var follow = camera.gameObject.AddComponent<IsometricFollowCamera>(); follow.target = player.transform;
            EditorSceneManager.SaveScene(scene, DemoPath);
            if (mode == NewSceneMode.Additive) EditorSceneManager.CloseScene(scene, true);
            return DemoPath;
        }
    }
}
