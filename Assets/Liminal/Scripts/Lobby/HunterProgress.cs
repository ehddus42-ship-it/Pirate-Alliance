using UnityEngine;

namespace AcRoguelike.Liminal
{
    /// <summary>
    /// Persistent meta progression between runs: magic stones (마석) earned in gates, spent on permanent
    /// upgrades with the Hunter Association agent in the lobby. Stored in PlayerPrefs.
    /// </summary>
    public static class HunterProgress
    {
        public enum Upgrade { Vitality, Swordsmanship, Evasion, Counter }

        public struct Info
        {
            public string name, effect;
            public int maxLevel;
            public int[] costs;
        }

        static readonly Info[] Infos =
        {
            new Info { name = "체력 단련", effect = "최대 체력 +12", maxLevel = 5, costs = new[] { 40, 70, 110, 160, 220 } },
            new Info { name = "검술 수련", effect = "공격 피해 +6%", maxLevel = 5, costs = new[] { 50, 85, 130, 185, 250 } },
            new Info { name = "회피 훈련", effect = "대시 재사용 -6%", maxLevel = 4, costs = new[] { 45, 90, 150, 220 } },
            new Info { name = "반격 각성", effect = "저스트 회피 반격 시간 +0.25초 · 반격 피해 +10%", maxLevel = 3, costs = new[] { 80, 150, 240 } },
        };

        const string CurrencyKey = "Hunter.MagicStones";

        public static Info Describe(Upgrade upgrade) => Infos[(int)upgrade];
        public static int Count => Infos.Length;

        public static int Currency
        {
            get => PlayerPrefs.GetInt(CurrencyKey, 0);
            private set { PlayerPrefs.SetInt(CurrencyKey, Mathf.Max(0, value)); PlayerPrefs.Save(); }
        }

        public static void Earn(int amount) { if (amount > 0) Currency += amount; }

        public static int Level(Upgrade upgrade) => Mathf.Clamp(PlayerPrefs.GetInt("Hunter.Upgrade." + upgrade, 0), 0, Infos[(int)upgrade].maxLevel);

        /// <summary>Price of the next level, or -1 when the upgrade is maxed.</summary>
        public static int Cost(Upgrade upgrade)
        {
            int level = Level(upgrade);
            var info = Infos[(int)upgrade];
            return level >= info.maxLevel ? -1 : info.costs[level];
        }

        public static bool TryBuy(Upgrade upgrade)
        {
            int cost = Cost(upgrade);
            if (cost < 0 || Currency < cost) return false;
            Currency -= cost;
            PlayerPrefs.SetInt("Hunter.Upgrade." + upgrade, Level(upgrade) + 1);
            PlayerPrefs.Save();
            return true;
        }

        static float baseDashCooldown = -1, baseCounterSeconds = -1, baseCounterMultiplier = -1;

        /// <summary>Applies every permanent upgrade to the player before a run starts.</summary>
        public static void Apply(GameObject player)
        {
            if (!player) return;
            var health = player.GetComponent<LiminalPlayerHealth>();
            if (health) health.baseMaximum = 100 + 12 * Level(Upgrade.Vitality);
            var melee = player.GetComponent<MeleeSlash>();
            var motor = player.GetComponent<PlayerMotor>();
            var dodge = player.GetComponent<JustDodgeFeedback>();
            if (melee)
            {
                melee.damageMultiplier = 1 + .06f * Level(Upgrade.Swordsmanship);
                if (baseCounterMultiplier < 0) baseCounterMultiplier = melee.counterMultiplier;
                melee.counterMultiplier = baseCounterMultiplier + .1f * Level(Upgrade.Counter);
            }
            if (motor)
            {
                if (baseDashCooldown < 0) baseDashCooldown = motor.dashCooldown;
                motor.dashCooldown = baseDashCooldown * (1 - .06f * Level(Upgrade.Evasion));
            }
            if (dodge)
            {
                if (baseCounterSeconds < 0) baseCounterSeconds = dodge.counterSeconds;
                dodge.counterSeconds = baseCounterSeconds + .25f * Level(Upgrade.Counter);
            }
        }
    }
}
