using UnityEngine;

namespace DD2Estate.Dungeon
{
    /// <summary>The curves DD1's results screens move by (its tweens name them: easeInOutQuad, easeInOutElastic).</summary>
    internal static class RaidResultsEase
    {
        public static float InOutQuad(float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f ? 2f * t * t : 1f - (-2f * t + 2f) * (-2f * t + 2f) * 0.5f;
        }

        /// <summary>Overshoots either end and swings in: the experience line of the second page comes in by it.</summary>
        public static float InOutElastic(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            const float c5 = 2f * Mathf.PI / 4.5f;
            return t < 0.5f
                ? -(Mathf.Pow(2f, 20f * t - 10f) * Mathf.Sin((20f * t - 11.125f) * c5)) * 0.5f
                : Mathf.Pow(2f, -20f * t + 10f) * Mathf.Sin((20f * t - 11.125f) * c5) * 0.5f + 1f;
        }

        /// <summary>There and back: 0 at either end, 1 in the middle (a total's swell).</summary>
        public static float Pulse(float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f ? InOutQuad(t * 2f) : InOutQuad((1f - t) * 2f);
        }
    }
}
