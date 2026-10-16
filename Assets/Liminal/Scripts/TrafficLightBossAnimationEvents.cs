using UnityEngine;
namespace AcRoguelike.Liminal
{
    /// <summary>Clip-timed car handoff; the Animator lives on the visual child.</summary>
    public sealed class TrafficLightBossAnimationEvents : MonoBehaviour
    {
        TrafficLightBoss owner;
        void Awake() => owner = GetComponentInParent<TrafficLightBoss>();
        public void SpawnCar() { if (owner) owner.SpawnCarFromAnimation(); }
        public void ReleaseCar() { if (owner) owner.ReleaseCarFromAnimation(); }
    }
}
