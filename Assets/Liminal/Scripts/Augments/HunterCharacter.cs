using UnityEngine;

namespace AcRoguelike.Liminal
{
    /// <summary>
    /// Which hunter the player is. Character-specific augments match on characterId; the current playable character
    /// is a test hunter with no augments of its own, so only common augments appear for it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HunterCharacter : MonoBehaviour
    {
        public const string TestHunterId = "test_hunter";

        [Tooltip("Matches AugmentDefinition.characterId of this hunter's own augments.")]
        public string characterId = TestHunterId;
        public string displayName = "테스트 헌터";
    }
}
