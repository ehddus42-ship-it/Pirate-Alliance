using System.Collections.Generic;
using UnityEngine;

namespace NamgungLuna.Motion
{
    /// <summary>
    /// 남궁루나 팔 리그. 원본 모델은 팔과 손이 몸 피부 메시(Body_VisibleSkin)에 붙은 정적 메시라서, 시작할 때 Visual 아래에
    /// 쇄골 → 위팔 → 아래팔 → 손 뼈를 만들고 팔 주변 메시를 스킨 메시로 바꿉니다.
    /// Procedural arm rig for the unrigged model, built at runtime (Awake):
    /// - bones per side: Clavicle → UpperArm → ForeArm → Hand (rest rotation = Visual's), plus a SleevePivot on the upper arm,
    /// - Body_VisibleSkin (arms, hands, neck and chest skin), Bodice_Navy (puff sleeve caps) and Puff_Sleeve_Join_L/R get a
    ///   skinned copy (the static renderer is switched off); weights come from the vertex position along the arm,
    /// - the Magica bell sleeves and bands hang from the SleevePivot (fixed band on the upper arm, swings part way with the
    ///   forearm) and the Collider_Forearm_L/R move with the forearm, so the cloth keeps falling around the arm,
    /// - the lantern pivot follows the grip in the right hand (<see cref="FollowLantern"/>).
    /// Unity +X is her right side; joint positions are for the right arm in Visual space and are mirrored for the left.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    public class LunaArmRig : MonoBehaviour
    {
        public LunaMoonGait gait;

        [Header("Joints (오른팔, Visual 공간 m. 왼팔은 x 반전)")]
        public Vector3 clavicle = new Vector3(0.04f, 1.365f, -0.03f);
        public Vector3 shoulder = new Vector3(0.165f, 1.33f, -0.03f);
        public Vector3 elbow = new Vector3(0.223f, 1.136f, -0.032f);
        public Vector3 wrist = new Vector3(0.332f, 1.0f, -0.016f);
        public Vector3 handTip = new Vector3(0.40f, 0.846f, -0.009f);
        [Tooltip("벨 소매가 매달리는 점 (위팔 고정 띠 가운데)")]
        public Vector3 sleevePivot = new Vector3(0.195f, 1.205f, -0.03f);
        [Tooltip("손바닥 가운데 (손 로컬, 오른손 기준). 투사체가 나오는 점")]
        public Vector3 palm = new Vector3(0.03f, -0.07f, 0.012f);
        [Range(0f, 1f)]
        [Tooltip("벨 소매가 아래팔 회전을 따라가는 비율. 나머지는 클로스가 처리")]
        public float sleeveFollow = 0.7f;

        public sealed class Arm
        {
            public Transform clavicle, upper, fore, hand, sleeve;
            public Vector3 clavicleRest, shoulderRest;
            public float upperLength, foreLength;
            public Quaternion upperFrame, foreFrame;
            public int side;   // +1 right, -1 left
        }

        /// <summary>One arm's target. Offsets are from the shoulder, in the character root's orientation (m).</summary>
        public struct ArmPose
        {
            public Vector3 target, pole;
            public Quaternion clavicle, hand;
        }

        public Arm Left { get; private set; }
        public Arm Right { get; private set; }
        public bool Built { get; private set; }
        public int SkinnedRenderers { get; private set; }

        Transform visual, lanternPivot;
        Vector3 lanternGrip;

        // weight profiles: lateral threshold (m from the upper-arm axis, negative = toward the body), shoulder blend along the arm
        struct Profile { public float lat0, lat1, ax0, ax1; }
        static readonly Dictionary<string, Profile> Skinned = new Dictionary<string, Profile>
        {
            { "Body_VisibleSkin", new Profile { lat0 = -0.06f, lat1 = -0.04f, ax0 = -0.035f, ax1 = 0.03f } },
            { "Bodice_Navy", new Profile { lat0 = -0.075f, lat1 = -0.04f, ax0 = -0.06f, ax1 = 0f } },
            { "Puff_Sleeve_Join_L", new Profile { lat0 = -0.10f, lat1 = -0.06f, ax0 = -0.06f, ax1 = 0f } },
            { "Puff_Sleeve_Join_R", new Profile { lat0 = -0.10f, lat1 = -0.06f, ax0 = -0.06f, ax1 = 0f } },
        };

        void Awake()
        {
            if (gait == null) gait = GetComponent<LunaMoonGait>();
            Build();
        }

        void Build()
        {
            if (Built || gait == null || gait.visual == null) return;
            visual = gait.visual;
            Left = MakeArm(-1);
            Right = MakeArm(1);

            var renderers = new Dictionary<string, MeshRenderer>();
            foreach (MeshRenderer r in visual.GetComponentsInChildren<MeshRenderer>(true))
                if (!renderers.ContainsKey(r.name)) renderers[r.name] = r;
            Transform[] bones = { visual, Left.clavicle, Left.upper, Left.fore, Left.hand, Right.clavicle, Right.upper, Right.fore, Right.hand };
            foreach (var entry in Skinned)
                if (renderers.TryGetValue(entry.Key, out MeshRenderer r) && Skin(r, entry.Value, bones)) SkinnedRenderers++;

            foreach (string side in new[] { "L", "R" })
            {
                Arm arm = side == "L" ? Left : Right;
                foreach (string part in new[] { "BellSleeve_", "Sleeve_Band_" })
                    if (renderers.TryGetValue(part + side, out MeshRenderer r)) r.transform.SetParent(arm.sleeve, true);
                foreach (Transform t in GetComponentsInChildren<Transform>(true))
                    if (t.name == "Collider_Forearm_" + side) t.SetParent(arm.fore, true);
            }

            lanternPivot = gait.lantern;
            if (lanternPivot != null) lanternGrip = Right.hand.InverseTransformPoint(lanternPivot.position);
            Built = true;
        }

        Arm MakeArm(int s)
        {
            Vector3 C = Mirror(clavicle, s), S = Mirror(shoulder, s), E = Mirror(elbow, s), W = Mirror(wrist, s);
            var arm = new Arm { side = s, clavicleRest = C, shoulderRest = S };
            string side = s > 0 ? "R" : "L";
            arm.clavicle = Bone("Clavicle_" + side, visual, C);
            arm.upper = Bone("UpperArm_" + side, arm.clavicle, S - C);
            arm.fore = Bone("ForeArm_" + side, arm.upper, E - S);
            arm.hand = Bone("Hand_" + side, arm.fore, W - E);
            arm.sleeve = Bone("SleevePivot_" + side, arm.upper, Mirror(sleevePivot, s) - S);
            arm.upperLength = Vector3.Distance(S, E);
            arm.foreLength = Vector3.Distance(E, W);
            // Rest frames: bone direction + the bend-plane normal, with the elbow pointing back (-Z) at rest.
            Vector3 dW = (W - S).normalized;
            Vector3 back = Vector3.ProjectOnPlane(Vector3.back, dW).normalized;
            Vector3 n = Vector3.Cross(dW, back).normalized;
            arm.upperFrame = Frame(E - S, n);
            arm.foreFrame = Frame(W - E, n);
            return arm;
        }

        static Vector3 Mirror(Vector3 v, int s) => new Vector3(v.x * s, v.y, v.z);

        static Transform Bone(string name, Transform parent, Vector3 localPosition)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            return t;
        }

        /// <summary>Rotation whose x axis is <paramref name="dir"/> and y axis is <paramref name="normal"/> made perpendicular to it.</summary>
        static Quaternion Frame(Vector3 dir, Vector3 normal)
        {
            Vector3 x = dir.normalized;
            Vector3 y = (normal - x * Vector3.Dot(normal, x)).normalized;
            return Quaternion.LookRotation(Vector3.Cross(x, y), y);
        }

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Weights for a vertex in right-arm coordinates (Visual space, x mirrored for the left arm).</summary>
        void ArmWeights(Vector3 q, Profile p, out float body, out float clav, out float upper, out float fore, out float hand)
        {
            Vector3 u0 = (elbow - shoulder).normalized, f0 = (wrist - elbow).normalized, h0 = (handTip - wrist).normalized;
            Vector3 lateral = new Vector3(-u0.y, u0.x, 0f).normalized;   // perpendicular to the upper arm, away from the body
            Vector3 d = q - shoulder;
            float along = Vector3.Dot(d, u0);
            float side = Vector3.Dot(d, lateral);
            float elbowSide = Vector3.Dot(q - elbow, (u0 + f0).normalized);
            // below the elbow there is no body next to the arm
            float lat = Mathf.Max(Smooth(p.lat0, p.lat1, side), Smooth(-0.02f, 0.02f, elbowSide));
            float arm = q.x > 0.05f ? lat * Smooth(p.ax0, p.ax1, along) : 0f;
            float bE = Smooth(-0.025f, 0.025f, elbowSide);
            float bW = Smooth(-0.015f, 0.02f, Vector3.Dot(q - wrist, (f0 + h0).normalized));
            float c = (1f - Smooth(0.03f, 0.10f, d.magnitude)) * Smooth(0.06f, 0.12f, q.x);
            body = (1f - arm) * (1f - c);
            clav = (1f - arm) * c;
            upper = arm * (1f - bE);
            fore = arm * bE * (1f - bW);
            hand = arm * bE * bW;
        }

        bool Skin(MeshRenderer renderer, Profile profile, Transform[] bones)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            Mesh source = filter != null ? filter.sharedMesh : null;
            if (source == null) return false;
            if (!source.isReadable)
            {
                Debug.LogWarning($"[LunaArmRig] {source.name} is not readable (enable Read/Write on the FBX); the arm stays static.", this);
                return false;
            }
            Matrix4x4 toVisual = visual.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            Vector3[] vertices = source.vertices;
            var weights = new BoneWeight[vertices.Length];
            var w = new float[9];
            var order = new int[9];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = toVisual.MultiplyPoint3x4(vertices[i]);
                int s = v.x >= 0f ? 1 : -1;
                int first = s > 0 ? 5 : 1;
                System.Array.Clear(w, 0, 9);
                ArmWeights(new Vector3(v.x * s, v.y, v.z), profile, out w[0], out w[first], out w[first + 1], out w[first + 2], out w[first + 3]);
                weights[i] = Strongest(w, order);
            }

            Mesh mesh = Instantiate(source);
            mesh.name = source.name + "_ArmSkinned";
            mesh.boneWeights = weights;
            var go = new GameObject(renderer.name + "_Skinned");
            go.transform.SetParent(renderer.transform, false);
            var bindposes = new Matrix4x4[bones.Length];
            for (int b = 0; b < bones.Length; b++) bindposes[b] = bones[b].worldToLocalMatrix * go.transform.localToWorldMatrix;
            mesh.bindposes = bindposes;

            var skinned = go.AddComponent<SkinnedMeshRenderer>();
            skinned.sharedMesh = mesh;
            skinned.sharedMaterials = renderer.sharedMaterials;
            skinned.bones = bones;
            skinned.rootBone = visual;
            skinned.localBounds = new Bounds(new Vector3(0f, 1.1f, 0f), new Vector3(2.4f, 1.8f, 2.4f));
            skinned.shadowCastingMode = renderer.shadowCastingMode;
            skinned.receiveShadows = renderer.receiveShadows;
            renderer.enabled = false;
            return true;
        }

        /// <summary>Up to four strongest weights, normalised, largest first.</summary>
        static BoneWeight Strongest(float[] w, int[] order)
        {
            for (int i = 0; i < w.Length; i++) order[i] = i;
            System.Array.Sort(order, (a, b) => w[b].CompareTo(w[a]));
            float sum = 0f;
            for (int k = 0; k < 4; k++) sum += w[order[k]];
            if (sum <= 1e-6f) return new BoneWeight { boneIndex0 = 0, weight0 = 1f };
            return new BoneWeight
            {
                boneIndex0 = order[0], weight0 = w[order[0]] / sum,
                boneIndex1 = order[1], weight1 = w[order[1]] / sum,
                boneIndex2 = order[2], weight2 = w[order[2]] / sum,
                boneIndex3 = order[3], weight3 = w[order[3]] / sum,
            };
        }

        /// <summary>Pose both arms (two-bone IK on top of the clavicle). <paramref name="weight"/> 0 = rest pose.</summary>
        public void Pose(ArmPose left, ArmPose right, float weight)
        {
            if (!Built) return;
            Solve(Left, left, weight);
            Solve(Right, right, weight);
        }

        void Solve(Arm arm, ArmPose pose, float weight)
        {
            Quaternion clav = Quaternion.Slerp(Quaternion.identity, pose.clavicle, weight);
            arm.clavicle.localRotation = clav;
            arm.hand.localRotation = Quaternion.Slerp(Quaternion.identity, pose.hand, weight);
            if (weight <= 0f)
            {
                arm.upper.localRotation = arm.fore.localRotation = arm.sleeve.localRotation = Quaternion.identity;
                return;
            }
            // Visual space: the targets are offsets from the shoulder in the root's orientation
            Vector3 S = arm.clavicleRest + clav * (arm.shoulderRest - arm.clavicleRest);
            Quaternion toVisual = Quaternion.Inverse(visual.localRotation);
            Vector3 target = S + toVisual * pose.target, pole = S + toVisual * pose.pole;
            float a = arm.upperLength, b = arm.foreLength;
            Vector3 d = target - S;
            Vector3 dn = d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.down;
            float length = Mathf.Clamp(d.magnitude, Mathf.Abs(a - b) + 0.01f, a + b - 0.002f);
            Vector3 bend = Vector3.ProjectOnPlane(pole - S, dn);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(Vector3.back, dn);
            bend.Normalize();
            float cosA = Mathf.Clamp((a * a + length * length - b * b) / (2f * a * length), -1f, 1f);
            float sinA = Mathf.Sqrt(1f - cosA * cosA);
            Vector3 E = S + dn * (a * cosA) + bend * (a * sinA);
            Vector3 W = S + dn * length;
            Vector3 n = Vector3.Cross(dn, bend).normalized;
            Quaternion upper = Frame(E - S, n) * Quaternion.Inverse(arm.upperFrame);
            Quaternion fore = Frame(W - E, n) * Quaternion.Inverse(arm.foreFrame);
            arm.upper.localRotation = Quaternion.Slerp(Quaternion.identity, Quaternion.Inverse(clav) * upper, weight);
            arm.fore.localRotation = Quaternion.Slerp(Quaternion.identity, Quaternion.Inverse(upper) * fore, weight);
            arm.sleeve.localRotation = Quaternion.Slerp(Quaternion.identity, arm.fore.localRotation, sleeveFollow);
        }

        /// <summary>Keep the lantern's handle in the right hand (call after posing, before the pendulum update).</summary>
        public void FollowLantern()
        {
            if (Built && lanternPivot != null) lanternPivot.position = Right.hand.TransformPoint(lanternGrip);
        }

        /// <summary>Middle of the palm (world), where a cast leaves the hand.</summary>
        public Vector3 Palm(Arm arm) => arm.hand.TransformPoint(Mirror(palm, arm.side));
    }
}
