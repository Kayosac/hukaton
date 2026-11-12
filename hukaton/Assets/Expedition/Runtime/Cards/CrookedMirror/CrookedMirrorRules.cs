using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>Pure rules of «Кривое зеркало» (EditMode-tested).</summary>
    public static class CrookedMirrorRules
    {
        /// <summary>A shrunk creature's strike or shot: damage × multiplier, never below ½ heart.</summary>
        public static int Damage(int amount, float multiplier) => WorldCreature.ScaledDamage(amount, multiplier);

        /// <summary>Mass of the shrunk body: Impulse pushes it 1 / multiplier times faster.</summary>
        public static float Mass(float authoredMass, float multiplier) => Mathf.Max(.01f, authoredMass * Mathf.Clamp(multiplier, .01f, 1));

        /// <summary>
        /// Repeated mirrors on a cube or mushroom do not stack: it grows to the larger of what it has and the new
        /// multiplier (both relative to its size before the first mirror). Never smaller than it was.
        /// </summary>
        public static float Growth(float current, float card) => Mathf.Max(1, Mathf.Max(current, card));

        /// <summary>
        /// Repeated mirrors on a creature: the latest multiplier replaces the previous one (like every timed card
        /// modifier) and it stays small until the later of the two ends.
        /// </summary>
        public static float Until(float currentUntil, float now, float seconds) => Mathf.Max(currentUntil > now ? currentUntil : now, now + Mathf.Max(0, seconds));

        /// <summary>
        /// The crooked part of the look at <paramref name="time"/> since the change: the drawing's width and height
        /// factors (a funhouse mirror: one side stretched, the other squeezed) and its lean in degrees. Settles to a
        /// steady skew of <paramref name="skew"/> after a decaying wobble.
        /// </summary>
        public static Vector3 Crook(float time, float seed, float skew, float lean, float wobbleSeconds)
        {
            float decay = wobbleSeconds > 0 ? Mathf.Exp(-Mathf.Max(0, time) * 3 / wobbleSeconds) : 0;
            float wave = Mathf.Sin(time * 17 + seed) * decay;
            float drift = Mathf.Sin(time * 1.3f + seed * 2) * .35f;
            float width = 1 + skew * (1 + drift * .3f) + wave * .22f;
            float height = 1 - skew * .6f * (1 - drift * .3f) - wave * .18f;
            float tilt = lean * (1 + drift * .25f) + wave * 14;
            return new Vector3(width, height, tilt);
        }
    }
}
