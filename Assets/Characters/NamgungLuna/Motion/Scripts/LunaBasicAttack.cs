using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace NamgungLuna.Motion
{
    public enum LunaAttackEventKind { Start, Release, End }

    public struct LunaAttackEvent
    {
        public LunaAttackEventKind kind;
        /// <summary>0 = 1타, 1 = 2타, 2 = 3타.</summary>
        public int hit;
        /// <summary>Release: where the orb leaves (palm or lantern), and its direction.</summary>
        public Vector3 position, direction;
    }

    public struct LunaAttackHit
    {
        public int hit;
        public Collider collider;
        public Vector3 point;
    }

    /// <summary>
    /// 남궁루나 기본공격(평타) 3타. 1타: 랜턴 안 든 손(왼손)을 앞으로 뻗어 발사, 2타: 왼손을 안쪽에서 바깥으로 휘둘러 발사,
    /// 3타: 랜턴 든 손(오른손)을 들어올려 발사. 30fps 기준 프레임 데이터(블로킹 v0.2)를 그대로 재생하고, 골반이 먼저·머리가
    /// 늦게 따라오는 오버랩을 줍니다. 몸 전체(Visual) 회전·다리는 <see cref="LunaMoonGait"/> 위에 덮어쓰고(ILunaPoseOverlay),
    /// 팔은 <see cref="LunaArmRig"/>로 움직입니다. 발사 프레임마다 빛 구체(<see cref="LunaLightOrb"/>)를 쏘고 이벤트를 냅니다.
    /// 3-hit basic attack combo for Namgung Luna. Input: <see cref="RequestAttack"/> (LunaMoonMotor: left mouse / J).
    /// Pressing again after the release frame queues the next hit, which starts at the combo cut; otherwise the hit plays
    /// out and the combo resets after <see cref="comboResetTime"/>. Damage is left to the game: subscribe to
    /// <see cref="AttackEvent"/> / <see cref="Hit"/> (this assembly cannot see the game's combat classes).
    /// </summary>
    [DefaultExecutionOrder(0)]
    [DisallowMultipleComponent]
    public class LunaBasicAttack : MonoBehaviour, ILunaPoseOverlay
    {
        public const float Fps = 30f;

        public LunaMoonGait gait;
        public LunaArmRig arms;
        public LunaMoonFx fx;

        [Header("Timing")]
        [Tooltip("재생 배율 (공격 속도). 프레임 비율은 그대로 유지됩니다")]
        public float attackSpeed = 1f;
        [Tooltip("한 타가 끝난 뒤 다음 타로 이어갈 수 있는 시간 (s). 지나면 1타부터")]
        public float comboResetTime = 0.6f;
        [Tooltip("연계 시 다음 타로 넘어가는 블렌드 (프레임)")]
        public float comboBlendFrames = 4f;
        public float blendIn = 0.08f, blendOut = 0.18f;

        [Header("Light orb")]
        public float orbRange = 9f;
        public float orbRadius = 0.12f;
        public LayerMask hitMask = ~0;
        [ColorUsage(false, true)] public Color moonlight = new Color(0.55f, 0.8f, 1.6f, 1f);
        [ColorUsage(false, true)] public Color lanternLight = new Color(1.7f, 1.15f, 0.55f, 1f);

        public event Action<LunaAttackEvent> AttackEvent;
        public event Action<LunaAttackHit> Hit;

        public bool IsAttacking => current >= 0;
        /// <summary>Movement input is ignored until the move-cancel frame of the current hit.</summary>
        public bool LocksMovement => current >= 0 && frame < Clips[current].moveCancel;
        public int CurrentHit => current;
        public float Frame => frame;
        public float Weight => weight;
        public int Releases { get; private set; }
        public int OrbsHit { get; private set; }

        [Serializable]
        public struct ArmKey
        {
            public Vector3 target, pole, clavicle, hand;
        }

        public struct AttackPose
        {
            public string name;
            /// <summary>Whole-body rotation (Euler, degrees) on top of the gait, lift (y) and shift (x, z) in metres.</summary>
            public Vector3 body;
            public float lift;
            public Vector2 shift;
            /// <summary>Leg angles: hip, knee, ankle, splay (LunaMoonGait.LegPose). L = casting side, R = lantern side.</summary>
            public Vector4 legL, legR;
            public ArmKey cast, lantern;
            public float castGlow, lanternGlow;
        }

        public struct Key
        {
            public int frame, pose;
            public Ease ease;
            public string name;
        }

        public enum Ease { InOut, In, Out }

        public sealed class Clip
        {
            public string name;
            public int frames, release, comboFrom, comboCut, moveCancel, stomp;
            public bool fromLantern;
            public float orbSpeed, orbSize;
            public Key[] keys;
        }

        // 프리비즈(블로킹 v0.2)에서 옮긴 키포즈. 랜턴은 오른손(+X), 시전은 왼손(-X).
        // Arm targets/poles are offsets from the shoulder in the root's orientation; clavicle/hand are Euler degrees.
        public static readonly AttackPose[] Poses =
        {
            new AttackPose { name = "READY", body = new Vector3(0f, 0f, 0f), lift = 0f, shift = new Vector2(0f, 0f), legL = new Vector4(-4f, 8f, 40f, 1f), legR = new Vector4(6f, 10f, 38f, 1f),
                cast = new ArmKey { target = new Vector3(-0.023f, -0.185f, 0.162f), pole = new Vector3(-0.139f, -0.647f, -0.231f), clavicle = new Vector3(0f, 0f, 0f), hand = new Vector3(0f, 0f, 0f) },
                lantern = new ArmKey { target = new Vector3(0.031f, -0.2f, 0.162f), pole = new Vector3(0.139f, -0.647f, -0.231f), clavicle = new Vector3(0f, 0f, 0f), hand = new Vector3(0f, 0f, 0f) },
                castGlow = 0f, lanternGlow = 1f },
            new AttackPose { name = "BA1_wind", body = new Vector3(-5.6f, -25.4f, -0.8f), lift = -0.018f, shift = new Vector2(-0.015f, -0.02f), legL = new Vector4(-6f, 10f, 40f, 1f), legR = new Vector4(18f, 40f, 30f, 2f),
                cast = new ArmKey { target = new Vector3(-0.046f, -0.031f, -0.062f), pole = new Vector3(-0.185f, -0.262f, -0.462f), clavicle = new Vector3(0f, -12f, 0f), hand = new Vector3(0f, 0f, 0f) },
                lantern = new ArmKey { target = new Vector3(0.023f, -0.193f, 0.169f), pole = new Vector3(0.154f, -0.647f, -0.193f), clavicle = new Vector3(0f, 0f, 0f), hand = new Vector3(0f, 0f, 0f) },
                castGlow = 0.55f, lanternGlow = 1f },
            new AttackPose { name = "BA1_fire", body = new Vector3(6f, 18.8f, -2.8f), lift = -0.024f, shift = new Vector2(0.015f, 0.03f), legL = new Vector4(-14f, 20f, 48f, 1f), legR = new Vector4(16f, 8f, 30f, 1f),
                cast = new ArmKey { target = new Vector3(0.092f, -0.023f, 0.57f), pole = new Vector3(-0.339f, -0.262f, 0.077f), clavicle = new Vector3(0f, 16f, 0f), hand = new Vector3(0f, 0f, 0f) },
                lantern = new ArmKey { target = new Vector3(0.062f, -0.216f, 0.092f), pole = new Vector3(0.185f, -0.647f, -0.27f), clavicle = new Vector3(0f, 0f, 0f), hand = new Vector3(0f, 0f, 0f) },
                castGlow = 1f, lanternGlow = 1f },
            new AttackPose { name = "BA1_follow", body = new Vector3(7.1f, 20.4f, -2.7f), lift = -0.024f, shift = new Vector2(0.015f, 0.035f), legL = new Vector4(-14f, 22f, 48f, 1f), legR = new Vector4(16f, 8f, 30f, 1f),
                cast = new ArmKey { target = new Vector3(0.1f, -0.031f, 0.586f), pole = new Vector3(-0.339f, -0.262f, 0.077f), clavicle = new Vector3(0f, 18f, 0f), hand = new Vector3(0f, 0f, 0f) },
                lantern = new ArmKey { target = new Vector3(0.062f, -0.216f, 0.085f), pole = new Vector3(0.185f, -0.647f, -0.27f), clavicle = new Vector3(0f, 0f, 0f), hand = new Vector3(0f, 0f, 0f) },
                castGlow = 0.15f, lanternGlow = 1f },
            new AttackPose { name = "BA2_wind", body = new Vector3(-2.4f, 27.9f, -4f), lift = -0.018f, shift = new Vector2(0.005f, 0.005f), legL = new Vector4(-8f, 24f, 50f, 1f), legR = new Vector4(4f, 8f, 36f, 2f),
                cast = new ArmKey { target = new Vector3(0.231f, -0.142f, 0.231f), pole = new Vector3(-0.185f, 0.123f, 0.154f), clavicle = new Vector3(0f, 14f, 0f), hand = new Vector3(0f, 0f, 0f) },
                lantern = new ArmKey { target = new Vector3(0.085f, -0.216f, 0.062f), pole = new Vector3(0.223f, -0.647f, -0.231f), clavicle = new Vector3(0f, 0f, 0f), hand = new Vector3(0f, 0f, 0f) },
                castGlow = 0.6f, lanternGlow = 1f },
            new AttackPose { name = "BA2_fire", body = new Vector3(2.9f, -15.5f, 1.1f), lift = -0.024f, shift = new Vector2(-0.005f, 0.015f), legL = new Vector4(-4f, 14f, 40f, 1f), legR = new Vector4(6f, 12f, 38f, 1f),
                cast = new ArmKey { target = new Vector3(-0.015f, -0.015f, 0.385f), pole = new Vector3(-0.339f, 0.008f, 0f), clavicle = new Vector3(0f, 8f, 0f), hand = new Vector3(0f, 0f, 0f) },
                lantern = new ArmKey { target = new Vector3(0.069f, -0.208f, 0.108f), pole = new Vector3(0.2f, -0.647f, -0.231f), clavicle = new Vector3(0f, 0f, 0f), hand = new Vector3(0f, 0f, 0f) },
                castGlow = 1f, lanternGlow = 1f },
            new AttackPose { name = "BA2_follow", body = new Vector3(4f, -29.6f, -0.3f), lift = -0.024f, shift = new Vector2(-0.015f, 0.005f), legL = new Vector4(-2f, 8f, 36f, 1f), legR = new Vector4(2f, 22f, 48f, -3f),
                cast = new ArmKey { target = new Vector3(-0.308f, -0.046f, 0f), pole = new Vector3(-0.3f, -0.185f, -0.347f), clavicle = new Vector3(0f, -6f, 0f), hand = new Vector3(0f, 0f, 0f) },
                lantern = new ArmKey { target = new Vector3(0.046f, -0.208f, 0.169f), pole = new Vector3(0.169f, -0.647f, -0.231f), clavicle = new Vector3(0f, 0f, 0f), hand = new Vector3(0f, 0f, 0f) },
                castGlow = 0.1f, lanternGlow = 1f },
            new AttackPose { name = "BA3_gather", body = new Vector3(11.8f, -12f, 0.1f), lift = -0.06f, shift = new Vector2(0.01f, -0.025f), legL = new Vector4(6f, 30f, 40f, 2f), legR = new Vector4(14f, 34f, 34f, 2f),
                cast = new ArmKey { target = new Vector3(0.077f, -0.169f, 0.2f), pole = new Vector3(-0.185f, -0.57f, -0.231f), clavicle = new Vector3(0f, 6f, 0f), hand = new Vector3(0f, 0f, 0f) },
                lantern = new ArmKey { target = new Vector3(0.062f, -0.354f, -0.092f), pole = new Vector3(0.223f, -0.647f, -0.347f), clavicle = new Vector3(0f, 0f, -6f), hand = new Vector3(0f, 0f, 0f) },
                castGlow = 0f, lanternGlow = 1.5f },
            new AttackPose { name = "BA3_raise", body = new Vector3(-11.8f, 9f, 7.2f), lift = 0.024f, shift = new Vector2(-0.02f, -0.005f), legL = new Vector4(-2f, 4f, 32f, 1f), legR = new Vector4(20f, 52f, 44f, 3f),
                cast = new ArmKey { target = new Vector3(-0.185f, -0.108f, 0.092f), pole = new Vector3(-0.378f, -0.185f, -0.308f), clavicle = new Vector3(0f, -4f, 0f), hand = new Vector3(0f, 0f, 0f) },
                lantern = new ArmKey { target = new Vector3(-0.046f, 0.401f, 0.095f), pole = new Vector3(0.493f, 0.2f, -0.154f), clavicle = new Vector3(0f, 4f, 24f), hand = new Vector3(0f, 0f, 0f) },
                castGlow = 0f, lanternGlow = 2.3f },
            new AttackPose { name = "BA3_peak", body = new Vector3(-13f, 10f, 8.3f), lift = 0.03f, shift = new Vector2(-0.02f, 0f), legL = new Vector4(-3f, 4f, 30f, 1f), legR = new Vector4(20f, 52f, 46f, 3f),
                cast = new ArmKey { target = new Vector3(-0.193f, -0.1f, 0.085f), pole = new Vector3(-0.378f, -0.185f, -0.308f), clavicle = new Vector3(0f, -4f, 0f), hand = new Vector3(0f, 0f, 0f) },
                lantern = new ArmKey { target = new Vector3(-0.046f, 0.416f, 0.126f), pole = new Vector3(0.493f, 0.2f, -0.154f), clavicle = new Vector3(0f, 4f, 25f), hand = new Vector3(0f, 0f, 0f) },
                castGlow = 0f, lanternGlow = 2.8f },
            new AttackPose { name = "BA3_fire", body = new Vector3(6f, -7.7f, -1.9f), lift = -0.012f, shift = new Vector2(0.01f, 0.045f), legL = new Vector4(-14f, 22f, 50f, 1f), legR = new Vector4(14f, 6f, 28f, 1f),
                cast = new ArmKey { target = new Vector3(-0.169f, -0.139f, 0.046f), pole = new Vector3(-0.378f, -0.185f, -0.308f), clavicle = new Vector3(0f, -6f, 0f), hand = new Vector3(0f, 0f, 0f) },
                lantern = new ArmKey { target = new Vector3(0f, 0.324f, 0.354f), pole = new Vector3(0.493f, 0.123f, 0f), clavicle = new Vector3(0f, -10f, 18f), hand = new Vector3(0f, 0f, 0f) },
                castGlow = 0f, lanternGlow = 3.2f },
            new AttackPose { name = "BA3_follow", body = new Vector3(6f, -9.9f, -2.1f), lift = -0.024f, shift = new Vector2(0.01f, 0.05f), legL = new Vector4(-12f, 18f, 46f, 1f), legR = new Vector4(12f, 8f, 30f, 1f),
                cast = new ArmKey { target = new Vector3(-0.077f, -0.185f, 0.154f), pole = new Vector3(-0.185f, -0.609f, -0.231f), clavicle = new Vector3(0f, 0f, 0f), hand = new Vector3(0f, 0f, 0f) },
                lantern = new ArmKey { target = new Vector3(0.015f, 0.015f, 0.478f), pole = new Vector3(0.339f, -0.262f, 0f), clavicle = new Vector3(0f, -6f, 8f), hand = new Vector3(0f, 0f, 0f) },
                castGlow = 0f, lanternGlow = 1.3f },        };

        static Key K(int frame, int pose, Ease ease, string name) => new Key { frame = frame, pose = pose, ease = ease, name = name };

        public static readonly Clip[] Clips =
        {
            new Clip { name = "1타 손 뻗어 발사", frames = 20, release = 8, comboFrom = 11, comboCut = 13, moveCancel = 12, stomp = 7, orbSpeed = 18f, orbSize = 1f,
                keys = new[] { K(0, 0, Ease.InOut, "준비"), K(5, 1, Ease.InOut, "당김"), K(8, 2, Ease.In, "발사"), K(11, 3, Ease.Out, "팔로스루"), K(20, 0, Ease.InOut, "복귀") } },
            new Clip { name = "2타 휘둘러 발사", frames = 22, release = 9, comboFrom = 12, comboCut = 15, moveCancel = 14, stomp = 8, orbSpeed = 18f, orbSize = 1f,
                keys = new[] { K(0, 0, Ease.InOut, "준비"), K(6, 4, Ease.InOut, "감기"), K(9, 5, Ease.In, "발사"), K(12, 6, Ease.Out, "휘둘러 뻗음"), K(22, 0, Ease.InOut, "복귀") } },
            new Clip { name = "3타 랜턴 들어올려 발사", frames = 30, release = 15, comboFrom = -1, comboCut = -1, moveCancel = 21, stomp = 14, orbSpeed = 14f, orbSize = 1.8f, fromLantern = true,
                keys = new[] { K(0, 0, Ease.InOut, "준비"), K(6, 7, Ease.InOut, "모으기"), K(12, 8, Ease.Out, "들어올림"), K(14, 9, Ease.InOut, "정점"),
                               K(15, 10, Ease.In, "발사"), K(19, 11, Ease.Out, "내림"), K(30, 0, Ease.InOut, "복귀") } },
        };

        // overlap: the body leads by half a frame and the legs by one, the arms keep the authored timing
        const float BodyLead = 0.5f, LegLead = 1f;

        struct Sample
        {
            public Quaternion body;
            public Vector3 offset;
            public LunaMoonGait.LegPose legL, legR;
            public LunaArmRig.ArmPose cast, lantern;
            public float castGlow, lanternGlow;
        }

        int current = -1, last = -1;
        float frame, weight, sinceEnd = 99f, blendFrom = -1f;
        bool queued;
        Sample pose, blendPose;
        Transform palmGlow, lanternGlowT;
        MeshRenderer palmGlowRenderer, lanternGlowRenderer;
        MaterialPropertyBlock block;
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static Mesh quad;

        void Awake()
        {
            if (gait == null) gait = GetComponent<LunaMoonGait>();
            if (arms == null) arms = GetComponent<LunaArmRig>();
            if (fx == null) fx = GetComponent<LunaMoonFx>();
            block = new MaterialPropertyBlock();
            pose = Evaluate(0, 0f);
        }

        void Start()
        {
            // the arm rig exists from its own Awake; the glows hang on its bones
            if (fx != null && fx.glowMaterial != null && arms != null && arms.Built)
            {
                palmGlow = MakeGlow("CastGlow", arms.Left.hand, out palmGlowRenderer);
                palmGlow.position = arms.Palm(arms.Left);
                if (gait != null && gait.lantern != null)
                {
                    lanternGlowT = MakeGlow("LanternGlow", gait.lantern, out lanternGlowRenderer);
                    lanternGlowT.localPosition = gait.lanternCenterLocal;
                }
            }
        }

        /// <summary>Attack input. Starts the next hit, or queues it while a hit is past its release frame.</summary>
        public void RequestAttack()
        {
            if (current < 0)
            {
                int next = sinceEnd <= comboResetTime && last >= 0 && last < Clips.Length - 1 ? last + 1 : 0;
                Begin(next, false);
            }
            else if (current < Clips.Length - 1 && frame >= Clips[current].release - 2) queued = true;
        }

        /// <summary>Stop at once (e.g. a dodge); the body blends back to the gait.</summary>
        public void Cancel()
        {
            if (current < 0) return;
            End(false);
        }

        void Begin(int hit, bool chained)
        {
            if (chained) { blendPose = pose; blendFrom = 0f; } else blendFrom = -1f;
            current = hit;
            frame = 0f;
            queued = false;
            Emit(LunaAttackEventKind.Start, transform.position, transform.forward);
        }

        void End(bool finished)
        {
            Emit(LunaAttackEventKind.End, transform.position, transform.forward);
            last = finished ? current : -1;
            current = -1;
            sinceEnd = 0f;
            queued = false;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (current < 0) sinceEnd += dt;
            else
            {
                Clip clip = Clips[current];
                float before = frame;
                frame += dt * Fps * Mathf.Max(0.05f, attackSpeed);
                if (blendFrom >= 0f) blendFrom += dt * Fps * Mathf.Max(0.05f, attackSpeed);
                if (before < clip.stomp && frame >= clip.stomp) Stomp();
                if (before < clip.release && frame >= clip.release) Release(clip);
                if (queued && clip.comboCut >= 0 && frame >= clip.comboCut) Begin(current + 1, true);
                else if (frame >= clip.frames) End(true);
            }
            float target = current >= 0 ? 1f : 0f;
            weight = Mathf.MoveTowards(weight, target, dt / Mathf.Max(0.01f, target > weight ? blendIn : blendOut));
            if (current >= 0)
            {
                pose = Evaluate(current, frame);
                if (blendFrom >= 0f && blendFrom < comboBlendFrames) pose = Blend(blendPose, pose, Smooth01(blendFrom / comboBlendFrames));
            }
        }

        static float Smooth01(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        static float Apply(Ease ease, float u)
        {
            u = Mathf.Clamp01(u);
            switch (ease)
            {
                case Ease.In: return u * u * u;
                case Ease.Out: return 1f - (1f - u) * (1f - u) * (1f - u);
                default: return u * u * (3f - 2f * u);
            }
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }

        /// <summary>Find the key segment around <paramref name="f"/> and its eased fraction.</summary>
        static void Segment(Clip clip, float f, out int i, out float e)
        {
            Key[] keys = clip.keys;
            f = Mathf.Clamp(f, 0f, clip.frames);
            i = 0;
            while (i < keys.Length - 2 && f > keys[i + 1].frame) i++;
            e = Apply(keys[i + 1].ease, (f - keys[i].frame) / (keys[i + 1].frame - keys[i].frame));
        }

        static ref readonly AttackPose PoseAt(Clip clip, int k) => ref Poses[clip.keys[Mathf.Clamp(k, 0, clip.keys.Length - 1)].pose];

        static LunaArmRig.ArmPose ArmAt(Clip clip, int i, float e, bool lantern)
        {
            ArmKey a0 = lantern ? PoseAt(clip, i - 1).lantern : PoseAt(clip, i - 1).cast;
            ArmKey a = lantern ? PoseAt(clip, i).lantern : PoseAt(clip, i).cast;
            ArmKey b = lantern ? PoseAt(clip, i + 1).lantern : PoseAt(clip, i + 1).cast;
            ArmKey b1 = lantern ? PoseAt(clip, i + 2).lantern : PoseAt(clip, i + 2).cast;
            return new LunaArmRig.ArmPose
            {
                target = CatmullRom(a0.target, a.target, b.target, b1.target, e),
                pole = Vector3.Lerp(a.pole, b.pole, e),
                clavicle = Quaternion.Slerp(Quaternion.Euler(a.clavicle), Quaternion.Euler(b.clavicle), e),
                hand = Quaternion.Slerp(Quaternion.Euler(a.hand), Quaternion.Euler(b.hand), e),
            };
        }

        static LunaMoonGait.LegPose Leg(Vector4 v) => new LunaMoonGait.LegPose(v.x, v.y, v.z, v.w);

        static Sample Evaluate(int hit, float f)
        {
            Clip clip = Clips[hit];
            var s = new Sample();
            Segment(clip, f + BodyLead, out int i, out float e);
            ref readonly AttackPose A = ref PoseAt(clip, i);
            ref readonly AttackPose B = ref PoseAt(clip, i + 1);
            s.body = Quaternion.Slerp(Quaternion.Euler(A.body), Quaternion.Euler(B.body), e);
            Vector2 shift = Vector2.Lerp(A.shift, B.shift, e);
            s.offset = new Vector3(shift.x, Mathf.Lerp(A.lift, B.lift, e), shift.y);

            Segment(clip, f + LegLead, out i, out e);
            s.legL = LunaMoonGait.LegPose.Lerp(Leg(PoseAt(clip, i).legL), Leg(PoseAt(clip, i + 1).legL), e);
            s.legR = LunaMoonGait.LegPose.Lerp(Leg(PoseAt(clip, i).legR), Leg(PoseAt(clip, i + 1).legR), e);

            Segment(clip, f, out i, out e);
            s.cast = ArmAt(clip, i, e, false);
            s.lantern = ArmAt(clip, i, e, true);
            s.castGlow = Mathf.Lerp(PoseAt(clip, i).castGlow, PoseAt(clip, i + 1).castGlow, e);
            s.lanternGlow = Mathf.Lerp(PoseAt(clip, i).lanternGlow, PoseAt(clip, i + 1).lanternGlow, e);
            return s;
        }

        static LunaArmRig.ArmPose Blend(LunaArmRig.ArmPose a, LunaArmRig.ArmPose b, float t) => new LunaArmRig.ArmPose
        {
            target = Vector3.Lerp(a.target, b.target, t), pole = Vector3.Lerp(a.pole, b.pole, t),
            clavicle = Quaternion.Slerp(a.clavicle, b.clavicle, t), hand = Quaternion.Slerp(a.hand, b.hand, t),
        };

        static Sample Blend(Sample a, Sample b, float t) => new Sample
        {
            body = Quaternion.Slerp(a.body, b.body, t),
            offset = Vector3.Lerp(a.offset, b.offset, t),
            legL = LunaMoonGait.LegPose.Lerp(a.legL, b.legL, t),
            legR = LunaMoonGait.LegPose.Lerp(a.legR, b.legR, t),
            cast = Blend(a.cast, b.cast, t),
            lantern = Blend(a.lantern, b.lantern, t),
            castGlow = Mathf.Lerp(a.castGlow, b.castGlow, t),
            lanternGlow = Mathf.Lerp(a.lanternGlow, b.lanternGlow, t),
        };

        // ---- ILunaPoseOverlay (called by LunaMoonGait in LateUpdate) ----

        public void OverlayLegs(ref LunaMoonGait.LegPose left, ref LunaMoonGait.LegPose right)
        {
            if (weight <= 0f) return;
            left = LunaMoonGait.LegPose.Lerp(left, pose.legL, weight);
            right = LunaMoonGait.LegPose.Lerp(right, pose.legR, weight);
        }

        public void OverlayBody(ref Quaternion rotation, ref Vector3 offset)
        {
            if (weight <= 0f) return;
            rotation = rotation * Quaternion.Slerp(Quaternion.identity, pose.body, weight);
            offset += pose.offset * weight;
        }

        public void AfterBodyPosed()
        {
            if (arms == null) return;
            arms.Pose(pose.cast, pose.lantern, weight);
            arms.FollowLantern();
            UpdateGlows();
        }

        // ---- release, effects ----

        void Release(Clip clip)
        {
            Vector3 origin = clip.fromLantern && gait != null && gait.lantern != null
                ? gait.lantern.TransformPoint(gait.lanternCenterLocal)
                : arms != null && arms.Built ? arms.Palm(arms.Left) : transform.position + Vector3.up * 1.2f + transform.forward * 0.4f;
            Vector3 direction = transform.forward;
            Releases++;
            int hit = current;
            Color color = clip.fromLantern ? lanternLight : moonlight;
            LunaLightOrb.Launch(origin, direction, clip.orbSpeed, orbRange, orbRadius, clip.orbSize, color,
                fx != null ? fx.glowMaterial : null, fx != null ? fx.trailMaterial : null, hitMask, transform,
                (collider, point) => OnOrbHit(hit, collider, point));
            if (fx != null)
            {
                fx.Flash(clip.fromLantern ? 1f : 0.55f);
                fx.Sparkle(origin, clip.fromLantern ? 18 : 8, 0.12f, clip.fromLantern ? 1.6f : 1f);
            }
            Emit(LunaAttackEventKind.Release, origin, direction);
        }

        void Stomp()
        {
            if (fx == null || gait == null) return;
            // the lead (lantern-side) toe touches down before the cast
            Transform toe = gait.right.ankle != null ? gait.right.ankle : transform;
            Vector3 ground = transform.InverseTransformPoint(toe.position);
            ground.y = gait.groundLevel;
            fx.Ripple(transform.TransformPoint(ground), current == 2 ? 0.8f : 0.5f, current == 2 ? 1.2f : 0.7f);
        }

        void OnOrbHit(int hit, Collider collider, Vector3 point)
        {
            OrbsHit++;
            if (fx != null) fx.Sparkle(point, 10, 0.15f, 1.4f);
            Hit?.Invoke(new LunaAttackHit { hit = hit, collider = collider, point = point });
        }

        void Emit(LunaAttackEventKind kind, Vector3 position, Vector3 direction)
        {
            AttackEvent?.Invoke(new LunaAttackEvent { kind = kind, hit = current, position = position, direction = direction });
        }

        Transform MakeGlow(string name, Transform parent, out MeshRenderer renderer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = fx.glowMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
            return go.transform;
        }

        void UpdateGlows()
        {
            Camera cam = Camera.main;
            float w = weight;
            Glow(palmGlow, palmGlowRenderer, pose.castGlow * w, 0.16f, moonlight, cam);
            // the lantern burns at 1 already: only the charge above that shows
            Glow(lanternGlowT, lanternGlowRenderer, Mathf.Max(0f, pose.lanternGlow - 1f) * w, 0.22f, lanternLight, cam);
        }

        void Glow(Transform t, MeshRenderer r, float level, float size, Color color, Camera cam)
        {
            if (t == null) return;
            r.enabled = level > 0.02f;
            if (!r.enabled) return;
            if (cam != null) t.rotation = Quaternion.LookRotation(t.position - cam.transform.position, cam.transform.up);
            t.localScale = Vector3.one * (size * (0.5f + 0.5f * Mathf.Min(level, 2.5f)) / Mathf.Max(1e-4f, t.parent.lossyScale.x));
            block.Clear();
            block.SetColor(ColorId, color * (0.6f * level));
            r.SetPropertyBlock(block);
        }

        internal static Mesh Quad()
        {
            if (quad != null) return quad;
            quad = new Mesh { name = "LunaAttackQuad" };
            quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            quad.RecalculateNormals();
            quad.RecalculateBounds();
            return quad;
        }
    }
}
