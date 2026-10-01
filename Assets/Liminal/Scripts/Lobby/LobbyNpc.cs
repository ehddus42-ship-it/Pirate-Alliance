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
    ///   (armature-only Humanoid FBX files, retargeted onto the model);
    /// - clips play through a Playables mixer: looping states cross-fade, and one-shots (a bow) return to the loop;
    /// - the NPC turns toward a look target or the player, otherwise back to its rest direction.
    /// <see cref="LobbyRoutine"/> drives what ambient officials do.
    /// </summary>
    public sealed class LobbyNpc : MonoBehaviour
    {
        const float Fade = .3f;

        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        readonly List<AnimationClipPlayable> inputs = new List<AnimationClipPlayable>();
        readonly List<string> labels = new List<string>();
        float[] weights = Array.Empty<float>();
        int loop, once = -1;
        Action onceDone;
        float restYaw;
        Vector3? lookTarget;
        bool talking;
        Animator animator;

        public string Character { get; private set; }
        public float ModelHeight { get; private set; }
        public bool Talking => talking;
        public Animator Animator => animator;
        public string Current => once >= 0 ? labels[once] : loop < labels.Count ? labels[loop] : null;
        public bool Has(string label) => labels.IndexOf(label) >= 0;
        public float TurnSpeed { get; set; } = 240;

        public static string ModelPath(string character) => $"LiminalLobby/{character}/{character}";
        public static string ClipPath(string character, string label) => $"LiminalLobby/{character}/{character}@{label}";
        public static string TexturePath(string character) => $"LiminalLobby/{character}/{character}_albedo";

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
                var playable = AnimationClipPlayable.Create(graph, clips[i].clip);
                // A random phase so a row of identical guards does not move in lockstep.
                playable.SetTime(UnityEngine.Random.value * clips[i].clip.length);
                graph.Connect(playable, 0, mixer, i);
                inputs.Add(playable);
                labels.Add(clips[i].label);
            }
            weights = new float[clips.Count];
            weights[0] = 1;
            for (int i = 0; i < clips.Count; i++) mixer.SetInputWeight(i, weights[i]);
            output.SetSourcePlayable(mixer);
            graph.Play();
        }

        /// <summary>Cross-fades to a looping state. Unknown labels fall back to the default loop.</summary>
        public void Play(string label)
        {
            int index = labels.IndexOf(label);
            if (index < 0) index = 0;
            if (index == loop) return;
            loop = index;
            if (once < 0 && index < inputs.Count) Restart(index, keepPhase: true);
        }

        /// <summary>Plays a clip once (a bow, a gesture), then returns to the current loop.</summary>
        public bool PlayOnce(string label, Action done = null)
        {
            int index = labels.IndexOf(label);
            if (index < 0) { done?.Invoke(); return false; }
            once = index;
            onceDone = done;
            Restart(index, keepPhase: false);
            return true;
        }

        void Restart(int index, bool keepPhase)
        {
            if (!keepPhase) inputs[index].SetTime(0);
        }

        /// <summary>Conversation with the player: the talking loop, facing the player.</summary>
        public void Talk(bool on)
        {
            talking = on;
            Play(on ? "talk" : "idle");
        }

        public void LookAt(Vector3? target) => lookTarget = target;

        /// <summary>Rest direction in the parent's space (walkers turn along their path).</summary>
        public void SetRestYaw(float yaw) => restYaw = yaw;

        void Update()
        {
            if (graph.IsValid() && inputs.Count > 0)
            {
                int target = once >= 0 ? once : loop;
                for (int i = 0; i < inputs.Count; i++)
                {
                    if (i != once) Wrap(inputs[i]);
                    weights[i] = Mathf.MoveTowards(weights[i], i == target ? 1 : 0, Time.deltaTime / Fade);
                }
                float sum = 0;
                foreach (var w in weights) sum += w;
                for (int i = 0; i < inputs.Count; i++) mixer.SetInputWeight(i, sum > .001f ? weights[i] / sum : (i == target ? 1 : 0));
                if (once >= 0)
                {
                    var clip = inputs[once].GetAnimationClip();
                    if (!clip || inputs[once].GetTime() >= clip.length - Fade)
                    {
                        once = -1;
                        var done = onceDone; onceDone = null;
                        done?.Invoke();
                    }
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

        static void Wrap(AnimationClipPlayable playable)
        {
            if (!playable.IsValid()) return;
            var clip = playable.GetAnimationClip();
            // The FBX clips are not authored as loops: wrap them by hand.
            if (clip && clip.length > .01f && playable.GetTime() >= clip.length) playable.SetTime(playable.GetTime() % clip.length);
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
    /// What an ambient official does, without any text:
    /// - <see cref="Mode.Station"/>: stands at a post, now and then plays an activity (a guard looks around).
    /// - <see cref="Mode.Chat"/>: faces a partner and alternates talking and listening.
    /// - <see cref="Mode.Patrol"/>: walks a loop of points, pausing at each (and taking a phone call at some).
    /// Everyone notices the player: an official within a couple of metres turns to look; a walker whose way is
    /// blocked stops and waits.
    /// </summary>
    public sealed class LobbyRoutine : MonoBehaviour
    {
        public enum Mode { Station, Chat, Patrol }

        Mode mode;
        LobbyNpc npc, partner;
        string activity;
        Vector3[] path;
        int next;
        float clock, pause, speed;
        bool busy, walking;
        System.Random random;
        const float Notice = 2.3f;

        public Mode Kind => mode;
        public bool Walking => walking;

        public static LobbyRoutine Station(LobbyNpc npc, string activity, int seed)
            => Add(npc, Mode.Station, activity, seed);

        public static LobbyRoutine Chat(LobbyNpc npc, LobbyNpc partner, string activity, bool startTalking, int seed)
        {
            var r = Add(npc, Mode.Chat, activity, seed);
            r.partner = partner;
            r.busy = !startTalking;
            r.clock = 0;
            return r;
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
                    clock -= dt;
                    if (clock <= 0)
                    {
                        busy = !busy;
                        clock = busy ? Range(3f, 6f) : Range(2.5f, 5f);
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
            // Someone in the way: stop, look at them, carry on when they move.
            if (player && toPlayer.magnitude < 1.4f && Vector3.Dot(dir, toPlayer.normalized) > .35f)
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
    /// its face turned away from the body, so it stays a plausible grip when the arm gestures.
    /// </summary>
    public sealed class HeldBoard : MonoBehaviour
    {
        Transform hand, forearm, body;
        float side, height;

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
            transform.SetPositionAndRotation(hand.position - up * (height * .36f) + outward * .045f, Quaternion.LookRotation(outward, up));
        }
    }
}
