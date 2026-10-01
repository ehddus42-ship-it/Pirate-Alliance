using UnityEngine;

namespace AcRoguelike.Liminal
{
    /// <summary>
    /// The concept-map props the office monsters are built from, plus the boss placed in boss rooms.
    /// Lives at Resources/LiminalMonsters/LiminalMonsterLibrary so the run director finds it without a scene
    /// reference. The CRT monitor model is loaded from Resources/LiminalMonsters/crt_monitor.
    /// </summary>
    [CreateAssetMenu(menuName = "AC Roguelike/Liminal/Monster Library")]
    public sealed class LiminalMonsterLibrary : ScriptableObject
    {
        public GameObject photocopier;
        public GameObject lockers;
        public GameObject trafficLightBoss;

        public const string ResourcePath = "LiminalMonsters/LiminalMonsterLibrary";
        public const string MonitorPath = "LiminalMonsters/crt_monitor";

        static LiminalMonsterLibrary cached;
        static GameObject monitor;
        static bool monitorLoaded;

        public static LiminalMonsterLibrary Instance
        {
            get
            {
                if (!cached) cached = Resources.Load<LiminalMonsterLibrary>(ResourcePath);
                return cached;
            }
        }

        public static GameObject Monitor
        {
            get
            {
                if (!monitorLoaded) { monitor = Resources.Load<GameObject>(MonitorPath); monitorLoaded = true; }
                return monitor;
            }
        }
    }
}
