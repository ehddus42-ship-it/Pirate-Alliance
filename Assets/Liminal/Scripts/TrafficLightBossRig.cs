using System;
using System.Collections.Generic;
using UnityEngine;

namespace AcRoguelike.Liminal
{
    public enum TrafficLightBossState { Dormant, Awakening, Idle, Walk, FieldCast, CarThrow, Recovery, Dead }

    /// <summary>Authoring poses for the wire-limbed signal boss; sampled into editable animation clips.</summary>
    public sealed class TrafficLightBossRig : MonoBehaviour
    {
        public const float HipHeight = 4.8f, AnkleHeight = .95f, StandingHeight = 12f, DormantHeight = 7.2f;
        public const float AwakenDuration = 3.6f, WalkDuration = 1.8f, RecoveryDuration = .8f;
        public const float FieldDuration = 3.6f, FieldJudgeTime = 2.6f;
        public const float ThrowDuration = 3f, CarSpawnTime = .45f, CarAimTime = 1.3f, CarReleaseTime = 1.95f;
        static readonly Vector3 RestHandL = new Vector3(-3.3f, 2.4f, .6f), RestHandR = new Vector3(3.3f, 2.4f, .6f);
        static readonly Vector3 RestFootL = new Vector3(-1.35f, AnkleHeight, 0), RestFootR = new Vector3(1.35f, AnkleHeight, 0);

        [Serializable] public class Limb { public Transform upper, lower, end; }
        public Transform body;
        public Limb leftArm = new Limb(), rightArm = new Limb(), leftLeg = new Limb(), rightLeg = new Limb();
        [SerializeField, HideInInspector] Transform[] bindTransforms;
        [SerializeField, HideInInspector] Vector3[] bindPositions, bindScales;
        [SerializeField, HideInInspector] Quaternion[] bindRotations;
        readonly Dictionary<Transform, Pose> rest = new Dictionary<Transform, Pose>();
        readonly Dictionary<Transform, Vector3> scales = new Dictionary<Transform, Vector3>();

        public void CacheRestPose()
        {
            rest.Clear(); scales.Clear();
            foreach (var t in GetComponentsInChildren<Transform>(true))
            { if (t == transform) continue; rest[t] = new Pose(t.localPosition, t.localRotation); scales[t] = t.localScale; }
            bindTransforms = new List<Transform>(rest.Keys).ToArray(); bindPositions = new Vector3[bindTransforms.Length];
            bindScales = new Vector3[bindTransforms.Length]; bindRotations = new Quaternion[bindTransforms.Length];
            for (int i = 0; i < bindTransforms.Length; i++)
            { bindPositions[i] = rest[bindTransforms[i]].position; bindRotations[i] = rest[bindTransforms[i]].rotation; bindScales[i] = scales[bindTransforms[i]]; }
        }

        public void RestoreRestPose()
        {
            if (rest.Count == 0)
            {
                if (bindTransforms == null || bindTransforms.Length == 0) CacheRestPose();
                else for (int i = 0; i < bindTransforms.Length; i++)
                { if (!bindTransforms[i]) continue; rest[bindTransforms[i]] = new Pose(bindPositions[i], bindRotations[i]); scales[bindTransforms[i]] = bindScales[i]; }
            }
            foreach (var pair in rest)
            { if (!pair.Key) continue; pair.Key.localPosition = pair.Value.position; pair.Key.localRotation = pair.Value.rotation; pair.Key.localScale = scales[pair.Key]; }
        }

        static float Ease(float v) { v = Mathf.Clamp01(v); return v * v * v * (v * (v * 6 - 15) + 10); }
        static float Window(float time, float start, float end) => Ease(Mathf.InverseLerp(start, end, time));
        Vector3 World(Vector3 p) => transform.TransformPoint(p);
        static Vector3 Mirror(Vector3 p) => new Vector3(-p.x, p.y, p.z);

        public static float Duration(TrafficLightBossState state)
        {
            switch (state)
            {
                case TrafficLightBossState.Dormant: return 1;
                case TrafficLightBossState.Awakening: return AwakenDuration;
                case TrafficLightBossState.Walk: return WalkDuration;
                case TrafficLightBossState.FieldCast: return FieldDuration;
                case TrafficLightBossState.CarThrow: return ThrowDuration;
                case TrafficLightBossState.Recovery: return RecoveryDuration;
                default: return 2.6f;
            }
        }

        public void Sample(TrafficLightBossState state, float time)
        {
            RestoreRestPose();
            float drop = 0, pitch = 0, roll = 0, yaw = 0, armScale = 1, legScale = 1, leftFootPitch = 0, rightFootPitch = 0;
            Vector3 leftHand = RestHandL, rightHand = RestHandR, leftFoot = RestFootL, rightFoot = RestFootR;
            Vector3 leftPole = new Vector3(-7, 10, -4), rightPole = new Vector3(7, 10, -4);
            float breathing = Mathf.Sin(time * Mathf.PI * 2 / 2.6f);
            if (state == TrafficLightBossState.Dormant || state == TrafficLightBossState.Awakening)
            {
                // Wires push out of the housing first, then the legs lift the whole signal off the ground.
                float t = state == TrafficLightBossState.Dormant ? 0 : time;
                float arms = Window(t, .3f, 1.5f), legs = Window(t, 1.6f, 3f);
                armScale = Mathf.Lerp(.008f, 1, arms); legScale = Mathf.Lerp(.008f, 1, legs);
                drop = -HipHeight * (1 - legs);
                float shake = Mathf.Sin(t * 47) * Mathf.Sin(t * 19) * (1 - Window(t, 1.4f, 1.9f));
                roll = shake * 1.2f - Mathf.Sin(Mathf.PI * legs) * 3;
                pitch = Mathf.Sin(Mathf.PI * legs) * 4;
                float spread = Mathf.Sin(Mathf.PI * arms);
                leftHand = new Vector3(-3.3f - 1.2f * arms - 1.4f * spread, Mathf.Max(.9f, 2.4f + drop * .5f + 2.5f * spread), .6f + spread);
                rightHand = Mirror(leftHand);
                drop += Mathf.Sin((t - 3f) * 16) * Mathf.Exp(-Mathf.Max(0, t - 3f) * 8) * .12f * Window(t, 2.95f, 3.1f);
            }
            else if (state == TrafficLightBossState.Idle || state == TrafficLightBossState.Recovery)
            {
                drop = breathing * .05f; roll = breathing * .5f; pitch = -.5f;
                leftHand += new Vector3(.08f * breathing, .1f * breathing, .15f * breathing);
                rightHand += new Vector3(-.08f * breathing, -.1f * breathing, -.15f * breathing);
            }
            else if (state == TrafficLightBossState.Walk)
            {
                float cycle = time / WalkDuration, wave = Mathf.Sin(cycle * Mathf.PI * 2);
                leftFoot = Gait(cycle, -1.35f, 2.3f, .95f, out leftFootPitch);
                rightFoot = Gait(cycle + .5f, 1.35f, 2.3f, .95f, out rightFootPitch);
                drop = -.25f + Mathf.Cos(cycle * Mathf.PI * 4) * .12f; pitch = 3; roll = wave * 2.5f;
                leftHand = new Vector3(-3.4f, 2.6f, .3f - wave * 1.1f); rightHand = new Vector3(3.4f, 2.6f, .3f + wave * 1.1f);
            }
            else if (state == TrafficLightBossState.FieldCast)
            {
                // Raise both wire arms, shake while the floor charges, then hammer the light field down.
                float up = Window(time, 0, .9f), slam = Window(time, FieldJudgeTime, FieldJudgeTime + .3f), recover = Window(time, FieldJudgeTime + .45f, FieldDuration);
                float charging = time < FieldJudgeTime ? Window(time, .6f, 1.2f) : 0;
                float shake = Mathf.Sin(time * 41) * charging;
                Vector3 raised = new Vector3(-4.8f, 12.6f + shake * .35f, .8f), slammed = new Vector3(-2.3f, .8f, 4.4f);
                leftHand = Vector3.Lerp(Vector3.Lerp(RestHandL, raised, up), slammed, slam);
                leftHand = Vector3.Lerp(leftHand, RestHandL, recover); rightHand = Mirror(leftHand);
                pitch = -11 * up * (1 - slam) + 16 * slam * (1 - recover); roll = shake * 2.2f;
                drop = -.3f * up * (1 - slam) - .8f * slam * (1 - recover);
                leftPole = Vector3.Lerp(leftPole, new Vector3(-9, 12, 0), up * (1 - slam)); rightPole = Mirror(leftPole);
                leftFoot.x = -1.35f - .3f * up * (1 - recover); rightFoot.x = -leftFoot.x;
            }
            else if (state == TrafficLightBossState.CarThrow)
            {
                // Scoop a car off the floor, carry it above the signal, wind back, then hurl it along the floor line.
                Vector3 pickup = new Vector3(.8f, 2.3f, 3.4f), lift = new Vector3(1.2f, 12f, 1.2f), wind = new Vector3(1.8f, 12.4f, -2.4f);
                Vector3 throwPoint = new Vector3(.6f, 9.6f, 5.2f), follow = new Vector3(-.8f, 5.5f, 6.5f);
                if (time < CarSpawnTime) rightHand = Vector3.Lerp(RestHandR, pickup, Window(time, 0, CarSpawnTime));
                else if (time < 1.25f) rightHand = Vector3.Lerp(pickup, lift, Window(time, CarSpawnTime + .1f, 1.25f));
                else if (time < 1.75f) rightHand = Vector3.Lerp(lift, wind, Window(time, 1.25f, 1.75f));
                else if (time < CarReleaseTime) rightHand = Vector3.Lerp(wind, throwPoint, Window(time, 1.75f, CarReleaseTime));
                else if (time < 2.35f) rightHand = Vector3.Lerp(throwPoint, follow, Window(time, CarReleaseTime, 2.35f));
                else rightHand = Vector3.Lerp(follow, RestHandR, Window(time, 2.35f, ThrowDuration));
                float carrying = Window(time, CarSpawnTime, .8f) * (1 - Window(time, CarReleaseTime, CarReleaseTime + .2f));
                leftHand = Vector3.Lerp(leftHand, rightHand + new Vector3(-2.6f, .4f, .4f), carrying);
                float crouch = Window(time, .1f, CarSpawnTime) * (1 - Window(time, CarSpawnTime + .15f, .95f));
                float windUp = Window(time, 1.3f, 1.75f) * (1 - Window(time, 1.78f, CarReleaseTime));
                float followWeight = Window(time, CarReleaseTime - .1f, CarReleaseTime + .25f) * (1 - Window(time, 2.35f, ThrowDuration));
                drop = -1.2f * crouch - .25f * followWeight; pitch = 13 * crouch - 12 * windUp + 14 * followWeight;
                yaw = 10 * windUp - 12 * followWeight;
                rightPole = new Vector3(7, 12, -3);
            }
            if (body)
            {
                body.position += transform.up * drop;
                body.rotation = transform.rotation * Quaternion.Euler(pitch, yaw, roll) * Quaternion.Inverse(transform.rotation) * body.rotation;
            }
            ScaleLimb(leftArm, armScale); ScaleLimb(rightArm, armScale); ScaleLimb(leftLeg, legScale); ScaleLimb(rightLeg, legScale);
            if (armScale > .03f)
            {
                Solve(leftArm, World(leftHand), World(leftPole)); Solve(rightArm, World(rightHand), World(rightPole));
                FollowForearm(leftArm); FollowForearm(rightArm);
            }
            if (legScale > .03f)
            {
                Solve(leftLeg, World(leftFoot), World(new Vector3(-1.4f, 3, 6))); Solve(rightLeg, World(rightFoot), World(new Vector3(1.4f, 3, 6)));
                KeepFlat(leftLeg, Quaternion.Euler(leftFootPitch, 0, 0)); KeepFlat(rightLeg, Quaternion.Euler(rightFootPitch, 0, 0));
            }
        }

        Vector3 Gait(float phase, float x, float stride, float lift, out float pitch)
        {
            float p = Mathf.Repeat(phase, 1), z, y;
            if (p < .58f) { float t = p / .58f; z = Mathf.Lerp(stride * .5f, -stride * .5f, t); y = AnkleHeight; pitch = Mathf.Lerp(-6, 8, t); }
            else { float t = (p - .58f) / .42f; z = Mathf.Lerp(-stride * .5f, stride * .5f, Ease(t)); y = AnkleHeight + Mathf.Sin(t * Mathf.PI) * lift; pitch = -18 * Mathf.Sin(t * Mathf.PI); }
            return new Vector3(x, y, z);
        }

        void ScaleLimb(Limb limb, float scale) { if (limb.upper) limb.upper.localScale = scales[limb.upper] * scale; }

        // The wire hand continues the forearm so a raised arm never points its claws the wrong way.
        void FollowForearm(Limb limb) { if (limb.end) limb.end.localRotation = rest[limb.end].rotation; }

        // Feet keep the rest orientation in rig space so the toe wires stay flat on the floor.
        void KeepFlat(Limb limb, Quaternion offset)
        {
            if (!limb.end) return;
            Quaternion rotation = Quaternion.identity; Transform t = limb.end;
            var chain = new List<Transform>(); while (t && t != transform) { chain.Add(t); t = t.parent; }
            for (int i = chain.Count - 1; i >= 0; i--) rotation *= rest[chain[i]].rotation;
            limb.end.rotation = transform.rotation * offset * rotation;
        }

        public static void Solve(Limb limb, Vector3 target, Vector3 pole)
        {
            VendingMonsterRig.Solve(new VendingMonsterRig.Limb { upper = limb.upper, lower = limb.lower, end = limb.end }, target, pole);
        }
    }
}
