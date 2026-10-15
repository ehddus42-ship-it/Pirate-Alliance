using UnityEngine;

namespace AcRoguelike.Liminal
{
    /// <summary>Committed projectile groups with clear lanes between them.</summary>
    public static class EnemyFanPattern
    {
        // At 4 m, a 28-degree opening leaves over 1 m between even 0.4 m-radius shots.
        public const float GapDegrees = 28f;
        public const float MinimumHalfAngle = 32f;

        /// <summary>Preserves the shot count, placing three groups around two empty angular lanes.</summary>
        public static float Angle(int index, int count, float halfAngle)
        {
            if (count <= 1) return 0;
            halfAngle = Mathf.Max(MinimumHalfAngle, Mathf.Abs(halfAngle));
            index = Mathf.Clamp(index, 0, count - 1);
            if (count == 2) return index == 0 ? -halfAngle : halfAngle;

            float bandWidth = (halfAngle * 2 - GapDegrees * 2) / 3;
            int firstCount = (count + 2) / 3;
            int middleCount = (count + 1) / 3;
            int group = index < firstCount ? 0 : index < firstCount + middleCount ? 1 : 2;
            int groupStart = group == 0 ? 0 : group == 1 ? firstCount : firstCount + middleCount;
            int groupCount = group == 0 ? firstCount : group == 1 ? middleCount : count - firstCount - middleCount;
            float t = groupCount == 1 ? .5f : (index - groupStart) / (float)(groupCount - 1);
            return -halfAngle + group * (bandWidth + GapDegrees) + bandWidth * t;
        }

        /// <summary>Fixed diagonal lanes for radial/sweeping patterns; apply after any wave rotation.</summary>
        public static bool IsRadialGap(float angle)
        {
            return Mathf.Abs(Mathf.Repeat(angle, 90f) - 45f) <= GapDegrees * .5f;
        }
    }
}
