using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace AcRoguelike.RuinsBoss
{
    /// <summary>Clip-driven caster, heavy machinery recoil, and the visible electricity that awakens each side unit.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(65)]
    public sealed class RuinsBossRig : MonoBehaviour
    {
        public RuinsBossVisualKind kind;
        public Transform model;
        public Animator humanoidAnimator;
        public AnimationClip idleClip, castClip, hitClip, deathClip;
        public AnimationClip walkClip, runClip;
        public float walkPlaybackSpeed = .75f, runPlaybackSpeed = 1.2f;
        public Material glowMaterial;
        public Material lightningMaterial;
        public float coreHeight = 2.8f, haloRadius = .95f;
        public bool UsesAnimationClips => graph.IsValid();
        public bool UsesLocomotionClips => graph.IsValid() && hasClip[4];
        public bool UsesMechanicalLegs => mechanicalLegs;
        public string ActiveClipName { get; private set; } = "";
        public bool Dormant => dormant;
        public bool ActivationVisible => activationLine && activationLine.enabled;
        public int ActivationCount { get; private set; }
        public int LightningBoltCount => lightning == null ? 0 : lightning.Length;
        public int LightningAnchorCount { get { int count = 0; foreach (var anchor in lightningAnchors) if (anchor) count++; return count; } }
        public int LightningTick => Mathf.FloorToInt(clock * 19);
        public int VisibleLightningBolts { get { int count = 0; if (lightning != null) foreach (var bolt in lightning) if (bolt.Core.enabled) count++; return count; } }

        Transform effects, core, activationTarget;
        RuinsWalkerLegRig mechanicalLegs;
        LineRenderer halo, activationLine;
        readonly LineRenderer[] arcs = new LineRenderer[3];
        readonly Transform[] lightningAnchors = new Transform[16];
        LightningBolt[] lightning;
        static readonly string[] AnchorNames = { "hips", "spine", "spine02", "neck", "head", "headfront",
            "leftarm", "leftforearm", "lefthand", "rightarm", "rightforearm", "righthand", "leftleg", "leftfoot", "rightleg", "rightfoot" };
        static readonly int[] BoltFrom = { 0, 2, 3, 4, 6, 7, 9, 10, 12, 14, 1 };
        static readonly int[] BoltTo = { 2, 3, 4, 5, 7, 8, 10, 11, 13, 15, 0 };
        static readonly Vector3[] AnchorFallback = {
            new Vector3(0, 2.3f, 0), new Vector3(0, 2.65f, 0), new Vector3(0, 3.25f, 0), new Vector3(0, 3.7f, 0),
            new Vector3(0, 4.02f, 0), new Vector3(0, 4.15f, .2f), new Vector3(-.6f, 3.38f, 0), new Vector3(-.9f, 2.95f, .1f),
            new Vector3(-1.1f, 2.52f, .1f), new Vector3(.6f, 3.38f, 0), new Vector3(.9f, 2.95f, .1f), new Vector3(1.1f, 2.52f, .1f),
            new Vector3(-.35f, 1.15f, 0), new Vector3(-.35f, .15f, .1f), new Vector3(.35f, 1.15f, 0), new Vector3(.35f, .15f, .1f) };
        Vector3 modelRest, coreRest;
        Quaternion modelRotation;
        RuinsBossState state;
        float stateTime, speed, clock, hitTime, deathTime, weaponTime, activationTime, activationDuration;
        bool initialized, dead, dormant;
        Material ownedGlow;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        readonly AnimationClipPlayable[] players = new AnimationClipPlayable[6];
        readonly bool[] hasClip = new bool[6];
        readonly float[] weights = new float[6];

        void Awake() => Initialize();
        public void Initialize()
        {
            if (initialized) return;
            if (!model) model = transform.Find("MeshyModel");
            if (model) mechanicalLegs = model.GetComponentInChildren<RuinsWalkerLegRig>(true);
            modelRest = model ? model.localPosition : Vector3.zero;
            modelRotation = model ? model.localRotation : Quaternion.identity;
            if (!glowMaterial)
            {
                ownedGlow = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Storm electric cyan" };
                ownedGlow.SetColor("_BaseColor", new Color(.1f, 2.5f, 4f, 1));
                glowMaterial = ownedGlow;
            }
            effects = Child(transform, "StormEffects");
            core = Child(effects, "ChargeCore");
            coreRest = new Vector3(0, coreHeight, kind == RuinsBossVisualKind.StormSovereign ? .22f : .55f);
            core.localPosition = coreRest;
            halo = Line("Electric halo", 65, .022f, false);
            activationLine = Line("Activation lightning", 13, .085f, true);
            activationLine.enabled = false;
            for (int i = 0; i < arcs.Length; i++) arcs[i] = Line("Coil arc " + i, 9, .028f, false);
            if (kind == RuinsBossVisualKind.StormSovereign) BuildLightning();
            initialized = true;
            if (Application.isPlaying) InitializeAnimation();
            Pose();
        }
        public void RefreshRestPose()
        {
            Initialize(); clock = hitTime = deathTime = weaponTime = 0;
            dead = false; state = RuinsBossState.Idle; stateTime = speed = 0;
            CancelEffects(); Pose();
        }
        static Transform Child(Transform parent, string name)
        {
            var found = parent.Find(name); if (found) return found;
            var child = new GameObject(name).transform; child.SetParent(parent, false); return child;
        }
        LineRenderer Line(string name, int count, float width, bool world)
        {
            var child = Child(effects, name);
            child.gameObject.layer = 2;
            var line = child.GetComponent<LineRenderer>(); if (!line) line = child.gameObject.AddComponent<LineRenderer>();
            line.sharedMaterial = glowMaterial; line.useWorldSpace = world;
            line.positionCount = count; line.widthMultiplier = width;
            line.numCornerVertices = 1; line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }
        public void SetState(RuinsBossState next, float elapsed, float move01 = 0)
        { state = next; stateTime = elapsed; speed = Mathf.Clamp01(move01); }
        public void SetDormant(bool sleeping) { dormant = sleeping; if (initialized) Pose(); }
        public void PlayHit() { if (!dormant && !dead) hitTime = .26f; }
        public void PlayDeath() { dead = true; deathTime = 0; state = RuinsBossState.Dead; CancelEffects(); }
        public void PulseWeapon() { Initialize(); weaponTime = .4f; }
        public void BeginActivation(Transform target, float seconds)
        {
            Initialize(); activationTarget = target; activationTime = 0;
            activationDuration = Mathf.Max(.2f, seconds); ActivationCount++;
            if (activationLine) activationLine.enabled = isActiveAndEnabled && target;
            PoseActivation();
        }
        public void CancelEffects()
        {
            activationTarget = null; activationTime = activationDuration = weaponTime = 0;
            if (activationLine) activationLine.enabled = false;
            if (lightning != null && (dead || dormant))
                foreach (var bolt in lightning) { bolt.Core.enabled = false; bolt.Haze.enabled = false; bolt.Branch.enabled = false; }
        }
        void LateUpdate()
        {
            if (!Application.isPlaying || Time.deltaTime <= 0) return;
            Initialize(); float dt = Time.deltaTime;
            clock += dt; hitTime = Mathf.Max(0, hitTime - dt); weaponTime = Mathf.Max(0, weaponTime - dt);
            if (dead) deathTime += dt;
            if (activationTarget)
            {
                activationTime += dt;
                if (activationTime >= activationDuration) { activationTarget = null; activationLine.enabled = false; }
            }
            Pose(); Animate(dt);
        }
        void Pose()
        {
            if (!initialized) return;
            bool caster = kind == RuinsBossVisualKind.StormSovereign;
            bool casting = state == RuinsBossState.Windup || state == RuinsBossState.Attack || state == RuinsBossState.Awakening;
            float energy = dead ? 0 : dormant ? .08f : casting ? 1 : .35f;
            if (activationTarget) energy = .3f + Mathf.Clamp01(activationTime / Mathf.Max(.01f, activationDuration));
            if (model)
            {
                Vector3 bob = Vector3.zero;
                Quaternion lean = Quaternion.identity;
                if (!dead && !dormant)
                {
                    if (caster) bob.y = Mathf.Sin(clock * 1.45f) * .025f;
                    else
                    {
                        // A skeletal walk already lifts and plants the legs; do not layer a rigid body bounce over it.
                        if (!UsesLocomotionClips && !mechanicalLegs)
                            bob.y = Mathf.Abs(Mathf.Sin(clock * (kind == RuinsBossVisualKind.IronRam ? 17 : 8))) * .038f * speed;
                        float recoil = weaponTime > 0 ? Mathf.Sin(weaponTime / .4f * Mathf.PI) : 0;
                        bob.z = -recoil * .11f;
                        lean = Quaternion.Euler(recoil * -2.5f, 0, UsesLocomotionClips || mechanicalLegs ? 0 : Mathf.Sin(clock * 7) * speed * .65f);
                    }
                }
                model.localPosition = modelRest + bob; model.localRotation = lean * modelRotation;
            }
            // The sovereign's energy clings to her moving body, without the smooth water-like halo.
            halo.enabled = !caster && !dead && (!dormant || activationTarget);
            float radius = haloRadius * (1 + Mathf.Sin(clock * 2) * .035f);
            for (int i = 0; i < halo.positionCount; i++)
            {
                float angle = i / (halo.positionCount - 1f) * Mathf.PI * 2;
                halo.SetPosition(i, coreRest + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, -.3f));
            }
            halo.widthMultiplier = .014f + .02f * energy;
            for (int a = 0; a < arcs.Length; a++)
            {
                var line = arcs[a];
                line.enabled = !caster && !dead && (!dormant || activationTarget) && (casting || weaponTime > 0 || activationTarget);
                float phase = clock * 1.4f + a * Mathf.PI * 2 / arcs.Length;
                Vector3 start = coreRest + new Vector3(Mathf.Cos(phase) * radius, Mathf.Sin(phase) * radius, -.28f);
                Vector3 end = caster ? new Vector3((a - 1) * .6f, 1.05f + (a == 1 ? 0 : .8f), .15f) : coreRest + Vector3.forward * .6f;
                for (int i = 0; i < line.positionCount; i++)
                {
                    float t = i / (line.positionCount - 1f);
                    Vector3 point = Vector3.Lerp(start, end, t);
                    point.x += Mathf.Sin(i * 4.1f + Mathf.Floor(clock * 18) * 2.4f + a) * Mathf.Sin(t * Mathf.PI) * .13f;
                    point.z += Mathf.Sin(i * 2.8f - clock * 12 + a) * Mathf.Sin(t * Mathf.PI) * .07f;
                    line.SetPosition(i, point);
                }
                line.widthMultiplier = .018f + .025f * energy;
            }
            if (caster) PoseLightning(energy);
            PoseActivation();
            if (mechanicalLegs) mechanicalLegs.SetMotion(state, speed, dormant, dead);
        }

        void BuildLightning()
        {
            activationLine.sharedMaterial = lightningMaterial ? lightningMaterial : Application.isPlaying ? HitFeedback.Additive : glowMaterial;
            activationLine.numCornerVertices = 0;
            activationLine.startColor = new Color(.95f, 1, 1, .95f);
            activationLine.endColor = new Color(.2f, .6f, 1, .65f);
            if (model)
            {
                foreach (var candidate in model.GetComponentsInChildren<Transform>(true))
                {
                    string name = candidate.name.ToLowerInvariant();
                    int prefix = name.LastIndexOf(':'); if (prefix >= 0) name = name.Substring(prefix + 1);
                    for (int i = 0; i < AnchorNames.Length; i++)
                        if (name == AnchorNames[i]) lightningAnchors[i] = candidate;
                }
            }
            lightning = new LightningBolt[BoltFrom.Length];
            for (int i = 0; i < lightning.Length; i++)
            {
                var bolt = new LightningBolt(); lightning[i] = bolt;
                bolt.Core = LightningLine("Body lightning " + i + " white core", 9, .016f);
                bolt.Haze = LightningLine("Body lightning " + i + " blue edge", 9, .045f);
                bolt.Branch = LightningLine("Body lightning " + i + " fork", 4, .011f);
                var blue = new MaterialPropertyBlock();
                blue.SetColor("_BaseColor", new Color(.12f, .48f, 1, .45f)); bolt.Haze.SetPropertyBlock(blue);
                bolt.Branch.startColor = new Color(.65f, .91f, 1, .9f);
                bolt.Branch.endColor = new Color(.12f, .42f, 1, 0);
            }
        }

        LineRenderer LightningLine(string name, int points, float width)
        {
            var line = Line(name, points, width, false);
            // The persistent authoring material is assigned by the builder. Runtime fallback supports vertex alpha.
            line.sharedMaterial = lightningMaterial ? lightningMaterial : Application.isPlaying ? HitFeedback.Additive : glowMaterial;
            line.numCornerVertices = 0; line.numCapVertices = 0;
            line.widthCurve = AnimationCurve.Linear(0, 1, 1, .45f);
            return line;
        }

        Vector3 LightningAnchor(int index)
        {
            var anchor = lightningAnchors[index];
            Vector3 point = anchor ? effects.InverseTransformPoint(anchor.position) : AnchorFallback[index];
            // A small outward offset reveals the sparks on the skin rather than drawing a disconnected loop.
            point.z += index == 4 || index == 5 ? .09f : .14f;
            if (index >= 6 && index <= 11) point.x += index <= 8 ? -.065f : .065f;
            return point;
        }

        void PoseLightning(float energy)
        {
            if (lightning == null) return;
            bool active = isActiveAndEnabled && !dead && (!dormant || activationTarget);
            for (int b = 0; b < lightning.Length; b++)
            {
                var bolt = lightning[b];
                int tick = Mathf.FloorToInt(clock * (18 + b % 3));
                int seed = tick * 1543 + b * 7919;
                bool visible = active && Noise(seed + 1) > (energy > .6f ? .08f : .22f);
                bolt.Core.enabled = bolt.Haze.enabled = visible;
                bolt.Branch.enabled = visible && Noise(seed + 17) > .36f;
                if (!visible) continue;
                Vector3 start = LightningAnchor(BoltFrom[b]), end = LightningAnchor(BoltTo[b]);
                Vector3 direction = (end - start).normalized;
                Vector3 side = Vector3.Cross(direction, Vector3.forward).normalized;
                if (side.sqrMagnitude < .01f) side = Vector3.right;
                Vector3 depth = Vector3.Cross(direction, side).normalized;
                float length = Vector3.Distance(start, end);
                float jitter = Mathf.Clamp(length * .15f, .025f, .19f) * Mathf.Lerp(.8f, 1.25f, energy);
                for (int p = 0; p < bolt.Points.Length; p++)
                {
                    float t = p / (bolt.Points.Length - 1f);
                    float envelope = Mathf.Min(1, Mathf.Min(t * 5, (1 - t) * 5));
                    float zig = (p % 2 == 0 ? 1 : -1) * Mathf.Lerp(.35f, 1, Noise(seed + p * 97));
                    bolt.Points[p] = Vector3.Lerp(start, end, t) +
                        side * (zig * jitter * envelope) + depth * ((Noise(seed + p * 137 + 31) - .5f) * jitter * envelope);
                }
                bolt.Core.SetPositions(bolt.Points); bolt.Haze.SetPositions(bolt.Points);
                float pulse = Mathf.Lerp(.65f, 1.3f, Noise(seed + 211));
                bolt.Core.widthMultiplier = (.010f + energy * .010f) * pulse;
                bolt.Haze.widthMultiplier = bolt.Core.widthMultiplier * 2.9f;
                for (int k = 0; k < bolt.Alphas.Length; k++)
                    bolt.Alphas[k] = new GradientAlphaKey(Mathf.Lerp(.36f, 1, Noise(seed + 293 + k * 113)), k / (bolt.Alphas.Length - 1f));
                bolt.Gradient.SetKeys(bolt.Colors, bolt.Alphas);
                bolt.Core.colorGradient = bolt.Haze.colorGradient = bolt.Gradient;
                int junction = 2 + b % 4;
                Vector3 branchStart = bolt.Points[junction];
                Vector3 branchDirection = (side * (Noise(seed + 503) > .5f ? 1 : -1) - direction * .3f).normalized;
                float forkLength = Mathf.Min(.32f, length * .35f) * Mathf.Lerp(.65f, 1.2f, Noise(seed + 541));
                for (int p = 0; p < 4; p++)
                {
                    float t = p / 3f;
                    Vector3 fork = branchStart + branchDirection * (forkLength * t);
                    if (p == 1 || p == 2) fork += direction * ((p == 1 ? 1 : -1) * forkLength * .23f);
                    bolt.Branch.SetPosition(p, fork);
                }
                bolt.Branch.widthMultiplier = bolt.Core.widthMultiplier * .65f;
            }
        }

        static float Noise(int seed)
        {
            unchecked
            {
                uint value = (uint)seed;
                value ^= value >> 16; value *= 0x7feb352d; value ^= value >> 15; value *= 0x846ca68b; value ^= value >> 16;
                return (value & 0xffff) / 65535f;
            }
        }

        sealed class LightningBolt
        {
            public LineRenderer Core, Haze, Branch;
            public readonly Vector3[] Points = new Vector3[9];
            public readonly Gradient Gradient = new Gradient();
            public readonly GradientAlphaKey[] Alphas = new GradientAlphaKey[5];
            public readonly GradientColorKey[] Colors = {
                new GradientColorKey(new Color(.42f, .78f, 1), 0),
                new GradientColorKey(new Color(1, 1, 1), .18f),
                new GradientColorKey(new Color(.92f, .98f, 1), .72f),
                new GradientColorKey(new Color(.26f, .64f, 1), 1) };
        }
        void PoseActivation()
        {
            if (!isActiveAndEnabled || !activationLine || !activationTarget || dead) return;
            bool caster = kind == RuinsBossVisualKind.StormSovereign;
            Vector3 from = caster && lightningAnchors[11] ? effects.TransformPoint(LightningAnchor(11)) : transform.TransformPoint(coreRest);
            Vector3 to = activationTarget.position + Vector3.up * 2;
            Vector3 side = Vector3.Cross((to - from).normalized, Vector3.up).normalized;
            int tick = Mathf.FloorToInt(clock * 19);
            for (int i = 0; i < activationLine.positionCount; i++)
            {
                float t = i / (activationLine.positionCount - 1f);
                Vector3 point = Vector3.Lerp(from, to, t);
                float bend = Mathf.Sin(t * Mathf.PI);
                if (caster)
                {
                    point += side * ((i % 2 == 0 ? 1 : -1) * Mathf.Lerp(.08f, .32f, Noise(tick * 719 + i * 31)) * bend);
                    point.y += (Noise(tick * 353 + i * 101) - .5f) * bend * .3f;
                }
                else
                {
                    point += side * (Mathf.Sin(i * 4.3f + Mathf.Floor(clock * 16) * 1.7f) * bend * .24f);
                    point.y += Mathf.Sin(i * 3.1f - clock * 23) * bend * .17f;
                }
                activationLine.SetPosition(i, point);
            }
            if (caster) activationLine.widthMultiplier = Mathf.Lerp(.045f, .075f, Noise(tick * 887));
        }
        void InitializeAnimation()
        {
            if ((kind != RuinsBossVisualKind.StormSovereign && kind != RuinsBossVisualKind.SiegeWalker) || graph.IsValid()) return;
            if (!humanoidAnimator && model) humanoidAnimator = model.GetComponentInChildren<Animator>();
            if (!humanoidAnimator || !idleClip) return;
            humanoidAnimator.applyRootMotion = false; humanoidAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            graph = PlayableGraph.Create(kind + " skeletal motion"); graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            mixer = AnimationMixerPlayable.Create(graph, players.Length);
            var output = AnimationPlayableOutput.Create(graph, kind.ToString(), humanoidAnimator); output.SetSourcePlayable(mixer);
            AnimationClip[] clips = { idleClip, castClip, hitClip, deathClip, walkClip, runClip };
            for (int i = 0; i < clips.Length; i++)
            {
                if (!clips[i]) continue;
                players[i] = AnimationClipPlayable.Create(graph, clips[i]); players[i].SetApplyFootIK(false); players[i].SetApplyPlayableIK(false);
                graph.Connect(players[i], 0, mixer, i); hasClip[i] = true;
            }
            weights[0] = 1; mixer.SetInputWeight(0, 1); graph.Play();
        }
        void Animate(float dt)
        {
            if (!graph.IsValid()) { InitializeAnimation(); if (!graph.IsValid()) return; }
            bool walker = kind == RuinsBossVisualKind.SiegeWalker;
            int selected = 0;
            if (dead && hasClip[3]) selected = 3;
            else if (!dormant)
            {
                if (hitTime > .025f && hasClip[2]) selected = 2;
                else if (walker && state == RuinsBossState.Attack && speed > .05f && hasClip[5]) selected = 5;
                else if (walker && (state == RuinsBossState.Idle || state == RuinsBossState.Attack) && speed > .05f && hasClip[4]) selected = 4;
                else if ((state == RuinsBossState.Windup || state == RuinsBossState.Attack ||
                    state == RuinsBossState.Awakening || state == RuinsBossState.Recovery) && hasClip[1]) selected = 1;
            }
            AnimationClip selectedClip = selected == 0 ? idleClip : selected == 1 ? castClip : selected == 2 ? hitClip :
                selected == 3 ? deathClip : selected == 4 ? walkClip : runClip;
            ActiveClipName = selectedClip ? selectedClip.name : "";
            float total = 0;
            for (int i = 0; i < weights.Length; i++) { weights[i] = Mathf.MoveTowards(weights[i], selected == i ? 1 : 0, dt * 7); if (hasClip[i]) total += weights[i]; }
            for (int i = 0; i < weights.Length; i++) mixer.SetInputWeight(i, hasClip[i] ? weights[i] / Mathf.Max(.001f, total) : 0);
            if (hasClip[0])
            {
                // A sleeping machine holds its authored idle stance until the awakening beam reaches it.
                players[0].SetSpeed(dormant ? 0 : 1);
                if (dormant) players[0].SetTime(Mathf.Min(.2f, idleClip.length * .5f));
            }
            if (hasClip[4]) players[4].SetSpeed(Mathf.Max(.1f, walkPlaybackSpeed) * Mathf.Lerp(.45f, 1, speed));
            if (hasClip[5]) players[5].SetSpeed(Mathf.Max(.1f, runPlaybackSpeed) * Mathf.Lerp(.7f, 1, speed));
            if (selected == 1)
            {
                float progress = state == RuinsBossState.Windup ? Mathf.Lerp(0, .44f, Mathf.Clamp01(stateTime / 1.35f)) :
                    state == RuinsBossState.Recovery ? Mathf.Lerp(.75f, .999f, Mathf.Clamp01(stateTime / 1.65f)) :
                    state == RuinsBossState.Awakening ? Mathf.Lerp(0, .75f, Mathf.Clamp01(stateTime / 1.6f)) : .45f + Mathf.Sin(stateTime * 1.7f) * .07f;
                players[1].SetTime(castClip.length * progress); players[1].SetSpeed(0);
            }
            if (selected == 2) { players[2].SetTime(hitClip.length * (1 - hitTime / .26f)); players[2].SetSpeed(0); }
            if (selected == 3) { players[3].SetTime(Mathf.Min(deathClip.length - .001f, deathTime)); players[3].SetSpeed(0); }
        }
        void OnEnable()
        {
            // Component disable leaves its renderers active, so restore body electricity explicitly on re-enable.
            if (initialized && lightning != null) Pose();
        }
        void OnDisable()
        {
            CancelEffects();
            if (lightning != null)
                foreach (var bolt in lightning)
                {
                    if (bolt.Core) bolt.Core.enabled = false;
                    if (bolt.Haze) bolt.Haze.enabled = false;
                    if (bolt.Branch) bolt.Branch.enabled = false;
                }
        }
        void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
            if (ownedGlow) { if (Application.isPlaying) Destroy(ownedGlow); else DestroyImmediate(ownedGlow); }
        }
    }
}
