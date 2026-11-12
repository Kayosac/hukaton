using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>Tuning for one outing: card flight, presentation, player vitals. Card power lives on each CardDefinition.</summary>
    [CreateAssetMenu(menuName = "Hakaton/Expedition/Settings")]
    public sealed class ExpeditionSettings : ScriptableObject
    {
        [Header("Card flight (board v0.1: charge 0.65 s, 6–18 m/s, g 9.81)")]
        [Min(.05f)] public float chargeSeconds = .65f;
        [Min(.1f)] public float minimumSpeed = 6, maximumSpeed = 18;
        [Min(0)] public float cardGravity = 9.81f;
        [Tooltip("Share of the player's own planar velocity added to the throw.")]
        [Range(0, 1)] public float inheritVelocity = .25f;
        [Min(.01f)] public float cardRadius = .06f;
        [Min(1)] public float projectileLife = 6;
        [Header("Aiming")]
        [Tooltip("Preview length at full charge, seconds of flight. Short on purpose: aiming stays a skill.")]
        [Min(0)] public float previewSeconds = .55f;
        [Min(1)] public float aimConvergeDistance = 18;
        [Header("Hand")]
        [Tooltip("Input grace after the Return card appears, so a held click can't send you home by accident.")]
        [Min(0)] public float returnGrace = .6f;
        [Min(0)] public float discardCooldown = .65f;
        [Header("Player")]
        [Min(2)] public int maximumHalfHearts = 8;
        [Min(0)] public float hurtInvulnerability = .75f;
        [Tooltip("Fall-damage thresholds in reference speed (m/s at g 9.81): 11 / 14 / 17 → ½ / 1 / 2 hearts.")]
        [Min(0)] public float fallDamageSpeed = 11;
        [Min(.1f)] public float fallDamageStep = 3;
        [Min(.5f)] public float interactionReach = 3.2f;
        [Tooltip("Creature drops within this distance of the player fly into the bag by themselves (metres).")]
        [Min(0)] public float lootMagnetRadius = 2;
        [Tooltip("A fresh drop is left to fly out of the creature for this long before it can be pulled in (seconds).")]
        [Min(0)] public float lootMagnetDelay = .5f;
        [Range(0, 1)] public float defeatLootLoss = .5f;
        [Tooltip("Below this the world is out of bounds.")]
        public float killHeight = -8;
        [Header("Water")]
        [Tooltip("Water at least this deep over the player's feet, m, drowns at once. Shallower water does nothing.")]
        [Min(.1f)] public float drownDepth = 1f;
        [Tooltip("Deep water: the screen goes black this fast, seconds.")]
        [Min(.01f)] public float drownDarkenSeconds = .25f;
        [Tooltip("Speed the cards fly out of the hand at, m/s.")]
        [Min(0)] public float drownCardsSpeed = 2.4f;
        [Tooltip("Then the body keeps sinking in the dark this long before the shop, seconds. The mouse still looks around.")]
        [Min(0)] public float drownSeconds = .6f;
        [Tooltip("Sinking speed while drowning, m/s.")]
        [Min(0)] public float drownSinkSpeed = .9f;
        [Tooltip("Colour of the darkness under water.")]
        public Color drownColour = new Color(.02f, .07f, .09f, 1);
        [Header("Creature physics (board v0.1)")]
        [Tooltip("Normal impact speed for 1 / 2 / 4 damage: 6 / 9 / 12 m/s.")]
        [Min(0)] public float collisionDamageSpeed = 6;
        [Min(.1f)] public float collisionDamageStep = 3;
        [Header("Transitions")]
        [Min(.05f)] public float returnFadeSeconds = .7f;
        [Min(.05f)] public float arriveFadeSeconds = .55f;
    }
}
