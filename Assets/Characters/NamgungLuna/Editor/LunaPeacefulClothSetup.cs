#if UNITY_EDITOR && MAGICACLOTH2
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MagicaCloth2;
using NamgungLuna.Magica;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NamgungLuna.Editor
{
    /// <summary>Reproducible cloth installation on the moving peaceful prefab; the active scene is never edited.</summary>
    public static class LunaPeacefulClothSetup
    {
        public const string DataPath = "Documentation/NamgungLuna/PeacefulCloth/cloth_data.json";
        public const string Folder = LunaStageSetup.Root + "/Peaceful/Cloth";
        const string BundlePath = Folder + "/LunaPeaceful_ClothMeshes.asset";
        [Serializable] public class Group { public string name, renderer; public float[] points, mobility; }
        [Serializable] public class ColliderSpec { public string name, bone, type; public float[] start, end; public float r0, r1; }
        [Serializable] public class Data { public int schemaVersion; public string source, sourceSha256; public float[] triangles; public Group[] groups; public ColliderSpec[] colliders; }
        sealed class Plan
        {
            public string name, preset;
            public float distance, damping, restoration, wind, inertia, radius, reduction, angle;
        }
        static readonly Plan[] Plans = {
            new Plan { name="Skirt",preset="Skirt",distance=.14f,damping=.22f,restoration=.38f,wind=.5f,inertia=.55f,radius=.006f,reduction=.009f,angle=48f },
            new Plan { name="Veil",preset="Cape",distance=.06f,damping=.16f,restoration=.4f,wind=.55f,inertia=.6f,radius=.005f,reduction=.009f,angle=36f },
            new Plan { name="CollarRibbon",preset="Accessory",distance=.028f,damping=.16f,restoration=.46f,wind=.2f,inertia=.35f,radius=.003f,reduction=.008f,angle=28f },
            new Plan { name="Cuff_L",preset="Skirt",distance=.005f,damping=.18f,restoration=.6f,wind=.15f,inertia=.3f,radius=.002f,reduction=.006f,angle=22f },
            new Plan { name="Cuff_R",preset="Skirt",distance=.005f,damping=.18f,restoration=.6f,wind=.15f,inertia=.3f,radius=.002f,reduction=.006f,angle=22f }
        };

        [MenuItem("Tools/Namgung Luna/Stage Capture/Install Peaceful Cloth")]
        public static void Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Finish Play mode before cloth installation.");
            if (!File.Exists(DataPath)) throw new FileNotFoundException("Generate peaceful_cloth_data.py first.", DataPath);
            LunaStageSetup.InstallPeaceful();
        }

        public static void ApplyTo(GameObject model)
        {
            var data = JsonUtility.FromJson<Data>(File.ReadAllText(DataPath));
            if (data.schemaVersion != 1 || data.groups.Length != Plans.Length) throw new InvalidOperationException("Unexpected peaceful cloth data schema.");
            using (var hash = System.Security.Cryptography.SHA256.Create())
            using (var source = File.OpenRead(LunaStageSetup.Root + "/Peaceful/Models/LunaPeaceful_Game.fbx"))
                if (!string.Equals(BitConverter.ToString(hash.ComputeHash(source)).Replace("-", ""), data.sourceSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The peaceful model changed. Regenerate peaceful_cloth_data.py before rebuilding its cloth.");
            EnsureFolder(Folder); EnsureFolder(Folder + "/PreBuild");
            var log = new StringBuilder("Peaceful Luna Magica Cloth 2 setup\n");
            var original = model.GetComponentsInChildren<SkinnedMeshRenderer>().ToDictionary(r => r.name);
            var bindWorld = original.ToDictionary(p => p.Key, p => SkinWorld(p.Value));
            var bundle = AssetDatabase.LoadAssetAtPath<LunaPeacefulMeshBundle>(BundlePath);
            if (!bundle) { bundle = ScriptableObject.CreateInstance<LunaPeacefulMeshBundle>(); AssetDatabase.CreateAsset(bundle, BundlePath); }
            var skins = Split(original[data.source], bindWorld[data.source], data, bundle, log);
            foreach (var group in data.groups.Where(g => g.renderer != data.source))
            {
                var skin = original[group.renderer];
                // The solid-color sleeve mesh has no authored UVs. Supply stable UV0 for Magica's mesh import.
                var mesh = Object.Instantiate(skin.sharedMesh); mesh.name = "LunaPeaceful_" + group.name;
                if (mesh.uv.Length != mesh.vertexCount)
                {
                    var bounds = mesh.bounds;
                    mesh.uv = mesh.vertices.Select(v => new Vector2(
                        Mathf.InverseLerp(bounds.min.x, bounds.max.x, v.x),
                        Mathf.InverseLerp(bounds.min.y, bounds.max.y, v.y))).ToArray();
                }
                skin.sharedMesh = SaveMesh(mesh, bundle); skins[group.name] = skin;
            }
            AssetDatabase.SaveAssetIfDirty(bundle);

            var bones = model.GetComponentsInChildren<Transform>().ToDictionary(t => t.name);
            var colliders = new Dictionary<string, ColliderComponent>();
            foreach (var spec in data.colliders)
            {
                if (!bones.TryGetValue(spec.bone, out Transform bone)) throw new InvalidOperationException("Missing collider bone " + spec.bone);
                var go = new GameObject("PeacefulCollider_" + spec.name);
                go.transform.SetParent(bone, false);
                Vector3 scale = bone.lossyScale;
                go.transform.localScale = new Vector3(1f / Mathf.Abs(scale.x), 1f / Mathf.Abs(scale.y), 1f / Mathf.Abs(scale.z));
                Vector3 a = ToUnity(spec.start), b = ToUnity(spec.end), direction = (a - b).normalized;
                if (spec.type == "sphere")
                {
                    go.transform.position = a;
                    var c = go.AddComponent<MagicaSphereCollider>(); c.SetSize(spec.r0); colliders.Add(spec.name, c);
                }
                else
                {
                    float length = Vector3.Distance(a, b) + spec.r0 + spec.r1;
                    go.transform.SetPositionAndRotation(a - direction * (length * .5f - spec.r0), Quaternion.FromToRotation(Vector3.up, direction));
                    var c = go.AddComponent<MagicaCapsuleCollider>(); c.direction = MagicaCapsuleCollider.Direction.Y;
                    c.alignedOnCenter = true; c.SetSize(spec.r0, spec.r1, length); colliders.Add(spec.name, c);
                }
            }

            // Paint source is in the model bind pose. Keep its nearest-vertex association while the bones enter the
            // folded-hands idle pose, then prebuild in that actual spawn pose to avoid a first-frame cloth snap.
            var paint = new Dictionary<string, bool[]>();
            foreach (var group in data.groups)
            {
                var skin = skins[group.name]; var points = new List<Vector3>();
                for (int i = 0; i < group.points.Length; i += 4) points.Add(ToUnity(group.points, i));
                var nearest = new PointLookup(points.ToArray(), .006f);
                paint[group.name] = SkinWorld(skin).Select(v => group.points[nearest.Nearest(v) * 4 + 3] < 1.5f).ToArray();
            }
            var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(LunaStageSetup.Root + "/Stage/LunaPeaceful_Idle.anim");
            idle.SampleAnimation(model, 0f);
            var clothRoot = new GameObject("Peaceful_MagicaCloth").transform;
            clothRoot.SetParent(model.transform, false);
            bool oldBurstSync = Unity.Burst.BurstCompiler.Options.EnableBurstCompileSynchronously;
            try
            {
                Unity.Burst.BurstCompiler.Options.EnableBurstCompileSynchronously = true;
                foreach (var plan in Plans)
                {
                    var group = data.groups.Single(g => g.name == plan.name); var skin = skins[plan.name];
                    var go = new GameObject("Cloth_" + plan.name); go.transform.SetParent(clothRoot, false);
                    var cloth = go.AddComponent<MagicaCloth>(); var s = cloth.SerializeData;
                    var preset = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/MagicaCloth2/Res/Preset/MC2_Preset_" + plan.preset + ".json");
                    if (!preset || !s.ImportJson(preset.text)) throw new InvalidOperationException("Missing cloth preset " + plan.preset);
                    s.clothType = ClothProcess.ClothType.MeshCloth;
                    s.sourceRenderers = new List<Renderer> { skin };
                    s.paintMode = ClothSerializeData.PaintMode.Manual;
                    s.updateMode = ClothUpdateMode.AnimatorLinkage;
                    s.disableMode = ClothDisableMode.Reset;
                    // Retain the dress's authored folds while adding secondary fabric motion. This also
                    // keeps the render surface clear of the forward shin during low-frame-rate dashes.
                    s.blendWeight = plan.name == "Skirt" ? .5f : 1f;
                    s.stablizationTimeAfterReset = .2f;
                    s.reductionSetting.simpleDistance = plan.reduction;
                    s.reductionSetting.shapeDistance = plan.reduction * 1.3f;
                    s.gravity = 4f; s.gravityFalloff = 1f;
                    s.damping = new CurveSerializeData(plan.damping);
                    s.radius = new CurveSerializeData(plan.radius);
                    s.distanceConstraint.stiffness = new CurveSerializeData(1f, 1f, .9f);
                    s.triangleBendingConstraint.stiffness = .7f;
                    s.angleRestorationConstraint.useAngleRestoration = true;
                    s.angleRestorationConstraint.stiffness = new CurveSerializeData(plan.restoration, 1f, .5f);
                    s.angleLimitConstraint.useAngleLimit = true;
                    s.angleLimitConstraint.limitAngle = new CurveSerializeData(plan.angle, .2f, 1f);
                    s.motionConstraint.useMaxDistance = true;
                    s.motionConstraint.maxDistance = new CurveSerializeData(plan.distance, .05f, 1f);
                    // Keep the reconstructed skirt surface outside its animated body-facing envelope.
                    // Capsule collisions alone can miss concave folds between reduced proxy vertices.
                    if (plan.name == "Skirt")
                    {
                        s.motionConstraint.useBackstop = true;
                        s.motionConstraint.backstopRadius = .3f;
                        s.motionConstraint.backstopDistance = new CurveSerializeData(.006f);
                    }
                    s.wind.influence = .2f; s.wind.movingWind = plan.wind;
                    s.wind.turbulence = .25f; s.wind.frequency = .8f; s.wind.depthWeight = .5f;
                    var inertia = s.inertiaConstraint;
                    inertia.worldInertia = plan.inertia; inertia.localInertia = .65f;
                    inertia.movementInertiaSmoothing = .6f; inertia.depthInertia = .4f; inertia.centrifualAcceleration = .03f;
                    inertia.particleSpeedLimit = new CheckSliderSerializeData(true, 3f);
                    inertia.movementSpeedLimit = new CheckSliderSerializeData(true, 4f);
                    inertia.localMovementSpeedLimit = new CheckSliderSerializeData(true, 3f);
                    inertia.rotationSpeedLimit = new CheckSliderSerializeData(true, 360f);
                    inertia.localRotationSpeedLimit = new CheckSliderSerializeData(true, 360f);
                    inertia.teleportMode = InertiaConstraint.TeleportMode.Reset;
                    inertia.teleportDistance = 2f; inertia.teleportRotation = 150f;
                    s.colliderCollisionConstraint.mode = ColliderCollisionConstraint.Mode.Edge;
                    s.colliderCollisionConstraint.friction = .08f;
                    s.colliderCollisionConstraint.colliderList = colliders.Where(p => ColliderFor(plan.name, p.Key)).Select(p => p.Value).ToList();
                    // The source reconstruction fuses ivory/navy panels: one skirt solver keeps their common seam intact.
                    s.selfCollisionConstraint.selfMode = SelfCollisionConstraint.SelfCollisionMode.None;
                    s.DataValidate();
                    Vector3[] world = SkinWorld(skin); bool[] fixedMask = paint[plan.name];
                    var selection = new SelectionData(world.Length) { userEdit = true, maxConnectionDistance = .004f };
                    for (int i = 0; i < world.Length; i++)
                    {
                        selection.positions[i] = (float3)go.transform.InverseTransformPoint(world[i]);
                        selection.attributes[i] = fixedMask[i] ? MagicaCloth2.VertexAttribute.Fixed : MagicaCloth2.VertexAttribute.Move;
                    }
                    if (!fixedMask.Any(f => f) || fixedMask.All(f => f)) throw new InvalidOperationException("Cloth needs fixed and movable vertices: " + plan.name);
                    cloth.GetSerializeData2().selectionData = selection;
                    Build(cloth, Folder + "/PreBuild/LunaPeaceful_" + plan.name + ".asset");
                    log.AppendLine($"{plan.name}: {world.Length} vertices, {fixedMask.Count(f => f)} fixed, max motion {plan.distance:F3}m, {s.colliderCollisionConstraint.colliderList.Count} colliders.");
                }
            }
            finally { Unity.Burst.BurstCompiler.Options.EnableBurstCompileSynchronously = oldBurstSync; }
            log.AppendLine("Collider count: " + colliders.Count + "; spawn pose: folded hands; reset on disable / >2m teleport.");
            File.WriteAllText("Documentation/NamgungLuna/PeacefulCloth/setup.txt", log.ToString());
        }

        static bool ColliderFor(string part, string collider)
        {
            collider = collider.ToLowerInvariant();
            if (part == "Skirt") return collider.Contains("pelvis") || collider.Contains("thigh") || collider.Contains("shin") || collider.Contains("boot") || collider.Contains("waist");
            if (part == "Veil") return collider.Contains("head") || collider.Contains("neck") || collider.Contains("torso") || collider.Contains("shoulder") || collider.Contains("back");
            if (part == "CollarRibbon") return collider.Contains("torso") || collider.Contains("neck");
            return collider.Contains(part.EndsWith("L") ? "left" : "right") && (collider.Contains("forearm") || collider.Contains("hand") || collider.Contains("wrist"));
        }

        static void Build(MagicaCloth cloth, string path)
        {
            var fresh = ScriptableObject.CreateInstance<PreBuildScriptableObject>();
            fresh.name = Path.GetFileNameWithoutExtension(path);
            var data = cloth.GetSerializeData2().preBuildData; data.preBuildScriptableObject = fresh;
            var result = PreBuildDataCreation.CreatePreBuildData(cloth, false);
            if (result.IsError()) { Object.DestroyImmediate(fresh); throw new InvalidOperationException(cloth.name + ": " + result.GetResultString()); }
            if (fresh.sharePreBuildDataList.Count == 0) throw new InvalidOperationException("Empty cloth prebuild: " + cloth.name);
            var saved = AssetDatabase.LoadAssetAtPath<PreBuildScriptableObject>(path);
            if (saved) { EditorUtility.CopySerialized(fresh, saved); Object.DestroyImmediate(fresh); EditorUtility.SetDirty(saved); }
            else { saved = fresh; AssetDatabase.CreateAsset(saved, path); }
            saved.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.SaveAssetIfDirty(saved); data.preBuildScriptableObject = saved; data.enabled = true;
            EditorUtility.SetDirty(cloth);
        }

        static Dictionary<string, SkinnedMeshRenderer> Split(SkinnedMeshRenderer src, Vector3[] world, Data data, LunaPeacefulMeshBundle bundle, StringBuilder log)
        {
            var centers = new List<Vector3>(); var groups = new List<int>();
            for (int i = 0; i < data.triangles.Length; i += 4) { centers.Add(ToUnity(data.triangles, i)); groups.Add(Mathf.RoundToInt(data.triangles[i + 3])); }
            var nearest = new PointLookup(centers.ToArray(), .001f);
            var tris = src.sharedMesh.triangles; var perGroup = new Dictionary<int, List<int>>();
            foreach (int group in new[] { -1 }.Concat(Enumerable.Range(0, data.groups.Length).Where(g => data.groups[g].renderer == data.source))) perGroup[group] = new List<int>();
            float largestError = 0;
            for (int t = 0; t < tris.Length; t += 3)
            {
                Vector3 center = (world[tris[t]] + world[tris[t + 1]] + world[tris[t + 2]]) / 3f;
                int index = nearest.Nearest(center); largestError = Mathf.Max(largestError, Vector3.Distance(center, centers[index]));
                perGroup[groups[index]].AddRange(new[] { tris[t], tris[t + 1], tris[t + 2] });
            }
            if (largestError > .002f) throw new InvalidOperationException("Cloth triangle mapping mismatch: " + largestError + "m");
            var result = new Dictionary<string, SkinnedMeshRenderer>(); var sourceMesh = src.sharedMesh;
            foreach (var pair in perGroup)
            {
                string name = pair.Key == -1 ? "FixedBody" : data.groups[pair.Key].name;
                Mesh mesh = Subset(sourceMesh, pair.Value, "LunaPeaceful_" + name);
                mesh = SaveMesh(mesh, bundle);
                var renderer = src;
                if (pair.Key != -1)
                {
                    var go = new GameObject("LunaPeaceful_" + name); go.transform.SetParent(src.transform.parent, false);
                    go.transform.localPosition = src.transform.localPosition; go.transform.localRotation = src.transform.localRotation; go.transform.localScale = src.transform.localScale;
                    renderer = go.AddComponent<SkinnedMeshRenderer>(); renderer.bones = src.bones; renderer.rootBone = src.rootBone;
                    renderer.sharedMaterials = src.sharedMaterials; renderer.quality = src.quality;
                    renderer.updateWhenOffscreen = true; renderer.localBounds = src.localBounds;
                    renderer.shadowCastingMode = src.shadowCastingMode; renderer.receiveShadows = src.receiveShadows;
                }
                renderer.sharedMesh = mesh; result[name] = renderer;
                log.AppendLine(name + ": split " + mesh.vertexCount + " vertices.");
            }
            return result;
        }

        static Mesh SaveMesh(Mesh mesh, LunaPeacefulMeshBundle bundle)
        {
            var existing = AssetDatabase.LoadAllAssetsAtPath(BundlePath).OfType<Mesh>().FirstOrDefault(m => m.name == mesh.name);
            if (existing) { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); mesh = existing; EditorUtility.SetDirty(mesh); }
            else AssetDatabase.AddObjectToAsset(mesh, bundle);
            AssetDatabase.SaveAssetIfDirty(mesh); return mesh;
        }

        static Mesh Subset(Mesh src, List<int> triangles, string name)
        {
            int[] order = triangles.Distinct().ToArray(); var map = order.Select((old, index) => new { old, index }).ToDictionary(p => p.old, p => p.index);
            var mesh = new Mesh { name = name }; T[] Pick<T>(T[] values) => order.Select(i => values[i]).ToArray();
            mesh.vertices = Pick(src.vertices); mesh.normals = Pick(src.normals); mesh.tangents = Pick(src.tangents);
            for (int channel = 0; channel < 8; channel++)
            {
                var uv = new List<Vector4>(); src.GetUVs(channel, uv); if (uv.Count > 0) mesh.SetUVs(channel, order.Select(i => uv[i]).ToList());
            }
            if (src.colors32.Length > 0) mesh.colors32 = Pick(src.colors32);
            mesh.boneWeights = Pick(src.boneWeights); mesh.bindposes = src.bindposes;
            mesh.triangles = triangles.Select(i => map[i]).ToArray(); mesh.RecalculateBounds(); return mesh;
        }

        public static Vector3[] SkinWorld(SkinnedMeshRenderer skin)
        {
            Mesh mesh = skin.sharedMesh; var positions = mesh.vertices; var weights = mesh.boneWeights;
            var matrices = skin.bones.Select((b, i) => b.localToWorldMatrix * mesh.bindposes[i]).ToArray();
            var result = new Vector3[positions.Length];
            for (int i = 0; i < result.Length; i++)
            {
                var w = weights[i]; Vector3 v = positions[i];
                result[i] = matrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0 + matrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1
                    + matrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2 + matrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
            }
            return result;
        }
        static Vector3 ToUnity(float[] p, int offset = 0) => new Vector3(-p[offset], p[offset + 2], -p[offset + 1]);
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        sealed class PointLookup
        {
            readonly Vector3[] points; readonly float cell; readonly Dictionary<Vector3Int, List<int>> grid = new Dictionary<Vector3Int, List<int>>();
            public PointLookup(Vector3[] points, float cell)
            {
                this.points = points; this.cell = cell;
                for (int i = 0; i < points.Length; i++) { var key = Vector3Int.FloorToInt(points[i] / cell); if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>(); list.Add(i); }
            }
            public int Nearest(Vector3 point)
            {
                var center = Vector3Int.FloorToInt(point / cell); int best = -1; float distance = float.MaxValue;
                for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                    if (grid.TryGetValue(center + new Vector3Int(x,y,z), out var list)) foreach (int i in list)
                    { float d = (points[i] - point).sqrMagnitude; if (d < distance) { distance = d; best = i; } }
                if (best >= 0) return best;
                for (int i = 0; i < points.Length; i++) { float d = (points[i] - point).sqrMagnitude; if (d < distance) { distance = d; best = i; } }
                if (best < 0) throw new InvalidOperationException("Empty cloth point cloud."); return best;
            }
        }
    }
}
#endif
