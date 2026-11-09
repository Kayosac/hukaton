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
        [Tooltip("Sideways acceleration at full spin and maximum throw speed, m/s²; scales with the card's speed.")]
        [Min(0)] public float curveAcceleration = 22;
        [Tooltip("Vertical acceleration at full up/down spin, m/s². Above gravity: full up-spin makes the card climb off the hand.")]
        [Min(0)] public float liftAcceleration = 15;
        [Tooltip("Longest time up/down spin lift stays at full strength, seconds. Matters for near-vertical throws that never stall.")]
        [Min(0)] public float liftHoldSeconds = 2.5f;
        [Tooltip("Below this speed, m/s, the card has stalled at the top of its climb: the lift lets go and it falls.")]
        [Min(0)] public float liftReleaseSpeed = 3;
        [Tooltip("Seconds over which the lift then wears off to nothing; after that only gravity acts, so the card slows on the way up and falls faster and faster.")]
        [Min(0)] public float liftFadeSeconds = .6f;
        [Tooltip("Mouse travel (any direction, Shift held) for a full-strength spin, pixels.")]
        [Min(1)] public float gesturePixels = 220;
        [Tooltip("Share of the player's own planar velocity added to the throw.")]
        [Range(0, 1)] public float inheritVelocity = .25f;
        [Min(.01f)] public float cardRadius = .06f;
        [Min(1)] public float projectileLife = 6;
        [Header("Aiming")]
        [Tooltip("Preview length at full charge, seconds of flight. Short on purpose: aiming stays a skill.")]
        [Min(0)] public float previewSeconds = .55f;
        [Tooltip("Extra preview at full spin, seconds, so the curve the spin will cause is visible before the throw.")]
        [Min(0)] public float spinPreviewSeconds = .6f;
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
        [Range(0, 1)] public float defeatLootLoss = .5f;
        [Tooltip("Below this the world is out of bounds.")]
        public float killHeight = -8;
        [Header("Water")]
        [Tooltip("Water at least this deep over the player's feet, m, drowns at once. Shallower water does nothing.")]
        [Min(.1f)] public float drownDepth = 1f;
        [Tooltip("How long the view darkens while the body sinks before the defeat fade, seconds.")]
        [Min(.05f)] public float drownSeconds = 1.8f;
        [Tooltip("Sinking speed while drowning, m/s.")]
        [Min(0)] public float drownSinkSpeed = .9f;
        [Tooltip("How quickly the screen darkens once the head goes under, seconds.")]
        [Min(.01f)] public float drownDarkenSeconds = .12f;
        [Tooltip("Thrashing under water: the head heads for a new direction this often, seconds (random within the range).")]
        public Vector2 drownTurnSeconds = new Vector2(.45f, .75f);
        [Tooltip("How softly the head follows each new direction, seconds; larger is smoother and slower.")]
        [Min(.01f)] public float drownTurnSmoothing = .28f;
        [Tooltip("Each new direction is this far left or right of where the player was looking, degrees; sides alternate.")]
        public Vector2 drownTurnYaw = new Vector2(45, 95);
        [Tooltip("Each new direction looks up or down to this pitch, degrees (negative is up).")]
        public Vector2 drownTurnPitch = new Vector2(-60, 35);
        [Tooltip("When it is clear the player is drowning, the cards burst out of the hand: delay after going under, seconds.")]
        [Min(0)] public float drownCardsDelay = .2f;
        [Tooltip("Speed the cards fly out of the hand at, m/s.")]
        [Min(0)] public float drownCardsSpeed = 3.2f;
        [Tooltip("How dark the screen gets while sinking, before the defeat fade.")]
        [Range(0, 1)] public float drownDarkness = .72f;
        [Tooltip("Colour of the darkening under water; the defeat fade keeps it.")]
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
