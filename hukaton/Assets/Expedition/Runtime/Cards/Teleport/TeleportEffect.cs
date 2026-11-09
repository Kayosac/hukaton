using Hakaton.Character;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Телепорт» (тьма*, ранг 5/5, без улучшения). At the card's first contact (floor, wall, ceiling, an object or a
    /// creature) the player moves to the nearest free capsule pose on the side of the contact with a minimal gap — by a
    /// wall or under a ceiling that may be in the air; the shift never passes through an obstacle and never lifts onto a
    /// ledge (<see cref="PlayerCapsulePlacement"/>). No free pose → no move (the card is spent anyway). The full world
    /// velocity at the moment of the CARD'S CONTACT is kept, as are the view and every running effect's timer (Moon
    /// gravity goes on); no freeze, no impulse, no immunity. A player hanging on a rope lets go first and carries the
    /// rope's velocity (<see cref="FirstPersonMotor.TeleportKeepingVelocity"/>).
    /// Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
    /// </summary>
    public sealed class TeleportEffect : CardEffect
    {
        [Header("Placement")]
        [Tooltip("Gap between the contact surface and the player's capsule, m.")]
        [Min(0)] public float gap = .05f;
        [Tooltip("Furthest the arrival pose may be shifted from the contact to find room, m.")]
        [Min(0)] public float maxShift = 1.2f;
        [Tooltip("Highest upward shift while looking for room: the player is never lifted onto a ledge, m.")]
        [Min(0)] public float maxRise = .3f;

        [Header("Presentation")]
        public VfxBurst departVfx;
        public VfxBurst arriveVfx;
        public SfxCue teleportSound;
        public Color tint = new Color(.62f, .36f, .95f);
        [Min(0)] public float fovPunch = 70;

        public override void Apply(in ImpactContext c)
        {
            var motor = c.Motor;
            if (!TryFindArrival(motor, c.Point, c.Normal, out var feet))
            {
                Feedback(c, false);
                c.World.Announce("Телепорт: там нет места", 1.6f);
                return;
            }
            Vector3 departed = CapsuleCentre(motor);
            MoveFeetTo(motor, feet);
            Vector3 arrived = CapsuleCentre(motor);
            var world = c.World;
            world.Spawn(departVfx, departed, Quaternion.identity, tint);
            world.Spawn(arriveVfx, arrived, Quaternion.identity, tint);
            world.Play(teleportSound, arrived);
            if (world.feel) { world.feel.PunchFov(fovPunch); world.feel.AddTrauma(.15f); }
            Feedback(c, true);
        }

        /// <summary>Search settings for the player's controller (its skin counts as part of the capsule).</summary>
        public PlayerCapsulePlacement.Settings SettingsFor(CharacterController controller)
        {
            var settings = PlayerCapsulePlacement.Settings.For(controller.radius + controller.skinWidth, controller.height);
            settings.gap = gap;
            settings.maxShift = maxShift;
            settings.maxRise = maxRise;
            return settings;
        }

        public bool TryFindArrival(FirstPersonMotor motor, Vector3 point, Vector3 normal, out Vector3 feet)
            => PlayerCapsulePlacement.TryFind(point, normal, SettingsFor(motor.characterController), out feet);

        /// <summary>The bottom of the player's capsule in the world.</summary>
        public static Vector3 Feet(FirstPersonMotor motor)
        {
            var controller = motor.characterController;
            return motor.transform.TransformPoint(controller.center) - Vector3.up * controller.height * .5f;
        }

        public static Vector3 CapsuleCentre(FirstPersonMotor motor) => motor.transform.TransformPoint(motor.characterController.center);

        /// <summary>Moves the player so the capsule's bottom is at <paramref name="feet"/>, keeping velocity, view and modifiers.</summary>
        public static void MoveFeetTo(FirstPersonMotor motor, Vector3 feet)
        {
            Vector3 offset = motor.transform.position - Feet(motor);
            motor.TeleportKeepingVelocity(feet + offset);
        }
    }
}
