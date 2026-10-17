#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MoonLuna.Editor
{
    /// <summary>Samples the shipped mesh and clips in a private preview scene, without touching the user's scene.</summary>
    public static class LunaPeacefulPoseValidation
    {
        const string Output = "Documentation/MoonLuna/PeacefulPrayerQA/Unity";
        [Serializable] sealed class Pose
        {
            public string clip;
            public float time, handSeparation;
            public Vector3 leftHand, rightHand, leftBoot, rightBoot;
            public int vertices;
            public bool finite;
        }
        [Serializable] sealed class Report
        {
            public string status = "running", error;
            public string[] skinnedMeshes;
            public int leftArmWeightedVertices, rightArmWeightedVertices, leftLegWeightedVertices, rightLegWeightedVertices;
            public List<Pose> poses = new List<Pose>();
            public List<string> captures = new List<string>();
        }

        [MenuItem("Tools/Moon Luna/Stage Capture/Validate Peaceful Poses")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run the pose preview in Edit mode.");
            Directory.CreateDirectory(Output);
            var report = new Report();
            var preview = new PreviewRenderUtility();
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LunaStageSetup.PeacePrefab);
                if (!prefab) throw new InvalidOperationException("Peaceful prefab is missing.");
                var body = preview.InstantiatePrefabInScene(prefab);
                foreach (var animator in body.GetComponentsInChildren<Animator>()) animator.enabled = false;
                var bones = body.GetComponentsInChildren<Transform>().ToDictionary(t => t.name, t => t);
                foreach (string name in new[] { "LunaLeftPrayerArm", "LunaRightPrayerArm", "LunaLeftPrayerForeArm", "LunaRightPrayerForeArm", "LunaLeftPrayerHand", "LunaRightPrayerHand", "LunaLeftBoot", "LunaRightBoot", "LunaLeftThigh", "LunaRightThigh", "LunaLeftShin", "LunaRightShin" })
                    if (!bones.ContainsKey(name)) throw new InvalidOperationException("Missing limb bone: " + name);
                var skins = body.GetComponentsInChildren<SkinnedMeshRenderer>();
                report.skinnedMeshes = skins.Select(s => s.name).ToArray();
                report.leftArmWeightedVertices = WeightedVertices(skins, "LunaLeftPrayerArm", "LunaLeftPrayerForeArm", "LunaLeftPrayerHand");
                report.rightArmWeightedVertices = WeightedVertices(skins, "LunaRightPrayerArm", "LunaRightPrayerForeArm", "LunaRightPrayerHand");
                report.leftLegWeightedVertices = WeightedVertices(skins, "LunaLeftThigh", "LunaLeftShin");
                report.rightLegWeightedVertices = WeightedVertices(skins, "LunaRightThigh", "LunaRightShin");
                if (Mathf.Min(report.leftArmWeightedVertices, report.rightArmWeightedVertices,
                    report.leftLegWeightedVertices, report.rightLegWeightedVertices) == 0)
                    throw new InvalidOperationException("An arm or added leg has no skinned geometry.");
                preview.camera.orthographic = true;
                preview.camera.nearClipPlane = .01f;
                preview.camera.farClipPlane = 30f;
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = new Color(.07f, .09f, .14f);
                preview.ambientColor = new Color(.65f, .65f, .68f);
                preview.lights[0].intensity = 1.2f;
                preview.lights[0].transform.rotation = Quaternion.Euler(35f, -35f, 0f);
                preview.lights[1].intensity = .65f;
                preview.lights[1].transform.rotation = Quaternion.Euler(20f, 135f, 0f);
                foreach (string name in new[] { "Idle", "Walk", "Dash" })
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(LunaStageSetup.Root + "/Stage/LunaPeaceful_" + name + ".anim");
                    if (!clip) throw new InvalidOperationException("Missing peaceful clip: " + name);
                    if (AnimationUtility.GetAnimationClipSettings(clip).loopTime != (name != "Dash"))
                        throw new InvalidOperationException("Incorrect loop setting: " + name);
                    float[] times = name == "Idle" ? new[] { 0f } : name == "Walk" ? new[] { 0f, clip.length * .25f, clip.length * .5f, clip.length * .75f } : new[] { 0f, .10f, .19f, Mathf.Max(0f, clip.length - .01f) };
                    for (int i = 0; i < times.Length; i++)
                    {
                        float time = Mathf.Clamp(times[i], 0f, clip.length);
                        clip.SampleAnimation(body, time);
                        var sample = new Pose { clip = name, time = time,
                            leftHand = bones["LunaLeftPrayerHand"].position, rightHand = bones["LunaRightPrayerHand"].position,
                            leftBoot = bones["LunaLeftBoot"].position, rightBoot = bones["LunaRightBoot"].position, finite = true };
                        sample.handSeparation = Vector3.Distance(sample.leftHand, sample.rightHand);
                        Bounds bounds = new Bounds(body.transform.position + Vector3.up * .8f, Vector3.zero);
                        bool first = true;
                        foreach (var skin in skins)
                        {
                            var mesh = new Mesh();
                            try
                            {
                                // Account for FBX renderer scale before converting local vertices to world space.
                                skin.BakeMesh(mesh, true);
                                foreach (Vector3 local in mesh.vertices)
                                {
                                    Vector3 point = skin.transform.TransformPoint(local);
                                    sample.finite &= Finite(point);
                                    if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                                    else bounds.Encapsulate(point);
                                }
                                sample.vertices += mesh.vertexCount;
                            }
                            finally { Object.DestroyImmediate(mesh); }
                        }
                        sample.finite &= Finite(sample.leftHand) && Finite(sample.rightHand) && Finite(sample.leftBoot) && Finite(sample.rightBoot);
                        report.poses.Add(sample);
                        if (!sample.finite || sample.vertices == 0) throw new InvalidOperationException("Invalid deformed mesh: " + name);
                        if (sample.handSeparation > .22f) throw new InvalidOperationException("The peaceful hands are not held together: " + name);
                        Capture(preview, bounds, name + "_" + i, new Vector3(-1.8f, .2f, 3f), report);
                        if (i == 0)
                        {
                            Capture(preview, bounds, name + "_front", new Vector3(0f, .08f, 3f), report);
                            Capture(preview, bounds, name + "_side", new Vector3(-3f, .08f, 0f), report);
                        }
                    }
                }
                report.status = "passed";
            }
            catch (Exception exception) { report.status = "failed"; report.error = exception.ToString(); throw; }
            finally
            {
                preview.Cleanup();
                File.WriteAllText(Output + "/pose-validation.json", JsonUtility.ToJson(report, true));
            }
        }

        static void Capture(PreviewRenderUtility preview, Bounds bounds, string name, Vector3 direction, Report report)
        {
            preview.camera.orthographicSize = Mathf.Max(.85f, bounds.size.y * .57f);
            preview.camera.transform.position = bounds.center + direction.normalized * 5f;
            preview.camera.transform.LookAt(bounds.center);
            string path = Output + "/" + name + ".png";
            Texture2D texture = null;
            try
            {
                preview.BeginStaticPreview(new Rect(0, 0, 720, 900));
                try { preview.Render(true); }
                finally { texture = preview.EndStaticPreview(); }
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally { if (texture) Object.DestroyImmediate(texture); }
            report.captures.Add(path);
        }

        static bool Finite(Vector3 p) => !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsNaN(p.z)
            && !float.IsInfinity(p.x) && !float.IsInfinity(p.y) && !float.IsInfinity(p.z);

        static int WeightedVertices(IEnumerable<SkinnedMeshRenderer> skins, params string[] names)
        {
            int count = 0;
            foreach (var skin in skins)
            {
                var indices = new HashSet<int>(skin.bones.Select((bone, index) => new { bone, index })
                    .Where(b => b.bone && names.Contains(b.bone.name)).Select(b => b.index));
                foreach (var weight in skin.sharedMesh.boneWeights)
                    if ((weight.weight0 > .01f && indices.Contains(weight.boneIndex0))
                        || (weight.weight1 > .01f && indices.Contains(weight.boneIndex1))
                        || (weight.weight2 > .01f && indices.Contains(weight.boneIndex2))
                        || (weight.weight3 > .01f && indices.Contains(weight.boneIndex3))) count++;
            }
            return count;
        }
    }
}
#endif
