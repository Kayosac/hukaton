using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// The mushroom's bounce in design units (board, g = 9.81): the fall's equivalent height h_eq = v² / 2g, the bounce
    /// h = clamp(k · h_eq, min, max), the launch speed v_up = √(2g · h). The formula always uses the normal g (decision):
    /// under Moon gravity the same launch carries the player higher than 10 m, as confirmed on the board. When Lightness
    /// limits the fall, the limited (actual) speed is what goes in. A series of bounces (mushroom to mushroom without
    /// standing on the ground, see <see cref="MushroomSeries"/>) never drops below
    /// <see cref="SeriesHeight"/>: each next bounce adds 5 % of the series' first bounce, five times at most.
    /// Pure functions, checked by EditMode tests.
    /// </summary>
    public static class MushroomBounce
    {
        /// <summary>The gravity the board's numbers are written for, m/s².</summary>
        public const float DesignGravity = 9.81f;
        /// <summary>Share of the series' first bounce that every next bounce of the series adds (playtest: +5 %).</summary>
        public const float SeriesStepShare = .05f;
        /// <summary>How many bounces after the first still add height (playtest: up to +25 %, then it stays).</summary>
        public const int SeriesMaxSteps = 5;

        /// <summary>Height of a free fall that ends at this speed (design m/s → m).</summary>
        public static float EquivalentHeight(float downSpeed) => downSpeed * downSpeed / (2 * DesignGravity);

        /// <summary>Speed at the end of a free fall from this height (design m).</summary>
        public static float FallSpeed(float height) => Mathf.Sqrt(2 * DesignGravity * Mathf.Max(0, height));

        /// <summary>Bounce height over the cap: k · h_eq clamped to [min, max].</summary>
        public static float BounceHeight(float downSpeed, float heightReturn, float minHeight, float maxHeight)
            => Mathf.Clamp(heightReturn * EquivalentHeight(Mathf.Abs(downSpeed)), minHeight, Mathf.Max(minHeight, maxHeight));

        /// <summary>Upward speed that reaches this height at g = 9.81 (design m/s).</summary>
        public static float LaunchSpeed(float height) => FallSpeed(height);

        /// <summary>The whole rule: landing speed (design m/s, downward) → launch speed (design m/s, upward).</summary>
        public static float LaunchSpeedFor(float downSpeed, float heightReturn, float minHeight, float maxHeight)
            => LaunchSpeed(BounceHeight(downSpeed, heightReturn, minHeight, maxHeight));

        /// <summary>
        /// The series' line for bounce number <paramref name="step"/> of a series (0 = the first bounce):
        /// first · (1 + share · min(step, maxSteps)). With the defaults: +5 % per bounce, +25 % from the sixth bounce on.
        /// </summary>
        public static float SeriesHeight(float firstHeight, int step, float stepShare = SeriesStepShare, int maxSteps = SeriesMaxSteps)
            => firstHeight * (1 + Mathf.Max(0, stepShare) * Mathf.Clamp(step, 0, Mathf.Max(0, maxSteps)));

        /// <summary>
        /// A bounce inside a series: the fall's own bounce (<see cref="BounceHeight"/>) or the series' line, whichever is
        /// higher, so the height-return share no longer shrinks consecutive bounces. The series never lifts above
        /// <paramref name="maxHeight"/> (a bigger fall may still give its own, already capped, height).
        /// </summary>
        public static float SeriesBounceHeight(float fallBounceHeight, float firstHeight, int step, float maxHeight,
            float stepShare = SeriesStepShare, int maxSteps = SeriesMaxSteps)
            => Mathf.Max(fallBounceHeight, Mathf.Min(SeriesHeight(firstHeight, step, stepShare, maxSteps), maxHeight));
    }
}
