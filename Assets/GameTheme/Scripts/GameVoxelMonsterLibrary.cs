using UnityEngine;

namespace AcRoguelike.GameTheme
{
    [CreateAssetMenu(menuName = "AC Roguelike/Game/Monster Library")]
    public sealed class GameVoxelMonsterLibrary : ScriptableObject
    {
        public const string ResourcePath = "GameTheme/GameVoxelMonsterLibrary";
        public GameObject pixelMaw;
        public GameObject bitSentry;
        public GameObject stackGuardian;
        public Mesh projectileMesh;
        public Material paletteMaterial;

        public GameObject Prefab(GameVoxelRole role)
        {
            switch (role)
            {
                case GameVoxelRole.BitSentry: return bitSentry;
                case GameVoxelRole.StackGuardian: return stackGuardian;
                default: return pixelMaw;
            }
        }

        static GameVoxelMonsterLibrary instance;
        public static GameVoxelMonsterLibrary Instance => instance ? instance : instance = Resources.Load<GameVoxelMonsterLibrary>(ResourcePath);
    }
}
