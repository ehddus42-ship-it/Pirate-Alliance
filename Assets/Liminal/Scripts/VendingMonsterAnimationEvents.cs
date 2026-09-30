using UnityEngine;
namespace AcRoguelike.Liminal
{
    /// <summary>Clip-timed prop handoff; the Animator lives on the visual child.</summary>
    public sealed class VendingMonsterAnimationEvents : MonoBehaviour
    {
        VendingMonster owner;
        void Awake() => owner = GetComponentInParent<VendingMonster>();
        public void GrabCan() { if(owner) owner.GrabCanFromAnimation(); }
        public void ReleaseCan() { if(owner) owner.ReleaseCanFromAnimation(); }
    }
}
