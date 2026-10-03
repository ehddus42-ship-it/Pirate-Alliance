using System;
using System.Collections.Generic;
using UnityEngine;

namespace AcRoguelike.Liminal
{
    [Flags]
    public enum GateEnemyGimmick { None = 0, Armor = 1, DeathBurst = 2 }

    /// <summary>One stable contract and destination. DisplayDestination is the only pre-entry location label.</summary>
    [Serializable]
    public sealed class GateMissionDefinition
    {
        public string id;
        public string code;
        public string title;
        public string destinationId;
        public string destinationName;
        [TextArea] public string briefing;
        [Range(1, 3)] public int difficulty = 1;
        public string difficultyName;
        [Min(1)] public float healthMultiplier = 1;
        public GateEnemyGimmick enemyGimmicks;
        public string gimmickName;
        [TextArea] public string gimmickDescription;
        [TextArea] public string objective;
        public LiminalStageDefinition[] stages = new LiminalStageDefinition[0];

        // Attack values remain the authored values; difficulty scales health only.
        public float damageMultiplier => 1;
        public bool IsDiscovered => HunterProgress.IsDestinationDiscovered(destinationId);
        public string DisplayDestination => IsDiscovered ? destinationName : "???";
        public int StageCount => stages == null ? 0 : stages.Length;
        public int RoomCount
        {
            get
            {
                int count = 0;
                if (stages != null)
                    foreach (var stage in stages)
                        if (stage) count += Mathf.Max(0, stage.middleRoomCount) + 2;
                return count;
            }
        }

        public bool IsAvailable
        {
            get
            {
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(destinationId) || StageCount == 0) return false;
                foreach (var stage in stages)
                    if (!stage || !stage.startRoom || !stage.endRoom) return false;
                return true;
            }
        }
    }

    /// <summary>Resources asset references the original stage assets, including theme assets outside Resources.</summary>
    [CreateAssetMenu(menuName = "Liminal/Gate Mission Catalog", fileName = "GateMissions")]
    public sealed class GateMissionCatalog : ScriptableObject
    {
        public GateMissionDefinition[] missions = new GateMissionDefinition[0];
        public IReadOnlyList<GateMissionDefinition> Missions => missions ?? Array.Empty<GateMissionDefinition>();
    }
}
