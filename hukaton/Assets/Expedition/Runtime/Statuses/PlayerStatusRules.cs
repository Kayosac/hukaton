using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>The arithmetic of player debuffs, kept apart from Unity objects so it can be tested.</summary>
    public static class PlayerStatusRules
    {
        /// <summary>A repeat hit only refreshes the timer: it ends at the later of the old end and now + seconds.</summary>
        public static float RefreshedUntil(float currentUntil, bool active, float now, float seconds)
            => Mathf.Max(active ? currentUntil : now, now + Mathf.Max(0, seconds));

        /// <summary>
        /// Rot: half hearts taken off the maximum after another hit of <paramref name="add"/>; one whole heart
        /// (two halves) always stays.
        /// </summary>
        public static int RotAfter(int rotted, int add, int maximumHalfHearts)
            => Mathf.Clamp(rotted + Mathf.Max(0, add), 0, Mathf.Max(0, maximumHalfHearts - 2));

        /// <summary>Damage after Fragile's multiplier (never below the original, rounded up to whole half hearts).</summary>
        public static int FragileDamage(int halfHearts, float multiplier)
            => halfHearts <= 0 ? halfHearts : Mathf.Max(halfHearts, Mathf.CeilToInt(halfHearts * Mathf.Max(1, multiplier) - .001f));

        /// <summary>
        /// Fog density that leaves about 5 % of the colour at <paramref name="metres"/> for the given fog mode
        /// (exp: e^-3, exp²: e^-(1.73)²); never thinner than the scene's own.
        /// </summary>
        public static float BlindFogDensity(FogMode mode, float metres, float sceneDensity)
        {
            metres = Mathf.Max(.5f, metres);
            float density = mode == FogMode.Exponential ? 3f / metres : 1.73f / metres;
            return Mathf.Max(sceneDensity, density);
        }

        /// <summary>A card's flight turned by up to <paramref name="degrees"/> sideways and half that up or down.</summary>
        public static Vector3 Crook(Vector3 velocity, Vector3 up, Vector3 right, float degrees, float yawRoll, float pitchRoll)
        {
            if (degrees <= 0 || velocity.sqrMagnitude < 1e-6f) return velocity;
            float yaw = Mathf.Lerp(-degrees, degrees, Mathf.Clamp01(yawRoll));
            float pitch = Mathf.Lerp(-degrees, degrees, Mathf.Clamp01(pitchRoll)) * .5f;
            return Quaternion.AngleAxis(yaw, up) * Quaternion.AngleAxis(pitch, right) * velocity;
        }

        /// <summary>Leaky pocket: how many drops this much walking makes, keeping the rest of the distance for the next.</summary>
        public static int Drops(ref float walked, float step)
        {
            if (step <= 0) return 0;
            int drops = Mathf.FloorToInt(walked / step);
            walked -= drops * step;
            return drops;
        }
    }
}
