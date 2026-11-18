using System.Collections.Generic;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Вербовка» (trophy card of the anthill, pg_anthill-loot-cards). Every ant within 3 / 5 / 7 m (levels I / II / III)
    /// of the hit goes over to the player's side with no time limit — until it dies or the outing ends
    /// (<see cref="WorldCreature.Recruit"/>): the worker, the plug, the winged ant and the bubble larva follow the player
    /// and fight for them, the machine-gun crew holds its post and shoots at the player's foes. Which creatures are ants:
    /// those of the same name as <see cref="recruits"/> (the Queen is not among them: the card does nothing to her).
    /// Nothing to recruit in the zone (above ground, a miss): a short note, the card is spent.
    /// </summary>
    public sealed class RecruitmentEffect : CardEffect, ICardStatLine, ICardArea
    {
        [Header("Board numbers (I / II / III)")]
        [Tooltip("Every ant within this distance of the hit is recruited, m (pg_anthill-loot-cards: 3 / 5 / 7).")]
        public LevelFloat radius = new LevelFloat(3, 5, 7);

        [Header("Who")]
        [Tooltip("Creatures that can be recruited: those of the same name as these prefabs (the anthill's ants, not the Queen).")]
        public WorldCreature[] recruits = new WorldCreature[0];
        [Tooltip("Of those, the ones that hold their ground instead of following the player (the machine-gun crew).")]
        public WorldCreature[] holdGround = new WorldCreature[0];
        public RecruitOrders orders = new RecruitOrders();

        [Header("Look")]
        [Tooltip("A burst on every ant that goes over (Retro Arsenal).")]
        public VfxBurst recruitBurst;
        [Min(0)] public float recruitBurstScale = 1.2f;
        public Color recruitBurstTint = new Color(.55f, 1f, .62f);
        [Tooltip("Played on every ant that goes over.")]
        public SfxCue recruitSound;
        [Tooltip("The fight's centre is the ground found this far below a wall or creature hit, m.")]
        [Min(0)] public float dropDistance = 4;
        [Range(0, 90)] public float groundSlope = 60;
        [Tooltip("Shown when there is nobody to recruit.")]
        public string nothingNote = "Тут некого вербовать";

        static readonly List<WorldCreature> Found = new List<WorldCreature>();

        public override void Apply(in ImpactContext c)
        {
            var world = c.World;
            if (!CardGround.TryFind(c.Target, c.Point, c.Normal, groundSlope, dropDistance, out var centre, out _)) centre = c.Point;
            float reach = c.At(radius);
            Found.Clear();
            var creatures = world.ActiveCreatures;
            if (creatures != null)
                foreach (var creature in creatures)
                    if (creature && creature.isActiveAndEnabled && creature.Alive && !creature.Recruited && Recruitable(creature)
                        && DiscordRules.Within(centre, creature.Position, reach) && Mathf.Abs(creature.Position.y - centre.y) < reach + 2)
                        Found.Add(creature);
            int recruited = 0;
            foreach (var creature in Found)
            {
                if (!creature.Recruit(orders, Holds(creature))) continue;
                recruited++;
                world.Spawn(recruitBurst, creature.Centre, Quaternion.identity, recruitBurstTint, recruitBurstScale);
                world.Play(recruitSound, creature.Centre, 1, Random.Range(.95f, 1.1f));
            }
            Found.Clear();
            if (recruited == 0 && !string.IsNullOrEmpty(nothingNote)) world.Announce(nothingNote, 2.5f);
            Feedback(c, recruited > 0);
        }

        /// <summary>One of the anthill's ants (by name, as kin are told apart), not the Queen.</summary>
        public bool Recruitable(WorldCreature creature) => Matches(creature, recruits);

        /// <summary>Holds its post instead of following (the machine-gun crew).</summary>
        public bool Holds(WorldCreature creature) => Matches(creature, holdGround);

        static bool Matches(WorldCreature creature, WorldCreature[] kinds)
        {
            if (!creature || kinds == null) return false;
            foreach (var kind in kinds)
                if (kind && kind.displayName == creature.displayName) return true;
            return false;
        }

        public bool TryGetStat(out string label, out LevelFloat values, out string unit)
        {
            label = "Радиус";
            values = radius;
            unit = " м";
            return true;
        }

        bool ICardArea.TryGetArea(CardDefinition definition, int level, Collider target, out float radius)
        {
            radius = this.radius.At(level);
            return true;
        }

        bool ICardArea.LeavesOutlinedZone => false;
    }
}
