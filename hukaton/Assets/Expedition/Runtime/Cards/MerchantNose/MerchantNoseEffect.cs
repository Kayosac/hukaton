using System.Collections.Generic;
using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Нюх торгаша». Every shut treasure chest within 20 / 35 / 50 m of the hit gets a tall pillar of light, seen through
    /// walls, for 40 s or until it is opened (<see cref="ChestBeacon"/>). Mimics in reach get one too — they are shut
    /// chests to the nose — but under the light they now and then give a small shiver: a subtle hint, as rare and quiet
    /// as their tells close up. Nothing in reach: a snort and a note.
    /// </summary>
    public sealed class MerchantNoseEffect : CardEffect, ICardStatLine, ICardArea
    {
        [Header("Board numbers (I / II / III)")]
        [Tooltip("Shut chests within this distance of the hit get a pillar, m (table 13.11: 20 / 40 / 70).")]
        public LevelFloat radius = new LevelFloat(20, 40, 70);
        [Tooltip("How long a pillar stands unless its chest is opened first, s.")]
        [Min(1)] public float seconds = 40;

        [Header("Pillars")]
        public ChestBeacon beaconPrefab;
        [Tooltip("A mimic under a pillar gives its first small shiver this long after the light comes, s (from, to)…")]
        public Vector2 mimicFirstShiver = new Vector2(.8f, 2f);
        [Tooltip("…and one more every so often while lit — rare on purpose, s (from, to).")]
        public Vector2 mimicShiverEvery = new Vector2(9, 16);
        [Tooltip("Shown when no shut chest is in reach.")]
        public string nothingNote = "Сундуками тут не пахнет";

        static readonly List<TreasureChest> Found = new List<TreasureChest>();

        public override void Apply(in ImpactContext c)
        {
            float reach = c.At(radius);
            Found.Clear();
            foreach (var chest in TreasureChest.All)
                if (chest && !chest.Opened && chest.isActiveAndEnabled && MerchantNoseRules.Reaches(c.Point, chest.transform.position, reach))
                    Found.Add(chest);
            int lit = 0;
            foreach (var chest in Found)
                if (ChestBeacon.Light(c.World, beaconPrefab, chest, seconds, mimicFirstShiver, mimicShiverEvery)) lit++;
            Found.Clear();
            if (lit == 0 && !string.IsNullOrEmpty(nothingNote)) c.World.Announce(nothingNote, 2.5f);
            Feedback(c, lit > 0);
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
