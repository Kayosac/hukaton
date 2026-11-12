using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// Where each card of the first-person hand sits, for a hand of 1 to <see cref="MaxCards"/> cards and the view's
    /// aspect. Small hands keep the natural arc (radius, degrees per card). Bigger hands flatten it, so the outermost
    /// card sinks at most <c>maxSink</c> below the top of the arc (the screen's bottom edge cuts every card about the
    /// same), and squeeze the spacing, so the outermost card's outer edge stays within <c>screenShare</c> of the
    /// half-screen width at the card depth (the bottom corners stay free for the HUD). Pure maths: the hand view and
    /// its tests share it.
    /// </summary>
    public readonly struct CardFanLayout
    {
        /// <summary>The most cards fanned out at once; a bigger (cheat) hand scrolls a window of this size.</summary>
        public const int MaxCards = 20;
        const float StraightBelow = .01f;

        /// <summary>Cards in the fan.</summary>
        public readonly int Count;
        /// <summary>Arc angle (and roll) of the outermost card, degrees.</summary>
        public readonly float HalfAngle;
        /// <summary>Horizontal distance of the outermost card centre from the middle, metres.</summary>
        public readonly float HalfSpread;
        /// <summary>Radius of the (flattened) arc, metres; infinite for a straight row.</summary>
        public readonly float ArcRadius;

        CardFanLayout(int count, float halfAngle, float halfSpread)
        {
            Count = count;
            HalfAngle = halfAngle;
            HalfSpread = halfSpread;
            ArcRadius = halfAngle >= StraightBelow ? halfSpread / Mathf.Sin(halfAngle * Mathf.Deg2Rad) : float.PositiveInfinity;
        }

        /// <param name="count">Cards in the fan.</param>
        /// <param name="radius">Natural arc radius, metres.</param>
        /// <param name="degreesPerCard">Natural angle between neighbouring cards.</param>
        /// <param name="cardSize">Card width and height at rest, metres.</param>
        /// <param name="depth">Distance of the cards in front of the camera, metres.</param>
        /// <param name="maxSink">How far the outermost card may sink below the top of the arc, metres.</param>
        /// <param name="screenShare">Share of the half-screen width the outermost card's outer edge may reach.</param>
        /// <param name="verticalFov">Vertical field of view, degrees.</param>
        /// <param name="aspect">View width / height.</param>
        public static CardFanLayout Solve(int count, float radius, float degreesPerCard, Vector2 cardSize, float depth,
            float maxSink, float screenShare, float verticalFov, float aspect)
        {
            if (count <= 1) return new CardFanLayout(Mathf.Max(count, 0), 0, 0);
            float natural = Mathf.Clamp((count - 1) * .5f * degreesPerCard, 0, 80);
            float naturalSpread = Mathf.Max(radius, 0) * Mathf.Sin(natural * Mathf.Deg2Rad);
            float halfWidth = Mathf.Max(depth, 0) * Mathf.Tan(Mathf.Clamp(verticalFov, 1, 179) * .5f * Mathf.Deg2Rad) * Mathf.Max(aspect, .01f);
            float limit = halfWidth * Mathf.Clamp01(screenShare);
            float angle = natural, spread = naturalSpread;
            // The outer card's footprint grows with its roll and the roll with the spread's flattening. Each round fits
            // the spread to the previous (never smaller) roll, so the final card always stays inside the limit.
            for (int round = 0; round < 4; round++)
            {
                spread = Mathf.Clamp(limit - Footprint(cardSize, angle), 0, naturalSpread);
                angle = spread > 0 ? Mathf.Min(natural, 2 * Mathf.Atan2(Mathf.Max(maxSink, 0), spread) * Mathf.Rad2Deg) : 0;
            }
            return new CardFanLayout(count, angle, spread);
        }

        /// <summary>
        /// Half the horizontal extent of a card rolled by up to <paramref name="rollDegrees"/> (the widest roll in
        /// that range, so it never shrinks as the roll grows).
        /// </summary>
        static float Footprint(Vector2 cardSize, float rollDegrees)
        {
            float widest = Mathf.Atan2(cardSize.y, Mathf.Max(cardSize.x, 1e-6f));
            float roll = Mathf.Min(Mathf.Abs(rollDegrees) * Mathf.Deg2Rad, widest);
            return cardSize.x * .5f * Mathf.Cos(roll) + cardSize.y * .5f * Mathf.Sin(roll);
        }

        /// <summary>-1 for the leftmost card, 1 for the rightmost, 0 in the middle.</summary>
        public float Side(int index) => Count <= 1 ? 0 : (index - (Count - 1) * .5f) / ((Count - 1) * .5f);

        /// <summary>Roll of a card along the arc, degrees: positive on the right half (the card leans right).</summary>
        public float Roll(int index) => Side(index) * HalfAngle;

        /// <summary>A card centre relative to the top of the arc: x to the right, y up (never above zero).</summary>
        public Vector2 Offset(int index)
        {
            float side = Side(index);
            if (float.IsPositiveInfinity(ArcRadius)) return new Vector2(side * HalfSpread, 0);
            float angle = side * HalfAngle * Mathf.Deg2Rad;
            return new Vector2(ArcRadius * Mathf.Sin(angle), -ArcRadius * (1 - Mathf.Cos(angle)));
        }
    }
}
