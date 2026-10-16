using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace AcRoguelike.Ruins
{
    /// <summary>Original mechanical appendages and clip-driven humanoid motion, below the shared hit-reaction pivot.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(65)]
    public sealed class RuinsMonsterRig : MonoBehaviour
    {
        public RuinsMonsterKind kind;
        public Transform model;
        public Animator humanoidAnimator;
        public AnimationClip idleClip, walkClip, runClip, attackClip, hitClip, deathClip;
        public Material metalMaterial, jointMaterial, glowMaterial;
        public float medusaSocketHeight = 1.73f, medusaSocketRadius = .32f, tentacleLength = 1.55f;
        public float droneFanHeight = 1.7f, droneFanOffset = .6f, cannonHeight = .9f;
        [Range(0, 1)] public float attackAnticipationEnd = .35f;
        [Range(0, 1)] public float attackImpactNormalized = .49f;
        public Mesh[] GeneratedMeshes => generatedMeshes ?? System.Array.Empty<Mesh>();
        public int TentacleCount => tentacles == null ? 0 : tentacles.Length;
        public bool UsesAnimationClips => graph.IsValid();

        Transform parts, cannon;
        readonly List<Transform> fans = new List<Transform>();
        readonly List<Renderer> glowRenderers = new List<Renderer>();
        Tentacle[] tentacles;
        Mesh[] generatedMeshes;
        Vector3 modelRest, lastRootPosition, velocity, cannonRest;
        Quaternion modelRotation;
        RuinsMonsterState motion;
        float motionTime, move01, clock, hitTime, deathTime;
        int pattern;
        bool initialized, dead;
        MaterialPropertyBlock glowBlock;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        readonly AnimationClipPlayable[] clipPlayers = new AnimationClipPlayable[6];
        readonly bool[] hasClip = new bool[6];
        readonly float[] weights = new float[6];

        void Awake() => Initialize();

        public void Configure(RuinsMonsterKind monsterKind)
        {
            kind = monsterKind;
            Initialize();
        }

        public void Initialize()
        {
            if (initialized) return;
            if (!model) model = transform.Find("MeshyModel");
            modelRest = model ? model.localPosition : Vector3.zero;
            modelRotation = model ? model.localRotation : Quaternion.identity;
            if (!metalMaterial) metalMaterial = RuinsVisual.Iron;
            if (!jointMaterial) jointMaterial = RuinsVisual.Brass;
            if (!glowMaterial) glowMaterial = RuinsVisual.Amber;
            parts = transform.Find("RigParts");
            if (!parts) { parts = new GameObject("RigParts").transform; parts.SetParent(transform, false); }
            parts.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            parts.localScale = Vector3.one;
            switch (kind)
            {
                case RuinsMonsterKind.OssuaryMedusa: BuildTentacles(); break;
                case RuinsMonsterKind.CarrionDrone: BuildFans(); break;
                case RuinsMonsterKind.ScrapBulwark: BuildCannon(); break;
            }
            glowBlock = new MaterialPropertyBlock();
            foreach (var renderer in parts.GetComponentsInChildren<Renderer>())
                if (renderer.sharedMaterial == glowMaterial) glowRenderers.Add(renderer);
            lastRootPosition = transform.position;
            initialized = true;
            if (Application.isPlaying) InitializeAnimation();
            Pose(0);
        }

        public void RefreshRestPose()
        {
            Initialize();
            clock = hitTime = deathTime = motionTime = move01 = 0;
            dead = false; motion = RuinsMonsterState.Approach; velocity = Vector3.zero;
            Pose(0);
        }

        public void SetMotion(RuinsMonsterState state, float stateTime, float speed01, int attackPattern)
        {
            motion = state; motionTime = stateTime; move01 = Mathf.Clamp01(speed01); pattern = attackPattern;
        }
        public void PlayHit() => hitTime = .3f;
        public void PlayDeath()
        {
            dead = true; deathTime = 0; motion = RuinsMonsterState.Dead;
            if (hasClip[5]) clipPlayers[5].SetTime(0);
        }

        void LateUpdate()
        {
            if (!Application.isPlaying || Time.deltaTime <= 0) return;
            Initialize();
            float dt = Time.deltaTime;
            Vector3 worldVelocity = (transform.position - lastRootPosition) / Mathf.Max(.001f, dt);
            lastRootPosition = transform.position;
            velocity = Vector3.Lerp(velocity, transform.InverseTransformDirection(worldVelocity), 1 - Mathf.Exp(-5 * dt));
            velocity.y = 0; velocity = Vector3.ClampMagnitude(velocity, 8);
            clock += dt;
            hitTime = Mathf.Max(0, hitTime - dt);
            if (dead) deathTime += dt;
            Pose(dt);
            AnimateHumanoid(dt);
        }

        void Pose(float dt)
        {
            if (!initialized) return;
            float hit = Mathf.Sin(Mathf.Clamp01(hitTime / .3f) * Mathf.PI);
            float windup = motion == RuinsMonsterState.Windup ? Mathf.SmoothStep(0, 1, motionTime / .8f) : 0;
            float strike = motion == RuinsMonsterState.Attack ? Mathf.Sin(Mathf.Clamp01(motionTime / .6f) * Mathf.PI) : 0;
            Vector3 offset = Vector3.zero;
            Quaternion tilt = Quaternion.identity;
            if (kind == RuinsMonsterKind.OssuaryMedusa || kind == RuinsMonsterKind.CarrionDrone)
            {
                offset.y = Mathf.Sin(clock * 2.1f) * .075f;
                tilt = Quaternion.Euler(-velocity.z * 1.8f + hit * 6, 0, velocity.x * 2.1f);
            }
            else if (kind == RuinsMonsterKind.ScrapBulwark)
            {
                offset.y = Mathf.Abs(Mathf.Sin(clock * 15)) * .022f * move01;
                tilt = Quaternion.Euler(-windup * 4 + hit * 5, 0, Mathf.Sin(clock * 9) * move01);
            }
            else if (kind == RuinsMonsterKind.MourningMatron)
            {
                // A slow, uneven forward lean distinguishes the tall hunter from the shorter charging husk.
                tilt = Quaternion.Euler(2 + windup * 4, Mathf.Sin(clock * .85f) * 1.4f, Mathf.Sin(clock * 1.35f) * .9f);
            }
            if (dead)
            {
                offset = Vector3.zero;
                tilt = Quaternion.identity;
            }
            if (model)
            {
                model.localPosition = modelRest + offset;
                model.localRotation = tilt * modelRotation;
            }
            if (parts)
            {
                // Tentacles are authored in Pose space; follow the floating shell's elevated tilt pivot.
                parts.localPosition = kind == RuinsMonsterKind.OssuaryMedusa && model
                    ? offset + modelRest - tilt * modelRest : offset;
                parts.localRotation = tilt;
            }
            if (cannon)
            {
                float recoil = motion == RuinsMonsterState.Attack ? Mathf.Exp(-Mathf.Repeat(motionTime, .18f) * 33) * .19f : 0;
                cannon.localPosition = cannonRest + Vector3.back * recoil;
            }
            foreach (var fan in fans)
            {
                float spin = dead ? Mathf.Min(deathTime, .9f) * 550 : clock * (1250 + move01 * 600);
                fan.localRotation = Quaternion.Euler(0, spin * (fan.name.EndsWith("Left") ? 1 : -1), 0);
            }
            if (tentacles != null)
            {
                float curl = motion == RuinsMonsterState.Attack ? 1 - Mathf.Clamp01(motionTime / .3f) : windup;
                float lash = motion == RuinsMonsterState.Recovery && pattern % 2 == 0 ? Mathf.Exp(-motionTime * 12) : strike;
                for (int i = 0; i < tentacles.Length; i++) ShapeTentacle(i, curl, lash);
            }
            float emission = dead ? Mathf.Max(0, 1 - deathTime * 1.5f) : 1 + windup * 2.2f + strike * 2 + hit * 2;
            foreach (var renderer in glowRenderers)
            {
                if (!renderer) continue;
                renderer.GetPropertyBlock(glowBlock);
                glowBlock.SetColor("_EmissionColor", new Color(1, .34f, .025f) * emission);
                renderer.SetPropertyBlock(glowBlock);
            }
        }

        void BuildCannon()
        {
            cannon = parts.Find("Recoil barrel");
            if (!cannon) { cannon = new GameObject("Recoil barrel").transform; cannon.SetParent(parts, false); }
            cannonRest = new Vector3(0, cannonHeight, .86f);
            cannon.localPosition = cannonRest;
            RuinsVisual.Part(cannon, "Sleeve", PrimitiveType.Cylinder, new Vector3(0, 0, .16f),
                new Vector3(.3f, .24f, .3f), metalMaterial).localRotation = Quaternion.Euler(90, 0, 0);
            RuinsVisual.Part(cannon, "Muzzle band", PrimitiveType.Cylinder, new Vector3(0, 0, .39f),
                new Vector3(.32f, .035f, .32f), jointMaterial).localRotation = Quaternion.Euler(90, 0, 0);
            RuinsVisual.Part(cannon, "Muzzle heat", PrimitiveType.Cylinder, new Vector3(0, 0, .43f),
                new Vector3(.16f, .013f, .16f), glowMaterial).localRotation = Quaternion.Euler(90, 0, 0);
        }

        void BuildFans()
        {
            for (int side = -1; side <= 1; side += 2)
            {
                string suffix = side < 0 ? "Left" : "Right";
                var fan = parts.Find("Rotor " + suffix);
                if (!fan) { fan = new GameObject("Rotor " + suffix).transform; fan.SetParent(parts, false); }
                fan.localPosition = new Vector3(side * droneFanOffset, droneFanHeight, 0);
                fans.Add(fan);
                RuinsVisual.Part(fan, "Hub", PrimitiveType.Cylinder, Vector3.zero,
                    new Vector3(.12f, .028f, .12f), jointMaterial);
                for (int blade = 0; blade < 4; blade++)
                {
                    float angle = blade * 90;
                    var part = RuinsVisual.Part(fan, "Blade " + blade, PrimitiveType.Cube,
                        Quaternion.Euler(0, angle, 0) * Vector3.forward * .13f,
                        new Vector3(.075f, .018f, .21f), metalMaterial);
                    part.localRotation = Quaternion.Euler(0, angle + 17, 0);
                }
                RuinsVisual.Part(parts, "Engine glow " + suffix, PrimitiveType.Sphere,
                    new Vector3(side * droneFanOffset, droneFanHeight - .22f, -.12f),
                    new Vector3(.13f, .08f, .13f), glowMaterial);
            }
        }

        void BuildTentacles()
        {
            tentacles = new Tentacle[8]; generatedMeshes = new Mesh[8];
            for (int i = 0; i < 8; i++)
            {
                float angle = (i * 45 + 22.5f) * Mathf.Deg2Rad;
                var direction = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
                var socket = direction * medusaSocketRadius + Vector3.up * medusaSocketHeight;
                var collar = RuinsVisual.Part(parts, "Socket " + i, PrimitiveType.Cylinder, socket,
                    new Vector3(.20f, .08f, .20f), jointMaterial);
                collar.localRotation = Quaternion.Euler(direction.z * -18, 0, direction.x * 18);
                tentacles[i] = new Tentacle(parts, "Mechanical tentacle " + i, 33, metalMaterial, jointMaterial);
                generatedMeshes[i] = tentacles[i].Mesh;
                tentacles[i].Tip = RuinsVisual.Part(parts, "Induction tip " + i, PrimitiveType.Sphere,
                    socket, new Vector3(.13f, .2f, .13f), glowMaterial);
            }
        }

        void ShapeTentacle(int index, float windup, float strike)
        {
            var limb = tentacles[index];
            float angle = (index * 45 + 22.5f) * Mathf.Deg2Rad;
            Vector3 radial = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
            Vector3 side = new Vector3(radial.z, 0, -radial.x);
            Vector3 start = radial * medusaSocketRadius + Vector3.up * medusaSocketHeight;
            float length = tentacleLength * (1 + .06f * Mathf.Sin(index * 2.3f));
            Vector3 end = radial * (medusaSocketRadius + .55f) + Vector3.up * Mathf.Max(.16f, medusaSocketHeight - length);
            Vector3 c1 = start + radial * .12f + Vector3.down * .42f;
            Vector3 c2 = end - radial * .27f + Vector3.up * .32f;
            float drift = dead ? 0 : 1;
            end += side * (Mathf.Sin(clock * 2 + index * 1.7f) * .13f * drift);
            end -= velocity * .045f * drift;
            c2 -= velocity * .07f * drift;
            if (windup > 0)
            {
                end = Vector3.Lerp(end, radial * (medusaSocketRadius + .62f) + Vector3.up * (medusaSocketHeight - .17f), windup);
                c2 = Vector3.Lerp(c2, radial * (medusaSocketRadius + 1.15f) + Vector3.up * (medusaSocketHeight - .4f), windup);
            }
            if (strike > 0)
            {
                if (pattern % 2 != 0 && radial.z > -.1f)
                {
                    end = Vector3.Lerp(end, new Vector3(radial.x * 1.8f, .36f, 2.6f + radial.z * .45f), strike);
                    c1 = Vector3.Lerp(c1, start + Vector3.forward * .9f, strike);
                    c2 = Vector3.Lerp(c2, end + Vector3.up * .2f - Vector3.forward * .7f, strike);
                }
                else
                {
                    end = Vector3.Lerp(end, radial * (medusaSocketRadius + .9f) + Vector3.up * .12f, strike);
                    c2 = Vector3.Lerp(c2, end + Vector3.up * .65f, strike);
                }
            }
            if (dead)
            {
                float slack = Mathf.Clamp01(deathTime * 2);
                end = Vector3.Lerp(end, radial * .9f + Vector3.up * .07f, slack);
                c1 = Vector3.Lerp(c1, start + Vector3.down * .6f, slack);
                c2 = Vector3.Lerp(c2, end + Vector3.up * .15f, slack);
            }
            for (int r = 0; r < limb.Path.Length; r++)
            {
                float t = r / (limb.Path.Length - 1f), u = 1 - t;
                Vector3 point = u * u * u * start + 3 * u * u * t * c1 + 3 * u * t * t * c2 + t * t * t * end;
                float wave = Mathf.Sin(t * Mathf.PI) * .065f * drift;
                point += side * (Mathf.Sin(t * 9 - clock * 4 + index * 1.9f) * wave);
                limb.Path[r] = point;
            }
            limb.Apply();
            limb.Tip.localPosition = end;
            Vector3 tipDirection = end - limb.Path[limb.Path.Length - 2];
            if (tipDirection.sqrMagnitude > .0001f) limb.Tip.localRotation = Quaternion.FromToRotation(Vector3.up, tipDirection);
        }

        void InitializeAnimation()
        {
            if ((kind != RuinsMonsterKind.PenitentHusk && kind != RuinsMonsterKind.MourningMatron) || graph.IsValid()) return;
            if (!humanoidAnimator && model) humanoidAnimator = model.GetComponentInChildren<Animator>();
            if (!humanoidAnimator || !idleClip) return;
            humanoidAnimator.applyRootMotion = false;
            humanoidAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            graph = PlayableGraph.Create(kind + " motion");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            mixer = AnimationMixerPlayable.Create(graph, 6);
            var output = AnimationPlayableOutput.Create(graph, "Husk animation", humanoidAnimator);
            output.SetSourcePlayable(mixer);
            AnimationClip[] clips = { idleClip, walkClip, runClip, attackClip, hitClip, deathClip };
            for (int i = 0; i < clips.Length; i++)
            {
                if (!clips[i]) continue;
                clipPlayers[i] = AnimationClipPlayable.Create(graph, clips[i]);
                clipPlayers[i].SetApplyFootIK(false); clipPlayers[i].SetApplyPlayableIK(false);
                graph.Connect(clipPlayers[i], 0, mixer, i); hasClip[i] = true;
            }
            weights[0] = 1; mixer.SetInputWeight(0, 1);
            graph.Play();
        }

        void AnimateHumanoid(float dt)
        {
            if (!graph.IsValid()) { InitializeAnimation(); if (!graph.IsValid()) return; }
            int selected = 0;
            if (dead && hasClip[5]) selected = 5;
            else if (hitTime > .04f && hasClip[4]) selected = 4;
            else if ((motion == RuinsMonsterState.Windup || motion == RuinsMonsterState.Attack) && hasClip[3]) selected = 3;
            else if (motion == RuinsMonsterState.Approach && move01 > .08f)
                selected = kind != RuinsMonsterKind.MourningMatron && move01 > .72f && hasClip[2] ? 2 : hasClip[1] ? 1 : 0;
            float total = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                weights[i] = Mathf.MoveTowards(weights[i], selected == i ? 1 : 0, dt * (dead ? 12 : 9));
                if (hasClip[i]) total += weights[i];
            }
            for (int i = 0; i < weights.Length; i++) mixer.SetInputWeight(i, hasClip[i] ? weights[i] / Mathf.Max(.001f, total) : 0);
            if (hasClip[1]) clipPlayers[1].SetSpeed(kind == RuinsMonsterKind.MourningMatron ? Mathf.Lerp(.65f, .95f, move01) : Mathf.Lerp(.7f, 1.35f, move01));
            if (hasClip[2]) clipPlayers[2].SetSpeed(Mathf.Lerp(.8f, 1.2f, move01));
            if (selected == 3)
            {
                float windupSeconds = kind == RuinsMonsterKind.MourningMatron ? (pattern % 2 == 0 ? 1.2f : 1.3f) : .9f;
                float attackSeconds = kind == RuinsMonsterKind.MourningMatron ? .8f : .65f;
                float impactSeconds = kind == RuinsMonsterKind.MourningMatron ? .35f : .28f;
                float progress;
                if (motion == RuinsMonsterState.Windup)
                    progress = Mathf.Lerp(0, attackAnticipationEnd, Mathf.Clamp01(motionTime / windupSeconds));
                else if (motionTime <= impactSeconds)
                    progress = Mathf.Lerp(attackAnticipationEnd, attackImpactNormalized, Mathf.Clamp01(motionTime / impactSeconds));
                else
                    progress = Mathf.Lerp(attackImpactNormalized, 1, Mathf.InverseLerp(impactSeconds, attackSeconds, motionTime));
                clipPlayers[3].SetTime(attackClip.length * progress); clipPlayers[3].SetSpeed(0);
            }
            if (selected == 4) { clipPlayers[4].SetTime(hitClip.length * (1 - Mathf.Clamp01(hitTime / .3f))); clipPlayers[4].SetSpeed(0); }
            if (selected == 5) { clipPlayers[5].SetTime(Mathf.Min(deathClip.length - .001f, deathTime)); clipPlayers[5].SetSpeed(0); }
        }

        void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
            if (generatedMeshes != null)
                foreach (var mesh in generatedMeshes)
                {
                    if (!mesh) continue;
#if UNITY_EDITOR
                    if (UnityEditor.EditorUtility.IsPersistent(mesh)) continue;
#endif
                    if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
                }
        }

        sealed class Tentacle
        {
            const int Sides = 8;
            public readonly Vector3[] Path;
            public readonly Mesh Mesh;
            public Transform Tip;
            readonly Vector3[] vertices, normals;
            public Tentacle(Transform parent, string name, int rings, Material metal, Material joints)
            {
                Path = new Vector3[rings]; vertices = new Vector3[rings * Sides]; normals = new Vector3[vertices.Length];
                var uvs = new Vector2[vertices.Length];
                var armor = new List<int>(); var hinges = new List<int>();
                for (int r = 0; r < rings; r++)
                    for (int s = 0; s < Sides; s++)
                    {
                        uvs[r * Sides + s] = new Vector2(s / (float)Sides, r / (rings - 1f));
                        if (r == rings - 1) continue;
                        int a = r * Sides + s, b = r * Sides + (s + 1) % Sides, c = a + Sides, d = b + Sides;
                        var triangles = r % 4 == 0 ? hinges : armor;
                        triangles.Add(a); triangles.Add(b); triangles.Add(c);
                        triangles.Add(b); triangles.Add(d); triangles.Add(c);
                    }
                Mesh = new Mesh { name = name + " mesh", subMeshCount = 2 };
                Mesh.MarkDynamic(); Mesh.vertices = vertices; Mesh.normals = normals; Mesh.uv = uvs;
                Mesh.SetTriangles(armor, 0); Mesh.SetTriangles(hinges, 1);
                var existing = parent.Find(name);
                var go = existing ? existing.gameObject : new GameObject(name);
                go.layer = 2; go.transform.SetParent(parent, false);
                go.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity); go.transform.localScale = Vector3.one;
                var filter = go.GetComponent<MeshFilter>(); if (!filter) filter = go.AddComponent<MeshFilter>();
                var renderer = go.GetComponent<MeshRenderer>(); if (!renderer) renderer = go.AddComponent<MeshRenderer>();
                filter.sharedMesh = Mesh; renderer.sharedMaterials = new[] { metal, joints };
            }

            public void Apply()
            {
                Vector3 previous = Vector3.zero;
                for (int r = 0; r < Path.Length; r++)
                {
                    Vector3 tangent = r == Path.Length - 1 ? Path[r] - Path[r - 1] : Path[r + 1] - Path[r];
                    if (tangent.sqrMagnitude < .000001f) tangent = Vector3.down;
                    tangent.Normalize();
                    Vector3 normal = previous.sqrMagnitude > .01f ? Vector3.ProjectOnPlane(previous, tangent).normalized
                        : Vector3.Cross(tangent, Vector3.right).normalized;
                    if (normal.sqrMagnitude < .01f) normal = Vector3.Cross(tangent, Vector3.forward).normalized;
                    previous = normal;
                    Vector3 binormal = Vector3.Cross(tangent, normal);
                    float t = r / (Path.Length - 1f);
                    float radius = Mathf.Lerp(.085f, .043f, t) * (r % 4 == 0 ? .79f : 1.08f);
                    for (int s = 0; s < Sides; s++)
                    {
                        float angle = s * Mathf.PI * 2 / Sides;
                        int v = r * Sides + s;
                        normals[v] = normal * Mathf.Cos(angle) + binormal * Mathf.Sin(angle);
                        vertices[v] = Path[r] + normals[v] * radius;
                    }
                }
                Mesh.SetVertices(vertices); Mesh.SetNormals(normals); Mesh.RecalculateBounds();
            }
        }
    }
}
