using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Кривое зеркало». A hit on a creature shrinks it smoothly and crookedly to 50 / 40 / 30 % for 12 / 16 / 20 s,
    /// then it grows back (<see cref="CrookedMirrorShrink"/>): its strikes and shots deal that share of their damage
    /// (never below ½ heart) and its mass is scaled the same, so Impulse throws it further. A hit on a Stone cube or a
    /// Trampoline mushroom makes it 1.5 / 1.75 / 2 times bigger, colliders too, until it goes
    /// (<see cref="CrookedMirrorGrowth"/>). Anything else: a fizzle.
    /// </summary>
    public sealed class CrookedMirrorEffect : CardEffect, ICardStatLine
    {
        [Header("Creature (I / II / III)")]
        [Tooltip("Size, damage and mass multiplier of a hit creature.")]
        public LevelFloat shrink = new LevelFloat(.5f, .4f, .3f);
        [Tooltip("How long the creature stays small, s (then it grows back).")]
        public LevelFloat duration = new LevelFloat(12, 16, 20);

        [Header("Stone cube and trampoline mushroom (I / II / III)")]
        [Tooltip("Size multiplier of a hit cube or mushroom, until the end of the outing (it does not stack: the larger wins).")]
        public LevelFloat growth = new LevelFloat(1.5f, 1.75f, 2);

        [Header("Feel")]
        public CrookedMirrorShrink.Look look = new CrookedMirrorShrink.Look();
        [Tooltip("Seconds a cube or mushroom takes to grow, with a crooked wobble.")]
        [Min(.05f)] public float growSeconds = .8f;
        [Tooltip("Burst on a growing cube or mushroom.")]
        public VfxBurst growVfx;
        public SfxCue growSound;
        [Tooltip("Shake when a cube or mushroom grows.")]
        [Range(0, 1)] public float growShake = .15f;

        public override void Apply(in ImpactContext c)
        {
            var creature = c.Creature;
            if (creature && creature.Alive)
            {
                CrookedMirrorShrink.On(creature, c.World).Shrink(c.At(shrink), c.At(duration), look);
                Feedback(c, true);
                return;
            }
            var cube = c.TargetAs<StoneCubeBody>();
            var mushroom = cube ? null : c.TargetAs<MushroomTrampoline>();
            Transform grown = cube ? cube.transform : mushroom ? mushroom.transform : null;
            if (grown)
            {
                var growing = CrookedMirrorGrowth.On(grown.gameObject);
                bool bigger = growing.Grow(cube, mushroom, c.At(growth), growSeconds);
                Feedback(c, bigger);
                if (bigger)
                {
                    c.World.Spawn(growVfx, grown.position, Quaternion.identity, c.Accent, Mathf.Max(1, c.At(growth)));
                    c.World.Play(growSound, grown.position);
                    if (c.World.feel) c.World.feel.AddTrauma(growShake);
                }
                return;
            }
            Feedback(c, false);
        }

        public bool TryGetStat(out string label, out LevelFloat values, out string unit)
        {
            label = "Размер зверя";
            values = new LevelFloat(Mathf.Round(shrink.i * 100), Mathf.Round(shrink.ii * 100), Mathf.Round(shrink.iii * 100));
            unit = " %";
            return true;
        }
    }
}
