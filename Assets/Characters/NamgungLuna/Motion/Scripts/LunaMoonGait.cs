using System;
using UnityEngine;

namespace NamgungLuna.Motion
{
    /// <summary>Movement state the gait reads every frame. LunaMoonMotor implements it; any other mover can too.</summary>
    public interface ILunaMotionSource
    {
        /// <summary>World velocity (m/s), vertical included.</summary>
        Vector3 Velocity { get; }
        bool IsGrounded { get; }
        /// <summary>Running at full pace: the gait uses its fast (sprint) motion.</summary>
        bool IsSprinting { get; }
        /// <summary>0..1 through a dash (dodge), negative otherwise. The gait plays its floating leap meanwhile.</summary>
        float DashProgress { get; }
    }

    /// <summary>Hip → knee → ankle chain. The boots hang under the ankle; the legs themselves are hidden by the dress.</summary>
    [Serializable]
    public class LunaLeg
    {
        public Transform hip, knee, ankle;
        [Tooltip("부츠 밑창 윤곽점 (ankle local). 어느 각도로 발끝을 세워도 가장 낮은 점을 찾는 데 씁니다.")]
        public Vector3[] solePoints = new Vector3[0];
    }

    public enum LunaGaitEventKind { Step, Settle, Jump, Land, SprintStart, Dash }

    public struct LunaGaitEvent
    {
        public LunaGaitEventKind kind;
        /// <summary>Ground point under the foot (world).</summary>
        public Vector3 position;
        /// <summary>About 0.3 for a soft walking step up to 1.5 for a landing.</summary>
        public float strength;
        public float sprint;
    }

    /// <summary>One speed tier of the moon gait. Each "bound" is one toe touch followed by a floating glide.</summary>
    [Serializable]
    public struct LunaGaitProfile
    {
        [Tooltip("발끝이 한 번 닿고 다음에 닿을 때까지 (s)")]
        public float period;
        [Tooltip("한 번 떠오를 때의 최고 높이 (m)")]
        public float arc;
        [Tooltip("발끝이 닿는 순간 지면과의 간격 (m). 0이면 살짝 닿음")]
        public float contactGap;
        [Tooltip("앞으로 숙이는 각도 (도)")]
        public float lean;
        [Tooltip("다리 앞뒤 벌림 배율 (달리기 = 1)")]
        public float stride;
        [Tooltip("뒤로 접히는 다리의 무릎 각도 (도)")]
        public float kneeFold;

        public static LunaGaitProfile Lerp(LunaGaitProfile a, LunaGaitProfile b, float t)
        {
            return new LunaGaitProfile
            {
                period = Mathf.Lerp(a.period, b.period, t),
                arc = Mathf.Lerp(a.arc, b.arc, t),
                contactGap = Mathf.Lerp(a.contactGap, b.contactGap, t),
                lean = Mathf.Lerp(a.lean, b.lean, t),
                stride = Mathf.Lerp(a.stride, b.stride, t),
                kneeFold = Mathf.Lerp(a.kneeFold, b.kneeFold, t),
            };
        }
    }

    /// <summary>
    /// 원신 콜롬비나식 이동 모션. 발끝을 세운 채 살짝 떠 있는 대기, 한 번 발끝을 대면 달 표면처럼 길게 떠서 나아가는 걸음,
    /// 발끝이 닿을 때마다 달빛 물결, 크게 숙이는 질주(기본 이동), 다리를 접고 떠올랐다 내려오는 도약(대쉬)을
    /// 리깅 없는 남궁루나 모델에 절차적으로 입힙니다.
    /// Columbina-style (Genshin Impact) locomotion for the unrigged Namgung Luna model:
    /// - idle: hovers a few centimetres up on pointed toes with a slow bob,
    /// - walk/run: low-gravity bounds; one toe touches, the body floats forward on a long arc with the legs held in a split
    ///   (lead leg reaching, trailing knee folded) and the next toe touches,
    /// - sprint: deep forward lean, longer and flatter bounds, a burst when it starts,
    /// - dash (dodge): while the root slides along the floor the body floats up on an arc, dives forward, tucks the legs
    ///   and reaches them down to land (the old jump leap; falls off ledges use the same air pose).
    /// The whole body (Visual) carries the hover, lean and banking; the hidden leg chain swings the boots and the Magica
    /// leg colliders so the skirt is kicked from inside. The lantern swings as a pendulum and the full moon trails behind.
    /// Reads movement from an <see cref="ILunaMotionSource"/> on the same object, or from the transform's own motion.
    /// </summary>
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public class LunaMoonGait : MonoBehaviour
    {
        [Header("Rig (Tools > Namgung Luna > Setup Magica Cloth 가 연결)")]
        [Tooltip("몸 전체 (떠오름·기울임). 이 오브젝트의 자식")]
        public Transform visual;
        public LunaLeg left = new LunaLeg();
        public LunaLeg right = new LunaLeg();
        [Tooltip("랜턴 손잡이 피벗 (Visual 자식)")]
        public Transform lantern;
        [Tooltip("손잡이에서 랜턴 무게중심 (lantern local)")]
        public Vector3 lanternCenterLocal = new Vector3(0f, -0.17f, 0f);
        [Tooltip("뒤에 떠 있는 보름달 (이 오브젝트의 자식, Visual 밖)")]
        public Transform moon;
        [Tooltip("몸을 기울일 때의 회전 중심 (root local, 골반 높이)")]
        public Vector3 leanPivot = new Vector3(0f, 0.9f, 0f);
        [Tooltip("지면 높이 (root local y). CharacterController는 skinWidth만큼 떠 있으므로 모터가 맞춥니다")]
        public float groundLevel = 0f;

        [Header("Speed tiers (m/s)")]
        public float walkSpeed = 2.0f;
        public float runSpeed = 4.2f;
        public float sprintSpeed = 6.8f;

        [Header("Hover")]
        [Tooltip("대기 중 발끝과 지면 사이 (m)")]
        public float hoverHeight = 0.045f;
        public float hoverBob = 0.014f;
        [Tooltip("위아래로 떠오르는 주기 (Hz)")]
        public float hoverBobRate = 0.42f;

        [Header("Gait")]
        public LunaGaitProfile walk = new LunaGaitProfile { period = 0.5f, arc = 0.035f, contactGap = 0.012f, lean = 3f, stride = 0.42f, kneeFold = 26f };
        public LunaGaitProfile run = new LunaGaitProfile { period = 0.6f, arc = 0.2f, contactGap = 0f, lean = 9f, stride = 1f, kneeFold = 48f };
        public LunaGaitProfile sprint = new LunaGaitProfile { period = 0.56f, arc = 0.15f, contactGap = 0f, lean = 22f, stride = 1.15f, kneeFold = 56f };
        [Tooltip("걷기 한 걸음 거리 (m). 걷는 속도에서는 이 거리로 박자를 정합니다")]
        public float walkStepLength = 0.95f;
        [Tooltip("공중 체공감: 1보다 크면 꼭대기에서 더 오래 머뭅니다")]
        public float hangShape = 2.2f;

        [Header("Body")]
        public float maxBank = 14f;
        [Tooltip("회전 속도(도/s) × 속도(m/s) 당 옆 기울임")]
        public float bankGain = 0.012f;
        [Tooltip("가속(m/s²) 당 앞뒤 기울임 (도)")]
        public float accelLean = 1.1f;
        public float swayRoll = 2f;
        public float swayYaw = 2.5f;
        [Tooltip("달리기 시작 때 앞으로 쏠리는 양 (도/s 충격)")]
        public float sprintLunge = 30f;

        [Header("Dash (dodge)")]
        [Tooltip("대쉬 중 몸이 떠오르는 최고 높이 (m). 실제 위치(루트)는 바닥에 있고 몸만 뜹니다")]
        public float dashLift = 0.32f;
        [Tooltip("대쉬 중 앞으로 엎드리는 각도 (도)")]
        public float dashDive = 14f;
        [Tooltip("대쉬 시작 때 앞으로 쏠리는 양 (도/s 충격)")]
        public float dashLunge = 60f;
        [Tooltip("도약 모션 길이 (s). 실제 대쉬가 더 짧아도(게임 기본 0.19 s) 몸은 이만큼 떠서 납니다")]
        public float dashLeapSeconds = 0.3f;

        [Header("Moon / lantern")]
        [Tooltip("달이 따라오는 빠르기 (Hz)")]
        public float moonFollow = 1.1f;
        [Tooltip("이동 중 달이 뒤로 처지는 거리 (m)")]
        public float moonTrail = 0.25f;
        public float moonBob = 0.03f;
        [Tooltip("달 자전 (도/s)")]
        public float moonSpin = 7f;
        [Tooltip("랜턴 기울기가 안정되는 시간 (s). 손에 든 랜턴의 작은 관성만 유지합니다")]
        [Min(0.05f)] public float lanternSmoothTime = 0.24f;
        [Tooltip("손잡이 기준 랜턴의 최대 기울기 (도)")]
        [Range(0f, 45f)] public float lanternMaxAngle = 14f;

        public event Action<LunaGaitEvent> GaitEvent;

        // diagnostics (tests and debugging)
        public float Speed => speed;
        public float MoveWeight => moveW;
        public float RunWeight => runW;
        public float SprintWeight => sprintW;
        public bool IsDashing => dashProgress >= 0f;
        /// <summary>Current dash lift of the body (m).</summary>
        public float DashLiftNow { get; private set; }
        public float AirWeight => airW;
        public float Phase => psi;
        public float BodyPitch { get; private set; }
        /// <summary>Lowest boot point above <see cref="groundLevel"/> (m).</summary>
        public float LowestSoleHeight { get; private set; }
        public int StepCount { get; private set; }

        struct LegPose
        {
            public float hip, knee, ankle, splay;

            public LegPose(float hip, float knee, float ankle, float splay = 0f)
            {
                this.hip = hip; this.knee = knee; this.ankle = ankle; this.splay = splay;
            }

            public static LegPose Lerp(LegPose a, LegPose b, float t)
            {
                return new LegPose(Mathf.Lerp(a.hip, b.hip, t), Mathf.Lerp(a.knee, b.knee, t), Mathf.Lerp(a.ankle, b.ankle, t), Mathf.Lerp(a.splay, b.splay, t));
            }
        }

        // Idle: legs together on pointed toes, the left knee eased forward (Columbina's resting float).
        static readonly LegPose IdleL = new LegPose(5f, 11f, 40f, 1f);
        static readonly LegPose IdleR = new LegPose(-3f, 4f, 35f, 1f);
        // Dash leap (and falls): tucked on the way up, reaching down on the way down.
        // (kept modest: the dress is narrow in front, a higher knee would push through the underskirt)
        static readonly LegPose RiseL = new LegPose(20f, 52f, 44f, 3f);
        static readonly LegPose RiseR = new LegPose(8f, 32f, 40f, 3f);
        static readonly LegPose FallL = new LegPose(9f, 8f, 36f, 1f);
        static readonly LegPose FallR = new LegPose(-3f, 18f, 42f, 1f);

        ILunaMotionSource source;
        Vector3 lastPosition, planarVelocity, smoothedAccel;
        float speed, lastYaw, yawRate, verticalVelocity;
        float psi;                       // gait phase, two bounds per cycle: the left toe touches at 0, the right at 1
        float moveW, runW, sprintW, airW, riseW;
        // grounded: the root stands on the floor; contact: the feet do (not during a dash leap)
        bool grounded = true, contact = true, wasContact = true, sprinting, wasSprinting, moving;
        float dashProgress = -1f, leapTime;
        bool sourceDashing, leaping;
        float airTime, airOffset, lastOffset, fallSpeed;
        float dip, dipVelocity, lunge, lungeVelocity, clock;
        int pendingStepFoot = -1;
        Quaternion hipRestL, kneeRestL, ankleRestL, hipRestR, kneeRestR, ankleRestR, lanternRest;
        Vector3 lanternRestDir, lanternGravityRest, lanternTilt, lanternTiltVelocity, lanternLastPosition;
        bool lanternReady, initialized;
        Vector3 moonRestRoot, moonPosition, moonVelocity;
        Quaternion moonRestRotation;
        float moonSpinAngle;

        void Awake()
        {
            source = GetComponent<ILunaMotionSource>();
            if (visual == null) visual = transform;
            if (left.hip != null) { hipRestL = left.hip.localRotation; kneeRestL = left.knee.localRotation; ankleRestL = left.ankle.localRotation; }
            if (right.hip != null) { hipRestR = right.hip.localRotation; kneeRestR = right.knee.localRotation; ankleRestR = right.ankle.localRotation; }
            if (lantern != null)
            {
                lanternRest = lantern.localRotation;
                lanternRestDir = (lanternRest * lanternCenterLocal).normalized;
                lanternGravityRest = lantern.parent != null ? lantern.parent.InverseTransformDirection(Vector3.down) : Vector3.down;
            }
            if (moon != null)
            {
                moonRestRoot = transform.InverseTransformPoint(moon.position);
                moonRestRotation = Quaternion.Inverse(transform.rotation) * moon.rotation;
            }
        }

        void OnEnable() => ResetState();

        /// <summary>Forget all motion history, e.g. after a teleport.</summary>
        public void ResetState()
        {
            lastPosition = transform.position;
            lastYaw = transform.eulerAngles.y;
            planarVelocity = smoothedAccel = Vector3.zero;
            speed = yawRate = 0f;
            moveW = runW = sprintW = airW = 0f;
            dip = dipVelocity = lunge = lungeVelocity = 0f;
            moving = false;
            lanternReady = false;
            initialized = false;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || visual == null) return;
            clock += dt;
            ReadMotion(dt);
            UpdateWeights(dt);

            LunaGaitProfile p = CurrentProfile();
            AdvancePhase(dt, p);
            PoseLegs(p);
            PoseBody(dt, p);
            UpdateLantern(dt);
            UpdateMoon(dt);
            FlushEvents();
            initialized = true;
        }

        void ReadMotion(float dt)
        {
            Vector3 position = transform.position;
            bool teleported = initialized && (position - lastPosition).sqrMagnitude > 9f;
            if (teleported)
            {
                // Room changes are not acceleration. Discard the old movement and lantern history.
                planarVelocity = smoothedAccel = Vector3.zero;
                lanternReady = false;
            }
            Vector3 velocity;
            if (source != null)
            {
                velocity = source.Velocity;
                grounded = source.IsGrounded;
            }
            else
            {
                velocity = initialized && !teleported ? (position - lastPosition) / dt : Vector3.zero;
                grounded = true;
            }
            // The leap runs on its own clock from the moment a dash starts, so a short gameplay dash still reads as a leap.
            bool dashNow = source != null && source.DashProgress >= 0f;
            if (dashNow && !sourceDashing) { leaping = true; leapTime = 0f; }
            sourceDashing = dashNow;
            if (leaping)
            {
                leapTime += dt;
                if (leapTime >= dashLeapSeconds && !dashNow) leaping = false;
            }
            dashProgress = leaping ? Mathf.Clamp01(leapTime / Mathf.Max(0.05f, dashLeapSeconds)) : -1f;
            contact = grounded && dashProgress < 0f;
            lastPosition = position;
            verticalVelocity = velocity.y;
            if (!grounded) fallSpeed = Mathf.Max(fallSpeed, -velocity.y);

            Vector3 planar = Vector3.ProjectOnPlane(velocity, Vector3.up);
            // Derived velocities are noisy (variable frame times); a motor's are already smooth.
            if (source == null) planar = Vector3.Lerp(planarVelocity, planar, 1f - Mathf.Exp(-14f * dt));
            Vector3 accel = (planar - planarVelocity) / dt;
            smoothedAccel = teleported ? Vector3.zero : Vector3.Lerp(smoothedAccel, accel, 1f - Mathf.Exp(-8f * dt));
            planarVelocity = planar;
            speed = planar.magnitude;

            float yaw = transform.eulerAngles.y;
            float rate = initialized ? Mathf.DeltaAngle(lastYaw, yaw) / dt : 0f;
            yawRate = Mathf.Lerp(yawRate, rate, 1f - Mathf.Exp(-10f * dt));
            lastYaw = yaw;

            sprinting = source != null ? source.IsSprinting : speed > (runSpeed + sprintSpeed) * 0.5f;
        }

        void UpdateWeights(float dt)
        {
            float moveTarget = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.9f, speed));
            moveW = Mathf.MoveTowards(moveW, moveTarget, dt * (moveTarget > moveW ? 7f : 4f));
            runW = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(walkSpeed * 0.9f, runSpeed * 0.92f, speed));
            float sprintTarget = sprinting && speed > walkSpeed ? 1f : 0f;
            sprintW = Mathf.MoveTowards(sprintW, sprintTarget, dt * (sprintTarget > sprintW ? 5f : 2.5f));
            bool dashing = dashProgress >= 0f;
            airW = Mathf.MoveTowards(airW, contact ? 0f : 1f, dt * (contact ? 6f : dashing ? 14f : 9f));
            // a dash leap tucks the legs for its first third, then reaches them down to land
            riseW = dashing
                ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.65f, dashProgress))
                : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-1.5f, 1.5f, verticalVelocity));

            // events from state changes
            if (wasContact && !contact)
            {
                airTime = 0f;
                fallSpeed = 0f;
                airOffset = lastOffset;
                if (dashing)
                {
                    lungeVelocity += dashLunge;
                    Emit(LunaGaitEventKind.Dash, FeetCenter(), 1.3f);
                }
                else if (verticalVelocity > 1f) Emit(LunaGaitEventKind.Jump, FeetCenter(), 0.9f);
            }
            if (!contact) airTime += dt;
            if (!wasContact && contact && airTime > 0.15f)
            {
                float strength = Mathf.Clamp(Mathf.Max(fallSpeed / 6f, 0.8f), 0.6f, 1.5f);
                dipVelocity -= 0.4f * strength;
                Emit(LunaGaitEventKind.Land, FeetCenter(), strength);
                psi = 0f;   // the left toe has just touched
            }
            if (sprinting && !wasSprinting && speed > walkSpeed * 0.6f)
            {
                lungeVelocity += sprintLunge;
                Emit(LunaGaitEventKind.SprintStart, FeetCenter(), 1.2f);
            }
            if (!moving && moveW > 0.25f && contact)
            {
                moving = true;
                psi = 0f;   // first push from the left toe; the right leg leads the first glide
                Emit(LunaGaitEventKind.Step, FeetCenter(), 0.5f);
            }
            else if (moving && moveW < 0.2f)
            {
                moving = false;
                if (contact) Emit(LunaGaitEventKind.Settle, FeetCenter(), 0.45f);
            }
            wasContact = contact;
            wasSprinting = sprinting;
        }

        LunaGaitProfile CurrentProfile()
        {
            LunaGaitProfile w = walk;
            // Below run speed the cadence follows the speed, so slow steps stay short and soft.
            w.period = Mathf.Clamp(walkStepLength / Mathf.Max(speed, 0.3f), walk.period * 0.85f, walk.period * 1.8f);
            return LunaGaitProfile.Lerp(LunaGaitProfile.Lerp(w, run, runW), sprint, sprintW * runW);
        }

        void AdvancePhase(float dt, LunaGaitProfile p)
        {
            if (moveW <= 0.02f || airW > 0.5f) return;
            float before = psi;
            psi += dt / Mathf.Max(0.2f, p.period);
            if (Mathf.FloorToInt(psi) != Mathf.FloorToInt(before))
            {
                int foot = Mathf.FloorToInt(psi) % 2 == 0 ? 0 : 1;   // the left toe touches at even counts
                if (moveW > 0.4f && contact) pendingStepFoot = foot;
            }
            if (psi >= 2f) psi -= 2f;
        }

        // Smoothed square wave: +1 while this leg leads the glide, -1 while it trails. The swap happens around the touch.
        static float Lead(float phase, float sharpness)
        {
            const float delay = 0.06f;   // the touching leg stays forward until the toe is down
            return -(float)Math.Tanh(sharpness * Mathf.Sin(Mathf.PI * (phase - delay))) / (float)Math.Tanh(sharpness);
        }

        LegPose GaitLeg(float lead, bool swingingForward, LunaGaitProfile p)
        {
            var trail = new LegPose(-14f * p.stride, p.kneeFold, 48f);
            var reach = new LegPose(22f * p.stride, 6f, 32f);
            LegPose pose = LegPose.Lerp(trail, reach, (lead + 1f) * 0.5f);
            float through = 1f - lead * lead;   // 1 while the leg passes under the body
            pose.knee += through * (swingingForward ? 26f : 6f) * p.stride;
            pose.ankle += through * 6f;
            return pose;
        }

        void PoseLegs(LunaGaitProfile p)
        {
            if (left.hip == null || right.hip == null) return;
            const float sharpness = 2.2f;
            float leadL = Lead(psi, sharpness), leadR = -leadL;
            float nextL = Lead(psi + 0.02f, sharpness);
            LegPose gaitL = GaitLeg(leadL, nextL > leadL, p);
            LegPose gaitR = GaitLeg(leadR, -nextL > leadR, p);

            float breathe = Mathf.Sin(clock * 2f * Mathf.PI * 0.31f);
            LegPose idleL = IdleL, idleR = IdleR;
            idleL.hip += breathe * 1.5f; idleR.hip -= breathe * 1.0f;
            idleL.ankle += breathe * 2f;

            LegPose airL = LegPose.Lerp(FallL, RiseL, riseW), airR = LegPose.Lerp(FallR, RiseR, riseW);
            LegPose l = LegPose.Lerp(LegPose.Lerp(idleL, gaitL, moveW), airL, airW);
            LegPose r = LegPose.Lerp(LegPose.Lerp(idleR, gaitR, moveW), airR, airW);
            Apply(left, hipRestL, kneeRestL, ankleRestL, l, -1f);
            Apply(right, hipRestR, kneeRestR, ankleRestR, r, 1f);
        }

        static void Apply(LunaLeg leg, Quaternion hipRest, Quaternion kneeRest, Quaternion ankleRest, LegPose pose, float side)
        {
            // Bones share the body's axes (x right, y up, z forward): a negative x rotation swings the leg forward,
            // a positive one folds the shin back or points the toes down.
            leg.hip.localRotation = hipRest * Quaternion.AngleAxis(side * pose.splay, Vector3.forward) * Quaternion.AngleAxis(-pose.hip, Vector3.right);
            leg.knee.localRotation = kneeRest * Quaternion.AngleAxis(pose.knee, Vector3.right);
            leg.ankle.localRotation = ankleRest * Quaternion.AngleAxis(pose.ankle, Vector3.right);
        }

        void PoseBody(float dt, LunaGaitProfile p)
        {
            float bound = psi - Mathf.Floor(psi);
            float hang = 1f - Mathf.Pow(Mathf.Abs(2f * bound - 1f), hangShape);
            float forwardAccel = Vector3.Dot(smoothedAccel, transform.forward);

            // springs: landing dip and the sprint lunge
            dipVelocity += (-70f * dip - 2f * Mathf.Sqrt(70f) * 0.55f * dipVelocity) * dt;
            dip += dipVelocity * dt;
            lungeVelocity += (-60f * lunge - 2f * Mathf.Sqrt(60f) * 0.7f * lungeVelocity) * dt;
            lunge += lungeVelocity * dt;

            float glide = moveW * (1f - airW);
            float pitch = moveW * p.lean
                        + Mathf.Clamp(forwardAccel * accelLean, -7f, 9f) * (1f - airW)
                        - 2f * Mathf.Sin(2f * Mathf.PI * bound) * glide * runW
                        + lunge
                        + airW * (sprintW * 12f - 5f * riseW)
                        + (dashProgress >= 0f ? dashDive * Mathf.Sin(Mathf.PI * dashProgress) : 0f);
            float roll = Mathf.Clamp(-yawRate * speed * bankGain, -maxBank, maxBank) * (1f - 0.5f * airW)
                       + swayRoll * Mathf.Sin(Mathf.PI * psi) * glide
                       + 0.8f * Mathf.Sin(clock * 2f * Mathf.PI * 0.37f) * (1f - moveW);
            float twist = swayYaw * Mathf.Sin(Mathf.PI * psi) * glide;
            pitch += 0.6f * Mathf.Sin(clock * 2f * Mathf.PI * 0.29f) * (1f - moveW);
            BodyPitch = pitch;

            Quaternion rotation = Quaternion.AngleAxis(twist, Vector3.up) * Quaternion.AngleAxis(pitch, Vector3.right) * Quaternion.AngleAxis(roll, Vector3.forward);
            Vector3 basePosition = leanPivot - rotation * leanPivot;
            visual.localRotation = rotation;
            visual.localPosition = basePosition;

            // Lift the body so the lowest boot point sits exactly at the wanted gap: the toe just meets the ground at
            // each touch and the glide arc rises from there.
            float lowest = LowestSole(out _);
            float bob = hoverBob * Mathf.Sin(clock * 2f * Mathf.PI * hoverBobRate);
            float gap = Mathf.Lerp(hoverHeight + bob, p.contactGap + p.arc * hang, moveW);
            float offset = gap - (lowest - groundLevel);
            if (!contact || airW > 0f) offset = Mathf.Lerp(offset, airOffset, airW);
            lastOffset = offset;
            // the dash leap: the root slides along the floor, the body floats over it
            DashLiftNow = dashProgress >= 0f ? dashLift * Mathf.Sin(Mathf.PI * dashProgress) : 0f;
            // A landing dip or the end of the air blend may bring the toe down to the floor, never through it.
            float lifted = offset + dip + DashLiftNow;
            if (grounded) lifted = Mathf.Max(lifted, groundLevel - lowest);
            visual.localPosition = basePosition + Vector3.up * lifted;
            LowestSoleHeight = lowest + lifted - groundLevel;
        }

        /// <summary>Lowest boot point in root space (y) with the current pose; also returns which foot (0 left, 1 right).</summary>
        float LowestSole(out int foot)
        {
            foot = 0;
            float lowest = float.MaxValue;
            for (int f = 0; f < 2; f++)
            {
                LunaLeg leg = f == 0 ? left : right;
                if (leg.ankle == null) continue;
                foreach (Vector3 point in leg.solePoints)
                {
                    float y = transform.InverseTransformPoint(leg.ankle.TransformPoint(point)).y;
                    if (y < lowest) { lowest = y; foot = f; }
                }
            }
            return lowest == float.MaxValue ? 0f : lowest;
        }

        Vector3 SoleOnGround(int foot)
        {
            LunaLeg leg = foot == 0 ? left : right;
            if (leg.ankle == null || leg.solePoints.Length == 0) return FeetCenter();
            Vector3 best = leg.ankle.position;
            float lowest = float.MaxValue;
            foreach (Vector3 point in leg.solePoints)
            {
                Vector3 world = leg.ankle.TransformPoint(point);
                float y = transform.InverseTransformPoint(world).y;
                if (y < lowest) { lowest = y; best = world; }
            }
            return GroundUnder(best);
        }

        Vector3 FeetCenter()
        {
            if (left.ankle == null || right.ankle == null) return GroundUnder(transform.position);
            return GroundUnder((left.ankle.position + right.ankle.position) * 0.5f);
        }

        Vector3 GroundUnder(Vector3 world)
        {
            Vector3 local = transform.InverseTransformPoint(world);
            local.y = groundLevel;
            return transform.TransformPoint(local);
        }

        void UpdateLantern(float dt)
        {
            if (lantern == null || lantern.parent == null || dt <= 0f) return;
            Transform parent = lantern.parent;
            if (!lanternReady || dt > 0.25f || (transform.position - lanternLastPosition).sqrMagnitude > 9f)
            {
                lanternTilt = lanternTiltVelocity = Vector3.zero;
                lantern.localRotation = lanternRest;
                lanternLastPosition = transform.position;
                lanternReady = true;
                return;
            }
            lanternLastPosition = transform.position;

            // A hand-held prop follows a bounded tilt, rather than a free world-space pendulum.
            // Only the filtered motor acceleration contributes inertia; body bob and pivot jumps
            // cannot inject energy. Preserve the authored hanging angle while countering body lean.
            Vector3 down = Vector3.down - Vector3.ClampMagnitude(smoothedAccel, 10f) * 0.008f;
            Vector3 localDown = parent.InverseTransformDirection(down.normalized);
            Vector3 targetDirection = Quaternion.FromToRotation(lanternGravityRest, localDown) * lanternRestDir;
            Quaternion tilt = Quaternion.FromToRotation(lanternRestDir, targetDirection);
            tilt.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            float limit = Mathf.Clamp(lanternMaxAngle, 0f, 45f);
            Vector3 target = Mathf.Abs(angle) > 0.001f ? axis * Mathf.Clamp(angle, -limit, limit) : Vector3.zero;
            lanternTilt = Vector3.SmoothDamp(lanternTilt, target, ref lanternTiltVelocity,
                Mathf.Max(0.05f, lanternSmoothTime), 90f, dt);
            lanternTilt = Vector3.ClampMagnitude(lanternTilt, limit);
            float magnitude = lanternTilt.magnitude;
            lantern.localRotation = magnitude > 0.001f
                ? Quaternion.AngleAxis(magnitude, lanternTilt / magnitude) * lanternRest
                : lanternRest;
        }

        void UpdateMoon(float dt)
        {
            if (moon == null) return;
            Vector3 back = -transform.forward * (moonTrail * Mathf.Max(moveW * 0.6f, sprintW));
            float bob = moonBob * Mathf.Sin(clock * 2f * Mathf.PI * 0.27f + 1.3f);
            Vector3 target = transform.TransformPoint(moonRestRoot + Vector3.up * (lastOffset + bob)) + back;
            if (!initialized || (target - moonPosition).sqrMagnitude > 9f)
            {
                moonPosition = target;
                moonVelocity = Vector3.zero;
            }
            // under-damped spring: the moon drifts after her and settles with a slight overshoot
            float w = 2f * Mathf.PI * moonFollow;
            moonVelocity += (w * w * (target - moonPosition) - 2f * 0.7f * w * moonVelocity) * dt;
            moonPosition += moonVelocity * dt;
            moonSpinAngle = Mathf.Repeat(moonSpinAngle + moonSpin * dt, 360f);
            Vector3 lag = Vector3.ClampMagnitude(target - moonPosition, 0.5f);
            Quaternion tilt = Quaternion.AngleAxis(lag.magnitude * 25f, Vector3.Cross(Vector3.up, lag.normalized));
            moon.SetPositionAndRotation(moonPosition, tilt * transform.rotation * moonRestRotation * Quaternion.AngleAxis(moonSpinAngle, Vector3.up));
        }

        void FlushEvents()
        {
            if (pendingStepFoot < 0) return;
            StepCount++;
            float strength = Mathf.Lerp(0.35f, 0.8f, runW) + 0.3f * sprintW;
            Emit(LunaGaitEventKind.Step, SoleOnGround(pendingStepFoot), strength);
            pendingStepFoot = -1;
        }

        void Emit(LunaGaitEventKind kind, Vector3 position, float strength)
        {
            GaitEvent?.Invoke(new LunaGaitEvent { kind = kind, position = position, strength = strength, sprint = sprintW });
        }
    }
}
