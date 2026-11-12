using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Эхо». Repeats, at its own point of impact, the effect of the last card thrown on this outing (not Эхо itself),
    /// with that card's definition and the level of this Эхо copy (an Эхо III repeats a Fire I as Fire III). It never
    /// repeats Teleport, Эхо, «Пора домой» or story cards: with nothing to repeat it fizzles. Its area (the aim ring)
    /// is the repeated card's (<see cref="ICardArea"/>).
    /// </summary>
    public sealed class EchoEffect : CardEffect, ICardStatLine, ICardArea
    {
        [Header("What it repeats")]
        [Tooltip("Card ids never repeated besides Teleport, Эхо, story cards and «Пора домой» (always excluded).")]
        public string[] neverRepeat = { "teleport", "echo", "return" };

        [Header("Feedback")]
        [Tooltip("Shown instead of impactVfx when there is nothing to repeat (with missSound).")]
        public VfxBurst fizzleVfx;
        [Min(0)] public float fizzleScale = 1;

        public override void Apply(in ImpactContext c)
        {
            var last = EchoMemory.LastIn(c.World);
            if (!EchoRules.CanRepeat(last, EchoMemory.LastIsStory, neverRepeat) || !(last.effectAsset is CardEffect repeated))
            {
                c.World.Spawn(fizzleVfx, c.Point + c.Normal * .05f, Quaternion.LookRotation(c.Normal), c.Accent, fizzleScale);
                c.World.Play(missSound, c.Point);
                return;
            }
            // The echo's own ripple first, then the repeated card's effect with its own feedback.
            Feedback(c, true);
            repeated.Apply(new ImpactContext(c.World, c.Copy, last, c.Target, c.Point, c.Normal, c.Velocity, c.ThrowOrigin));
        }

        public bool TryGetStat(out string label, out LevelFloat values, out string unit)
        {
            // The repeat is as strong as this copy: I / II / III.
            label = "Уровень повтора";
            values = new LevelFloat(1, 2, 3);
            unit = "";
            return true;
        }

        bool ICardArea.TryGetArea(CardDefinition definition, int level, Collider target, out float radius)
        {
            radius = 0;
            var last = EchoMemory.Last;
            return EchoRules.CanRepeat(last, EchoMemory.LastIsStory, neverRepeat) && last.effectAsset is ICardArea area
                   && area.TryGetArea(last, level, target, out radius);
        }

        bool ICardArea.LeavesOutlinedZone
        {
            get
            {
                var last = EchoMemory.Last;
                return EchoRules.CanRepeat(last, EchoMemory.LastIsStory, neverRepeat) && last.effectAsset is ICardArea area && area.LeavesOutlinedZone;
            }
        }
    }

    /// <summary>What «Эхо» may repeat (EditMode-tested).</summary>
    public static class EchoRules
    {
        public static bool CanRepeat(CardDefinition definition, bool story, string[] neverRepeat)
        {
            if (!definition || story || !(definition.effectAsset is CardEffect effect)) return false;
            if (effect is EchoEffect || effect is TeleportEffect || definition.action == CardAction.Teleport) return false;
            return neverRepeat == null || System.Array.IndexOf(neverRepeat, definition.id) < 0;
        }
    }
}
