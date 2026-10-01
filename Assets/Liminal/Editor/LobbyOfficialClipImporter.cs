using System.IO;
using UnityEditor;

namespace AcRoguelike.Liminal.EditorTools
{
    /// <summary>
    /// Import settings for the lobby officials' Meshy clips (Assets/Liminal/Resources/LiminalLobby/*/name@label.fbx).
    /// The NPCs never use root motion (LobbyNpc plays them in place with applyRootMotion off), so the root
    /// rotation, height and horizontal motion are baked into the pose. With Unity's defaults, the body yaw and the
    /// centre-of-mass drift would be extracted as root motion and then thrown away: planted feet would swivel and
    /// slide. Each clip keeps its original orientation and position so the authored stance is unchanged. Looping
    /// is handled at runtime (LobbyNpc cross-fades each loop into its own start), so loopTime stays off.
    /// </summary>
    sealed class LobbyOfficialClipImporter : AssetPostprocessor
    {
        const string Folder = "Assets/Liminal/Resources/LiminalLobby/";

        public override uint GetVersion() => 1;

        static bool IsOfficialClip(string path)
            => path.StartsWith(Folder, System.StringComparison.Ordinal)
               && path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)
               && Path.GetFileName(path).Contains("@");

        void OnPreprocessAnimation()
        {
            if (!IsOfficialClip(assetPath) || !(assetImporter is ModelImporter importer)) return;
            var clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;
            foreach (var clip in clips)
            {
                clip.lockRootRotation = true;
                clip.keepOriginalOrientation = true;
                clip.lockRootHeightY = true;
                clip.keepOriginalPositionY = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalPositionXZ = true;
                clip.loopTime = false;
            }
            importer.clipAnimations = clips;
        }
    }
}
