using System;
using System.Collections.Generic;
using UnityEngine;

namespace AcRoguelike.Liminal
{
    /// <summary>Authoring poses for the dedicated generic skeleton; sampled into editable animation clips.</summary>
    public sealed class VendingMonsterRig : MonoBehaviour
    {
        public const float AwakenDuration = 3.2f, WindupDuration = .95f, ChargeRecoverDuration = 1.05f;
        public const float ThrowDuration = 2.65f, CanGrabTime = .88f, CanReleaseTime = 1.81f;
        [Serializable] public class Limb
        {
            public Transform upper, lower, end, tip;
            public Transform[] fingers = new Transform[0];
        }
        public Transform cabinet;
        public Limb leftArm = new Limb(), rightArm = new Limb(), leftLeg = new Limb(), rightLeg = new Limb();
        public Transform dispenser;
        public float cabinetLift = 1.16f;
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
            bindTransforms=new List<Transform>(rest.Keys).ToArray();bindPositions=new Vector3[bindTransforms.Length];bindScales=new Vector3[bindTransforms.Length];bindRotations=new Quaternion[bindTransforms.Length];
            for(int i=0;i<bindTransforms.Length;i++){bindPositions[i]=rest[bindTransforms[i]].position;bindRotations[i]=rest[bindTransforms[i]].rotation;bindScales[i]=scales[bindTransforms[i]];}
        }
        public void RestoreRestPose()
        {
            if (rest.Count == 0)
            {
                if(bindTransforms==null||bindTransforms.Length==0)CacheRestPose();
                else for(int i=0;i<bindTransforms.Length;i++){if(!bindTransforms[i])continue;rest[bindTransforms[i]]=new Pose(bindPositions[i],bindRotations[i]);scales[bindTransforms[i]]=bindScales[i];}
            }
            foreach (var pair in rest)
            { if (!pair.Key) continue; pair.Key.localPosition = pair.Value.position; pair.Key.localRotation = pair.Value.rotation; pair.Key.localScale = scales[pair.Key]; }
        }
        static float Ease(float v) { v = Mathf.Clamp01(v); return v*v*v*(v*(v*6-15)+10); }
        static float Window(float time, float start, float end) => Ease(Mathf.InverseLerp(start, end, time));
        Vector3 World(Vector3 p) => transform.TransformPoint(p);
        Vector3 Local(Transform t) => transform.InverseTransformPoint(t.position);
        public static float Duration(VendingMonsterState state)
        {
            switch (state)
            {
                case VendingMonsterState.Dormant: return 1;
                case VendingMonsterState.Awakening: return AwakenDuration;
                case VendingMonsterState.Chase: return .84f;
                case VendingMonsterState.ChargeWindup: return WindupDuration;
                case VendingMonsterState.Charging: return .30f;
                case VendingMonsterState.ChargeRecover: return ChargeRecoverDuration;
                case VendingMonsterState.CanThrow: return ThrowDuration;
                case VendingMonsterState.Recovery: return .6f;
                default: return 2.4f;
            }
        }

        public void Sample(VendingMonsterState state, float time)
        {
            RestoreRestPose();
            float bodyDrop = 0, pitch = 0, roll = 0, yaw = 0, armScale = 1, legScale = 1;
            float curlL = .13f, curlR = .18f;
            Vector3 leftHand = new Vector3(-.82f,1.0f,.08f), rightHand = new Vector3(.82f,.99f,.04f);
            Vector3 leftFoot = new Vector3(-.33f,.212f,.02f), rightFoot = new Vector3(.33f,.212f,-.02f);
            float leftFootPitch = 0, rightFootPitch = 0;
            float breathing = Mathf.Sin(time * Mathf.PI * 2 / 2.4f);
            Quaternion handTurnL = Quaternion.identity, handTurnR = Quaternion.identity;
            if (state == VendingMonsterState.Dormant || state == VendingMonsterState.Awakening)
            {
                float t = state == VendingMonsterState.Dormant ? 0 : time;
                float arms = Window(t,.32f,1.3f), legs = Window(t,1.38f,2.65f);
                armScale = Mathf.Lerp(.008f,1,arms); legScale = Mathf.Lerp(.008f,1,legs);
                bodyDrop = -cabinetLift * (1-legs);
                float shake = Mathf.Sin(t*51) * Mathf.Sin(t*23) * (1-Window(t,1.25f,1.65f));
                roll = shake * 1.5f + Mathf.Sin(Mathf.PI*legs)*-5;
                pitch = Mathf.Sin(Mathf.PI*legs)*5 + Mathf.Sin(t*31) * .35f * (1-legs);
                // First the elbows push out, then wrists unfurl; feet open only after the arms settle.
                float spread = Mathf.Sin(Mathf.PI*arms);
                leftHand = new Vector3(-.56f-.26f*arms-.28f*spread,Mathf.Max(.78f,1f + bodyDrop + .65f*spread),.1f+.25f*spread);
                rightHand = new Vector3(.56f+.26f*arms+.28f*spread,Mathf.Max(.78f,.99f + bodyDrop + .55f*spread),.05f+.3f*spread);
                curlL = curlR = Mathf.Lerp(.9f,.15f, Window(t,.8f,1.6f));
                bodyDrop += Mathf.Sin((t-2.65f)*18)*Mathf.Exp(-Mathf.Max(0,t-2.65f)*9)*.025f*Window(t,2.6f,2.8f);
            }
            else if (state == VendingMonsterState.Idle || state == VendingMonsterState.Recovery)
            {
                bodyDrop = breathing*.013f; roll = breathing*.7f; pitch = -1;
                leftHand += new Vector3(.015f, .018f*breathing, .027f*breathing);
                rightHand += new Vector3(-.015f, -.02f*breathing, -.03f*breathing);
                curlL += .025f*breathing; curlR -= .025f*breathing;
            }
            else if (state == VendingMonsterState.Chase || state == VendingMonsterState.Charging)
            {
                bool charge = state == VendingMonsterState.Charging;
                float cycle = time / (charge ? .30f : .84f), stride = charge ? 1.45f : .88f;
                leftFoot = Gait(cycle, -.33f, stride, charge ? .36f : .2f, out leftFootPitch);
                rightFoot = Gait(cycle+.5f, .33f, stride, charge ? .36f : .2f, out rightFootPitch);
                float wave = Mathf.Sin(cycle*Mathf.PI*2);
                bodyDrop = (charge ? -.25f : -.09f) + Mathf.Cos(cycle*Mathf.PI*4)* (charge ? .045f : .027f);
                pitch = charge ? 14 : 4; roll = wave * (charge ? 4 : 2);
                leftHand = new Vector3(-.86f,charge?1.2f:1.05f,-wave*(charge?.68f:.36f));
                rightHand = new Vector3(.86f,charge?1.2f:1.05f,wave*(charge?.68f:.36f));
                curlL = curlR = charge ? .65f : .22f;
            }
            else if (state == VendingMonsterState.ChargeWindup)
            {
                float a = Window(time,0,.75f);
                bodyDrop = -.22f*a; pitch = -9*a; roll = -3*a;
                leftHand = Vector3.Lerp(leftHand,new Vector3(-.86f,.9f,-.42f),a);
                rightHand = Vector3.Lerp(rightHand,new Vector3(.9f,1.02f,-.32f),a);
                leftFoot.z = .2f*a; rightFoot.z = -.32f*a;
                curlL = curlR = Mathf.Lerp(.15f,.82f,a);
            }
            else if (state == VendingMonsterState.ChargeRecover)
            {
                float a = Window(time,0,.22f), settle = Window(time,.24f,ChargeRecoverDuration);
                bodyDrop = -.18f*Mathf.Sin(Mathf.PI*Mathf.Clamp01(time/ChargeRecoverDuration));
                pitch = Mathf.Lerp(14,-10,a)*(1-settle); roll = -4*(1-settle);
                leftHand = Vector3.Lerp(new Vector3(-1.1f,1.04f,.63f),leftHand,settle);
                rightHand = Vector3.Lerp(new Vector3(1.07f,.95f,.62f),rightHand,settle);
                leftFoot.z = .35f*(1-settle); rightFoot.z = -.25f*(1-settle);
            }
            else if (state == VendingMonsterState.CanThrow)
            {
                // Fingertips approach the lower retrieval hatch, close, withdraw, load the shoulder, release, follow through.
                Vector3 hatch = dispenser ? Local(dispenser) : new Vector3(.02f,1.58f,.43f);
                Vector3 approach = hatch + new Vector3(.16f,.05f,.3f);
                Vector3 grip = hatch + new Vector3(0,.06f,.11f);
                Vector3 pulled = hatch + new Vector3(.32f,.24f,.48f);
                Vector3 loaded = new Vector3(.92f,2.63f,-.44f);
                Vector3 release = new Vector3(.5f,2.48f,1.03f);
                Vector3 follow = new Vector3(-.32f,1.25f,.85f);
                if(time < .53f) rightHand=Vector3.Lerp(rightHand,approach,Window(time,.08f,.53f));
                else if(time < .88f) rightHand=Vector3.Lerp(approach,grip,Window(time,.53f,.83f));
                else if(time < 1.24f) rightHand=Vector3.Lerp(grip,pulled,Window(time,.92f,1.24f));
                else if(time < 1.61f) rightHand=Vector3.Lerp(pulled,loaded,Window(time,1.24f,1.61f));
                else if(time < CanReleaseTime) rightHand=Vector3.Lerp(loaded,release,Window(time,1.64f,CanReleaseTime));
                else if(time < 2.13f) rightHand=Vector3.Lerp(release,follow,Window(time,CanReleaseTime,2.13f));
                else rightHand=Vector3.Lerp(follow,rightHand,Window(time,2.13f,ThrowDuration));
                curlR = time < .77f ? .06f : time < CanReleaseTime ? Mathf.Lerp(.06f,.9f,Window(time,.77f,.9f)) : Mathf.Lerp(.05f,.18f,Window(time,2.1f,ThrowDuration));
                float wind = Window(time,1.25f,1.6f)*(1-Window(time,1.64f,1.87f));
                float followWeight=Window(time,1.72f,1.95f)*(1-Window(time,2.12f,2.65f));
                yaw=-12*wind+11*followWeight; pitch=-7*wind+9*followWeight;
                bodyDrop=-.05f*Window(time,.2f,.65f)*(1-Window(time,1.2f,1.5f));
                leftHand += new Vector3(-.17f,.13f,-.33f)*Window(time,.4f,.95f)*(1-Window(time,2,2.65f));
                handTurnR = Quaternion.Euler(-75*Window(time,.23f,.64f)*(1-Window(time,1.22f,1.55f)),0,-18*wind);
            }
            if (cabinet)
            {
                cabinet.position += transform.up * bodyDrop;
                cabinet.rotation = transform.rotation * Quaternion.Euler(pitch,yaw,roll) * Quaternion.Inverse(transform.rotation) * cabinet.rotation;
            }
            ScaleLimb(leftArm,armScale); ScaleLimb(rightArm,armScale); ScaleLimb(leftLeg,legScale); ScaleLimb(rightLeg,legScale);
            if (armScale > .03f)
            {
                Solve(leftArm, World(leftHand), World(new Vector3(-1.5f,1.65f,-.6f)));
                float elbowForward=state==VendingMonsterState.CanThrow && time<1.3f ? .95f : -.35f;
                Solve(rightArm, World(rightHand), World(new Vector3(1.5f,1.65f,elbowForward)));
                StabilizeEnd(leftArm, handTurnL); StabilizeEnd(rightArm,handTurnR);
                Curl(leftArm,curlL); Curl(rightArm,curlR);
            }
            if(legScale > .03f)
            {
                Solve(leftLeg,World(leftFoot),World(new Vector3(-.5f,.65f,1)));
                Solve(rightLeg,World(rightFoot),World(new Vector3(.5f,.65f,1)));
                StabilizeEnd(leftLeg,Quaternion.Euler(leftFootPitch,0,-3));
                StabilizeEnd(rightLeg,Quaternion.Euler(rightFootPitch,0,3));
            }
        }
        Vector3 Gait(float phase,float x,float stride,float lift,out float pitch)
        {
            float p=Mathf.Repeat(phase,1); float z,y;
            if(p<.58f) { float t=p/.58f;z=Mathf.Lerp(stride*.5f,-stride*.5f,t);y=.212f;pitch=Mathf.Lerp(-9,12,t); }
            else {float t=(p-.58f)/.42f; z=Mathf.Lerp(-stride*.5f,stride*.5f,Ease(t));y=.212f+Mathf.Sin(t*Mathf.PI)*lift;pitch=-24*Mathf.Sin(t*Mathf.PI);}
            return new Vector3(x,y,z);
        }
        void ScaleLimb(Limb limb,float scale) { if(limb.upper) limb.upper.localScale=scales[limb.upper]*scale; }
        void StabilizeEnd(Limb limb,Quaternion offset)
        {
            if(!limb.end) return;
            // Preserve the imported bone's rest axis; the model need not use Unity's bone orientation convention.
            Quaternion rotation=Quaternion.identity; Transform t=limb.end;
            var chain=new List<Transform>();while(t && t!=transform){chain.Add(t);t=t.parent;}
            for(int i=chain.Count-1;i>=0;i--) rotation*=rest[chain[i]].rotation;
            limb.end.rotation=transform.rotation*offset*rotation;
        }
        void Curl(Limb limb,float amount)
        {
            foreach(var finger in limb.fingers)
            {
                if(!finger) continue;
                float angle=amount*(finger.name.Contains("Thumb")?40:65);
                // Bone-local X is the flexion axis authored by the Blender export.
                finger.localRotation=rest[finger].rotation*Quaternion.Euler(angle,0,0);
            }
        }
        public static void Solve(Limb limb,Vector3 target,Vector3 pole)
        {
            if(!limb.upper || !limb.lower || !limb.end) return;
            Vector3 a=limb.upper.position,b=limb.lower.position,c=limb.end.position;
            float l1=Vector3.Distance(a,b),l2=Vector3.Distance(b,c);
            if(l1<.0001f || l2<.0001f) return;
            Vector3 direction=target-a;float distance=Mathf.Clamp(direction.magnitude,Mathf.Abs(l1-l2)+.001f,l1+l2-.001f);
            direction=direction.sqrMagnitude>.00001f?direction.normalized:Vector3.down;
            Vector3 bend=Vector3.ProjectOnPlane(pole-a,direction).normalized;
            if(bend.sqrMagnitude<.01f)bend=Vector3.Cross(direction,Vector3.right).normalized;
            float along=(l1*l1+distance*distance-l2*l2)/(2*distance);
            Vector3 elbow=a+direction*along+bend*Mathf.Sqrt(Mathf.Max(0,l1*l1-along*along));
            limb.upper.rotation=Quaternion.FromToRotation(b-a,elbow-a)*limb.upper.rotation;
            limb.lower.rotation=Quaternion.FromToRotation(limb.end.position-limb.lower.position,a+direction*distance-limb.lower.position)*limb.lower.rotation;
        }
    }
}
