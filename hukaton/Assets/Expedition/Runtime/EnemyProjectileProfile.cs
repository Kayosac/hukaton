using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// What a creature shoots: the projectile prefab, how it flies, what it does on a hit, its effects and sounds.
    /// One asset per kind of projectile; several creatures may share it. When and how often a creature shoots, and
    /// how many at once, is set on the creature (<see cref="WorldCreature.ranged"/>).
    /// </summary>
    [CreateAssetMenu(menuName = "Hakaton/Expedition/Enemy projectile")]
    public sealed class EnemyProjectileProfile : ScriptableObject
    {
        public enum Path { Straight, Arc }
        public enum AimPoint { Body, Feet }

        [Tooltip("The flying object: picture, trail. Must have an EnemyProjectile component.")]
        public EnemyProjectile prefab;

        [Header("Flight")]
        [Tooltip("Straight: a line through the aim point. Arc: lobbed, rises and falls through the aim point and keeps falling after it.")]
        public Path path = Path.Straight;
        [Tooltip("Speed along the ground, m/s.")]
        [Min(.5f)] public float speed = 12;
        [Tooltip("Arc only: how high above the straight line the middle of the flight rises, metres.")]
        [Min(0)] public float arcHeight = 1.5f;
        [Tooltip("Turns toward the target while flying, degrees per second. 0: no homing. Best with a straight path.")]
        [Min(0)] public float homingTurnRate;
        [Tooltip("Gone after flying this far, metres.")]
        [Min(1)] public float maxDistance = 30;
        [Tooltip("Radius of the hit check, metres.")]
        [Min(.02f)] public float radius = .2f;
        [Tooltip("Body: aimed at the middle of the target. Feet: at the ground under it (lobbed splashes).")]
        public AimPoint aimAt = AimPoint.Body;

        [Header("Hit")]
        [Tooltip("Damage: ½ heart per unit to the player, 1 HP per unit to creatures and allies.")]
        [Min(0)] public int damage = 1;
        public DamageKind kind = DamageKind.Hit;
        [Tooltip("Push along the flight and upward on a hit (impulse units, as the melee attacks).")]
        [Min(0)] public float knockback = 3, knockbackUp = 1.5f;
        [Tooltip("Hits every fighter within this radius of the impact as well, metres. 0: only what it hits directly.")]
        [Min(0)] public float splashRadius;
        [Tooltip("Hits other creatures on its way (but never the one that shot it).")]
        public bool hitsCreatures = true;

        [Header("Effects on a hit")]
        [Tooltip("Debuffs a hit puts on the player, each with its own chance (the player's debuff system, board «Дебаффы»).")]
        public StatusInflict[] playerStatuses = new StatusInflict[0];
        [Tooltip("Movement speed multiplier while slowed — creatures only; the player gets debuffs from the list above.")]
        [Range(0, 1)] public float slowScale = 1;
        [Min(0)] public float slowSeconds;
        [Tooltip("Freezes a creature; the player cannot move for this long (not a debuff).")]
        [Min(0)] public float freezeSeconds;
        [Tooltip("Knocks a creature off its feet (interrupts its attack). No effect on the player.")]
        [Min(0)] public float stunSeconds;

        [Header("Cards")]
        [Tooltip("A thrown card that touches it knocks it out of the air; the card flies on.")]
        public bool cardsShootDown = true;

        [Header("Presentation")]
        public VfxBurst muzzleFlash;
        public VfxBurst impact;
        public VfxBurst shotDown;
        public Color impactTint = Color.white;
        [Min(.05f)] public float effectScale = 1;
        public SfxCue fireCue, impactCue, shotDownCue;
        [Tooltip("Played in a loop while it flies; optional.")]
        public SfxCue flightLoop;

        public bool HasEffects => slowSeconds > 0 && slowScale < 1 || freezeSeconds > 0 || stunSeconds > 0
            || playerStatuses != null && playerStatuses.Length > 0;

        /// <summary>Arc height of the flight: zero for a straight shot.</summary>
        public float FlightArc => path == Path.Arc ? arcHeight : 0;
    }
}
