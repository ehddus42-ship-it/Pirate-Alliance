namespace AcRoguelike
{
    /// <summary>Shared travel-speed tuning for player, support and enemy projectiles.</summary>
    public static class ProjectileTuning
    {
        public const float SpeedMultiplier = .6f;

        public static float ScaleSpeed(float baseSpeed) => baseSpeed * SpeedMultiplier;

        // Fixed-path throws need more flight time to keep their original destination and arc.
        public static float ScaleFlightDuration(float baseDuration) => baseDuration / SpeedMultiplier;
    }
}
