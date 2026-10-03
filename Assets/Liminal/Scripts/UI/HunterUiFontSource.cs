using UnityEngine;

namespace AcRoguelike.Liminal
{
    /// <summary>Build-safe references for Korean UI fonts and the common glyph prewarm corpus.</summary>
    public sealed class HunterUiFontSource : ScriptableObject
    {
        public const string ResourcePath = "UI/HunterUiFontSource";
        public Font sourceFont;
        public TextAsset commonCharacters;
    }
}
