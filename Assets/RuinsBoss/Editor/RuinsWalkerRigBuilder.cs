using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AcRoguelike.RuinsBoss.Editor
{
    /// <summary>Original mechanical skin over the unchanged Meshy model; not a Meshy-generated humanoid skeleton.</summary>
    public static class RuinsWalkerRigBuilder
    {
        const string Folder = "Assets/RuinsBoss/Art/Generated";

        public static RuinsWalkerLegRig Attach(Transform model)
        {
            if (!model) throw new ArgumentNullException(nameof(model));
            var existing = model.GetComponent<RuinsWalkerLegRig>();
            if (existing) return existing;
            var filters = model.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length == 0) throw new InvalidOperationException("Walker source has no mesh.");
            var points = new List<Vector3>();
            foreach (var filter in filters)
            {
                Matrix4x4 matrix = model.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                foreach (var vertex in filter.sharedMesh.vertices) points.Add(matrix.MultiplyPoint3x4(vertex));
            }
            Bounds bounds = new Bounds(points[0], Vector3.zero);
            foreach (var point in points) bounds.Encapsulate(point);
            float h = bounds.size.y, low = bounds.min.y;
            var rig = model.gameObject.AddComponent<RuinsWalkerLegRig>();
            var skeleton = Child(model, "MechanicalSkeleton", Vector3.zero);
            rig.bodyBone = Child(skeleton, "RigidBody", Vector3.zero);
            var bones = new Transform[13]; bones[0] = rig.bodyBone;
            for (int i = 0; i < 4; i++)
            {
                float side = i < 2 ? -1 : 1, front = i % 2 == 0 ? -1 : 1;
                Vector3 ankle = JointCenter(points, bounds, side, front, low + h * .105f);
                Vector3 knee = JointCenter(points, bounds, side, front, low + h * .34f);
                Vector3 hip = JointCenter(points, bounds, side, front, low + h * .52f);
                // Keep hydraulic knees aligned with their ankle column, avoiding the central body plate.
                hip.x = knee.x = ankle.x;
                rig.hipRest[i] = hip; rig.kneeRest[i] = knee; rig.ankleRest[i] = ankle;
                rig.hips[i] = Child(rig.bodyBone, "Leg " + i + " Hip", hip);
                rig.knees[i] = Child(rig.hips[i], "Leg " + i + " Knee", knee - hip);
                rig.ankles[i] = Child(rig.knees[i], "Leg " + i + " Ankle", ankle - knee);
                bones[1 + i * 3] = rig.hips[i]; bones[2 + i * 3] = rig.knees[i]; bones[3 + i * 3] = rig.ankles[i];
            }
            Directory.CreateDirectory(Folder);
            for (int index = 0; index < filters.Length; index++)
            {
                var filter = filters[index]; var sourceRenderer = filter.GetComponent<MeshRenderer>();
                if (!sourceRenderer) continue;
                var materials = sourceRenderer.sharedMaterials;
                Matrix4x4 matrix = model.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                var mesh = Object.Instantiate(filter.sharedMesh); mesh.name = "SiegeWalker mechanical skin " + index;
                Vector3[] vertices = mesh.vertices, normals = mesh.normals;
                Vector4[] tangents = mesh.tangents;
                var weights = new BoneWeight[vertices.Length];
                for (int v = 0; v < vertices.Length; v++)
                {
                    vertices[v] = matrix.MultiplyPoint3x4(vertices[v]);
                    if (normals.Length == vertices.Length) normals[v] = matrix.inverse.transpose.MultiplyVector(normals[v]).normalized;
                    if (tangents.Length == vertices.Length)
                    {
                        Vector3 tangent = matrix.MultiplyVector(new Vector3(tangents[v].x, tangents[v].y, tangents[v].z)).normalized;
                        tangents[v] = new Vector4(tangent.x, tangent.y, tangent.z, tangents[v].w);
                    }
                    weights[v] = Weight(vertices[v], rig, bounds);
                }
                mesh.vertices = vertices;
                if (normals.Length == vertices.Length) mesh.normals = normals;
                if (tangents.Length == vertices.Length) mesh.tangents = tangents;
                mesh.boneWeights = weights;
                var bindposes = new Matrix4x4[bones.Length];
                for (int b = 0; b < bones.Length; b++) bindposes[b] = bones[b].worldToLocalMatrix * model.localToWorldMatrix;
                mesh.bindposes = bindposes; mesh.RecalculateBounds();
                string path = Folder + "/SiegeWalker_Mechanical_" + index + ".asset";
                var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (saved) { EditorUtility.CopySerialized(mesh, saved); Object.DestroyImmediate(mesh); }
                else { saved = mesh; AssetDatabase.CreateAsset(saved, path); }
                EditorUtility.SetDirty(saved);
                var skinObject = Child(model, "Mechanical Skin " + index, Vector3.zero);
                var skin = skinObject.gameObject.AddComponent<SkinnedMeshRenderer>();
                skin.sharedMesh = saved; skin.sharedMaterials = materials; skin.bones = bones; skin.rootBone = rig.bodyBone;
                skin.updateWhenOffscreen = true;
                var movingBounds = bounds; movingBounds.Expand(new Vector3(1.2f, 1, 2.5f)); skin.localBounds = movingBounds;
                // Health.Configure enables every renderer, so remove the rigid duplicate rather than hiding it.
                var prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(filter.gameObject);
                if (prefabRoot) PrefabUtility.UnpackPrefabInstance(prefabRoot, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                Object.DestroyImmediate(sourceRenderer); Object.DestroyImmediate(filter);
            }
            rig.RestoreRestPose();
            return rig;
        }

        static Vector3 JointCenter(List<Vector3> points, Bounds bounds, float side, float front, float y)
        {
            Vector3 sum = Vector3.zero; int count = 0;
            foreach (var p in points)
            {
                if ((p.x - bounds.center.x) * side < bounds.size.x * .19f || (p.z - bounds.center.z) * front <= 0 ||
                    Mathf.Abs(p.y - y) > bounds.size.y * .035f) continue;
                sum += p; count++;
            }
            Vector3 result = count > 0 ? sum / count : bounds.center + new Vector3(side * bounds.size.x * .32f, 0, front * bounds.size.z * .25f);
            result.y = y; return result;
        }

        static BoneWeight Weight(Vector3 point, RuinsWalkerLegRig rig, Bounds bounds)
        {
            float hipY = bounds.min.y + bounds.size.y * .52f;
            if (point.y >= hipY + bounds.size.y * .02f || Mathf.Abs(point.x - bounds.center.x) < bounds.size.x * .13f)
                return Single(0);
            int leg = -1; float nearest = float.PositiveInfinity;
            for (int i = 0; i < 4; i++)
            {
                if ((point.x - bounds.center.x) * (rig.ankleRest[i].x - bounds.center.x) < 0) continue;
                Vector3 a = point.y > rig.kneeRest[i].y ? rig.kneeRest[i] : rig.ankleRest[i];
                Vector3 b = point.y > rig.kneeRest[i].y ? rig.hipRest[i] : rig.kneeRest[i];
                Vector3 center = Vector3.Lerp(a, b, Mathf.InverseLerp(a.y, b.y, point.y));
                float distance = (new Vector2(point.x - center.x, point.z - center.z)).sqrMagnitude;
                if (distance < nearest) { nearest = distance; leg = i; }
            }
            if (leg < 0) return Single(0);
            float seam = bounds.size.y * .018f;
            int hip = 1 + leg * 3, knee = hip + 1, ankle = hip + 2;
            if (point.y > rig.hipRest[leg].y - seam)
                return Blend(hip, 0, Mathf.InverseLerp(rig.hipRest[leg].y - seam, rig.hipRest[leg].y + seam, point.y));
            if (point.y > rig.kneeRest[leg].y - seam)
                return Blend(knee, hip, Mathf.InverseLerp(rig.kneeRest[leg].y - seam, rig.kneeRest[leg].y + seam, point.y));
            if (point.y > rig.ankleRest[leg].y - seam)
                return Blend(ankle, knee, Mathf.InverseLerp(rig.ankleRest[leg].y - seam, rig.ankleRest[leg].y + seam, point.y));
            return Single(ankle);
        }
        static BoneWeight Single(int bone) => new BoneWeight { boneIndex0 = bone, weight0 = 1 };
        static BoneWeight Blend(int a, int b, float blend) => new BoneWeight { boneIndex0 = a, weight0 = 1 - blend, boneIndex1 = b, weight1 = blend };
        static Transform Child(Transform parent, string name, Vector3 position)
        { var t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = position; return t; }
    }
}
