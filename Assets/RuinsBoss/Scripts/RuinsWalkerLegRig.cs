using UnityEngine;

namespace AcRoguelike.RuinsBoss
{
    /// <summary>Four mechanical two-bone legs. The original Meshy plates remain rigid away from narrow joint seams.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(75)]
    public sealed class RuinsWalkerLegRig : MonoBehaviour
    {
        public Transform bodyBone;
        public Transform[] hips = new Transform[4], knees = new Transform[4], ankles = new Transform[4];
        public Vector3[] hipRest = new Vector3[4], kneeRest = new Vector3[4], ankleRest = new Vector3[4];
        public float walkStride = .68f, chargeStride = 1.35f, walkLift = .2f, chargeLift = .32f;
        public bool Moving { get; private set; }
        public int MotionFrames { get; private set; }
        public float MaximumFootDisplacement { get; private set; }
        public int BoneCount => hips.Length * 3 + (bodyBone ? 1 : 0);

        readonly Vector3[] planted = new Vector3[4], swingStart = new Vector3[4];
        readonly bool[] wasSwing = new bool[4];
        Transform actor;
        Vector3 lastPosition;
        float cycle, speed;
        RuinsBossState state;
        bool dormant = true, dead, initialized;

        void Awake() => Initialize();
        void Initialize()
        {
            if (initialized) return;
            var machine = GetComponentInParent<RuinsWarMachine>();
            actor = machine ? machine.transform : transform;
            lastPosition = actor.position;
            for (int i = 0; i < ankles.Length; i++)
                planted[i] = swingStart[i] = transform.TransformPoint(ankleRest[i]);
            initialized = true;
        }

        public void SetMotion(RuinsBossState next, float move01, bool sleeping, bool isDead)
        { state = next; speed = Mathf.Clamp01(move01); dormant = sleeping; dead = isDead; }

        void LateUpdate()
        {
            if (!Application.isPlaying || Time.deltaTime <= 0) return;
            Initialize();
            float dt = Time.deltaTime;
            Vector3 delta = actor.position - lastPosition; delta.y = 0;
            lastPosition = actor.position;
            if (delta.magnitude > 2.5f)
            {
                for (int i = 0; i < ankles.Length; i++)
                    planted[i] = swingStart[i] = transform.TransformPoint(ankleRest[i]);
                delta = Vector3.zero; cycle = 0;
            }
            float actualSpeed = delta.magnitude / Mathf.Max(.001f, dt);
            Moving = !dormant && !dead && speed > .04f && actualSpeed > .03f;
            if (!Moving)
            {
                for (int i = 0; i < hips.Length; i++)
                {
                    if (!hips[i] || !knees[i] || !ankles[i]) continue;
                    // Hold sleeping machinery perfectly still. An active stop settles its feet over a short interval.
                    float blend = dormant || dead ? 1 : 1 - Mathf.Exp(-12 * dt);
                    hips[i].localRotation = Quaternion.Slerp(hips[i].localRotation, Quaternion.identity, blend);
                    knees[i].localRotation = Quaternion.Slerp(knees[i].localRotation, Quaternion.identity, blend);
                    ankles[i].localRotation = Quaternion.Slerp(ankles[i].localRotation, Quaternion.identity, blend);
                    planted[i] = swingStart[i] = transform.TransformPoint(ankleRest[i]);
                    wasSwing[i] = false;
                }
                return;
            }
            MotionFrames++;
            bool charging = state == RuinsBossState.Attack;
            float stride = charging ? chargeStride : walkStride;
            cycle += dt * Mathf.Clamp(actualSpeed / Mathf.Max(.2f, stride * 2), .2f, 4.5f);
            Vector3 forward = delta.sqrMagnitude > .000001f ? delta.normalized : actor.forward;
            for (int i = 0; i < hips.Length; i++)
            {
                // Diagonal pairs alternate: left rear + right front, then left front + right rear.
                float phase = Mathf.Repeat(cycle + (i == 0 || i == 3 ? 0 : .5f), 1);
                bool swing = phase >= .5f;
                Vector3 rest = transform.TransformPoint(ankleRest[i]);
                Vector3 desired = rest + forward * (stride * .5f);
                Vector3 worldTarget;
                if (swing)
                {
                    if (!wasSwing[i]) swingStart[i] = planted[i];
                    float t = (phase - .5f) * 2;
                    worldTarget = Vector3.Lerp(swingStart[i], desired, Mathf.SmoothStep(0, 1, t));
                    worldTarget.y += Mathf.Sin(t * Mathf.PI) * (charging ? chargeLift : walkLift);
                }
                else
                {
                    if (wasSwing[i]) planted[i] = desired;
                    worldTarget = planted[i];
                }
                wasSwing[i] = swing;
                Solve(i, transform.InverseTransformPoint(worldTarget));
            }
        }

        void Solve(int index, Vector3 foot)
        {
            if (!hips[index] || !knees[index] || !ankles[index]) return;
            Vector3 hip = hipRest[index], upper = kneeRest[index] - hip, lower = ankleRest[index] - kneeRest[index];
            float upperLength = upper.magnitude, lowerLength = lower.magnitude;
            Vector3 to = foot - hip;
            float distance = Mathf.Clamp(to.magnitude, Mathf.Abs(upperLength - lowerLength) + .002f, upperLength + lowerLength - .002f);
            Vector3 direction = to.sqrMagnitude > .00001f ? to.normalized : Vector3.down;
            foot = hip + direction * distance;
            Vector3 bend = Vector3.ProjectOnPlane(upper, (ankleRest[index] - hip).normalized);
            if (bend.sqrMagnitude < .0001f) bend = Vector3.forward * (ankleRest[index].z >= 0 ? 1 : -1);
            bend = Vector3.ProjectOnPlane(bend, direction).normalized;
            if (bend.sqrMagnitude < .0001f) bend = Vector3.forward;
            float along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2 * distance);
            float height = Mathf.Sqrt(Mathf.Max(0, upperLength * upperLength - along * along));
            Vector3 knee = hip + direction * along + bend * height;
            Quaternion upperRotation = Quaternion.FromToRotation(upper, knee - hip);
            Quaternion lowerRotation = Quaternion.FromToRotation(lower, foot - knee);
            hips[index].localRotation = upperRotation;
            knees[index].localRotation = Quaternion.Inverse(upperRotation) * lowerRotation;
            ankles[index].localRotation = Quaternion.Inverse(lowerRotation);
            MaximumFootDisplacement = Mathf.Max(MaximumFootDisplacement, Vector3.Distance(foot, ankleRest[index]));
        }

        /// <summary>Deterministic editor verification pose; does not advance gameplay or move the actor.</summary>
        public void PreviewPose(float phase, float strength = 1)
        {
            for (int i = 0; i < hips.Length; i++)
            {
                float p = phase + (i == 0 || i == 3 ? 0 : Mathf.PI);
                Solve(i, ankleRest[i] + new Vector3(0, Mathf.Max(0, Mathf.Cos(p)) * walkLift, Mathf.Sin(p) * walkStride * .45f) * strength);
            }
        }

        public void RestoreRestPose()
        {
            for (int i = 0; i < hips.Length; i++)
            {
                if (hips[i]) hips[i].localRotation = Quaternion.identity;
                if (knees[i]) knees[i].localRotation = Quaternion.identity;
                if (ankles[i]) ankles[i].localRotation = Quaternion.identity;
            }
        }
    }
}
