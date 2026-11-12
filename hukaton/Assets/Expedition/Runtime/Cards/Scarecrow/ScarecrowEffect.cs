using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Чучело» (ранг 2). The thrown card sticks upright into the ground where it lands (below a creature or a wall it
    /// hit) and grows into a ~1.7 m crooked standing card (<see cref="ScarecrowDecoy"/>): health 6 / 10 / 16, the
    /// player's side. Aggressive and territorial creatures within 12 m that go at the player turn on it. Stands 25 s
    /// or until destroyed; fire burns it down in 4 s; level III flares up when destroyed and sets creatures within 2.5 m
    /// alight. No ground under the hit: the card is spent with the miss feedback.
    /// Brief: tmp/new-cards-2/BRIEF.md (5); shared systems: tmp/new-cards/API.md.
    /// </summary>
    public sealed class ScarecrowEffect : CardEffect, ICardStatLine, ICardArea
    {
        [Header("Card numbers (I / II / III)")]
        [Tooltip("Scarecrow health (1 = half a heart of a creature's strike).")]
        public LevelInt health = new LevelInt(6, 10, 16);
        [Tooltip("How long it stands, s.")]
        public LevelFloat lifetime = LevelFloat.All(25);
        [Tooltip("Aggressive and territorial creatures going at the player within this radius turn on it, m.")]
        public LevelFloat drawRadius = LevelFloat.All(12);
        [Tooltip("Set alight, it burns this long and breaks, s.")]
        public LevelFloat burnSeconds = LevelFloat.All(4);
        [Tooltip("Destroyed, it flares up: creatures within this radius catch fire, m (0: no flare).")]
        public LevelFloat burstRadius = new LevelFloat(0, 0, 2.5f);
        [Tooltip("The flare's hit and how long it sets a creature burning, s.")]
        public LevelInt burstDamage = LevelInt.All(1);
        public LevelFloat burstBurnSeconds = LevelFloat.All(3);

        [Header("Placement")]
        public ScarecrowDecoy scarecrowPrefab;
        [Tooltip("The card stands on the ground found this far below a wall or creature hit, m.")]
        [Min(0)] public float dropDistance = 4;
        [Tooltip("Surfaces steeper than this (walls) are not ground, degrees.")]
        [Range(0, 90)] public float groundSlope = 55;
        public string noGroundNotice = "Чучелу негде встать";

        public override void Apply(in ImpactContext c)
        {
            if (!scarecrowPrefab)
            {
                Debug.LogWarning("Scarecrow: no prefab assigned.", this);
                Feedback(c, false);
                return;
            }
            if (!CardGround.TryFind(c.Target, c.Point, c.Normal, groundSlope, dropDistance, out var ground, out _))
            {
                Feedback(c, false);
                c.World.Announce(noGroundNotice, 1.6f);
                return;
            }
            // The illustration faces the thrower (the quad's front looks along -Z of the root).
            Vector3 away = ground - c.Motor.transform.position;
            away.y = 0;
            if (away.sqrMagnitude < .01f) { away = c.Direction; away.y = 0; }
            var rotation = away.sqrMagnitude > .0001f ? Quaternion.LookRotation(away.normalized) : Quaternion.identity;
            var decoy = c.World.SpawnForOuting(scarecrowPrefab, ground, rotation);
            if (decoy)
            {
                var back = c.World.hand ? c.World.hand.cardBack : null;
                var front = c.Copy != null ? c.World.Artwork(c.Copy) : null;
                decoy.Setup(front, back, c.Level, c.Accent, Numbers(c.Level));
            }
            Feedback(c, decoy);
        }

        /// <summary>The scarecrow's numbers at a card level.</summary>
        public ScarecrowNumbers Numbers(int level) => new ScarecrowNumbers
        {
            Health = health.At(level),
            Lifetime = lifetime.At(level),
            DrawRadius = drawRadius.At(level),
            BurnSeconds = burnSeconds.At(level),
            BurstRadius = burstRadius.At(level),
            BurstDamage = burstDamage.At(level),
            BurstBurnSeconds = burstBurnSeconds.At(level),
        };

        public bool TryGetStat(out string label, out LevelFloat values, out string unit)
        {
            label = "Здоровье";
            values = health;
            unit = "";
            return true;
        }

        bool ICardArea.TryGetArea(CardDefinition definition, int level, Collider target, out float radius)
        {
            radius = drawRadius.At(level);
            return true;
        }

        bool ICardArea.LeavesOutlinedZone => true;
    }
}
