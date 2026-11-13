using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Мародёр». A hit on a beast puts a mark over its head (<see cref="MarauderMark"/>) for 10 / 15 / 20 s
    /// (levels I / II / III). If it dies marked, its drop table is rolled 2 / 2 / 3 times instead of once (each roll by
    /// the table's usual rules, <see cref="WorldCreature.drops"/>). Another copy on a marked beast renews the mark.
    /// A miss does nothing.
    /// </summary>
    public sealed class MarauderEffect : CardEffect, ICardStatLine
    {
        [Header("Board numbers (I / II / III)")]
        [Tooltip("How long the mark holds, s (board: 10 / 15 / 20).")]
        public LevelFloat duration = new LevelFloat(10, 15, 20);
        [Tooltip("Rolls of the drop table when the beast dies marked (board: 2 / 2 / 3; 1 is the usual).")]
        public LevelInt lootRolls = new LevelInt(2, 2, 3);

        [Header("Mark")]
        [Tooltip("The mark over the beast's head (prefab with MarauderMark).")]
        public MarauderMark markPrefab;

        public override void Apply(in ImpactContext c)
        {
            var creature = c.Creature;
            if (!creature || !creature.Alive || !markPrefab)
            {
                Feedback(c, false);
                return;
            }
            var mark = MarauderMark.On(creature);
            if (!mark) mark = c.World.SpawnForOuting(markPrefab, creature.Position, Quaternion.identity, creature.transform);
            if (mark) mark.Mark(creature, c.At(duration), c.At(lootRolls));
            Feedback(c, mark);
        }

        public bool TryGetStat(out string label, out LevelFloat values, out string unit)
        {
            label = "Метка";
            values = duration;
            unit = " с";
            return true;
        }
    }
}
