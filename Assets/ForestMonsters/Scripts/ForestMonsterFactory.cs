using AcRoguelike.Liminal;
using UnityEngine;

namespace AcRoguelike.Forest
{
    public enum ForestMonsterKind { Moonworm, MossFrog, LunarButterfly, Elderwood }

    /// <summary>The forest roster shares combat contracts, not the office theme's silhouettes.</summary>
    public static class ForestMonsterFactory
    {
        public static LiminalPropMonster Create(ForestMonsterKind kind, Vector3 position, Quaternion rotation, Transform parent)
        {
            var prefab = Resources.Load<GameObject>("ForestMonsters/" + kind);
            if (!prefab) return null;
            return Object.Instantiate(prefab, position, rotation, parent).GetComponent<LiminalPropMonster>();
        }
    }
}
