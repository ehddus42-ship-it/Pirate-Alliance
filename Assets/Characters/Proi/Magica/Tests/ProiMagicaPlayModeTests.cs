using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MagicaCloth2;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>
/// 전투복 Proi_Magica 프리팹을 실제 애니메이터로 대기 → 걷기(2.1 m/s, 직선) → 달리기(4.2 m/s, 직선) → 선회 → 3타 콤보 → 대쉬 →
/// 정지 → 궁극기 시키며 클로스 빌드 / NaN / 폭주 / 콜라이더 관통(구간별), 머리카락이 진행 반대로 날리는 양, 뿌리~끝 움직임,
/// 상체 자세를 재고 앞 · 옆 · 뒤 화면을 프레임으로 남깁니다. 게임과 같은 컴포넌트(ProiRunPosture / ProiUltimate /
/// ProiHairFlutter)를 붙입니다. 평시복 Proi_General_Magica 는 이동만 하므로 대기 → 걷기 → 달리기 → 선회 → 정지 → 가라앉기.
/// 결과: Documentation/Proi/Magica/report.json (평시복 report_everyday.json), Logs/ProiMagicaFrames/f_0000.png…
/// (ffmpeg -framerate 30 로 영상화)
/// 환경 변수: PROI_MAGICA_LABEL=x → report_x.json / report_everyday_x.json,
/// PROI_BASELINE=spring → 예전 방식(전투복: FBX + ProiTailSpring, 자세 보정 없음 / 평시복: 물리 없는 FBX)으로 재기.
/// </summary>
public class ProiMagicaPlayModeTests
{
    const string PrefabPath = "Assets/Characters/Proi/Magica/Proi_Magica.prefab";
    const string FbxPath = "Assets/Characters/Proi/Models/Proi_Character.fbx";
    const string ControllerPath = "Assets/Characters/Proi/Animations/Proi.controller";
    const string EverydayPrefabPath = "Assets/Characters/Proi/Magica/Proi_General_Magica.prefab";
    const string EverydayFbxPath = "Assets/Characters/Proi/Models/Proi_General.fbx";
    const string EverydayControllerPath = "Assets/Characters/Proi/Animations/Proi_General.controller";
    const int Fps = 30;
    const float RunSpeed = 4.2f, WalkSpeed = 2.1f;   // PlayerMotor.moveSpeed / walkSpeed
    static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Documentation", "Proi", "Magica"));

    struct Shape { public string name; public Vector3 a, b; public float ra, rb; }

    /// <summary>One measured phase: sums for the averages in the report.</summary>
    class Stat
    {
        public int n;
        public double trail, rise, spread, lean, waist, neck;
        public double[] bandSpeed = new double[5];
        public List<float> twist = new List<float>();
        public List<float> ahead = new List<float>();
    }

    [UnityTest, Timeout(1800000)]
    public IEnumerator ProiClothFollowsCombatMotion() => Measure(false);

    /// <summary>The everyday outfit (lobby): hair, hoodie hem and track pants while she walks, runs, turns and stops.</summary>
    [UnityTest, Timeout(1800000)]
    public IEnumerator ProiEverydayClothFollowsMovement() => Measure(true);

    IEnumerator Measure(bool everyday)
    {
        LogAssert.ignoreFailingMessages = true;   // animation events of the combat clips have no receivers here
        Unity.Burst.BurstCompiler.Options.EnableBurstCompileSynchronously = true;
        string label = Environment.GetEnvironmentVariable("PROI_MAGICA_LABEL");
        // baseline: the battle outfit with the old tail spring / the everyday outfit with no physics at all
        bool spring = Environment.GetEnvironmentVariable("PROI_BASELINE") == "spring";
        // video frames are scratch output (Logs/ is not tracked); the report goes to Documentation/Proi/Magica
        string frames = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "ProiMagicaFrames"));
        Directory.CreateDirectory(frames);
        Directory.CreateDirectory(OutDir);
        foreach (var f in Directory.GetFiles(frames, "f_*.png")) File.Delete(f);
        GameObject prefab = null; RuntimeAnimatorController controller = null;
#if UNITY_EDITOR
        prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(everyday ? (spring ? EverydayFbxPath : EverydayPrefabPath) : spring ? FbxPath : PrefabPath);
        controller = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(everyday ? EverydayControllerPath : ControllerPath);
#endif
        Assert.NotNull(prefab); Assert.NotNull(controller);
        Time.captureDeltaTime = 1f / Fps;

        var light = new GameObject("Sun").AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 1.2f;
        light.transform.rotation = Quaternion.Euler(45f, 150f, 0f);
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.localScale = Vector3.one * 10f;
        // root: moved like the player (CharacterController for the ultimate's arch) / visual: the prefab, turned to face +Z
        var root = new GameObject("ProiTest").transform;
        var cc = root.gameObject.AddComponent<CharacterController>();
        cc.height = 1.9f; cc.radius = 0.28f; cc.center = new Vector3(0f, 0.97f, 0f);
        var visual = Object.Instantiate(prefab, root);
        var animator = visual.GetComponent<Animator>();
        if (animator == null) animator = visual.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var cloths = visual.GetComponentsInChildren<MagicaCloth>();
        Assert.AreEqual(spring ? 0 : 3, cloths.Length);
        Transform Bone(string n) => visual.GetComponentsInChildren<Transform>().First(t => t.name == n);
        Transform hips = Bone("Hips"), spine = Bone("Spine"), chest = Bone("UpperChest"), neck = Bone("Neck"), head = Bone("Head");
        Transform lHip = Bone("LeftUpperLeg"), rHip = Bone("RightUpperLeg"), lFoot = Bone("LeftFoot"), rFoot = Bone("RightFoot");
        float legLength = Vector3.Distance(lHip.position, lFoot.position);
        // facing from the hips (Unity: right = toward the right leg, forward = right x up)
        visual.transform.localRotation = Quaternion.FromToRotation(Vector3.Cross(Flat(rHip.position - lHip.position), Vector3.up), Vector3.forward);
        // hair bands by distance from the twin-tail ties, or (loose everyday hair) from the crown
        Transform[] ties = everyday ? new[] { Crown(head) } : new[] { Bone("LeftTail1"), Bone("RightTail1") };

        // the game's components (they live in Assembly-CSharp, which a test assembly cannot reference)
        Component ultimate = null, flutter = null;
        if (everyday)
        {
            if (!visual.GetComponent(GameType("ProiGeneralHands"))) visual.AddComponent(GameType("ProiGeneralHands"));
        }
        else
        {
            if (spring) visual.AddComponent(GameType("ProiTailSpring"));
            else visual.AddComponent(GameType("ProiRunPosture"));
            ultimate = visual.AddComponent(GameType("ProiUltimate"));
            GameType("ProiUltimate").GetField("externalInput").SetValue(ultimate, true);
            GameType("ProiUltimate").GetField("cinematicTime").SetValue(ultimate, false);   // in the game the warp is on her own clock
            flutter = spring ? null : visual.AddComponent(GameType("ProiHairFlutter"));
        }
        bool UltimateActive() => ultimate != null && (bool)GameType("ProiUltimate").GetProperty("Active").GetValue(ultimate);
        float WindNow() => flutter == null ? 0f : (float)GameType("ProiHairFlutter").GetProperty("WindSpeed").GetValue(flutter);

        float start = Time.realtimeSinceStartup;
        while (cloths.Any(c => !c.Process.IsRunning() && !c.Process.Result.IsError()) && Time.realtimeSinceStartup - start < 120f) yield return null;
        var report = new StringBuilder("{\n");
        string version = everyday ? (spring ? "everyday, no physics (before)" : "everyday, Magica Cloth 2") : spring ? "tail spring (before)" : "Magica Cloth 2";
        report.AppendLine($"  \"label\": \"{label ?? ""}\", \"version\": \"{version}\",");
        report.AppendLine($"  \"build_seconds\": {Time.realtimeSinceStartup - start:F2},");
        report.AppendLine("  \"cloth\": [" + string.Join(", ", cloths.Select(c =>
            $"{{\"name\": \"{c.name}\", \"running\": {c.Process.IsRunning().ToString().ToLower()}, \"prebuild\": {c.GetSerializeData2().preBuildData.UsePreBuild().ToString().ToLower()}, \"result\": \"{c.Process.Result.GetResultString().Replace("\"", "'")}\"}}")) + "],");
        foreach (var c in cloths) Assert.IsTrue(c.Process.IsRunning(), c.name + ": " + c.Process.Result.GetResultString());

        var skins = spring ? visual.GetComponentsInChildren<SkinnedMeshRenderer>()
                           : cloths.SelectMany(c => c.SerializeData.sourceRenderers).OfType<SkinnedMeshRenderer>().Distinct().ToArray();
        var hairSkin = spring ? skins[0] : skins.First(s => s.name.Contains("Hair"));
        bool[] hairMask = HairMask(hairSkin, spring, everyday);
        var bake = new Mesh();
        var cams = new[] { MakeCamera("Front"), MakeCamera("Side"), MakeCamera("Back") };
        const int W = 560, H = 800;
        var rt = new RenderTexture(W, H, 24) { antiAliasing = 4 };
        var tex = new Texture2D(W * cams.Length, H, TextureFormat.RGB24, false);

        Dictionary<string, bool[]> outsideAtStart = null;
        var worst = new Dictionary<string, int>();
        var stats = new Dictionary<string, Stat>();
        int[] band = null; float[] bandDist = new float[5]; Vector3[] prevRel = null;
        float idleTrail = 0f, idleRise = 0f;
        bool nan = false; float maxReach = 0f, maxWind = 0f; int frame = 0;
        float t = 0f, turn = 0f, focusY = 1f;
        Vector3 heading = Vector3.forward;
        // straight walk and run (moving wind alone), then a turn (inertia sideways)
        var phases = everyday
            ? new (float until, string label)[] { (1.5f, "idle"), (5.5f, "walk"), (9.5f, "run"), (11.5f, "turn"), (13f, "stop"), (15f, "settle") }
            : new (float until, string label)[]
            {
                (1.5f, "idle"), (5.5f, "walk"), (9.5f, "run"), (11.5f, "turn"), (12.2f, "Attack1"), (12.9f, "Attack2"), (13.9f, "Attack3"),
                (14.5f, "Dash"), (16f, "stop"), (23.5f, "ultimate"), (25f, "settle"),
            };
        string current = "";
        float phaseStart = 0f;
        while (t < phases[phases.Length - 1].until)
        {
            string phase = phases.First(p => t < p.until).label;
            if (phase != current)
            {
                current = phase; phaseStart = t;
                if (phase.StartsWith("Attack") || phase == "Dash") animator.CrossFade(phase, 0.08f);
                else if (phase == "ultimate")
                {
                    Physics.SyncTransforms();
                    Assert.IsTrue((bool)GameType("ProiUltimate").GetMethod("TryBegin").Invoke(ultimate, null), "ultimate did not start");
                }
                else if (phase != "settle" || everyday) animator.CrossFade("Locomotion", 0.15f);
            }
            float speed = phase == "walk" ? WalkSpeed : phase == "run" || phase == "turn" ? RunSpeed : phase == "Dash" ? 7f : 0f;
            animator.SetFloat("Speed", speed > 0f && phase != "Dash" ? speed / RunSpeed : 0f, 0.08f, 1f / Fps);
            if (phase == "turn") turn += 110f / Fps;   // 110 deg/s
            heading = Quaternion.Euler(0f, turn, 0f) * Vector3.forward;
            if (speed > 0f) root.position += heading * (speed / Fps);
            root.rotation = Quaternion.LookRotation(heading);   // she faces where she runs
            yield return null;
            t += 1f / Fps; frame++;
            maxWind = Mathf.Max(maxWind, WindNow());

            if (frame % 3 == 0)
            {
                var shapes = ReadColliders(visual.transform);
                var verts = new Dictionary<string, Vector3[]>();
                foreach (var s in skins)
                {
                    s.BakeMesh(bake);
                    // the bake leaves out the renderer's own scale (everyday Proi's rig carries x100)
                    var m = Matrix4x4.TRS(s.transform.position, s.transform.rotation, Vector3.one);
                    verts[s.name] = bake.vertices.Select(v => m.MultiplyPoint3x4(v)).ToArray();
                    if (verts[s.name].Any(v => float.IsNaN(v.x))) nan = true;
                    maxReach = Mathf.Max(maxReach, verts[s.name].Max(v => Vector3.Distance(Flat(v), Flat(hips.position))));
                }
                if (outsideAtStart == null)
                {
                    outsideAtStart = new Dictionary<string, bool[]>();
                    foreach (var kv in verts)
                        foreach (var s in shapes)
                            outsideAtStart[kv.Key + "/" + s.name] = kv.Value.Select(v => Penetration(s, v) < 0f).ToArray();
                }
                foreach (var kv in verts)
                    foreach (var s in shapes)
                    {
                        // vertices that started outside a collider and are now more than 1 cm inside it, worst per phase
                        var outside = outsideAtStart[kv.Key + "/" + s.name];
                        int inside = 0;
                        for (int i = 0; i < kv.Value.Length; i++) if (outside[i] && Penetration(s, kv.Value[i]) > 0.01f) inside++;
                        string key = (phase.StartsWith("Attack") ? "attack" : phase) + ": " + kv.Key + "/" + s.name;
                        worst[key] = Mathf.Max(worst.TryGetValue(key, out int w) ? w : 0, inside);
                    }

                // ---- hair: tail bands by distance from the tie (in the first, idle frame), positions relative to the head
                Vector3[] hv = verts[hairSkin.name];
                if (band == null)
                {
                    var d = hv.Select(v => ties.Min(tb => Vector3.Distance(v, tb.position))).ToArray();
                    float maxD = Enumerable.Range(0, d.Length).Where(i => hairMask[i]).Max(i => d[i]);
                    band = d.Select((x, i) => !hairMask[i] || x < 0.06f || Vector3.Distance(hv[i], head.position) < 0.16f ? -1 : Mathf.Min(4, (int)(x / maxD * 5f))).ToArray();
                    for (int b = 0; b < 5; b++) bandDist[b] = Enumerable.Range(0, d.Length).Where(i => band[i] == b).Select(i => d[i]).DefaultIfEmpty(1f).Average();
                }
                var rel = hv.Select(v => v - head.position).ToArray();
                Vector3 fwd = Flat(visual.transform.forward).normalized;
                Vector3 tipMean = Vector3.zero; int tipN = 0; float spread = 0f;
                for (int i = 0; i < hv.Length; i++) if (band[i] == 4) { tipMean += rel[i]; spread += Flat(rel[i]).magnitude; tipN++; }
                tipMean /= Mathf.Max(tipN, 1);
                spread /= Mathf.Max(tipN, 1);   // how far out from the head's vertical axis the tips hang: the hair's volume
                float trail = -Vector3.Dot(Flat(tipMean), fwd);   // how far behind the head the tips are
                float rise = tipMean.y;
                if (phase == "idle") { idleTrail = trail; idleRise = rise; }
                // posture in the body's side plane: lean of hips -> head, bend between pelvis and chest, neck lean
                Vector3 side = Flat(rHip.position - lHip.position).normalized;
                Vector3 P(Vector3 v) => Vector3.ProjectOnPlane(v, side);
                float lean = Vector3.Angle(Vector3.up, P(head.position - hips.position));
                float waist = Vector3.Angle(P(spine.position - hips.position), P(chest.position - spine.position));
                float neckLean = Vector3.Angle(Vector3.up, P(head.position - neck.position));
                float twist = Vector3.SignedAngle(side, Flat(Bone("RightUpperArm").position - Bone("LeftUpperArm").position), Vector3.up);
                string key2 = phase.StartsWith("Attack") ? "attack" : phase;
                if (!stats.TryGetValue(key2, out var st)) stats[key2] = st = new Stat();
                // skip the first half second of a phase: the blend / acceleration
                if (t - phaseStart > 0.5f)
                {
                    st.n++; st.trail += trail - idleTrail; st.rise += rise - idleRise; st.spread += spread; st.lean += lean; st.waist += waist; st.neck += neckLean; st.twist.Add(twist);
                    st.ahead.Add(Vector3.Dot(lFoot.position - rFoot.position, fwd) / legLength);
                    if (prevRel != null)
                    {
                        var sum = new double[5]; var cnt = new int[5];
                        for (int i = 0; i < hv.Length; i++)
                            if (band[i] >= 0) { sum[band[i]] += (rel[i] - prevRel[i]).sqrMagnitude; cnt[band[i]]++; }
                        for (int b = 0; b < 5; b++) st.bandSpeed[b] += Math.Sqrt(sum[b] / Math.Max(cnt[b], 1)) * Fps / 3.0;
                    }
                }
                prevRel = rel;
            }
            // cameras follow her height (the ultimate's leap) and stay square to where she faces
            Vector3 face = Flat(visual.transform.forward).normalized;
            focusY = Mathf.Lerp(focusY, hips.position.y + 0.05f, 0.6f);
            Capture(cams, new Vector3(hips.position.x, focusY, hips.position.z), face, phase == "ultimate" || phase == "settle" ? 6.2f : 4.4f, rt, tex,
                    Path.Combine(frames, $"f_{frame:0000}.png"));
            if (phase == "settle" && !UltimateActive() && t > 24.5f) break;
        }
        Time.captureDeltaTime = 0f;
        report.AppendLine($"  \"frames\": {frame},");
        report.AppendLine($"  \"nan\": {nan.ToString().ToLower()},");
        report.AppendLine($"  \"max_horizontal_reach_m\": {maxReach:F3},");
        report.AppendLine($"  \"ultimate_max_wind_m_s\": {maxWind:F1},");
        report.AppendLine("  \"hair_band_mean_distance_from_tie_m\": [" + string.Join(", ", bandDist.Select(x => x.ToString("F2"))) + "],");
        report.AppendLine("  \"phases\": {");
        report.AppendLine(string.Join(",\n", stats.Where(kv => kv.Value.n > 1).Select(kv =>
        {
            var s = kv.Value; int n = s.n;
            float mean = s.twist.Average(), amp = (float)Math.Sqrt(s.twist.Select(x => (x - mean) * (x - mean)).Average());
            // + : the shoulder opposite the leading foot comes forward (natural gait), - : the same side (the Run clip)
            float am = s.ahead.Average(), sa = (float)Math.Sqrt(s.ahead.Select(x => (x - am) * (x - am)).Average());
            float corr = amp > 0.5f && sa > 0.05f ? -s.twist.Zip(s.ahead, (x, y) => (x - mean) * (y - am)).Average() / (amp * sa) : 0f;
            // hair speed relative to the head per band, divided by the band's distance from the tie
            var ang = s.bandSpeed.Select((x, b) => x / Math.Max(n - 1, 1) / bandDist[b]).ToArray();
            return $"    \"{kv.Key}\": {{\"hair_tips_behind_head_m\": {s.trail / n:F3}, \"hair_tips_rise_m\": {s.rise / n:F3}, " +
                   $"\"hair_tips_spread_m\": {s.spread / n:F3}, \"hair_speed_per_m_by_band\": [{string.Join(", ", ang.Select(x => x.ToString("F2")))}], " +
                   $"\"torso_lean_deg\": {s.lean / n:F1}, \"waist_bend_deg\": {s.waist / n:F1}, \"neck_lean_deg\": {s.neck / n:F1}, \"shoulder_twist_rms_deg\": {amp.ToString("F1")}, \"counter_rotation\": {corr.ToString("F2")}}}";
        })));
        report.AppendLine("  },");
        report.AppendLine("  \"collider_penetration_worst_vertices\": {");
        report.AppendLine(string.Join(",\n", worst.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Select(kv => $"    \"{kv.Key}\": {kv.Value}")));
        report.AppendLine("  },");
        report.AppendLine($"  \"checked_vertices\": {{{string.Join(", ", skins.Select(s => $"\"{s.name}\": {s.sharedMesh.vertexCount}"))}}}");
        report.AppendLine("}");
        string stem = everyday ? "report_everyday" : "report";
        string name = string.IsNullOrEmpty(label) ? stem + ".json" : $"{stem}_{label}.json";
        File.WriteAllText(Path.Combine(OutDir, name), report.ToString(), new UTF8Encoding(false));
        Assert.IsFalse(nan, "NaN in Proi cloth");
        Assert.Less(maxReach, 1.6f, "cloth blew up");
    }

    /// <summary>
    /// Hair vertices: the whole hair renderer, or on the unsplit model the vertices skinned to the tail bones (battle) /
    /// of the hair material (everyday).
    /// </summary>
    static bool[] HairMask(SkinnedMeshRenderer skin, bool singleMesh, bool everyday)
    {
        var mesh = skin.sharedMesh;
        if (!singleMesh) return Enumerable.Repeat(true, mesh.vertexCount).ToArray();
        if (everyday)
        {
            var hair = new bool[mesh.vertexCount];
            for (int sm = 0; sm < mesh.subMeshCount && sm < skin.sharedMaterials.Length; sm++)
                if (skin.sharedMaterials[sm] && skin.sharedMaterials[sm].name.Contains("Hair"))
                    foreach (int i in mesh.GetTriangles(sm)) hair[i] = true;
            return hair;
        }
        var perVertex = mesh.GetBonesPerVertex(); var weights = mesh.GetAllBoneWeights();
        var mask = new bool[mesh.vertexCount];
        for (int i = 0, k = 0; i < mask.Length; i++)
        {
            float tail = 0f;
            for (int j = 0; j < perVertex[i]; j++, k++) if (skin.bones[weights[k].boneIndex].name.Contains("Tail")) tail += weights[k].weight;
            mask[i] = tail > 0.5f;
        }
        return mask;
    }

    /// <summary>A point on top of the skull that turns with the head: loose hair hangs from around it.</summary>
    static Transform Crown(Transform head)
    {
        var crown = new GameObject("Crown").transform;
        crown.SetParent(head, false);
        crown.position = head.position + Vector3.up * 0.24f;
        return crown;
    }

    static Type GameType(string name)
    {
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(x => x != null);
        Assert.NotNull(type, name + " not found");
        return type;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    static Camera MakeCamera(string name)
    {
        var cam = new GameObject(name).AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.16f, 0.17f, 0.21f);
        cam.fieldOfView = 32f;
        cam.enabled = false;
        return cam;
    }

    /// <summary>Front-left three-quarter, right side and back views side by side.</summary>
    static void Capture(Camera[] cams, Vector3 focus, Vector3 fwd, float distance, RenderTexture rt, Texture2D tex, string path)
    {
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        var dirs = new[] { Quaternion.Euler(0, -35, 0) * fwd, right, -fwd };
        for (int i = 0; i < cams.Length; i++)
        {
            var cam = cams[i];
            cam.transform.position = focus + dirs[i] * distance + Vector3.up * 0.35f;
            cam.transform.LookAt(focus);
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), i * rt.width, 0); RenderTexture.active = null;
            cam.targetTexture = null;
        }
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
    }

    static List<Shape> ReadColliders(Transform root)
    {
        var list = new List<Shape>();
        foreach (var s in root.GetComponentsInChildren<MagicaSphereCollider>())
        {
            Vector3 c = s.transform.TransformPoint(s.center);
            float r = s.GetSize().x * s.transform.lossyScale.x;
            list.Add(new Shape { name = s.name, a = c, b = c, ra = r, rb = r });
        }
        foreach (var c in root.GetComponentsInChildren<MagicaCapsuleCollider>())
        {
            Vector3 size = c.GetSize() * c.transform.lossyScale.x; float half = size.z * 0.5f;
            Vector3 up = c.transform.up, center = c.transform.TransformPoint(c.center);
            list.Add(new Shape { name = c.name, a = center + up * (half - size.x), b = center - up * (half - size.y), ra = size.x, rb = size.y });
        }
        return list;
    }

    static float Penetration(Shape s, Vector3 v)
    {
        Vector3 ab = s.b - s.a; float t = ab.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector3.Dot(v - s.a, ab) / ab.sqrMagnitude) : 0f;
        return Mathf.Lerp(s.ra, s.rb, t) - Vector3.Distance(v, s.a + ab * t);
    }
}
