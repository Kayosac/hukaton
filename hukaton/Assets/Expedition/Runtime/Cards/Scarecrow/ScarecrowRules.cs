using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// Pure rules of «Чучело» (tested in EditMode): which creatures the scarecrow draws away from the player, when it
    /// lets them go, how it grows out of the ground and whether it flares up when destroyed.
    /// </summary>
    public static class ScarecrowRules
    {
        /// <summary>The creature is on its way to (or hunting) its target: chasing, searching, between strikes, threatening.</summary>
        public static bool GoingAtTarget(CreatureState state) => state == CreatureState.Chase || state == CreatureState.Search
            || state == CreatureState.Recover || state == CreatureState.Reposition || state == CreatureState.Threat;

        /// <summary>Calm or heading home: a creature drawn to the scarecrow in this state is let go (back to the player).</summary>
        public static bool Settled(CreatureState state) => state == CreatureState.Idle || state == CreatureState.Wander
            || state == CreatureState.Graze || state == CreatureState.Return || state == CreatureState.Investigate
            || state == CreatureState.Flee || state == CreatureState.Alert || state == CreatureState.Dying;

        /// <summary>
        /// Draw this creature onto the scarecrow? Only aggressive and territorial creatures, only those going at the
        /// player (not mid-swing: a wind-up or a shot finishes first), within <paramref name="radius"/> of the scarecrow,
        /// never one fighting in a beast brawl (the event's own rivals keep each other busy).
        /// </summary>
        public static bool Draws(Temperament temperament, CreatureState state, bool targetsPlayer, bool brawling, float distance, float radius)
        {
            if (brawling || !targetsPlayer || distance > radius) return false;
            if (temperament != Temperament.Aggressive && temperament != Temperament.Territorial) return false;
            return GoingAtTarget(state);
        }

        /// <summary>
        /// Let a creature that fights the scarecrow go: it calmed down or went home (it lost the scarecrow), or it was
        /// dragged far beyond the scarecrow's reach (<paramref name="radius"/> × <paramref name="releaseFactor"/>).
        /// </summary>
        public static bool Releases(CreatureState state, float distance, float radius, float releaseFactor)
            => Settled(state) || distance > radius * Mathf.Max(1, releaseFactor);

        /// <summary>Health after a hit (never below zero; a negative amount does nothing).</summary>
        public static int AfterHit(int health, int amount) => Mathf.Max(0, health - Mathf.Max(0, amount));

        /// <summary>Scale of the card while it shoots up out of the ground: from <paramref name="start"/> with a small overshoot to 1.</summary>
        public static float Growth(float age, float seconds, float start)
        {
            if (seconds <= 0) return 1;
            float k = Mathf.Clamp01(age / seconds);
            float t = k - 1;
            float back = 1 + t * t * (2.70158f * t + 1.70158f);
            return Mathf.LerpUnclamped(start, 1, back);
        }

        /// <summary>Does the scarecrow flare up when destroyed (level III)? Only on destruction, never when its time runs out.</summary>
        public static bool Flares(float burstRadius, bool destroyed) => destroyed && burstRadius > 0;

        /// <summary>A creature is caught by the flare: its body (radius) reaches within the flare's radius of the scarecrow.</summary>
        public static bool CaughtByFlare(Vector3 scarecrow, Vector3 creature, float creatureRadius, float burstRadius)
        {
            Vector3 flat = creature - scarecrow;
            flat.y = 0;
            return burstRadius > 0 && flat.magnitude - Mathf.Max(0, creatureRadius) <= burstRadius && Mathf.Abs(creature.y - scarecrow.y) <= burstRadius + 1;
        }
    }
}
