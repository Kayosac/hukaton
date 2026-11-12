using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>Pure rules of «Тягучий час» (EditMode-tested).</summary>
    public static class StickyHourRules
    {
        /// <summary>«N times slower» as a clock multiplier: 2 → ½, 4 → ¼. Never faster than normal.</summary>
        public static float TimeScale(float slowdown) => 1 / Mathf.Max(1, slowdown);

        /// <summary>
        /// Is a point inside the zone: a vertical cylinder of <paramref name="radius"/> around <paramref name="centre"/>
        /// from <paramref name="depth"/> below to <paramref name="height"/> above it. <paramref name="margin"/> widens it
        /// (a body's own radius: touching the edge counts).
        /// </summary>
        public static bool Inside(Vector3 point, Vector3 centre, float radius, float height, float depth, float margin = 0)
        {
            float dx = point.x - centre.x, dz = point.z - centre.z, r = radius + Mathf.Max(0, margin);
            return dx * dx + dz * dz <= r * r && point.y >= centre.y - depth && point.y <= centre.y + height;
        }

        /// <summary>A timer of <paramref name="seconds"/> inside the zone lasts this long by the world's clock.</summary>
        public static float SlowedSeconds(float seconds, float timeScale) => seconds / Mathf.Max(.01f, timeScale);
    }
}
