using UnityEngine;

namespace AcRoguelike.Liminal
{
    [DisallowMultipleComponent]
    public sealed class LiminalEnemyVisualCleanup : MonoBehaviour
    {
        public Material[] ownedMaterials;

        void OnDestroy()
        {
            if (ownedMaterials == null) return;
            foreach (var material in ownedMaterials) if (material) Destroy(material);
        }
    }
}
