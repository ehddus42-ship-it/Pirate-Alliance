using UnityEngine;

namespace AcRoguelike.GameTheme
{
    public enum GameVoxelRole { PixelMaw, BitSentry, StackGuardian }

    /// <summary>The room opts in explicitly; other themes keep their own monster roster.</summary>
    [DisallowMultipleComponent]
    public sealed class GameThemeRoom : MonoBehaviour
    {
        public GameVoxelRole[] enemyRoles =
            { GameVoxelRole.PixelMaw, GameVoxelRole.BitSentry, GameVoxelRole.StackGuardian };
        public GameVoxelRole bossRole = GameVoxelRole.StackGuardian;
        GameThemeNavigation navigation;

        public Vector3 RouteDirection(Vector3 position, Vector3 destination)
        {
            if (navigation == null)
            {
                var room = GetComponent<AcRoguelike.Liminal.LiminalRoom>();
                if (!room) return Vector3.zero;
                navigation = new GameThemeNavigation(room);
            }
            return navigation.Direction(position, destination);
        }

#if UNITY_EDITOR
        public string DiagnoseNavigation(Vector3 position, Vector3 destination)
        {
            RouteDirection(position, destination);
            return navigation == null ? "no navigation" : navigation.Diagnose(position, destination);
        }
#endif

        public GameVoxelRole RoleAt(int index, bool boss)
        {
            if (boss && index == 0) return bossRole;
            return enemyRoles != null && enemyRoles.Length > 0
                ? enemyRoles[Mathf.Abs(index) % enemyRoles.Length] : GameVoxelRole.PixelMaw;
        }
    }
}
