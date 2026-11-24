using System;
using System.Collections.Generic;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// Trophy cards (board «Пинок и трофейные карты»): a slain creature, an opened chest, a killed mimic or a broken crate
    /// now and then leaves a card that flies straight into the hand (<see cref="TrophyCardFlight"/>) — one throw, over
    /// the hand's limit, for this outing only (R discards it, it is gone with the outing, it never enters the
    /// collection). Peaceful and timid creatures leave none. A card is sure after a few dry kills, and on the first kill
    /// of an outing while the player still has few cards. Not in the cave prologue, not at the god.
    /// Which card: <see cref="TrophyCardRules.Pick"/>. Underground (in a dungeon, <see cref="Underground"/>) a share of the
    /// cards are the dungeon's own (the config's <see cref="DeckPrototypeConfig.trophyCards"/>: Вербовка, Эхолот) — never sold by the goblin, their level
    /// by how tough the source was (a worker I, a plug II, the Queen III; a chest by its rank).
    /// </summary>
    public sealed class TrophyCards : MonoBehaviour
    {
        [Serializable]
        public struct HealthBand
        {
            [Tooltip("Creatures with at most this much health…")]
            public int upToHealth;
            [Tooltip("…leave a card with this chance.")]
            [Range(0, 1)] public float chance;
        }

        public ExpeditionWorld world;
        public TrophyCardFlight flightPrefab;

        [Header("Chances")]
        [Tooltip("Slain aggressive and territorial creatures, by their health (ascending).")]
        public HealthBand[] creatures =
        {
            new HealthBand { upToHealth = 3, chance = .2f }, new HealthBand { upToHealth = 8, chance = .35f },
            new HealthBand { upToHealth = 15, chance = .55f }, new HealthBand { upToHealth = 9999, chance = .85f },
        };
        [Tooltip("Opened chests by their rank 1–5 (beggar … legendary).")]
        public float[] chestsByRank = { .4f, .55f, .75f, .9f, 1f };
        [Tooltip("Chests of this rank and up give a card of at least this rarity (legendary: 3+).")]
        [Range(1, 5)] public int richChestRank = 5;
        [Range(1, 5)] public int richChestCardRank = 3;
        [Tooltip("A killed chest mimic.")]
        [Range(0, 1)] public float mimics = 1;
        [Tooltip("This many kills in a row without a card make the next one sure (0: never).")]
        [Min(0)] public int dryKills = 2;
        [Tooltip("While the collection holds at most this many cards, the first kill of an outing gives a card for sure.")]
        [Min(0)] public int startingCollection = 5;
        [Tooltip("A card the player does not own weighs this many times as much (a taste of the goblin's cards).")]
        [Min(1)] public float unownedWeight = 2;

        [Header("Dungeon")]
        [Tooltip("Underground, this share of the trophy cards is one of the dungeon's own (the config's trophyCards: Вербовка, Эхолот).")]
        [Range(0, 1)] public float dungeonShare = .5f;
        [Tooltip("A dungeon card from a creature is level II above this much health (the plug: 12)…")]
        [Min(1)] public int levelTwoHealth = 8;
        [Tooltip("…and level III above this much (the Queen: 20).")]
        [Min(1)] public int levelThreeHealth = 15;
        [Tooltip("A dungeon card from a chest is level II from this rank (a stash's rich chest)…")]
        [Range(1, 5)] public int levelTwoChestRank = 4;
        [Tooltip("…and level III from this rank (the anthill's prize chest).")]
        [Range(1, 5)] public int levelThreeChestRank = 5;

        [Header("Feel")]
        public SfxCue appearSound;
        public SfxCue arriveSound;
        public VfxBurst appearSparkle;
        [Min(.01f)] public float appearSparkleScale = .8f;
        public Color glow = new Color(1f, .86f, .45f);
        [TextArea] public string notice = "Трофейная карта: «{0}»";

        /// <summary>F1: every source gives a card.</summary>
        public bool Always { get; set; }
        /// <summary>A trophy card reached the hand.</summary>
        public event Action<CardCopy> Arrived;
        public TrophyLuck Luck { get; private set; }
        /// <summary>The player is in a dungeon (DungeonSite keeps it up to date): its own cards come too.</summary>
        public bool Underground { get; set; }
        /// <summary>Tests: underground every trophy is a dungeon card.</summary>
        public bool AlwaysDungeon { get; set; }

        readonly System.Random random = new System.Random();
        readonly List<(int, double)> bands = new List<(int, double)>();

        bool Allowed => world && world.Running && !world.InPrologue && world.workshop && world.workshop.Model?.Current != null;

        void OnEnable()
        {
            Luck ??= new TrophyLuck(dryKills);
            if (world) world.OutingBegan += Begin;
        }

        void OnDisable()
        {
            if (world) world.OutingBegan -= Begin;
        }

        void Begin()
        {
            Luck = new TrophyLuck(dryKills);
            int owned = world && world.workshop && world.workshop.Model != null ? world.workshop.Model.Owned.Count : 99;
            Luck.Begin(owned <= startingCollection);
        }

        /// <summary>A creature died (its loot already dropped): it may leave a card; <paramref name="rolls"/> — Marauder's extra rolls.</summary>
        public void CreatureDied(WorldCreature creature, Vector3 at, int rolls = 1)
        {
            if (!Allowed || !creature) return;
            bool harmless = creature.temperament == Temperament.Peaceful || creature.temperament == Temperament.Timid;
            bands.Clear();
            foreach (var band in creatures) bands.Add((band.upToHealth, band.chance));
            double chance = TrophyCardRules.CreatureChance(creature.maximumHealth, harmless, bands);
            if (chance <= 0 && !Always) return;
            bool card = Always;
            for (int roll = 0; roll < Mathf.Max(1, rolls) && !card; roll++) card = Luck.Kill(chance, random.NextDouble()) || card;
            if (card) Give(at, 1, TrophyCardRules.LevelByHealth(creature.maximumHealth, levelTwoHealth, levelThreeHealth));
        }

        /// <summary>A chest opened and poured out its coins.</summary>
        public void ChestOpened(ChestTier tier, Vector3 at)
        {
            if (!Allowed || !tier) return;
            float chance = chestsByRank.Length == 0 ? 0 : chestsByRank[Mathf.Clamp(tier.rank, 1, chestsByRank.Length) - 1];
            if (Always || random.NextDouble() < chance)
                Give(at, tier.rank >= richChestRank ? richChestCardRank : 1, TrophyCardRules.LevelByChestRank(tier.rank, levelTwoChestRank, levelThreeChestRank));
        }

        /// <summary>A chest mimic was killed.</summary>
        public void MimicKilled(Vector3 at)
        {
            if (Allowed && (Always || random.NextDouble() < mimics)) Give(at, 1);
        }

        /// <summary>A crate or a pot broke (its kind's chance).</summary>
        public void Broken(float chance, Vector3 at)
        {
            if (Allowed && (Always || chance > 0 && random.NextDouble() < chance)) Give(at, 1);
        }

        /// <summary>
        /// Picks a card and sends it flying from <paramref name="from"/> into the hand. False when nothing could be given.
        /// <paramref name="dungeonLevel"/>: the level a dungeon card from this source has.
        /// </summary>
        public bool Give(Vector3 from, int minimumRank, int dungeonLevel = 1)
        {
            if (!Allowed) return false;
            var own = DungeonCard();
            if (own) { Send(own, from, Mathf.Clamp(dungeonLevel, 1, 3)); return true; }
            var config = world.workshop.config;
            var model = world.workshop.Model;
            var definitions = new Dictionary<string, CardDefinition>();
            var specs = new List<CardSpec>();
            // Only the player's current pool stage (CardPool): no dummy, no story or dungeon cards.
            foreach (var card in StagedCards.OnSale(config, model.PoolStage))
            {
                definitions[card.id] = card;
                specs.Add(card.ToSpec());
            }
            var owned = new HashSet<string>();
            foreach (var copy in model.Owned) owned.Add(copy.Spec.Id);
            var spec = TrophyCardRules.Pick(specs, owned, random, unownedWeight, minimumRank);
            if (spec == null || !definitions.TryGetValue(spec.Id, out var definition)) return false;
            Send(definition, from, 1);
            return true;
        }

        /// <summary>Underground, by <see cref="dungeonShare"/>: one of the dungeon's own cards; otherwise null.</summary>
        CardDefinition DungeonCard()
        {
            var dungeonCards = world.workshop.config ? world.workshop.config.trophyCards : null;
            if (!Underground || dungeonCards == null) return null;
            int count = 0;
            foreach (var card in dungeonCards) if (card && card.artwork) count++;
            if (count == 0 || !AlwaysDungeon && random.NextDouble() >= dungeonShare) return null;
            int pick = random.Next(count);
            foreach (var card in dungeonCards)
                if (card && card.artwork && pick-- == 0) return card;
            return null;
        }

        /// <summary>Sends <paramref name="definition"/> at <paramref name="level"/> flying from <paramref name="from"/> into the hand.</summary>
        public void Send(CardDefinition definition, Vector3 from, int level)
        {
            if (!Allowed || !definition) return;
            world.Play(appearSound, from);
            world.Spawn(appearSparkle, from, Quaternion.identity, glow, appearSparkleScale);
            var front = definition.artwork ? definition.artwork.texture : null;
            if (flightPrefab && world.hand && world.viewCamera)
            {
                var flight = Instantiate(flightPrefab, from, Quaternion.identity, world.spawnedObjects);
                flight.Launch(world.hand, world.viewCamera.transform, front, world.hand.cardBack, level, glow, from, pose => Arrive(definition, level, pose));
            }
            else Arrive(definition, level, null);
        }

        void Arrive(CardDefinition definition, int level, Pose? pose)
        {
            var trip = world && world.Running ? world.workshop.Model.Current : null;
            if (trip == null) return;
            var copy = trip.GrantToHand(definition.ToSpec(), level, 1);
            if (copy == null) return;
            if (pose.HasValue && world.hand) world.hand.ArriveFrom(copy.Id, pose.Value);
            world.Play(arriveSound);
            world.Announce(string.Format(notice, definition.displayName), 2.5f);
            Arrived?.Invoke(copy);
        }
    }
}
