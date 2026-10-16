using UnityEngine;

namespace PirateAlliance.Characters
{
    /// <summary>Movement for the inspection scene only. Production prefabs have no demo controller.</summary>
    public sealed class ClothShowcaseMotion : MonoBehaviour
    {
        public float phase;
        public bool relaxedArms;
        Vector3 origin;
        Quaternion facing;
        void Awake()
        {
            origin = transform.localPosition; facing = transform.localRotation;
            if (!relaxedArms) return;
            var animator = GetComponent<Animator>();
            RelaxArm(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, -.2f);
            RelaxArm(animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, .2f);
        }
        void RelaxArm(Animator animator, HumanBodyBones upper, HumanBodyBones lower, float side)
        {
            if (!animator || !animator.isHuman) return;
            var a=animator.GetBoneTransform(upper);var b=animator.GetBoneTransform(lower);
            if (!a || !b) return;
            Vector3 desired=transform.TransformDirection(new Vector3(side,-1,.08f)).normalized;
            a.rotation=Quaternion.FromToRotation(b.position-a.position,desired)*a.rotation;
        }
        void Update()
        {
            float t = Time.time + phase;
            transform.localPosition = origin + new Vector3(Mathf.Sin(t * 1.7f) * .28f, Mathf.Sin(t * 3.4f) * .025f, Mathf.Sin(t * .85f) * .15f);
            transform.localRotation = facing * Quaternion.Euler(0, Mathf.Sin(t * 1.3f) * 32, 0);
        }
    }
}
