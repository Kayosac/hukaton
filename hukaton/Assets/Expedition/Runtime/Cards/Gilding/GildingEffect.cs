using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Позолота». A hit on a shut treasure chest raises its tier: +1 (I), +1 and coins ×1.5 (II), +2 (III), never above
    /// the richest tier. The chest takes the new tier's payout, effects and marker at once and its model in a short show
    /// (<see cref="ChestGildingShow"/>). Gilding the same chest again raises it further; the coin multiplier does not
    /// stack (the larger stays). A mimic is not made richer: the gold gives it away — it wakes, shows what it is and hops
    /// off without biting (<see cref="ChestMimic.Expose"/>). Anything else (a miss, an opened chest): nothing.
    /// The gilding lasts while the chest exists: chests are rolled anew every outing (and when a far chunk comes back).
    /// </summary>
    public sealed class GildingEffect : CardEffect, ICardStatLine
    {
        [Header("Board numbers (I / II / III)")]
        [Tooltip("Tiers a chest goes up (board: +1 / +1 / +2).")]
        public LevelInt tierSteps = new LevelInt(1, 1, 2);
        [Tooltip("Coins of the gilded chest times this (board: II ×1.5).")]
        public LevelFloat coinMultiplier = new LevelFloat(1, 1.5f, 1);

        [Header("Chests")]
        [Tooltip("The plain chest of every tier (Expedition/Prefabs/Chests): a gilded chest takes the tier and model of the one a rank up.")]
        public TreasureChest[] tiers = new TreasureChest[0];
        [Tooltip("The change on the chest (Game/Prefabs/Cards/Gilding).")]
        public ChestGildingShow showPrefab;
        [Tooltip("Shown when the chest is already the richest and the level adds nothing.")]
        public string topTierNote = "Сундук и так легендарный";

        [Header("Mimic")]
        [Tooltip("Gold flakes falling off a mimic as it gives itself away.")]
        public VfxBurst mimicFlakes;
        public Color mimicFlakesTint = new Color(1f, .82f, .3f);
        [Min(.01f)] public float mimicFlakesScale = .6f;

        /// <summary>The richest tier among <see cref="tiers"/>.</summary>
        public int TopRank
        {
            get
            {
                int top = 1;
                foreach (var t in tiers) if (t && t.tier) top = Mathf.Max(top, t.tier.rank);
                return top;
            }
        }

        /// <summary>The plain chest prefab of a tier; null when there is none.</summary>
        public TreasureChest LookOf(int rank)
        {
            foreach (var t in tiers) if (t && t.tier && t.tier.rank == rank) return t;
            return null;
        }

        public override void Apply(in ImpactContext c)
        {
            var chest = c.TargetAs<TreasureChest>();
            if (!chest || chest.Opened || !chest.tier)
            {
                Feedback(c, false);
                return;
            }

            var mimic = chest.GetComponent<ChestMimic>();
            if (mimic)
            {
                bool woke = mimic.Expose();
                if (woke) c.World.Spawn(mimicFlakes, mimic.Centre, Quaternion.identity, mimicFlakesTint, mimicFlakesScale);
                Feedback(c, woke);
                return;
            }

            int rank = chest.tier.rank;
            int next = GildingRules.NextRank(rank, c.At(tierSteps), TopRank);
            var look = next > rank ? LookOf(next) : null;
            if (!look) next = rank;
            float coins = GildingRules.CoinMultiplier(chest.CoinMultiplier, c.At(coinMultiplier));
            if (!GildingRules.Changes(rank, next, chest.CoinMultiplier, coins))
            {
                if (!string.IsNullOrEmpty(topTierNote)) c.World.Announce(topTierNote, 2.5f);
                Feedback(c, false);
                return;
            }

            if (look) chest.ChangeTier(look.tier);
            chest.CoinMultiplier = coins;
            if (showPrefab)
            {
                var show = c.World.SpawnForOuting(showPrefab, chest.transform.position, chest.transform.rotation);
                if (show) show.Play(c.World, chest, look);
            }
            else if (look) ChestLook.Copy(look, chest);
            Feedback(c, true);
        }

        public bool TryGetStat(out string label, out LevelFloat values, out string unit)
        {
            label = "Уровней сундука";
            values = tierSteps;
            unit = "";
            return true;
        }
    }
}
