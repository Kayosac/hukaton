using System.Collections.Generic;
using Hakaton.Decks.Unity;
using Hakaton.Expeditions.Events;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Раздор». Every live beast (<see cref="WorldCreature"/>) within 5 / 10 / 15 m (levels I / II / III) of the hit
    /// starts a free-for-all: each goes for the nearest other one and turns to the next when its rival falls, by the
    /// usual rules of blows (<see cref="WorldCreature"/>'s brawl mode, refereed by <see cref="DiscordBrawl"/>), until
    /// one is left or 45 s have passed. The survivors go back to their ordinary behaviour with their wounds, the fallen
    /// drop their usual loot. A beast the player hurts during the fight goes for the player. Fewer than two beasts in
    /// the radius: those there bristle with the red "!!" for about 2 s, and the card is spent.
    /// Beasts of a «Звериная разборка» (<see cref="BeastBrawl"/>): see <see cref="DiscordRules.Decide"/>.
    /// </summary>
    public sealed class DiscordEffect : CardEffect, ICardStatLine, ICardArea
    {
        [Header("Board numbers (I / II / III)")]
        [Tooltip("Every live beast within this distance of the hit joins the fight, m (board: 5 / 10 / 15).")]
        public LevelFloat radius = new LevelFloat(5, 10, 15);

        [Header("Fight")]
        [Tooltip("The fight is called off after this long; whoever is alive has won, s.")]
        [Min(1)] public float maximumSeconds = 45;
        [Tooltip("The beasts glare and growl at each other this long before the first blow, s.")]
        [Min(0)] public float standoffSeconds = .9f;
        [Tooltip("How often a beast looks for the nearest other fighter, s.")]
        [Min(.1f)] public float retargetSeconds = .8f;
        [Tooltip("A beast only drops its rival for another one that is nearer by more than this, m.")]
        [Min(0)] public float loyalty = 1.5f;
        [Tooltip("A fighter goes for the player when he comes this close, m (0: only when he hurts it).")]
        [Min(0)] public float noticeDistance = 1.5f;
        [Tooltip("The fight stays within the card's radius plus this margin around the hit, m.")]
        [Min(0)] public float arenaMargin = 3;
        [Tooltip("Fewer than two beasts: those in the radius bristle with the red \"!!\" this long, s.")]
        [Min(0)] public float bristleSeconds = 2;

        [Header("Placement")]
        [Tooltip("The referee of the fight (prefab with DiscordBrawl): hit flashes and dust.")]
        public DiscordBrawl brawlPrefab;
        [Tooltip("The fight's centre is the ground found this far below a wall or creature hit, m.")]
        [Min(0)] public float dropDistance = 4;
        [Tooltip("Surfaces steeper than this (walls) are not ground, degrees.")]
        [Range(0, 90)] public float groundSlope = 60;

        static readonly List<WorldCreature> found = new List<WorldCreature>();
        static readonly List<BeastBrawl> events = new List<BeastBrawl>();

        public override void Apply(in ImpactContext c)
        {
            var world = c.World;
            if (!CardGround.TryFind(c.Target, c.Point, c.Normal, groundSlope, dropDistance, out var centre, out _)) centre = c.Point;
            float reach = c.At(radius);
            found.Clear();
            events.Clear();
            var creatures = world.ActiveCreatures;
            if (creatures != null)
                foreach (var creature in creatures)
                {
                    if (!creature || !creature.isActiveAndEnabled || !DiscordRules.Within(centre, creature.Position, reach)) continue;
                    var beastBrawl = creature.Referee as BeastBrawl;
                    var phase = beastBrawl ? beastBrawl.Current : (BeastBrawl.Phase?)null;
                    switch (DiscordRules.Decide(creature.Alive, creature.Brawling, phase))
                    {
                        case DiscordRules.Entry.Join: found.Add(creature); break;
                        case DiscordRules.Entry.StartEvent: if (!events.Contains(beastBrawl)) events.Add(beastBrawl); break;
                    }
                }
            foreach (var beastBrawl in events) beastBrawl.Begin();
            bool fight = DiscordRules.Gathers(found.Count);
            if (found.Count > 0 && brawlPrefab)
            {
                var brawl = world.SpawnForOuting(brawlPrefab, centre, Quaternion.identity);
                if (brawl) brawl.Begin(found, fight, reach, this);
            }
            Feedback(c, fight || events.Count > 0);
            found.Clear();
            events.Clear();
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
