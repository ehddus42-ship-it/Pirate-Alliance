using System.IO;
using UnityEditor;

namespace AcRoguelike.Liminal.EditorTools
{
    /// <summary>
    /// Import settings for the lobby officials' Meshy clips (Assets/Liminal/Resources/LiminalLobby/*/name@label.fbx).
    /// The NPCs never use root motion (LobbyNpc plays them in place with applyRootMotion off), so the root
    /// rotation, height and horizontal motion are baked into the pose (rotation and XZ relative to the clip start). With Unity's defaults, the body yaw and the
    /// centre-of-mass drift would be extracted as root motion and then thrown away: planted feet would swivel and
    /// slide. Looping
    /// is handled at runtime (LobbyNpc cross-fades each loop into its own start), so loopTime stays off.
    /// </summary>
    sealed class LobbyOfficialClipImporter : AssetPostprocessor
    {
        const string Folder = "Assets/Liminal/Resources/LiminalLobby/";

        public override uint GetVersion() => 2;

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
                // Rotation and XZ are baked relative to the clip's start (body orientation, centre of mass), not
                // the file origin: some clips are authored off-centre (the officer's chat stands 0.67 m aside and
                // turned 30 degrees), which would slide and turn the body on every cross-fade.
                clip.lockRootRotation = true;
                clip.keepOriginalOrientation = false;
                clip.lockRootHeightY = true;
                clip.keepOriginalPositionY = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalPositionXZ = false;
                clip.loopTime = false;
            }
            importer.clipAnimations = clips;
        }
    }
}
