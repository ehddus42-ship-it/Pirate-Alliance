using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace AcRoguelike.Liminal
{
    /// <summary>
    /// A Hunter Association official in the lobby, made with Meshy (see Tools/LiminalLobby/meshy_lobby.py):
    /// - the rigged model is `LiminalLobby/{name}/{name}` and its clips are `LiminalLobby/{name}/{name}@{label}`
    ///   (skinned Humanoid FBX files from the same rig, retargeted onto the model);
    /// - clips play through a Playables mixer. Looping states cross-fade into each other, and each loop also
    ///   cross-fades into its own start at the end, since the library clips' first and last frames differ. One-shots
    ///   (a bow) return to the loop;
    /// - the feet are kept on the ground: most library clips carry the body 7–9 cm higher than the rest pose, so
    ///   the model is lowered each frame until the lower toe sits where it does in the rest pose;
    /// - the NPC turns toward a look target or the player, otherwise back to its rest direction.
    /// <see cref="LobbyRoutine"/> drives what ambient officials do.
    /// </summary>
    public sealed class LobbyNpc : MonoBehaviour
    {
        const float Fade = .3f, LoopBlend = .35f;

        /// <summary>One clip, played by two playables so a loop can cross-fade from its end into its start.</summary>
        sealed class Track
        {
            public string label;
            public AnimationClip clip;
            public AnimationMixerPlayable mixer;
            public readonly AnimationClipPlayable[] players = new AnimationClipPlayable[2];
            public int active;
            public float seam = -1;   // cross-fade progress, or -1 when not crossing the loop seam
            public float weight;      // weight in the main mixer
            public float Length => clip ? clip.length : 0;
            public double Time => players[active].GetTime();
        }

        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        readonly List<Track> tracks = new List<Track>();
        int loop, once = -1;
        float onceEnd;
        Action onceDone;
        float restYaw;
        Vector3? lookTarget;
        bool talking;
        Animator animator;
        Transform model, leftToe, rightToe;
        Vector3 modelBase;
        float restToe, ground;
        bool grounded;

        public string Character { get; private set; }
        public float ModelHeight { get; private set; }
        public bool Talking => talking;
        public Animator Animator => animator;
        public string Current => once >= 0 ? tracks[once].label : loop < tracks.Count ? tracks[loop].label : null;
        public bool Has(string label) => Index(label) >= 0;
        public bool PlayingOnce => once >= 0;
        public float TurnSpeed { get; set; } = 240;

        public static string ModelPath(string character) => $"LiminalLobby/{character}/{character}";
        public static string ClipPath(string character, string label) => $"LiminalLobby/{character}/{character}@{label}";
        public static string TexturePath(string character) => $"LiminalLobby/{character}/{character}_albedo";

        int Index(string label)
        {
            for (int i = 0; i < tracks.Count; i++) if (tracks[i].label == label) return i;
            return -1;
        }

        /// <summary>Spawns an official at a lobby-local position. The first available label is the default loop.</summary>
        public static LobbyNpc Create(Transform parent, string character, string[] clipLabels, Vector3 localPosition, float yaw,
            float height, List<UnityEngine.Object> owned, string objectName = null)
        {
            var root = new GameObject(objectName ?? character);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;
            root.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            var npc = root.AddComponent<LobbyNpc>();
            npc.Character = character;
            npc.restYaw = yaw;
            npc.ModelHeight = height;
            var source = Resources.Load<GameObject>(ModelPath(character));
            GameObject model;
            if (source)
            {
                model = Instantiate(source, root.transform);
                foreach (var c in model.GetComponentsInChildren<Collider>(true)) Destroy(c);
                FitHeight(model.transform, height);
                Skin(model, TexturePath(character), owned);
            }
            else
            {
                // The FBX is missing (for example Git LFS files not pulled): keep the lobby playable with a stand-in.
                model = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Destroy(model.GetComponent<Collider>());
                model.transform.SetParent(root.transform, false);
                model.transform.localPosition = Vector3.up * height * .5f;
                model.transform.localScale = new Vector3(.5f, height * .5f, .5f);
            }
            model.name = "Model";
            var body = root.AddComponent<CapsuleCollider>();
            body.center = Vector3.up * height * .5f; body.radius = .3f; body.height = height;
            npc.animator = model.GetComponent<Animator>();
            npc.model = model.transform;
            npc.modelBase = model.transform.localPosition;
            // Rest-pose toe height (the model is still in its bind pose here), the reference for grounding.
            npc.leftToe = npc.Bone(HumanBodyBones.LeftToes, "LeftToeBase");
            npc.rightToe = npc.Bone(HumanBodyBones.RightToes, "RightToeBase");
            if (npc.leftToe && npc.rightToe)
            {
                npc.restToe = Mathf.Min(npc.leftToe.position.y, npc.rightToe.position.y) - root.transform.position.y;
                npc.grounded = true;
            }
            var clips = new List<(string, AnimationClip)>();
            foreach (var label in clipLabels)
            {
                var clip = FirstClip(ClipPath(character, label));
                if (clip) clips.Add((label, clip));
            }
            npc.Build(clips);
            return npc;
        }

        static AnimationClip FirstClip(string path)
        {
            foreach (var clip in Resources.LoadAll<AnimationClip>(path))
                if (clip && !clip.name.StartsWith("__preview__", StringComparison.Ordinal)) return clip;
            return null;
        }

        void Build(List<(string label, AnimationClip clip)> clips)
        {
            if (!animator || clips.Count == 0) return;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            graph = PlayableGraph.Create("LobbyNpc." + Character);
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(graph, "Animation", animator);
            mixer = AnimationMixerPlayable.Create(graph, clips.Count);
            for (int i = 0; i < clips.Count; i++)
            {
                var track = new Track { label = clips[i].label, clip = clips[i].clip };
                track.mixer = AnimationMixerPlayable.Create(graph, 2);
                for (int k = 0; k < 2; k++)
                {
                    track.players[k] = AnimationClipPlayable.Create(graph, track.clip);
                    graph.Connect(track.players[k], 0, track.mixer, k);
                }
                // A random phase so a row of identical guards does not move in lockstep.
                track.players[0].SetTime(UnityEngine.Random.value * track.Length);
                track.mixer.SetInputWeight(0, 1);
                track.mixer.SetInputWeight(1, 0);
                graph.Connect(track.mixer, 0, mixer, i);
                tracks.Add(track);
            }
            tracks[0].weight = 1;
            for (int i = 0; i < tracks.Count; i++) mixer.SetInputWeight(i, tracks[i].weight);
            output.SetSourcePlayable(mixer);
            graph.Play();
        }

        /// <summary>Cross-fades to a looping state. Unknown labels fall back to the default loop.</summary>
        public void Play(string label)
        {
            int index = Index(label);
            if (index < 0) index = 0;
            loop = index;
        }

        /// <summary>
        /// Plays a clip once (a bow, a gesture), then returns to the current loop. `maxSeconds` cuts a clip with a
        /// long still tail (the library bow stands upright for its last 2.5 s).
        /// </summary>
        public bool PlayOnce(string label, Action done = null, float maxSeconds = 0)
        {
            int index = Index(label);
            if (index < 0) { done?.Invoke(); return false; }
            var track = tracks[index];
            once = index;
            onceEnd = maxSeconds > 0 ? Mathf.Min(track.Length, maxSeconds) : track.Length;
            onceDone = done;
            track.seam = -1;
            track.players[track.active].SetTime(0);
            track.mixer.SetInputWeight(track.active, 1);
            track.mixer.SetInputWeight(1 - track.active, 0);
            return true;
        }

        /// <summary>Conversation with the player: the talking loop, facing the player. A greeting in progress is cut
        /// short so the conversation starts at once.</summary>
        public void Talk(bool on)
        {
            talking = on;
            if (on) StopOnce();
            Play(on ? "talk" : "idle");
        }

        /// <summary>Ends a one-shot early; it fades back into the loop.</summary>
        public void StopOnce()
        {
            if (once < 0) return;
            once = -1;
            var done = onceDone; onceDone = null;
            done?.Invoke();
        }

        public void LookAt(Vector3? target) => lookTarget = target;

        /// <summary>Rest direction in the parent's space (walkers turn along their path).</summary>
        public void SetRestYaw(float yaw) => restYaw = yaw;

        void Update()
        {
            if (graph.IsValid() && tracks.Count > 0)
            {
                float dt = Time.deltaTime;
                int target = once >= 0 ? once : loop;
                float sum = 0;
                for (int i = 0; i < tracks.Count; i++)
                {
                    var track = tracks[i];
                    if (i != once) Seam(track, dt);
                    track.weight = Mathf.MoveTowards(track.weight, i == target ? 1 : 0, dt / Fade);
                    sum += track.weight;
                }
                for (int i = 0; i < tracks.Count; i++) mixer.SetInputWeight(i, sum > .001f ? tracks[i].weight / sum : (i == target ? 1 : 0));
                if (once >= 0 && tracks[once].Time >= onceEnd - Fade)
                {
                    once = -1;
                    var done = onceDone; onceDone = null;
                    done?.Invoke();
                }
            }
            Quaternion want = (transform.parent ? transform.parent.rotation : Quaternion.identity) * Quaternion.Euler(0, restYaw, 0);
            if (lookTarget.HasValue || talking)
            {
                var player = Player;
                Vector3 target = lookTarget ?? (player ? player.position : transform.position + transform.forward);
                Vector3 d = target - transform.position; d.y = 0;
                if (d.sqrMagnitude > .01f) want = Quaternion.LookRotation(d);
            }
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, TurnSpeed * Time.deltaTime);
        }

        /// <summary>
        /// Loops a clip without a pop: near its end the second player starts the clip again and the two cross-fade
        /// over <see cref="LoopBlend"/> seconds (the clips are not authored as seamless loops).
        /// </summary>
        static void Seam(Track track, float dt)
        {
            float length = track.Length;
            if (length < LoopBlend * 3) { if (track.Time >= length) track.players[track.active].SetTime(track.Time % Math.Max(.01f, length)); return; }
            if (track.seam < 0 && track.Time >= length - LoopBlend)
            {
                track.players[1 - track.active].SetTime(0);
                track.seam = 0;
            }
            if (track.seam < 0) return;
            track.seam += dt / LoopBlend;
            if (track.seam >= 1)
            {
                track.active = 1 - track.active;
                track.seam = -1;
                track.mixer.SetInputWeight(track.active, 1);
                track.mixer.SetInputWeight(1 - track.active, 0);
                return;
            }
            float k = Mathf.SmoothStep(0, 1, track.seam);
            track.mixer.SetInputWeight(track.active, 1 - k);
            track.mixer.SetInputWeight(1 - track.active, k);
        }

        void LateUpdate()
        {
            // Ground the feet after the animation has posed the skeleton this frame: lower (or raise) the model until
            // the lower toe sits at its rest-pose height. Smoothed, so a stepping foot does not jolt the body.
            if (!grounded || !model || !leftToe || !rightToe) return;
            float toe = Mathf.Min(leftToe.position.y, rightToe.position.y) - transform.position.y - ground;
            float want = Mathf.Clamp(restToe - toe, -.25f, .1f);
            ground = Mathf.Lerp(ground, want, 1 - Mathf.Exp(-Time.deltaTime * 14));
            model.localPosition = modelBase + Vector3.up * ground;
        }

        void OnDestroy() { if (graph.IsValid()) graph.Destroy(); }

        // ---- shared helpers ---------------------------------------------------------------------------------
        static Transform player;

        /// <summary>The player, cached (the lobby has one).</summary>
        public static Transform Player
        {
            get
            {
                if (!player)
                {
                    var motor = FindFirstObjectByType<PlayerMotor>();
                    player = motor ? motor.transform : null;
                }
                return player;
            }
        }

        public Transform Bone(HumanBodyBones bone, string fallbackName)
        {
            if (animator && animator.isHuman)
            {
                var t = animator.GetBoneTransform(bone);
                if (t) return t;
            }
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == fallbackName || t.name.EndsWith(":" + fallbackName, StringComparison.Ordinal)) return t;
            return null;
        }

        public static float Height(Transform model, float fallback)
        {
            bool any = false; var b = new Bounds();
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any && b.size.y > .3f ? b.size.y : fallback;
        }

        /// <summary>Scales a model so its renderers span `height` metres, feet on the parent's origin.</summary>
        public static void FitHeight(Transform model, float height)
        {
            float h = Height(model, -1);
            if (h > 0) model.localScale *= height / h;
            bool any = false; var b = new Bounds();
            foreach (var r in model.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            if (any && model.parent) model.position += Vector3.up * (model.parent.position.y - b.min.y);
        }

        /// <summary>Gives a Meshy model a URP Lit material with its albedo (the FBX is imported without materials).</summary>
        public static void Skin(GameObject model, string texturePath, List<UnityEngine.Object> owned)
        {
            var texture = Resources.Load<Texture2D>(texturePath);
            var material = LiminalMonsterKit.Lit(Color.white, .2f);
            if (texture) { material.mainTexture = texture; if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture); }
            owned?.Add(material);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var materials = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                r.sharedMaterials = materials;
                if (r is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
            }
        }
    }

    /// <summary>
    /// What an ambient official does. There is no text anywhere, only motion:
    /// - <see cref="Mode.Station"/>: stands at a post, now and then plays an activity (a guard looks around).
    /// - <see cref="Mode.Chat"/>: faces a partner and takes turns talking; one clock drives both, so exactly one of the
    ///   pair talks at a time.
    /// - <see cref="Mode.Patrol"/>: walks a loop of points, pausing at each (and taking a phone call at some).
    /// Everyone notices the player. Within <see cref="Notice"/> metres an official turns to look and greets once
    /// with its "greet" clip (staff bow, guards salute). Greeting again needs the player to step back beyond
    /// <see cref="Rearm"/> metres and a cooldown. A walker whose way is blocked stops and waits.
    /// </summary>
    public sealed class LobbyRoutine : MonoBehaviour
    {
        public enum Mode { Station, Chat, Patrol }

        Mode mode;
        LobbyNpc npc, partner;
        LobbyRoutine leader;   // chat: the partner whose clock drives the turn-taking (null on the leader)
        string activity;
        Vector3[] path;
        int next;
        float clock, pause, speed;
        bool busy, walking;
        System.Random random;
        bool greetArmed = true;
        float lastGreet = -100;
        public const float Notice = 2.3f, Rearm = 3.6f, GreetCooldown = 12f, GreetSeconds = 5.2f;

        public Mode Kind => mode;
        public bool Walking => walking;

        public static LobbyRoutine Station(LobbyNpc npc, string activity, int seed)
            => Add(npc, Mode.Station, activity, seed);

        /// <summary>Two officials in conversation: `first` talks first, then they take turns on one shared clock.</summary>
        public static (LobbyRoutine, LobbyRoutine) ChatPair(LobbyNpc first, string firstActivity, LobbyNpc second, string secondActivity, int seed)
        {
            var a = Add(first, Mode.Chat, firstActivity, seed);
            var b = Add(second, Mode.Chat, secondActivity, seed + 1);
            a.partner = second; b.partner = first;
            a.busy = true;
            a.clock = a.Range(3f, 6f);
            b.leader = a;
            return (a, b);
        }

        public static LobbyRoutine Patrol(LobbyNpc npc, Vector3[] localPath, string pauseActivity, float speed, int seed)
        {
            var r = Add(npc, Mode.Patrol, pauseActivity, seed);
            r.path = localPath;
            r.speed = speed;
            var body = npc.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;
            return r;
        }

        static LobbyRoutine Add(LobbyNpc npc, Mode mode, string activity, int seed)
        {
            var r = npc.gameObject.AddComponent<LobbyRoutine>();
            r.npc = npc;
            r.mode = mode;
            r.activity = activity;
            r.random = new System.Random(seed);
            r.clock = r.Range(1, 5);
            return r;
        }

        float Range(float a, float b) => a + (float)random.NextDouble() * (b - a);

        void Update()
        {
            if (!npc) return;
            float dt = Time.deltaTime;
            var player = LobbyNpc.Player;
            Vector3 toPlayer = player ? player.position - transform.position : Vector3.positiveInfinity;
            toPlayer.y = 0;
            bool near = player && toPlayer.magnitude < Notice;
            if (!player || toPlayer.magnitude > Rearm) greetArmed = true;
            if (near && greetArmed && Time.time - lastGreet > GreetCooldown && npc.Has("greet") && !npc.PlayingOnce)
            {
                greetArmed = false;
                lastGreet = Time.time;
                npc.PlayOnce("greet", null, GreetSeconds);
            }
            switch (mode)
            {
                case Mode.Station:
                    npc.LookAt(near ? player.position : (Vector3?)null);
                    clock -= dt;
                    if (clock <= 0)
                    {
                        busy = !busy;
                        clock = busy ? Range(2.5f, 5f) : Range(5f, 11f);
                    }
                    npc.Play(busy && !near ? activity : "idle");
                    break;
                case Mode.Chat:
                    npc.LookAt(near ? player.position : partner ? partner.transform.position : (Vector3?)null);
                    if (leader) busy = !leader.busy;
                    else
                    {
                        clock -= dt;
                        if (clock <= 0)
                        {
                            busy = !busy;
                            clock = busy ? Range(3f, 6f) : Range(2.5f, 5f);
                        }
                    }
                    npc.Play(busy && !near ? activity : "idle");
                    break;
                case Mode.Patrol:
                    UpdatePatrol(dt, player, toPlayer, near);
                    break;
            }
        }

        void UpdatePatrol(float dt, Transform player, Vector3 toPlayer, bool near)
        {
            if (path == null || path.Length < 2) return;
            // Mid-greeting: stand still and face the player.
            if (npc.PlayingOnce)
            {
                walking = false;
                if (player) npc.LookAt(player.position);
                return;
            }
            if (pause > 0)
            {
                walking = false;
                pause -= dt;
                npc.LookAt(near ? player.position : (Vector3?)null);
                return;
            }
            Vector3 target = transform.parent ? transform.parent.TransformPoint(path[next]) : path[next];
            Vector3 d = target - transform.position; d.y = 0;
            if (d.magnitude < .12f)
            {
                next = (next + 1) % path.Length;
                pause = Range(2.5f, 5.5f);
                // Every other stop is a phone call, the rest a short wait.
                npc.Play(next % 2 == 0 && npc.Has(activity) ? activity : "idle");
                walking = false;
                return;
            }
            Vector3 dir = d.normalized;
            // Someone in the way (ahead and close to the line of travel): stop, look at them, carry on when they move.
            float ahead = Vector3.Dot(dir, toPlayer);
            float aside = (toPlayer - dir * ahead).magnitude;
            if (player && ahead > 0 && ahead < 1.4f && aside < .65f)
            {
                walking = false;
                npc.Play("idle");
                npc.LookAt(player.position);
                return;
            }
            npc.LookAt(null);
            npc.Play("walk");
            walking = true;
            float yaw = Quaternion.LookRotation(transform.parent ? transform.parent.InverseTransformDirection(dir) : dir).eulerAngles.y;
            npc.SetRestYaw(yaw);
            // Walk only once roughly facing the way, so turns read as turns rather than a slide.
            float facing = Vector3.Dot(transform.forward, dir);
            float step = Mathf.Min(d.magnitude, speed * dt * Mathf.Clamp01((facing - .2f) / .6f));
            transform.position += dir * step;
        }
    }

    /// <summary>
    /// A clipboard or file folder held by its top edge, after the concept art. It follows the hand bone in world
    /// space (independent of the rig's bone axes): the board hangs below the hand, tilted a little with the forearm,
    /// its face turned away from the body, so it stays a plausible grip when the arm gestures. It is kept at least
    /// <see cref="Clearance"/> to the side of the body's centre line, so a hand that comes in (a bow) does not push it
    /// into the hip. Runs after <see cref="LobbyNpc"/> has grounded the model.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class HeldBoard : MonoBehaviour
    {
        Transform hand, forearm, body;
        float side, height;
        const float Clearance = .2f;

        public static HeldBoard Attach(LobbyNpc npc, bool leftHand, Vector3 size, Material board, Material clip, Material face, List<UnityEngine.Object> owned)
        {
            var hand = npc.Bone(leftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand, leftHand ? "LeftHand" : "RightHand");
            var forearm = npc.Bone(leftHand ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm, leftHand ? "LeftForeArm" : "RightForeArm");
            if (!hand || !forearm) return null;
            var go = new GameObject(leftHand ? "Clipboard_L" : "Clipboard_R");
            go.transform.SetParent(npc.transform, false);
            var held = go.AddComponent<HeldBoard>();
            held.hand = hand; held.forearm = forearm; held.body = npc.transform;
            held.side = leftHand ? -1 : 1;
            held.height = size.y;
            Part(go.transform, PrimitiveType.Cube, Vector3.zero, size, board);
            // Metal clip at the top edge and the association emblem on the outward face.
            Part(go.transform, PrimitiveType.Cube, new Vector3(0, size.y * .46f, size.z * .6f), new Vector3(size.x * .45f, size.y * .07f, size.z * 1.4f), clip);
            if (face)
            {
                var quad = Part(go.transform, PrimitiveType.Quad, new Vector3(0, -size.y * .04f, size.z * .51f), new Vector3(size.x * .62f, size.x * .62f, 1), face);
                quad.localRotation = Quaternion.Euler(0, 180, 0);
            }
            return held;
        }

        static Transform Part(Transform parent, PrimitiveType type, Vector3 p, Vector3 s, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = p;
            go.transform.localScale = s;
            go.GetComponent<Renderer>().sharedMaterial = m;
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            return go.transform;
        }

        void LateUpdate()
        {
            if (!hand || !forearm || !body) return;
            Vector3 arm = hand.position - forearm.position;
            if (arm.sqrMagnitude < 1e-6f) return;
            arm.Normalize();
            // Mostly upright, leaning a little with the forearm.
            Vector3 up = (Vector3.up * 1.5f - arm).normalized;
            Vector3 outward = Vector3.ProjectOnPlane(body.right * side, up);
            if (outward.sqrMagnitude < 1e-6f) outward = Vector3.ProjectOnPlane(body.forward, up);
            outward.Normalize();
            // Gripped near its top edge: the board hangs below the hand, just outside the leg.
            Vector3 centre = hand.position - up * (height * .36f) + outward * .045f;
            Vector3 sideways = body.right * side;
            float lateral = Vector3.Dot(centre - body.position, sideways);
            if (lateral < Clearance) centre += sideways * (Clearance - lateral);
            transform.SetPositionAndRotation(centre, Quaternion.LookRotation(outward, up));
        }
    }
}
