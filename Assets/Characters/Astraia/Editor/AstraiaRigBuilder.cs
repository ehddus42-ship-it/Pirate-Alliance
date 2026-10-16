using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PirateAlliance.Characters.Editor
{
    /// <summary>
    /// Rebuilds Astraia's incomplete arm-only source rig as a reusable Humanoid.
    /// Meshes are bound in the supplied A-pose, then the actual skeleton is placed
    /// in a T-pose before its Avatar is built. Source meshes are never modified.
    /// </summary>
    public static class AstraiaRigBuilder
    {
        public const string OriginalModelPath = "Assets/Characters/Astraia/Model/Astraia.fbx";
        public const string OptimizedModelPath = "Assets/Characters/Astraia/Model/AstraiaOptimized.fbx";
        public const string RigFolder = "Assets/Characters/Astraia/Rig";
        public const string PrefabPath = RigFolder + "/AstraiaVisual.prefab";
        public const string AvatarPath = RigFolder + "/AstraiaHumanoidAvatar.asset";
        public const float TargetHeight = 1.72f;

        private sealed class Part
        {
            public string name;
            public Mesh mesh;
            public Material[] materials;
            public Matrix4x4 sourceToWorld;
            public bool ownsMesh;
            public Bounds bounds;
        }

        private sealed class Rig
        {
            public readonly List<Transform> bones = new List<Transform>();
            public readonly Dictionary<HumanBodyBones, int> indices = new Dictionary<HumanBodyBones, int>();
            public readonly Dictionary<HumanBodyBones, Vector3> rest = new Dictionary<HumanBodyBones, Vector3>();
            public Transform root;
            public float scale;

            public Transform Add(HumanBodyBones id, HumanBodyBones? parent, Vector3 position)
            {
                var bone = new GameObject(id.ToString()).transform;
                bone.SetParent(parent.HasValue ? bones[indices[parent.Value]] : root, false);
                bone.position = position;
                bone.rotation = Quaternion.identity;
                bone.localScale = Vector3.one;
                indices.Add(id, bones.Count);
                bones.Add(bone);
                rest.Add(id, position);
                return bone;
            }

            public Vector3 P(HumanBodyBones id) { return rest[id]; }
            public Transform T(HumanBodyBones id) { return bones[indices[id]]; }
        }

        // A small fixed accumulator avoids per-vertex allocations on the original dense source.
        private struct Weights
        {
            public int i0, i1, i2, i3;
            public float w0, w1, w2, w3;

            public void Add(int index, float weight)
            {
                if (weight <= 0f) return;
                if (w0 > 0f && i0 == index) { w0 += weight; return; }
                if (w1 > 0f && i1 == index) { w1 += weight; return; }
                if (w2 > 0f && i2 == index) { w2 += weight; return; }
                if (w3 > 0f && i3 == index) { w3 += weight; return; }
                if (w0 == 0f) { i0 = index; w0 = weight; return; }
                if (w1 == 0f) { i1 = index; w1 = weight; return; }
                if (w2 == 0f) { i2 = index; w2 = weight; return; }
                if (w3 == 0f) { i3 = index; w3 = weight; return; }
                if (w0 <= w1 && w0 <= w2 && w0 <= w3) { if (weight > w0) { i0 = index; w0 = weight; } }
                else if (w1 <= w2 && w1 <= w3) { if (weight > w1) { i1 = index; w1 = weight; } }
                else if (w2 <= w3) { if (weight > w2) { i2 = index; w2 = weight; } }
                else if (weight > w3) { i3 = index; w3 = weight; }
            }

            public BoneWeight Finish(int fallback)
            {
                float sum = w0 + w1 + w2 + w3;
                if (sum < 0.00001f) { i0 = fallback; w0 = 1f; sum = 1f; }
                // Unity expects the largest influence first.
                SwapIfLess(ref i0, ref w0, ref i1, ref w1);
                SwapIfLess(ref i2, ref w2, ref i3, ref w3);
                SwapIfLess(ref i0, ref w0, ref i2, ref w2);
                SwapIfLess(ref i1, ref w1, ref i3, ref w3);
                SwapIfLess(ref i1, ref w1, ref i2, ref w2);
                return new BoneWeight
                {
                    boneIndex0 = i0, weight0 = w0 / sum,
                    boneIndex1 = i1, weight1 = w1 / sum,
                    boneIndex2 = i2, weight2 = w2 / sum,
                    boneIndex3 = i3, weight3 = w3 / sum
                };
            }

            private static void SwapIfLess(ref int a, ref float x, ref int b, ref float y)
            {
                if (x >= y) return;
                int oldIndex = a; a = b; b = oldIndex;
                float oldWeight = x; x = y; y = oldWeight;
            }
        }

        [MenuItem("Tools/Pirate Alliance/Astraia/Rebuild Humanoid Visual")]
        private static void BuildFromMenu()
        {
            Selection.activeGameObject = Build();
        }

        public static GameObject Build()
        {
            string path = AssetDatabase.LoadAssetAtPath<GameObject>(OptimizedModelPath) != null
                ? OptimizedModelPath : OriginalModelPath;
            return Build(path);
        }

        public static GameObject Build(string sourceAssetPath)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before rebuilding Astraia.");

            GameObject sourceAsset = AssetDatabase.LoadAssetAtPath<GameObject>(sourceAssetPath);
            if (sourceAsset == null) throw new FileNotFoundException("Astraia model was not imported.", sourceAssetPath);
            EnsureFolder(RigFolder);
            var stage = new GameObject("Astraia_RigBuild_Source") { hideFlags = HideFlags.HideAndDontSave };
            var source = UnityEngine.Object.Instantiate(sourceAsset, stage.transform, false);
            GameObject visual = null;
            List<Part> parts = null;
            try
            {
                parts = ReadParts(source);
                if (parts.Count == 0) throw new InvalidOperationException("The source contains no character meshes.");
                Bounds modelBounds = parts[0].bounds;
                foreach (Part part in parts) modelBounds.Encapsulate(part.bounds);
                if (modelBounds.size.y < 0.1f || modelBounds.size.y > 10f || modelBounds.size.x > 10f)
                    throw new InvalidOperationException("Source mesh transforms are invalid. Use the evaluated, static AstraiaOptimized.fbx export.");

                float scale = TargetHeight / modelBounds.size.y;
                // The authoring origin is on the center line; using the symmetric footwear
                // bounds avoids moving the body because an asymmetric hair ornament protrudes.
                Part footwear = parts.Find(p => Contains(p.name, "shoe") || Contains(p.name, "boot"));
                float centerX = footwear != null ? footwear.bounds.center.x : modelBounds.center.x;
                var sourceOrigin = new Vector3(centerX, modelBounds.min.y, 0f);
                Matrix4x4 normalize = Matrix4x4.Scale(Vector3.one * scale) * Matrix4x4.Translate(-sourceOrigin);

                visual = new GameObject("AstraiaVisual");
                var rig = CreateSkeleton(visual.transform, source, normalize, scale);
                Matrix4x4[] bindposes = rig.bones.Select(b => b.worldToLocalMatrix * visual.transform.localToWorldMatrix).ToArray();

                for (int i = 0; i < parts.Count; i++)
                {
                    Part part = parts[i];
                    EditorUtility.DisplayProgressBar("Astraia Humanoid", "Binding " + part.name, (float)i / parts.Count);
                    Mesh mesh = ConvertMesh(part, normalize, rig, bindposes);
                    mesh = SaveOrUpdate(mesh, RigFolder + "/" + CleanFileName(part.name) + "_Skinned.asset");
                    var child = new GameObject(part.name);
                    child.transform.SetParent(visual.transform, false);
                    var renderer = child.AddComponent<SkinnedMeshRenderer>();
                    renderer.sharedMesh = mesh;
                    renderer.sharedMaterials = part.materials;
                    renderer.bones = rig.bones.ToArray();
                    renderer.rootBone = rig.T(HumanBodyBones.Hips);
                    renderer.quality = SkinQuality.Bone4;
                    renderer.updateWhenOffscreen = false;
                    renderer.skinnedMotionVectors = true;
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    // Skinned bounds are relative to rootBone (hips), not the visual origin.
                    renderer.localBounds = new Bounds(Vector3.zero, new Vector3(2.6f, 2.5f, 2.2f));
                }

                PlaceArmsInTPose(rig);
                Avatar avatar = CreateAvatar(visual, rig);
                if (avatar == null || !avatar.isValid || !avatar.isHuman)
                {
                    if (avatar != null) UnityEngine.Object.DestroyImmediate(avatar);
                    throw new InvalidOperationException("Astraia Avatar validation failed; no invalid prefab was saved.");
                }
                avatar.name = "AstraiaHumanoidAvatar";
                avatar = SaveOrUpdate(avatar, AvatarPath);
                var animator = visual.AddComponent<Animator>();
                animator.avatar = avatar;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                PrefabUtility.SaveAsPrefabAssetAndConnect(visual, PrefabPath, InteractionMode.AutomatedAction);
                AssetDatabase.SaveAssets();
                Debug.Log("Astraia Humanoid created: " + parts.Sum(p => p.mesh.vertexCount) + " vertices, " + rig.bones.Count + " mapped bones, height " + TargetHeight + "m. " + PrefabPath);
                return visual;
            }
            catch
            {
                if (visual != null) UnityEngine.Object.DestroyImmediate(visual);
                throw;
            }
            finally
            {
                if (parts != null)
                    foreach (Part part in parts)
                        if (part.ownsMesh && part.mesh != null) UnityEngine.Object.DestroyImmediate(part.mesh);
                UnityEngine.Object.DestroyImmediate(stage);
                EditorUtility.ClearProgressBar();
            }
        }

        private static List<Part> ReadParts(GameObject source)
        {
            var result = new List<Part>();
            foreach (Renderer renderer in source.GetComponentsInChildren<Renderer>(true))
            {
                // Deliberately excludes authoring cameras/lights and Blender startup objects.
                if (!renderer.name.StartsWith("Astraia", StringComparison.OrdinalIgnoreCase)) continue;
                Mesh mesh;
                bool owns = false;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    if (skinned.sharedMesh == null) continue;
                    mesh = new Mesh();
                    skinned.BakeMesh(mesh, false);
                    owns = true;
                }
                else
                {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null) continue;
                    mesh = filter.sharedMesh;
                }
                Vector3[] vertices = mesh.vertices;
                if (vertices.Length == 0) { if (owns) UnityEngine.Object.DestroyImmediate(mesh); continue; }
                Matrix4x4 matrix = renderer.localToWorldMatrix;
                var bounds = new Bounds(matrix.MultiplyPoint3x4(vertices[0]), Vector3.zero);
                for (int i = 1; i < vertices.Length; i++) bounds.Encapsulate(matrix.MultiplyPoint3x4(vertices[i]));
                result.Add(new Part { name = renderer.name, mesh = mesh, materials = renderer.sharedMaterials,
                    sourceToWorld = matrix, ownsMesh = owns, bounds = bounds });
            }
            return result;
        }

        private static Rig CreateSkeleton(Transform visual, GameObject source, Matrix4x4 normalize, float scale)
        {
            var skeleton = new GameObject("AstraiaSkeleton").transform;
            skeleton.SetParent(visual, false);
            var rig = new Rig { root = skeleton, scale = scale };
            Func<Vector3, Vector3> p = v => normalize.MultiplyPoint3x4(v);
            rig.Add(HumanBodyBones.Hips, null, p(new Vector3(0f, 0.866f, -0.025f)));
            rig.Add(HumanBodyBones.Spine, HumanBodyBones.Hips, p(new Vector3(0f, 0.994f, -0.030f)));
            rig.Add(HumanBodyBones.Chest, HumanBodyBones.Spine, p(new Vector3(0f, 1.154f, -0.040f)));
            rig.Add(HumanBodyBones.UpperChest, HumanBodyBones.Chest, p(new Vector3(0f, 1.253f, -0.043f)));
            rig.Add(HumanBodyBones.Neck, HumanBodyBones.UpperChest, p(new Vector3(0f, 1.397f, -0.033f)));
            rig.Add(HumanBodyBones.Head, HumanBodyBones.Neck, p(new Vector3(0f, 1.470f, -0.024f)));
            AddSide(rig, source, normalize, true);
            AddSide(rig, source, normalize, false);
            var headTip = new GameObject("HeadTip").transform;
            headTip.SetParent(rig.T(HumanBodyBones.Head), false);
            headTip.position = p(new Vector3(0f, 1.69f, -0.02f));
            return rig;
        }

        private static void AddSide(Rig rig, GameObject source, Matrix4x4 normalize, bool left)
        {
            float sign = left ? -1f : 1f;
            string suffix = left ? ".L" : ".R";
            HumanBodyBones shoulder = left ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder;
            HumanBodyBones upper = left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
            HumanBodyBones lower = left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm;
            HumanBodyBones hand = left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
            HumanBodyBones thigh = left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg;
            HumanBodyBones shin = left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg;
            HumanBodyBones foot = left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot;
            HumanBodyBones toe = left ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes;
            Func<Vector3, Vector3> p = v => normalize.MultiplyPoint3x4(v);
            rig.Add(shoulder, HumanBodyBones.UpperChest, p(new Vector3(sign * 0.066f, 1.276f, -0.042f)));
            rig.Add(upper, shoulder, p(SourcePosition(source, "upperarm" + suffix, new Vector3(sign * 0.160f, 1.2849f, -0.0413f))));
            rig.Add(lower, upper, p(SourcePosition(source, "forearm" + suffix, new Vector3(sign * 0.2034f, 1.1089f, -0.0359f))));
            rig.Add(hand, lower, p(SourcePosition(source, "hand" + suffix, new Vector3(sign * 0.321f, 0.9509f, 0.0234f))));
            var tip = new GameObject(left ? "LeftHandTip" : "RightHandTip").transform;
            tip.SetParent(rig.T(hand), false);
            tip.position = p(SourcePosition(source, "hand" + suffix + "_end", new Vector3(sign * 0.4067f, 0.7968f, 0.0704f)));
            rig.Add(thigh, HumanBodyBones.Hips, p(new Vector3(sign * 0.076f, 0.840f, -0.025f)));
            rig.Add(shin, thigh, p(new Vector3(sign * 0.087f, 0.451f, -0.015f)));
            rig.Add(foot, shin, p(new Vector3(sign * 0.091f, 0.115f, -0.029f)));
            rig.Add(toe, foot, p(new Vector3(sign * 0.091f, 0.049f, 0.089f)));
            var toeTip = new GameObject(left ? "LeftToeTip" : "RightToeTip").transform;
            toeTip.SetParent(rig.T(toe), false);
            toeTip.position = p(new Vector3(sign * 0.091f, 0.042f, 0.132f));
        }

        private static Vector3 SourcePosition(GameObject source, string name, Vector3 fallback)
        {
            Transform bone = source.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
            return bone != null ? bone.position : fallback;
        }

        private static Mesh ConvertMesh(Part part, Matrix4x4 normalize, Rig rig, Matrix4x4[] bindposes)
        {
            Mesh mesh = UnityEngine.Object.Instantiate(part.mesh);
            mesh.name = part.name + "_Humanoid";
            mesh.ClearBlendShapes();
            Matrix4x4 matrix = normalize * part.sourceToWorld;
            Matrix4x4 normalMatrix = matrix.inverse.transpose;
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector4[] tangents = mesh.tangents;
            bool mirrored = matrix.determinant < 0f;
            var weights = new BoneWeight[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
                weights[i] = WeightVertex(vertices[i], part.name, rig);
                if (normals.Length == vertices.Length) normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
                if (tangents.Length == vertices.Length)
                {
                    Vector3 tangent = matrix.MultiplyVector(new Vector3(tangents[i].x, tangents[i].y, tangents[i].z)).normalized;
                    tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, tangents[i].w * (mirrored ? -1f : 1f));
                }
            }
            mesh.vertices = vertices;
            if (normals.Length == vertices.Length) mesh.normals = normals; else mesh.RecalculateNormals();
            if (tangents.Length == vertices.Length) mesh.tangents = tangents;
            if (mirrored)
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    if (mesh.GetTopology(submesh) != MeshTopology.Triangles) continue;
                    int[] indices = mesh.GetTriangles(submesh);
                    for (int j = 0; j < indices.Length; j += 3) { int temp = indices[j + 1]; indices[j + 1] = indices[j + 2]; indices[j + 2] = temp; }
                    mesh.SetTriangles(indices, submesh, false);
                }
            mesh.boneWeights = weights;
            mesh.bindposes = bindposes;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static BoneWeight WeightVertex(Vector3 position, string partName, Rig rig)
        {
            var weights = new Weights();
            bool headPart = Contains(partName, "face") || Contains(partName, "hair") || Contains(partName, "earring") || Contains(partName, "eye");
            if (headPart)
            {
                weights.Add(rig.indices[HumanBodyBones.Head], 1f);
                return weights.Finish(rig.indices[HumanBodyBones.Head]);
            }

            bool dress = Contains(partName, "dress") || Contains(partName, "skirt");
            bool footwear = Contains(partName, "shoe") || Contains(partName, "boot");
            bool stockings = Contains(partName, "stocking");
            float hipsY = rig.P(HumanBodyBones.Hips).y;
            float scale = rig.scale;

            if (footwear || stockings)
            {
                AddLeg(ref weights, position, rig, 1f, footwear);
                return weights.Finish(rig.indices[HumanBodyBones.Hips]);
            }

            float arm = ArmAmount(position, rig);
            // The skirt is wide near resting hands; this gate keeps it attached to the pelvis.
            if (dress) arm *= Smooth(hipsY + 0.075f * scale, hipsY + 0.18f * scale, position.y);
            if (arm > 0.001f) AddArm(ref weights, position, rig, arm);
            float body = 1f - arm;
            if (body > 0.001f)
            {
                if (dress && position.y < hipsY)
                {
                    float drop = Mathf.Clamp01((hipsY - position.y) / (0.57f * scale));
                    float leg = Mathf.Lerp(0.18f, 0.43f, drop);
                    weights.Add(rig.indices[HumanBodyBones.Hips], body * (1f - leg));
                    float side = Smooth(-0.11f * scale, 0.11f * scale, position.x);
                    weights.Add(rig.indices[HumanBodyBones.LeftUpperLeg], body * leg * (1f - side));
                    weights.Add(rig.indices[HumanBodyBones.RightUpperLeg], body * leg * side);
                }
                else if (position.y < hipsY - 0.025f * scale)
                {
                    float pelvis = Smooth(hipsY - 0.115f * scale, hipsY + 0.015f * scale, position.y);
                    weights.Add(rig.indices[HumanBodyBones.Hips], body * pelvis);
                    AddLeg(ref weights, position, rig, body * (1f - pelvis), false);
                }
                else AddTorso(ref weights, position.y, rig, body);
            }
            return weights.Finish(rig.indices[HumanBodyBones.Hips]);
        }

        private static void AddTorso(ref Weights weights, float y, Rig rig, float amount)
        {
            HumanBodyBones[] chain = TorsoChain;
            for (int i = 0; i < chain.Length - 1; i++)
            {
                float lower = rig.P(chain[i]).y;
                float upper = rig.P(chain[i + 1]).y;
                if (y > upper) continue;
                float blend = Smooth(lower, upper, y);
                weights.Add(rig.indices[chain[i]], amount * (1f - blend));
                weights.Add(rig.indices[chain[i + 1]], amount * blend);
                return;
            }
            weights.Add(rig.indices[HumanBodyBones.Head], amount);
        }

        private static readonly HumanBodyBones[] TorsoChain =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest,
            HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head
        };

        private static float ArmAmount(Vector3 p, Rig rig)
        {
            bool left = p.x < 0f;
            Vector3 upper = rig.P(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            Vector3 lower = rig.P(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            Vector3 hand = rig.P(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            Vector3 tip = hand + (hand - lower).normalized * 0.175f * rig.scale;
            float distance = Mathf.Min(SegmentDistance(p, upper, lower), Mathf.Min(SegmentDistance(p, lower, hand), SegmentDistance(p, hand, tip)));
            float lateral = Smooth(0.110f * rig.scale, 0.173f * rig.scale, Mathf.Abs(p.x));
            float proximity = 1f - Smooth(0.055f * rig.scale, 0.12f * rig.scale, distance);
            return lateral * proximity;
        }

        private static void AddArm(ref Weights weights, Vector3 p, Rig rig, float amount)
        {
            bool left = p.x < 0f;
            HumanBodyBones upper = left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
            HumanBodyBones lower = left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm;
            HumanBodyBones hand = left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
            Vector3 a = rig.P(upper), b = rig.P(lower), c = rig.P(hand);
            float upperLength = Vector3.Distance(a, b), lowerLength = Vector3.Distance(b, c);
            float dUpper = SegmentDistance(p, a, b), dLower = SegmentDistance(p, b, c);
            float along;
            if (dUpper < dLower) along = Vector3.Dot(p - a, (b - a).normalized);
            else along = upperLength + Vector3.Dot(p - b, (c - b).normalized);
            float elbow = Smooth(upperLength - 0.047f * rig.scale, upperLength + 0.047f * rig.scale, along);
            float wrist = Smooth(upperLength + lowerLength - 0.042f * rig.scale, upperLength + lowerLength + 0.022f * rig.scale, along);
            weights.Add(rig.indices[upper], amount * (1f - elbow));
            weights.Add(rig.indices[lower], amount * elbow * (1f - wrist));
            weights.Add(rig.indices[hand], amount * wrist);
        }

        private static void AddLeg(ref Weights weights, Vector3 p, Rig rig, float amount, bool footwear)
        {
            if (amount < 0.0001f) return;
            // The center transition prevents a visible weight seam in the pelvis.
            float right = Smooth(-0.018f * rig.scale, 0.018f * rig.scale, p.x);
            if (right < 0.9999f) AddSingleLeg(ref weights, p, rig, amount * (1f - right), true, footwear);
            if (right > 0.0001f) AddSingleLeg(ref weights, p, rig, amount * right, false, footwear);
        }

        private static void AddSingleLeg(ref Weights weights, Vector3 p, Rig rig, float amount, bool left, bool footwear)
        {
            HumanBodyBones thigh = left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg;
            HumanBodyBones shin = left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg;
            HumanBodyBones foot = left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot;
            HumanBodyBones toe = left ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes;
            float knee = rig.P(shin).y;
            float ankle = rig.P(foot).y;
            float upper = Smooth(knee - 0.065f * rig.scale, knee + 0.075f * rig.scale, p.y);
            float lower = Smooth(ankle - 0.035f * rig.scale, ankle + 0.060f * rig.scale, p.y);
            // Stiff boots keep their heel and sole together; the shaft still bends at the ankle.
            float toes = footwear ? 0f : Smooth(rig.P(toe).z - 0.012f * rig.scale, rig.P(toe).z + 0.030f * rig.scale, p.z);
            weights.Add(rig.indices[thigh], amount * upper);
            weights.Add(rig.indices[shin], amount * (1f - upper) * lower);
            weights.Add(rig.indices[foot], amount * (1f - upper) * (1f - lower) * (1f - toes));
            weights.Add(rig.indices[toe], amount * (1f - upper) * (1f - lower) * toes);
        }

        private static void PlaceArmsInTPose(Rig rig)
        {
            foreach (bool left in new[] { true, false })
            {
                HumanBodyBones upper = left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
                HumanBodyBones lower = left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm;
                HumanBodyBones hand = left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
                Vector3 direction = left ? Vector3.left : Vector3.right;
                Transform tip = rig.T(hand).GetChild(0);
                Vector3 handDirection = tip.position - rig.P(hand);
                rig.T(upper).rotation = Quaternion.FromToRotation(rig.P(lower) - rig.P(upper), direction);
                rig.T(lower).rotation = Quaternion.FromToRotation(rig.P(hand) - rig.P(lower), direction);
                rig.T(hand).rotation = Quaternion.FromToRotation(handDirection, direction);
            }
        }

        private static Avatar CreateAvatar(GameObject visual, Rig rig)
        {
            HumanBone[] human = rig.indices.Select(pair => new HumanBone
            {
                boneName = rig.bones[pair.Value].name,
                humanName = HumanTrait.BoneName[(int)pair.Key],
                limit = new HumanLimit { useDefaultValues = true }
            }).ToArray();
            SkeletonBone[] skeleton = visual.GetComponentsInChildren<Transform>(true).Select(t => new SkeletonBone
            {
                name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale
            }).ToArray();
            return AvatarBuilder.BuildHumanAvatar(visual, new HumanDescription
            {
                human = human, skeleton = skeleton,
                upperArmTwist = 0.5f, lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                armStretch = 0.025f, legStretch = 0.025f,
                feetSpacing = 0f, hasTranslationDoF = false
            });
        }

        private static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 0.000001f));
            return Vector3.Distance(p, a + ab * t);
        }

        private static float Smooth(float a, float b, float value)
        {
            float t = Mathf.InverseLerp(a, b, value);
            return t * t * (3f - 2f * t);
        }

        private static bool Contains(string text, string value)
        {
            return text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string CleanFileName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return value;
        }

        private static T SaveOrUpdate<T>(T generated, string path) where T : UnityEngine.Object
        {
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null) { AssetDatabase.CreateAsset(generated, path); return generated; }
            EditorUtility.CopySerialized(generated, existing);
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(generated);
            return existing;
        }

        private static void EnsureFolder(string path)
        {
            string[] pieces = path.Split('/');
            string current = pieces[0];
            for (int i = 1; i < pieces.Length; i++)
            {
                string next = current + "/" + pieces[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, pieces[i]);
                current = next;
            }
        }
    }
}
