using UnityEngine;

namespace AcRoguelike.Liminal
{
    /// <summary>Keeps the one-sided architectural lettering material aligned with Unity's dynamic font atlas.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class LiminalSignageAtlas : MonoBehaviour
    {
        public Font font;
        public Material material;

        void OnEnable()
        {
            Font.textureRebuilt += OnTextureRebuilt;
            SynchronizeAtlas();
        }

        void OnDisable() { Font.textureRebuilt -= OnTextureRebuilt; }

        void OnTextureRebuilt(Font rebuiltFont)
        {
            if (font == rebuiltFont) SynchronizeAtlas();
        }

        void SynchronizeAtlas()
        {
            if (font && font.material && material && material.mainTexture != font.material.mainTexture)
                material.mainTexture = font.material.mainTexture;
        }
    }
}
