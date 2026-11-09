using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// The mushroom's bounce in design units (board, g = 9.81): the fall's equivalent height h_eq = v² / 2g, the bounce
    /// h = clamp(k · h_eq, min, max), the launch speed v_up = √(2g · h). The formula always uses the normal g (decision):
    /// under Moon gravity the same launch carries the player higher than 10 m, as confirmed on the board. When Lightness
    /// limits the fall, the limited (actual) speed is what goes in. Pure functions, checked by EditMode tests.
    /// </summary>
    public static class MushroomBounce
    {
        /// <summary>The gravity the board's numbers are written for, m/s².</summary>
        public const float DesignGravity = 9.81f;

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
    }
}
