using AcRoguelike.Liminal;
using UnityEngine;

namespace AcRoguelike.RuinsBoss
{
    /// <summary>Authored encounter sockets, kept separate from the randomly selected ordinary ruins rooms.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(LiminalRoom))]
    public sealed class RuinsBossArena : MonoBehaviour
    {
        public Transform bossSpawn, leftSpawn, rightSpawn, turretSpawn;
        public RuinsStormBoss Boss { get; private set; }

        public RuinsStormBoss Spawn(LiminalPlayerHealth player, int stage)
        {
            if (Boss) return Boss;
            var room = GetComponent<LiminalRoom>();
            var left = RuinsWarMachine.Create(RuinsWarMachineKind.IronRam, leftSpawn.position, leftSpawn.rotation, transform);
            var right = RuinsWarMachine.Create(RuinsWarMachineKind.SiegeWalker, rightSpawn.position, rightSpawn.rotation, transform);
            var turretPrefab = Resources.Load<GameObject>("RuinsBoss/MissileTurret");
            var bossPrefab = Resources.Load<GameObject>("RuinsBoss/StormSovereign");
            if (!left || !right || !turretPrefab || !bossPrefab)
                throw new System.InvalidOperationException("Ruins boss encounter assets are missing. Rebuild the Apocalypse Boss kit.");
            var turret = Instantiate(turretPrefab, turretSpawn.position, turretSpawn.rotation, transform);
            Boss = Instantiate(bossPrefab, bossSpawn.position, bossSpawn.rotation, transform).GetComponent<RuinsStormBoss>();
            left.Setup(player, room, stage); right.Setup(player, room, stage);
            Boss.ConfigureEncounter(left, right, turret.transform);
            Boss.Setup(player, room, stage);
            return Boss;
        }
    }
}
