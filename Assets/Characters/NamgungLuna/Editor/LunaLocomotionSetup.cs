// Unity 6 Editor utility. Builds the moon-gait rig (LunaMoonGait) on the Magica character and the playable demo scene.
// Called from Tools > Namgung Luna > Setup Magica Cloth (LunaMagicaSetup.Setup), which regenerates the prefab.
// The model is static (no skeleton): the rig adds a "Visual" pivot for the whole body and a hidden hip/knee/ankle chain
// per leg that carries the boots and the Magica leg colliders, plus pivots for the lantern and the floating moon.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NamgungLuna.Motion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace NamgungLuna.Editor
{
    public static class LunaLocomotionSetup
    {
        public static string MotionFolder => LunaAssetSetup.Root + "/Motion";
        public static string MaterialFolder => MotionFolder + "/Materials";
        public static string MeshFolder => MotionFolder + "/Meshes";
        public static string ScenePath => MotionFolder + "/NamgungLuna_MoonGaitDemo.unity";
        const float HipHeight = 0.8f;   // matches the Leg_L / Leg_R collider specs

        public sealed class Rig
        {
            public Transform visual, lantern, moon;
            public Vector3 lanternCenter;
            public LunaLeg left, right;
            /// <summary>Toe tips in world space at bind (for the foot colliders).</summary>
            public Vector3 toeLeft, toeRight;

            public LunaLeg Leg(string side) => side == "L" ? left : right;
            public Vector3 Toe(string side) => side == "L" ? toeLeft : toeRight;
        }

        public sealed class Materials
        {
            public Material ripple, glow, trail, sparkle, floor, pillar;
        }

        /// <summary>
        /// Insert the Visual pivot under <paramref name="character"/> (at the origin, unrotated), move the model under it and
        /// build the leg chains, lantern and moon pivots. Every part keeps its world pose.
        /// </summary>
        public static Rig BuildRig(GameObject character, GameObject model)
        {
            Transform root = character.transform;
            var visual = Bone("Visual", root, root.position);
            model.transform.SetParent(visual, false);
            var renderers = model.GetComponentsInChildren<Renderer>(true).GroupBy(r => r.name).ToDictionary(g => g.Key, g => g.First());

            var rig = new Rig { visual = visual };
            rig.left = BuildLeg(visual, renderers, "L", out rig.toeLeft);
            rig.right = BuildLeg(visual, renderers, "R", out rig.toeRight);

            if (renderers.TryGetValue("Lantern_Frame", out Renderer frame))
            {
                Bounds b = frame.bounds;
                // the ring at the top rests in her right hand
                rig.lantern = Bone("LanternPivot", visual, new Vector3(b.center.x, b.max.y - 0.012f, b.center.z));
                frame.transform.SetParent(rig.lantern, true);
                if (renderers.TryGetValue("Lantern_Emissive_Core", out Renderer core)) core.transform.SetParent(rig.lantern, true);
                rig.lanternCenter = rig.lantern.InverseTransformPoint(b.center);
            }
            if (renderers.TryGetValue("Floating_FullMoon", out Renderer moon))
            {
                // outside Visual: the moon floats after her instead of leaning with her
                rig.moon = Bone("MoonPivot", root, moon.bounds.center);
                moon.transform.SetParent(rig.moon, true);
            }
            return rig;
        }

        static LunaLeg BuildLeg(Transform visual, Dictionary<string, Renderer> renderers, string side, out Vector3 toe)
        {
            Renderer boot = renderers["Boot_" + side], shaft = renderers["Boot_Shaft_Underlap_" + side];
            Bounds b = boot.bounds, s = shaft.bounds;
            var ankle = new Vector3(s.center.x, b.min.y + b.size.y * 0.52f, s.center.z);
            var hip = new Vector3(s.center.x * 0.55f, HipHeight, s.center.z);
            var knee = Vector3.Lerp(hip, ankle, 0.5f) + Vector3.forward * 0.012f;
            Transform hipBone = Bone("Hip_" + side, visual, hip);
            Transform kneeBone = Bone("Knee_" + side, hipBone, knee);
            Transform ankleBone = Bone("Ankle_" + side, kneeBone, ankle);
            // the shaft lining follows the shin; the boot bends at the ankle
            shaft.transform.SetParent(kneeBone, true);
            Vector3[] world = boot.GetComponent<MeshFilter>().sharedMesh.vertices.Select(v => boot.transform.TransformPoint(v)).ToArray();

            // The dress hides the legs, so the model has none: slim stocking legs (the shaft lining's material) fill the
            // gap between hem and boot when the skirt lifts or a knee folds. Same radii as the Magica leg colliders.
            Material stocking = shaft.sharedMaterial;
            float shaftRadius = Mathf.Min(s.extents.x, s.extents.z);
            AddLimb("Thigh_" + side, hipBone, hip + Vector3.up * 0.02f, knee, 0.045f, 0.041f, stocking);
            AddLimb("Shin_" + side, kneeBone, knee, new Vector3(ankle.x, s.center.y, ankle.z), 0.04f, shaftRadius * 0.95f, stocking);
            SkinBoot(boot, kneeBone, ankleBone, ankle.y, side);

            // Outline of the foot in the side plane: whatever the toe angle, its lowest point is one of these.
            Vector3[] foot = world.Where(v => v.y < ankle.y + 0.01f).ToArray();
            var outline = new List<Vector3>();
            for (int i = 0; i < 24; i++)
            {
                float a = 2f * Mathf.PI * i / 24f;
                float dz = Mathf.Cos(a), dy = Mathf.Sin(a);
                Vector3 best = foot[0];
                float bestDot = float.MinValue;
                foreach (Vector3 v in foot)
                {
                    float d = v.z * dz + v.y * dy;
                    if (d > bestDot) { bestDot = d; best = v; }
                }
                Vector3 local = ankleBone.InverseTransformPoint(best);
                if (!outline.Any(o => (o - local).sqrMagnitude < 1e-6f)) outline.Add(local);
            }
            toe = foot.OrderByDescending(v => v.z).First();
            return new LunaLeg { hip = hipBone, knee = kneeBone, ankle = ankleBone, solePoints = outline.ToArray() };
        }

        /// <summary>
        /// Replace the rigid boot with a two-bone skinned copy: the cuff follows the shin, the foot follows the ankle, and
        /// the leather between bends, so pointing the toes does not tilt the whole boot off the leg.
        /// </summary>
        static void SkinBoot(Renderer boot, Transform shin, Transform foot, float ankleY, string side)
        {
            Mesh source = boot.GetComponent<MeshFilter>().sharedMesh;
            Matrix4x4 toShin = shin.worldToLocalMatrix * boot.transform.localToWorldMatrix;
            Vector3[] vertices = source.vertices, normals = source.normals;
            Vector4[] tangents = source.tangents;
            var weights = new BoneWeight[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                float y = boot.transform.TransformPoint(vertices[i]).y;
                float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ankleY - 0.015f, ankleY + 0.055f, y));   // 1 = shin
                weights[i] = w >= 0.5f
                    ? new BoneWeight { boneIndex0 = 0, weight0 = w, boneIndex1 = 1, weight1 = 1f - w }
                    : new BoneWeight { boneIndex0 = 1, weight0 = 1f - w, boneIndex1 = 0, weight1 = w };
                vertices[i] = toShin.MultiplyPoint3x4(vertices[i]);
                if (normals.Length > i) normals[i] = toShin.MultiplyVector(normals[i]).normalized;
                if (tangents.Length > i)
                {
                    Vector3 t = toShin.MultiplyVector(tangents[i]).normalized;
                    tangents[i] = new Vector4(t.x, t.y, t.z, tangents[i].w);
                }
            }
            var mesh = new Mesh { name = "LunaBoot_" + side + "_Skinned", indexFormat = source.indexFormat };
            mesh.vertices = vertices;
            if (normals.Length == vertices.Length) mesh.normals = normals;
            if (tangents.Length == vertices.Length) mesh.tangents = tangents;
            var uv = new List<Vector2>();
            source.GetUVs(0, uv);
            mesh.SetUVs(0, uv);
            if (source.colors.Length == vertices.Length) mesh.colors = source.colors;
            mesh.subMeshCount = source.subMeshCount;
            for (int k = 0; k < source.subMeshCount; k++) mesh.SetTriangles(source.GetTriangles(k), k);
            mesh.boneWeights = weights;
            mesh.bindposes = new[] { Matrix4x4.identity, foot.worldToLocalMatrix * shin.localToWorldMatrix };
            mesh.RecalculateBounds();
            mesh = SaveMesh(mesh);

            var go = new GameObject(boot.name);
            go.transform.SetParent(shin, false);
            var skinned = go.AddComponent<SkinnedMeshRenderer>();
            skinned.sharedMesh = mesh;
            skinned.sharedMaterials = boot.sharedMaterials;
            skinned.bones = new[] { shin, foot };
            skinned.rootBone = shin;
            skinned.localBounds = new Bounds(mesh.bounds.center, mesh.bounds.size + Vector3.one * 0.15f);
            skinned.shadowCastingMode = boot.shadowCastingMode;
            skinned.receiveShadows = boot.receiveShadows;
            UnityEngine.Object.DestroyImmediate(boot.gameObject);
        }

        /// <summary>A tapered capsule from <paramref name="top"/> to <paramref name="bottom"/> (world), parented to a bone.</summary>
        static void AddLimb(string name, Transform bone, Vector3 top, Vector3 bottom, float topRadius, float bottomRadius, Material material)
        {
            float length = Vector3.Distance(top, bottom);
            var go = new GameObject(name);
            go.transform.SetParent(bone, false);
            go.transform.SetPositionAndRotation(top, Quaternion.FromToRotation(Vector3.down, (bottom - top).normalized));
            go.AddComponent<MeshFilter>().sharedMesh = SaveMesh(TaperedCapsule("LunaLeg_" + name, length, topRadius, bottomRadius));
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        /// <summary>Capsule along local -Y: a hemisphere of <paramref name="r0"/> at the origin, one of <paramref name="r1"/> at -length.</summary>
        static Mesh TaperedCapsule(string name, float length, float r0, float r1, int segments = 16, int capRings = 5)
        {
            var profile = new List<(float y, float radius, Vector2 normal)>();
            for (int i = 0; i <= capRings; i++)
            {
                float phi = Mathf.PI * 0.5f * (1f - (float)i / capRings);   // top pole -> equator
                profile.Add((r0 * Mathf.Sin(phi), r0 * Mathf.Cos(phi), new Vector2(Mathf.Cos(phi), Mathf.Sin(phi))));
            }
            for (int i = 0; i <= capRings; i++)
            {
                float phi = -Mathf.PI * 0.5f * i / capRings;                 // equator -> bottom pole
                profile.Add((-length + r1 * Mathf.Sin(phi), r1 * Mathf.Cos(phi), new Vector2(Mathf.Cos(phi), Mathf.Sin(phi))));
            }
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            for (int r = 0; r < profile.Count; r++)
                for (int s = 0; s <= segments; s++)
                {
                    float a = 2f * Mathf.PI * s / segments;
                    var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    vertices.Add(radial * profile[r].radius + Vector3.up * profile[r].y);
                    normals.Add((radial * profile[r].normal.x + Vector3.up * profile[r].normal.y).normalized);
                    uvs.Add(new Vector2((float)s / segments, (float)r / (profile.Count - 1)));
                }
            var triangles = new List<int>();
            for (int r = 0; r < profile.Count - 1; r++)
                for (int s = 0; s < segments; s++)
                {
                    int i0 = r * (segments + 1) + s, i1 = i0 + 1, i2 = i0 + segments + 1, i3 = i2 + 1;
                    triangles.AddRange(new[] { i0, i1, i2, i1, i3, i2 });
                }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>Store a generated mesh as an asset (prefabs cannot keep scene-only meshes), reusing the existing asset.</summary>
        static Mesh SaveMesh(Mesh mesh)
        {
            EnsureFolder(MeshFolder);
            string path = MeshFolder + "/" + mesh.name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            EditorUtility.CopySerialized(mesh, existing);
            UnityEngine.Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        static Transform Bone(string name, Transform parent, Vector3 worldPosition)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.position = worldPosition;
            t.rotation = parent.rotation;
            t.localScale = Vector3.one;
            return t;
        }

        /// <summary>Add LunaMoonGait + LunaMoonFx on the character root, wired to the rig.</summary>
        public static void AttachGait(GameObject character, Rig rig)
        {
            Materials materials = EnsureMaterials();
            var gait = character.AddComponent<LunaMoonGait>();
            gait.visual = rig.visual;
            gait.left = rig.left;
            gait.right = rig.right;
            gait.lantern = rig.lantern;
            gait.lanternCenterLocal = rig.lanternCenter;
            gait.moon = rig.moon;
            gait.leanPivot = new Vector3(0f, 0.92f, 0f);
            var fx = character.AddComponent<LunaMoonFx>();
            fx.gait = gait;
            fx.rippleMaterial = materials.ripple;
            fx.glowMaterial = materials.glow;
            fx.trailMaterial = materials.trail;
            fx.sparkleMaterial = materials.sparkle;
            if (rig.moon != null)
            {
                // the moon floats 0.8 m behind her: fade it while it hides her from the camera
                var fade = character.AddComponent<LunaOcclusionFade>();
                fade.renderers = rig.moon.GetComponentsInChildren<Renderer>(true);
                fade.body = rig.visual;
            }
        }

        public static Materials EnsureMaterials()
        {
            EnsureFolder(MaterialFolder);
            var moonlight = new Color(0.55f, 0.8f, 1.6f, 1f);
            return new Materials
            {
                ripple = EnsureMaterial("LunaMoonRipple", "NamgungLuna/Moon Ripple", m => { m.SetColor("_Color", moonlight); m.SetFloat("_Width", 0.045f); }),
                glow = EnsureMaterial("LunaMoonGlow", "NamgungLuna/Moon Glow", m => { m.SetColor("_Color", moonlight); m.SetFloat("_Mode", 0f); m.SetFloat("_Falloff", 3.5f); }),
                trail = EnsureMaterial("LunaMoonTrail", "NamgungLuna/Moon Glow", m => { m.SetColor("_Color", moonlight * 0.8f); m.SetFloat("_Mode", 2f); m.SetFloat("_Falloff", 4f); }),
                sparkle = EnsureMaterial("LunaMoonSparkle", "NamgungLuna/Moon Glow", m => { m.SetColor("_Color", moonlight * 1.4f); m.SetFloat("_Mode", 1f); }),
                floor = EnsureMaterial("LunaDemoFloor", "Universal Render Pipeline/Lit", m =>
                {
                    m.SetTexture("_BaseMap", EnsureGridTexture());
                    m.SetTextureScale("_BaseMap", new Vector2(40f, 40f));
                    m.SetColor("_BaseColor", Color.white);
                    m.SetFloat("_Smoothness", 0.35f);
                }),
                pillar = EnsureMaterial("LunaDemoPillar", "Universal Render Pipeline/Lit", m =>
                {
                    m.SetColor("_BaseColor", new Color(0.62f, 0.66f, 0.78f));
                    m.SetFloat("_Smoothness", 0.2f);
                }),
            };
        }

        static Material EnsureMaterial(string name, string shaderName, Action<Material> configure)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException("Shader not found: " + shaderName);
            string path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            configure(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Texture2D EnsureGridTexture()
        {
            string path = MaterialFolder + "/LunaDemoFloorGrid.png";
            if (!File.Exists(path))
            {
                const int size = 256;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var base0 = new Color(0.16f, 0.18f, 0.27f);
                var line = new Color(0.36f, 0.4f, 0.56f);
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        bool edge = x < 3 || y < 3;
                        bool half = (x > 126 && x < 129) || (y > 126 && y < 129);
                        tex.SetPixel(x, y, edge ? line : half ? Color.Lerp(base0, line, 0.35f) : base0);
                    }
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.anisoLevel = 8;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static VolumeProfile EnsureVolumeProfile()
        {
            string path = MotionFolder + "/LunaMoonGaitDemo_PostFX.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            if (!profile.TryGet(out Bloom bloom))
            {
                bloom = profile.Add<Bloom>(true);
                AssetDatabase.AddObjectToAsset(bloom, profile);
            }
            // only the HDR moonlight effects bloom; the emissive moon prop keeps its texture
            bloom.threshold.Override(1.25f);
            bloom.intensity.Override(0.8f);
            bloom.scatter.Override(0.65f);
            if (!profile.TryGet(out Vignette vignette))
            {
                vignette = profile.Add<Vignette>(true);
                AssetDatabase.AddObjectToAsset(vignette, profile);
            }
            vignette.intensity.Override(0.22f);
            EditorUtility.SetDirty(bloom);
            EditorUtility.SetDirty(vignette);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        /// <summary>
        /// Playable demo: floor grid, a ring of pillars for scale, moonlit light, Luna with LunaMoonMotor (WASD / Shift / Space /
        /// Ctrl) and LunaMoonShowcase (tours every gait when the keyboard is left alone), and an orbit camera with bloom.
        /// </summary>
        public static Scene BuildDemoScene(GameObject prefab, Action<Scene, GameObject> decorate = null)
        {
            Materials materials = EnsureMaterials();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(8f, 1f, 8f);
            floor.GetComponent<Renderer>().sharedMaterial = materials.floor;
            var pillars = new GameObject("Pillars").transform;
            for (int i = 0; i < 12; i++)
            {
                var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pillar.name = "Pillar_" + i;
                pillar.transform.SetParent(pillars, false);
                float a = i * Mathf.PI * 2f / 12f;
                pillar.transform.position = new Vector3(Mathf.Cos(a) * 14f, 1.6f, Mathf.Sin(a) * 14f);
                pillar.transform.localScale = new Vector3(0.7f, 1.6f, 0.7f);
                pillar.GetComponent<Renderer>().sharedMaterial = materials.pillar;
            }

            var lightGo = new GameObject("Moonlight");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.86f, 0.9f, 1f);
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(42f, 150f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.46f, 0.49f, 0.6f);
            RenderSettings.skybox = null;

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.08f, 0.14f);
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 300f;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            camGo.AddComponent<AudioListener>();
            var volume = new GameObject("PostFX").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = EnsureVolumeProfile();

            var luna = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            luna.transform.position = new Vector3(0f, 0.02f, 0f);
            var controller = luna.AddComponent<CharacterController>();
            controller.skinWidth = 0.02f;
            controller.radius = 0.24f;
            controller.height = 1.62f;
            controller.center = new Vector3(0f, 0.81f, 0f);
            controller.stepOffset = 0.3f;
            var motor = luna.AddComponent<LunaMoonMotor>();
            motor.cameraTransform = camGo.transform;
            var showcase = luna.AddComponent<LunaMoonShowcase>();
            showcase.motor = motor;

            var orbit = camGo.AddComponent<LunaOrbitCamera>();
            orbit.target = luna.transform;
            orbit.yaw = 155f;
            orbit.pitch = 16f;
            orbit.distance = 4.6f;
            camGo.transform.SetPositionAndRotation(new Vector3(1.9f, 1.95f, 4.2f), Quaternion.Euler(12f, 205f, 0f));

            decorate?.Invoke(scene, luna);
            EnsureFolder(MotionFolder);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Scene save failed: " + ScenePath);
            return scene;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
