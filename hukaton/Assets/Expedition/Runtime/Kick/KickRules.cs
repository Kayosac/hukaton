namespace Hakaton.Expeditions
{
    /// <summary>How a creature takes the player's kick (set per creature prefab).</summary>
    public enum KickResponse
    {
        /// <summary>Hurt, knocked back by its weight and knocked off its feet.</summary>
        Normal,
        /// <summary>The leg goes through it: swarms, the sheet ghost.</summary>
        Immune,
        /// <summary>Rooted (the fly agaric turret): hurt, but it does not budge and is not stunned.</summary>
        Anchored,
    }

    /// <summary>
    /// The kick's numbers (board «Пинок и трофейные карты»): it is for control, not for killing — one point of damage,
    /// a shove that heavy creatures shrug off and a short stun only the small ones feel. Pure rules for tests.
    /// </summary>
    public static class KickRules
    {
        /// <summary>Health a kick takes off a creature (the ice shell doubles it as for any physical hit).</summary>
        public const int Damage = 1;

        /// <summary>Stun by the creature's toughness: up to 5 HP — 0.6 s, 6–13 HP — 0.3 s, from 14 HP — none.</summary>
        public static float StaggerSeconds(int maximumHealth, KickResponse response)
        {
            if (response != KickResponse.Normal) return 0;
            if (maximumHealth <= 5) return .6f;
            if (maximumHealth <= 13) return .3f;
            return 0;
        }

        /// <summary>Share of the kick's shove a creature takes by its toughness: small ones fly, big ones barely move.</summary>
        public static float ShoveShare(int maximumHealth, KickResponse response)
        {
            if (response != KickResponse.Normal) return 0;
            if (maximumHealth <= 5) return 1;
            if (maximumHealth <= 13) return .5f;
            return .12f;
        }

        /// <summary>Whether a kick can land on a creature at all.</summary>
        public static bool Lands(KickResponse response, bool alive, bool burrowed) => alive && !burrowed && response != KickResponse.Immune;
    }
}

