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
            /// <summary>Mean leg radius after fitting inside the skirt (for the Magica colliders).</summary>
            public LegFit fitLeft, fitRight;
            /// <summary>Widest hip section (world centre, half width, smaller of front/back depth).</summary>
            public Vector3 hipsCenter;
            public float hipsHalfWidth, hipsDepth;
            public string lowerBodyReport;

            public LunaLeg Leg(string side) => side == "L" ? left : right;
            public Vector3 Toe(string side) => side == "L" ? toeLeft : toeRight;
            public LegFit Fit(string side) => side == "L" ? fitLeft : fitRight;
        }

        public sealed class LegFit
        {
            public float thighTop, aboveKnee, knee, kneeMax, calf, ankle;
        }

        // Slender anime proportions that fit the authored dress. s = 0 at the hip joint, 1 at the ankle (negative = up into
        // the pelvis). Radii in metres: front, back, outer, inner.
        static readonly float[,] LegProfile =
        {
            { -0.14f, 0.050f, 0.056f, 0.052f, 0.040f },
            { -0.06f, 0.054f, 0.060f, 0.056f, 0.046f },
            {  0.00f, 0.054f, 0.058f, 0.055f, 0.048f },
            {  0.15f, 0.050f, 0.053f, 0.051f, 0.047f },
            {  0.30f, 0.045f, 0.047f, 0.045f, 0.042f },
            {  0.42f, 0.040f, 0.040f, 0.039f, 0.037f },
            {  0.49f, 0.042f, 0.034f, 0.036f, 0.035f },   // knee: the kneecap in front
            {  0.56f, 0.034f, 0.038f, 0.035f, 0.034f },
            {  0.65f, 0.032f, 0.045f, 0.038f, 0.039f },   // calf
            {  0.76f, 0.029f, 0.039f, 0.033f, 0.033f },
            {  0.87f, 0.027f, 0.030f, 0.028f, 0.028f },
            {  0.97f, 0.025f, 0.027f, 0.026f, 0.026f },   // ends inside the boot cuff, above the ankle pivot
        };
        // Height (m), half width, front, back. Centred front-to-back inside the underskirt.
        static readonly float[,] PelvisProfile =
        {
            { 1.06f, 0.080f, 0.070f, 0.040f },
            { 1.01f, 0.090f, 0.075f, 0.045f },
            { 0.96f, 0.105f, 0.075f, 0.058f },
            { 0.91f, 0.115f, 0.072f, 0.070f },
            { 0.86f, 0.118f, 0.068f, 0.076f },
            { 0.81f, 0.112f, 0.062f, 0.072f },
            { 0.77f, 0.095f, 0.052f, 0.058f },
            { 0.74f, 0.065f, 0.038f, 0.040f },
        };
        const int RingSides = 20;
        const float GarmentMargin = 0.008f;   // keep this far inside the skirt at rest

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
            Material stocking = renderers["Boot_Shaft_Underlap_L"].sharedMaterial;
            rig.left = BuildLeg(visual, renderers, "L", out rig.toeLeft);
            rig.right = BuildLeg(visual, renderers, "R", out rig.toeRight);
            BuildLowerBody(rig, renderers, stocking);

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
            var hip = new Vector3(s.center.x * 0.6f, HipHeight, s.center.z);
            var knee = Vector3.Lerp(hip, ankle, 0.5f) + Vector3.forward * 0.012f;
            Transform hipBone = Bone("Hip_" + side, visual, hip);
            Transform kneeBone = Bone("Knee_" + side, hipBone, knee);
            Transform ankleBone = Bone("Ankle_" + side, kneeBone, ankle);
            // the shaft lining follows the shin; the boot bends at the ankle
            shaft.transform.SetParent(kneeBone, true);
            Vector3[] world = boot.GetComponent<MeshFilter>().sharedMesh.vertices.Select(v => boot.transform.TransformPoint(v)).ToArray();
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

        /// <summary>
        /// The model has no body under the dress, so the skirt had nothing to fall against. This builds a skinned lower
        /// body: a pelvis and two legs with anatomical cross-sections (thigh, kneecap, calf, ankle) in the boot lining's
        /// stocking material. Every section is shrunk where needed to stay just inside the authored underskirt and waist,
        /// so nothing pokes through at rest. The pelvis follows Visual, the thighs blend from the pelvis into the hip bones
        /// and the shins into the knee bones; the legs end inside the boot cuffs.
        /// </summary>
        static void BuildLowerBody(Rig rig, Dictionary<string, Renderer> renderers, Material stocking)
        {
            var garments = new Clearance(new[] { "Skirt_Ivory", "Skirt_Navy", "Waist_Navy_Inset", "Bodice_Navy" }
                .Where(renderers.ContainsKey).Select(n => renderers[n]));
            var bones = new[] { rig.visual, rig.left.hip, rig.left.knee, rig.right.hip, rig.right.knee };
            var vertices = new List<Vector3>();
            var weights = new List<BoneWeight>();
            var triangles = new List<int>();
            var report = new System.Text.StringBuilder();

            // pelvis
            var pelvisRings = new List<Ring>();
            float widest = 0f;
            for (int i = 0; i < PelvisProfile.GetLength(0); i++)
            {
                float y = PelvisProfile[i, 0];
                float centreZ = garments.DepthCentre(y, 0.06f, -0.0175f);
                var ring = new Ring { centre = new Vector3(0f, y, centreZ), u = Vector3.right, v = Vector3.forward, weight = Single(0) };
                for (int k = 0; k < RingSides; k++)
                {
                    float a = 2f * Mathf.PI * k / RingSides;
                    float r = Ellipse(a, PelvisProfile[i, 1], PelvisProfile[i, 1], PelvisProfile[i, 2], PelvisProfile[i, 3]);
                    ring.radius[k] = Fit(garments, ring, k, r, out bool clamped);
                    ring.clamped |= clamped;
                }
                pelvisRings.Add(ring);
            }
            Smooth(pelvisRings, garments);
            foreach (var ring in pelvisRings)
            {
                float halfWidth = (ring.radius[0] + ring.radius[RingSides / 2]) * 0.5f;
                float depth = Mathf.Min(ring.radius[RingSides / 4], ring.radius[3 * RingSides / 4]);
                if (ring.centre.y < 0.95f && halfWidth > widest)
                {
                    widest = halfWidth;
                    rig.hipsCenter = ring.centre;
                    rig.hipsHalfWidth = halfWidth;
                    rig.hipsDepth = depth;
                }
            }
            AddTube(pelvisRings, vertices, weights, triangles);
            report.Append($"pelvis {pelvisRings.Count} rings, hips {rig.hipsHalfWidth * 2f:F3} m wide, {pelvisRings.Count(r => r.clamped)} fitted to the dress");

            // legs
            foreach (string side in new[] { "L", "R" })
            {
                LunaLeg leg = rig.Leg(side);
                int hipIndex = side == "L" ? 1 : 3, kneeIndex = hipIndex + 1;
                Vector3 hip = leg.hip.position, knee = leg.knee.position, ankle = leg.ankle.position;
                float thigh = Vector3.Distance(hip, knee), shin = Vector3.Distance(knee, ankle), length = thigh + shin;
                Vector3 outward = side == "L" ? Vector3.left : Vector3.right;
                var rings = new List<Ring>();
                float first = LegProfile[0, 0], last = LegProfile[LegProfile.GetLength(0) - 1, 0];
                int count = Mathf.CeilToInt((last - first) * length / 0.018f);
                for (int i = 0; i <= count; i++)
                {
                    float s = Mathf.Lerp(first, last, (float)i / count);
                    float along = s * length;
                    Vector3 centre, tangent;
                    if (along < thigh) { tangent = (knee - hip).normalized; centre = hip + tangent * along; }
                    else { tangent = (ankle - knee).normalized; centre = knee + tangent * (along - thigh); }
                    Vector3 v = Vector3.ProjectOnPlane(Vector3.forward, tangent).normalized;
                    Vector3 u = Vector3.Cross(v, tangent).normalized;
                    if (Vector3.Dot(u, outward) < 0f) u = -u;
                    var ring = new Ring { centre = centre, u = u, v = v, s = s };
                    float wTop = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.12f, 0.03f, s));
                    float wKnee = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.43f, 0.55f, s));
                    ring.weight = wKnee > 0f ? Pair(kneeIndex, wKnee, hipIndex) : Pair(hipIndex, wTop, 0);
                    Profile(s, out float front, out float back, out float outer, out float inner);
                    for (int k = 0; k < RingSides; k++)
                    {
                        float a = 2f * Mathf.PI * k / RingSides;
                        ring.radius[k] = Fit(garments, ring, k, Ellipse(a, outer, inner, front, back), out bool clamped);
                        ring.clamped |= clamped;
                    }
                    rings.Add(ring);
                }
                Smooth(rings, garments);
                AddTube(rings, vertices, weights, triangles);
                var fit = new LegFit
                {
                    thighTop = MeanRadius(rings, 0.03f), aboveKnee = MeanRadius(rings, 0.42f), knee = MeanRadius(rings, 0.5f),
                    kneeMax = rings.OrderBy(r => Mathf.Abs(r.s - 0.49f)).First().radius.Max(),
                    calf = MeanRadius(rings, 0.65f), ankle = MeanRadius(rings, 0.93f),
                };
                if (side == "L") rig.fitLeft = fit; else rig.fitRight = fit;
                report.Append($"; leg {side} {rings.Count} rings ({rings.Count(r => r.clamped)} fitted), thigh {fit.thighTop * 2f:F3} / knee {fit.knee * 2f:F3} / calf {fit.calf * 2f:F3} / ankle {fit.ankle * 2f:F3} m");
            }

            Transform visual = rig.visual;
            var mesh = new Mesh { name = "LunaLowerBody_Skinned" };
            mesh.SetVertices(vertices.Select(p => visual.InverseTransformPoint(p)).ToList());
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, Enumerable.Repeat(Vector2.zero, vertices.Count).ToList());
            mesh.boneWeights = weights.ToArray();
            mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * visual.localToWorldMatrix).ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            mesh = SaveMesh(mesh);

            var go = new GameObject("LowerBody");
            go.transform.SetParent(visual, false);
            var skinned = go.AddComponent<SkinnedMeshRenderer>();
            skinned.sharedMesh = mesh;
            skinned.sharedMaterial = stocking;
            skinned.bones = bones;
            skinned.rootBone = visual;
            skinned.localBounds = new Bounds(mesh.bounds.center, mesh.bounds.size + Vector3.one * 0.6f);
            rig.lowerBodyReport = report.ToString() + $"; {vertices.Count} vertices";
        }

        sealed class Ring
        {
            public Vector3 centre, u, v;
            public float s;
            public readonly float[] radius = new float[RingSides];
            public readonly float[] limit = Enumerable.Repeat(float.PositiveInfinity, RingSides).ToArray();
            public BoneWeight weight;
            public bool clamped;
        }

        /// <summary>Garment vertices bucketed by height, for "how far is the dress in this direction" queries.</summary>
        sealed class Clearance
        {
            const float Slab = 0.01f;
            readonly Dictionary<int, List<Vector3>> slabs = new Dictionary<int, List<Vector3>>();

            public Clearance(IEnumerable<Renderer> renderers)
            {
                foreach (Renderer r in renderers)
                {
                    var filter = r.GetComponent<MeshFilter>();
                    if (filter == null) continue;
                    Matrix4x4 m = r.transform.localToWorldMatrix;
                    foreach (Vector3 v in filter.sharedMesh.vertices)
                    {
                        Vector3 w = m.MultiplyPoint3x4(v);
                        int key = Mathf.FloorToInt(w.y / Slab);
                        if (!slabs.TryGetValue(key, out var list)) slabs[key] = list = new List<Vector3>();
                        list.Add(w);
                    }
                }
            }

            IEnumerable<Vector3> Near(float y, float halfHeight)
            {
                for (int key = Mathf.FloorToInt((y - halfHeight) / Slab); key <= Mathf.FloorToInt((y + halfHeight) / Slab); key++)
                    if (slabs.TryGetValue(key, out var list))
                        foreach (Vector3 p in list)
                            if (Mathf.Abs(p.y - y) <= halfHeight) yield return p;
            }

            /// <summary>Horizontal distance to the nearest garment point within a narrow cone around <paramref name="dir"/>.</summary>
            public float Distance(Vector3 centre, Vector3 dir)
            {
                dir.y = 0f;
                if (dir.sqrMagnitude < 1e-8f) return float.PositiveInfinity;
                dir.Normalize();
                const float cone = 0.97f;   // cos 14 deg
                float best = float.PositiveInfinity;
                foreach (Vector3 p in Near(centre.y, 0.012f))
                {
                    float dx = p.x - centre.x, dz = p.z - centre.z;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d < 1e-4f || (dx * dir.x + dz * dir.z) / d < cone) continue;
                    if (d < best) best = d;
                }
                return best;
            }

            /// <summary>Middle of the dress front-to-back near the centre line, or <paramref name="fallback"/> if none.</summary>
            public float DepthCentre(float y, float halfWidth, float fallback)
            {
                float min = float.PositiveInfinity, max = float.NegativeInfinity;
                foreach (Vector3 p in Near(y, 0.012f))
                {
                    if (Mathf.Abs(p.x) > halfWidth) continue;
                    min = Mathf.Min(min, p.z);
                    max = Mathf.Max(max, p.z);
                }
                return min < max ? (min + max) * 0.5f : fallback;
            }
        }

        static float Fit(Clearance garments, Ring ring, int k, float radius, out bool clamped)
        {
            float angle = 2f * Mathf.PI * k / RingSides;
            Vector3 dir = Mathf.Cos(angle) * ring.u + Mathf.Sin(angle) * ring.v;
            ring.limit[k] = garments.Distance(ring.centre, dir) - GarmentMargin;
            // never thinner than 60 %: where the dress is that tight it may rest on her
            float fitted = Mathf.Max(Mathf.Min(radius, ring.limit[k]), radius * 0.6f);
            clamped = fitted < radius - 1e-4f;
            return fitted;
        }

        /// <summary>Soften the fitted outline around and along the tube, without growing back past the dress.</summary>
        static void Smooth(List<Ring> rings, Clearance garments)
        {
            for (int pass = 0; pass < 3; pass++)
            {
                var next = rings.Select(r => (float[])r.radius.Clone()).ToList();
                for (int i = 0; i < rings.Count; i++)
                    for (int k = 0; k < RingSides; k++)
                    {
                        float around = (rings[i].radius[(k + RingSides - 1) % RingSides] + 2f * rings[i].radius[k] + rings[i].radius[(k + 1) % RingSides]) * 0.25f;
                        float alongPrev = i > 0 ? rings[i - 1].radius[k] : rings[i].radius[k];
                        float alongNext = i < rings.Count - 1 ? rings[i + 1].radius[k] : rings[i].radius[k];
                        float along = (alongPrev + 2f * rings[i].radius[k] + alongNext) * 0.25f;
                        next[i][k] = Mathf.Min((around + along) * 0.5f, Mathf.Max(rings[i].limit[k], rings[i].radius[k]));
                    }
                for (int i = 0; i < rings.Count; i++) next[i].CopyTo(rings[i].radius, 0);
            }
        }

        /// <summary>Closed tube through the rings, capped at both ends; outward-facing triangles.</summary>
        static void AddTube(List<Ring> rings, List<Vector3> vertices, List<BoneWeight> weights, List<int> triangles)
        {
            int start = vertices.Count;
            foreach (Ring ring in rings)
                for (int k = 0; k < RingSides; k++)
                {
                    float a = 2f * Mathf.PI * k / RingSides;
                    vertices.Add(ring.centre + (Mathf.Cos(a) * ring.u + Mathf.Sin(a) * ring.v) * ring.radius[k]);
                    weights.Add(ring.weight);
                }
            int top = vertices.Count;
            vertices.Add(rings[0].centre); weights.Add(rings[0].weight);
            int bottom = vertices.Count;
            vertices.Add(rings[rings.Count - 1].centre); weights.Add(rings[rings.Count - 1].weight);

            var tube = new List<int>();
            for (int i = 0; i < rings.Count - 1; i++)
                for (int k = 0; k < RingSides; k++)
                {
                    int a = start + i * RingSides + k, b = start + i * RingSides + (k + 1) % RingSides;
                    int c = a + RingSides, d = b + RingSides;
                    tube.AddRange(new[] { a, b, c, b, d, c });
                }
            for (int k = 0; k < RingSides; k++)
            {
                tube.AddRange(new[] { top, start + (k + 1) % RingSides, start + k });
                int lastRing = start + (rings.Count - 1) * RingSides;
                tube.AddRange(new[] { bottom, lastRing + k, lastRing + (k + 1) % RingSides });
            }
            // Unity treats Cross(b - a, c - a) as the face normal: make the side faces point away from the axis.
            Vector3 p0 = vertices[tube[0]], p1 = vertices[tube[1]], p2 = vertices[tube[2]];
            Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
            if (Vector3.Dot(n, p0 - rings[0].centre) < 0f)
                for (int t = 0; t < tube.Count; t += 3) (tube[t + 1], tube[t + 2]) = (tube[t + 2], tube[t + 1]);
            triangles.AddRange(tube);
        }

        static float Ellipse(float angle, float outer, float inner, float front, float back)
        {
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            float a = c >= 0f ? outer : inner, b = s >= 0f ? front : back;
            return 1f / Mathf.Sqrt(c * c / (a * a) + s * s / (b * b));
        }

        static void Profile(float s, out float front, out float back, out float outer, out float inner)
        {
            int last = LegProfile.GetLength(0) - 1;
            int i = 0;
            while (i < last - 1 && s > LegProfile[i + 1, 0]) i++;
            float t = Mathf.Clamp01(Mathf.InverseLerp(LegProfile[i, 0], LegProfile[i + 1, 0], s));
            t = t * t * (3f - 2f * t);
            front = Mathf.Lerp(LegProfile[i, 1], LegProfile[i + 1, 1], t);
            back = Mathf.Lerp(LegProfile[i, 2], LegProfile[i + 1, 2], t);
            outer = Mathf.Lerp(LegProfile[i, 3], LegProfile[i + 1, 3], t);
            inner = Mathf.Lerp(LegProfile[i, 4], LegProfile[i + 1, 4], t);
        }

        static float MeanRadius(List<Ring> rings, float s)
        {
            Ring best = rings.OrderBy(r => Mathf.Abs(r.s - s)).First();
            return best.radius.Average();
        }

        static BoneWeight Single(int bone) => new BoneWeight { boneIndex0 = bone, weight0 = 1f };

        /// <summary>Two-bone weight: <paramref name="w"/> on <paramref name="a"/>, the rest on <paramref name="b"/>, largest first.</summary>
        static BoneWeight Pair(int a, float w, int b)
        {
            if (w >= 1f - 1e-4f) return Single(a);
            if (w <= 1e-4f) return Single(b);
            return w >= 0.5f
                ? new BoneWeight { boneIndex0 = a, weight0 = w, boneIndex1 = b, weight1 = 1f - w }
                : new BoneWeight { boneIndex0 = b, weight0 = 1f - w, boneIndex1 = a, weight1 = w };
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

        /// <summary>Add LunaMoonGait + LunaMoonFx + LunaArmRig + LunaBasicAttack on the character root, wired to the rig.</summary>
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
            // arms are rigged at runtime from the static mesh (LunaArmRig); the basic attack layers over the gait
            var arms = character.AddComponent<LunaArmRig>();
            arms.gait = gait;
            var attack = character.AddComponent<LunaBasicAttack>();
            attack.gait = gait;
            attack.arms = arms;
            attack.fx = fx;
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
        /// Ctrl / left mouse or J to attack) and LunaMoonShowcase (tours every gait when the keyboard is left alone), and an orbit camera with bloom.
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
