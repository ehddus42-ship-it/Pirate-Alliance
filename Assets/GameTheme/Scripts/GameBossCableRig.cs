using UnityEngine;

namespace AcRoguelike.GameTheme
{
    /// <summary>Local-space earphone cables: a plugged lead, body coils, a Y split and two flexible arms.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(60)]
    public sealed class GameBossCableRig : MonoBehaviour
    {
        public Transform leftHand, rightHand, jackAnchor;
        public Material cableMaterial;
        public Vector3 coilRadii = new Vector3(1.13f, 0, .64f);
        public float coilBottom = .65f, coilTop = 1.5f;
        public Vector3 splitPosition = new Vector3(0, 2.35f, -.62f);
        public Vector3 handSocket = new Vector3(0, -.18f, 0);
        public Vector3 jackSocket = new Vector3(.4f, 0, 0);
        public float cableRadius = .063f;

        public Mesh[] GeneratedMeshes => generatedMeshes;
        public int CableVertexCount => tubes == null ? 0 : 8 * (24 + 104 + 32 + 34 + 34);

        Tube[] tubes;
        Mesh[] generatedMeshes;
        Material ownedMaterial;
        GameTetrominoBoss boss;
        Vector3 leftRest, rightRest, leftVelocity, rightVelocity;
        Quaternion leftRotation, rightRotation;
        GameTetrominoBossState motion;
        float motionTime, clock;
        bool initialized;

        void Awake() => Initialize();

        public void Initialize()
        {
            if (initialized) return;
            leftHand = leftHand ? leftHand : transform.Find("LeftHand");
            rightHand = rightHand ? rightHand : transform.Find("RightHand");
            jackAnchor = jackAnchor ? jackAnchor : transform.Find("JackAnchor");
            boss = GetComponentInParent<GameTetrominoBoss>();
            leftRest = leftHand ? leftHand.localPosition : new Vector3(-1.85f, 1.6f, .35f);
            rightRest = rightHand ? rightHand.localPosition : new Vector3(1.85f, 1.6f, .35f);
            leftRotation = leftHand ? leftHand.localRotation : Quaternion.identity;
            rightRotation = rightHand ? rightHand.localRotation : Quaternion.identity;
            if (!cableMaterial)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (!shader) shader = Shader.Find("Standard");
                if (!shader) return;
                ownedMaterial = new Material(shader) { name = "Earphone cable (runtime)" };
                ownedMaterial.SetColor("_BaseColor", new Color(.026f, .044f, .068f));
                ownedMaterial.color = new Color(.026f, .044f, .068f);
                if (ownedMaterial.HasProperty("_Smoothness")) ownedMaterial.SetFloat("_Smoothness", .42f);
                cableMaterial = ownedMaterial;
            }
            tubes = new[]
            {
                new Tube(transform, "Cable_PlugLead", 24, cableMaterial),
                new Tube(transform, "Cable_BodyCoils", 104, cableMaterial),
                new Tube(transform, "Cable_YLead", 32, cableMaterial),
                new Tube(transform, "Cable_LeftArm", 34, cableMaterial),
                new Tube(transform, "Cable_RightArm", 34, cableMaterial)
            };
            generatedMeshes = new Mesh[tubes.Length];
            for (int i = 0; i < tubes.Length; i++) generatedMeshes[i] = tubes[i].Mesh;
            initialized = true;
            RefreshCables(0);
        }

        /// <summary>Creates a neutral rig without entering play mode. A prefab builder may persist GeneratedMeshes.</summary>
        public void RefreshRestPose()
        {
            Initialize();
            if (!initialized) return;
            if (leftHand) leftHand.SetLocalPositionAndRotation(leftRest, leftRotation);
            if (rightHand) rightHand.SetLocalPositionAndRotation(rightRest, rightRotation);
            leftVelocity = rightVelocity = Vector3.zero;
            RefreshCables(0);
        }

        public void SetMotion(GameTetrominoBossState state, float time)
        {
            motion = state;
            motionTime = time;
        }

        void LateUpdate()
        {
            if (!Application.isPlaying || Time.deltaTime <= 0) return;
            Initialize();
            if (!initialized) return;
            if (boss && boss.Health && boss.Health.IsAlive && boss.Target && !boss.Target.IsAlive)
            {
                motion = GameTetrominoBossState.Approach;
                motionTime = 0;
            }
            if (boss && boss.DeathFinished) return;
            clock += Time.deltaTime;
            PoseHands(Time.deltaTime);
            RefreshCables(clock);
        }

        void PoseHands(float dt)
        {
            Vector3 left = leftRest, right = rightRest;
            float sway = Mathf.Sin(clock * 2.8f);
            left += new Vector3(.09f * sway, .14f * Mathf.Sin(clock * 3.3f), .14f * Mathf.Cos(clock * 2.6f));
            right += new Vector3(.09f * sway, .14f * Mathf.Sin(clock * 3.3f + 2), -.14f * Mathf.Cos(clock * 2.6f));
            Quaternion lr = leftRotation * Quaternion.Euler(8 * sway, 0, -8);
            Quaternion rr = rightRotation * Quaternion.Euler(-8 * sway, 0, 8);
            switch (motion)
            {
                case GameTetrominoBossState.VolleyWindup:
                case GameTetrominoBossState.Volley:
                    float reach = motion == GameTetrominoBossState.Volley ? 1 : Mathf.SmoothStep(0, 1, motionTime / .7f);
                    left = Vector3.Lerp(left, new Vector3(-1.15f, 1.7f, 1.35f), reach);
                    right = Vector3.Lerp(right, new Vector3(1.15f, 1.7f, 1.35f), reach);
                    if (motion == GameTetrominoBossState.Volley)
                    {
                        float recoil = Mathf.Exp(-Mathf.Repeat(motionTime, .25f) * 20) * .25f;
                        left.z -= recoil;
                        right.z -= recoil;
                    }
                    lr = leftRotation * Quaternion.Euler(-45, 0, -22);
                    rr = rightRotation * Quaternion.Euler(-45, 0, 22);
                    break;
                case GameTetrominoBossState.BarWindup:
                    float lift = Mathf.SmoothStep(0, 1, motionTime / .5f);
                    float spin = Mathf.Max(0, motionTime - .45f) * 9f;
                    right = Vector3.Lerp(rightRest, new Vector3(1.15f + Mathf.Cos(spin) * .62f, 3.75f, .25f + Mathf.Sin(spin) * .62f), lift);
                    left = Vector3.Lerp(leftRest, new Vector3(-1.65f, 2.05f, .7f), lift);
                    rr = rightRotation * Quaternion.Euler(-80, spin * Mathf.Rad2Deg, 0);
                    lr = leftRotation * Quaternion.Euler(-35, 0, -30);
                    break;
                case GameTetrominoBossState.BarThrow:
                    right = Vector3.Lerp(new Vector3(1.4f, 3.7f, .2f), new Vector3(.6f, 1.7f, 2f), Mathf.Clamp01(motionTime / .28f));
                    left = new Vector3(-2f, 1.4f, -.2f);
                    rr = rightRotation * Quaternion.Euler(-100, 0, 15);
                    break;
                case GameTetrominoBossState.SummonWindup:
                case GameTetrominoBossState.Summon:
                    float gather = motion == GameTetrominoBossState.Summon ? 1 : Mathf.SmoothStep(0, 1, motionTime / 1.1f);
                    left = Vector3.Lerp(leftRest, new Vector3(-2.1f, 2.9f, .7f), gather);
                    right = Vector3.Lerp(rightRest, new Vector3(2.1f, 2.9f, .7f), gather);
                    if (motion == GameTetrominoBossState.Summon)
                    {
                        left.y -= Mathf.Clamp01(motionTime / .3f) * 1.4f;
                        right.y = left.y;
                    }
                    lr = leftRotation * Quaternion.Euler(-30, 0, -55);
                    rr = rightRotation * Quaternion.Euler(-30, 0, 55);
                    break;
                case GameTetrominoBossState.Dead:
                    left = new Vector3(-1.5f, .3f, .15f);
                    right = new Vector3(1.5f, .3f, .15f);
                    break;
            }
            // Smoothed endpoints and travelling bends make the cable behave like a loose wire, not an arm bone.
            if (leftHand)
            {
                leftHand.localPosition = Vector3.SmoothDamp(leftHand.localPosition, left, ref leftVelocity, .11f, 25, dt);
                leftHand.localRotation = Quaternion.Slerp(leftHand.localRotation, lr, 1 - Mathf.Exp(-12 * dt));
            }
            if (rightHand)
            {
                rightHand.localPosition = Vector3.SmoothDamp(rightHand.localPosition, right, ref rightVelocity, .11f, 25, dt);
                rightHand.localRotation = Quaternion.Slerp(rightHand.localRotation, rr, 1 - Mathf.Exp(-12 * dt));
            }
        }

        void RefreshCables(float time)
        {
            Vector3 coilStart = CoilPoint(0), coilEnd = CoilPoint(1);
            Vector3 plug = jackAnchor ? transform.InverseTransformPoint(jackAnchor.TransformPoint(jackSocket)) : new Vector3(1.45f, 1.35f, .05f);
            FillBezier(tubes[0].Path, plug, plug + Vector3.right * .28f, coilStart + new Vector3(.35f, -.28f, 0), coilStart);
            for (int i = 0; i < tubes[1].Path.Length; i++) tubes[1].Path[i] = CoilPoint(i / (tubes[1].Path.Length - 1f));
            FillBezier(tubes[2].Path, coilEnd, coilEnd + new Vector3(-.4f, .25f, -.7f), splitPosition + new Vector3(-.75f, -.15f, 0), splitPosition);
            ShapeArm(tubes[3].Path, leftHand, leftRest, -1, time);
            ShapeArm(tubes[4].Path, rightHand, rightRest, 1, time);
            for (int i = 0; i < tubes.Length; i++) tubes[i].Apply(cableRadius * (i == 2 ? 1.2f : 1));
        }

        Vector3 CoilPoint(float t)
        {
            float angle = t * Mathf.PI * 5;
            float x = Mathf.Cos(angle), z = Mathf.Sin(angle);
            // A rounded rectangle follows the handheld case rather than cutting through its flat front.
            return new Vector3(Mathf.Sign(x) * Mathf.Pow(Mathf.Abs(x), .42f) * coilRadii.x,
                Mathf.Lerp(coilBottom, coilTop, t), Mathf.Sign(z) * Mathf.Pow(Mathf.Abs(z), .42f) * coilRadii.z);
        }

        void ShapeArm(Vector3[] path, Transform hand, Vector3 rest, int side, float time)
        {
            Vector3 end = hand ? transform.InverseTransformPoint(hand.TransformPoint(handSocket)) : rest;
            Vector3 c1 = splitPosition + new Vector3(side * 1.2f, .5f, -.15f);
            Vector3 c2 = end + new Vector3(-side * .2f, -.65f, -.5f);
            FillBezier(path, splitPosition, c1, c2, end);
            for (int i = 1; i < path.Length - 1; i++)
            {
                float u = i / (path.Length - 1f), envelope = Mathf.Sin(u * Mathf.PI);
                path[i] += new Vector3(side * .1f * Mathf.Sin(u * 7 - time * 3 + side),
                    .1f * Mathf.Sin(u * 8 - time * 4), .13f * Mathf.Cos(u * 6 - time * 2.8f + side)) * envelope;
            }
        }

        static void FillBezier(Vector3[] points, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            for (int i = 0; i < points.Length; i++)
            {
                float t = i / (points.Length - 1f), u = 1 - t;
                points[i] = u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d;
            }
        }

        void OnDestroy()
        {
            if (tubes != null) foreach (var tube in tubes) DestroyOwned(tube.Mesh);
            DestroyOwned(ownedMaterial);
        }

        static void DestroyOwned(Object value)
        {
            if (!value) return;
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(value)) return;
#endif
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }

        sealed class Tube
        {
            const int Sides = 8;
            public readonly Vector3[] Path;
            public readonly Mesh Mesh;
            readonly Vector3[] vertices, normals;

            public Tube(Transform parent, string name, int rings, Material material)
            {
                Path = new Vector3[rings];
                vertices = new Vector3[rings * Sides];
                normals = new Vector3[vertices.Length];
                var uvs = new Vector2[vertices.Length];
                var triangles = new int[(rings - 1) * Sides * 6];
                int cursor = 0;
                for (int r = 0; r < rings; r++)
                    for (int s = 0; s < Sides; s++)
                    {
                        uvs[r * Sides + s] = new Vector2(s / (float)Sides, r / (rings - 1f) * 8);
                        if (r == rings - 1) continue;
                        int a = r * Sides + s, b = r * Sides + (s + 1) % Sides, c = a + Sides, d = b + Sides;
                        triangles[cursor++] = a; triangles[cursor++] = b; triangles[cursor++] = c;
                        triangles[cursor++] = b; triangles[cursor++] = d; triangles[cursor++] = c;
                    }
                Mesh = new Mesh { name = name + "_Mesh" };
                Mesh.MarkDynamic();
                Mesh.vertices = vertices;
                Mesh.normals = normals;
                Mesh.uv = uvs;
                Mesh.triangles = triangles;
                Transform existing = parent.Find(name);
                var go = existing ? existing.gameObject : new GameObject(name);
                go.layer = 2;
                go.transform.SetParent(parent, false);
                go.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                go.transform.localScale = Vector3.one;
                var filter = go.GetComponent<MeshFilter>();
                if (!filter) filter = go.AddComponent<MeshFilter>();
                var renderer = go.GetComponent<MeshRenderer>();
                if (!renderer) renderer = go.AddComponent<MeshRenderer>();
                filter.sharedMesh = Mesh;
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            public void Apply(float radius)
            {
                Vector3 previous = Vector3.zero;
                Vector3 low = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), high = -low;
                for (int r = 0; r < Path.Length; r++)
                {
                    Vector3 tangent = r == Path.Length - 1 ? Path[r] - Path[r - 1] : Path[r + 1] - Path[r];
                    if (tangent.sqrMagnitude < .000001f) tangent = Vector3.up;
                    tangent.Normalize();
                    Vector3 normal = previous.sqrMagnitude > .01f ? Vector3.ProjectOnPlane(previous, tangent).normalized : Vector3.Cross(tangent, Vector3.up).normalized;
                    if (normal.sqrMagnitude < .01f) normal = Vector3.Cross(tangent, Vector3.right).normalized;
                    previous = normal;
                    Vector3 binormal = Vector3.Cross(tangent, normal);
                    for (int s = 0; s < Sides; s++)
                    {
                        float angle = s * Mathf.PI * 2 / Sides;
                        int index = r * Sides + s;
                        normals[index] = normal * Mathf.Cos(angle) + binormal * Mathf.Sin(angle);
                        vertices[index] = Path[r] + normals[index] * radius;
                        low = Vector3.Min(low, vertices[index]);
                        high = Vector3.Max(high, vertices[index]);
                    }
                }
                Mesh.SetVertices(vertices);
                Mesh.SetNormals(normals);
                Mesh.bounds = new Bounds((low + high) * .5f, high - low);
            }
        }
    }
}
